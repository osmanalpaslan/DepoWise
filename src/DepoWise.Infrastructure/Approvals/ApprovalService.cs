using System.Data.Common;
using DepoWise.Application.Approvals;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Approvals;

/// <summary>Bir onay süreci tamamlandığında ilgili varlığın kendi iş kaydını AYNI transaction içinde
/// günceller. Motor varlık türlerini bilmez; bağlama kompozisyon kökünde (ServerServices) yapılır →
/// tek motor, iki süreç (PK-EK-03) ve döngüsel bağımlılık yok.</summary>
public delegate void ApprovalCompletionHandler(DbConnection conn, DbTransaction tx, SessionContext session,
    string entityType, string entityId, bool approved, string? reason, long nowMs);

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187 + ADR-188) — TEK ONAY MOTORU ═══
///
/// <b>İKİNCİ MOTOR KURULMAZ (PK-EK-03).</b> Malzeme Talebi ve Satın Alma aynı
/// <c>approval_instance</c>/<c>approval_step</c> yapısını kullanır; fark yalnız <c>entity_type</c>'tır.
///
/// <b>SNAPSHOT (PK-EK-04):</b> <see cref="Start"/> anında hiyerarşi çözülür ve adım sahipleri
/// <c>approval_step.approver_user_id</c>'ye YAZILIR. Bundan sonra hiyerarşi/ekip değişse bile açık
/// süreç ETKİLENMEZ — motor bir daha canlı hiyerarşiye bakmaz.
///
/// <b>OPSİYONELLİK (İK-3 / ADR-188 §4):</b> başlatıcının üstü yoksa zincir YOKTUR → <see cref="Start"/>
/// <c>null</c> döner ve varlık bugünkü akışıyla devam eder. Hiçbir backfill yapılmadığı için hiyerarşi
/// tanımlanana kadar sistemin davranışı BİREBİR aynıdır.
///
/// <b>ÇEVRİMDIŞI YASAK (PK-EK-05 / İK-9):</b> bu tablolar hiçbir senkron yoluna girmez; onay yalnız
/// sunucu otoritesinde ilerler. Masaüstü çevrimdışıyken onay adımı YOKTUR ve yazamaz.
///
/// <b>EŞZAMANLILIK (§19):</b> karar <c>BeginImmediate</c> transaction'ı içinde
/// <c>UPDATE … WHERE id=@i AND status='pending'</c> ile uygulanır; etkilenen satır 0 ise işlem
/// reddedilir. Aynı adıma iki eşzamanlı onaydan YALNIZ biri başarılı olur. Bu LWW DEĞİLDİR.
/// </summary>
public sealed class ApprovalService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    /// <summary>Süreç tamamlandığında varlığı güncelleyen bağlayıcılar (entity_type → işleyici).</summary>
    private readonly Dictionary<string, ApprovalCompletionHandler> _handlers = new(StringComparer.Ordinal);

    public ApprovalService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Varlık türünü motora bağlar. Bilinmeyen tür kabul edilmez (PK-EK-01 kapsam kilidi).</summary>
    public void Register(string entityType, ApprovalCompletionHandler handler)
    {
        if (!ApprovalEntityTypes.IsKnown(entityType))
            throw new ArgumentException($"Onay kapsamı dışında varlık türü: {entityType}");
        _handlers[entityType] = handler;
    }

    // ══════════════════════════════════════ BAŞLATMA ══════════════════════════════════════

    /// <summary>
    /// Süreç başlatır. Zincir YOKSA <c>null</c> döner (opsiyonellik) — çağıran mevcut akışına devam eder.
    /// AYNI transaction içinde çalışır: varlığın oluşturulması ile süreç başlatılması atomiktir.
    /// </summary>
    /// <param name="initiatorUserId">Zincirin çözüleceği kişi (talebi/siparişi başlatan).</param>
    public string? Start(DbConnection conn, DbTransaction tx, SessionContext s,
        string entityType, string entityId, string initiatorUserId, long nowMs)
    {
        if (!ApprovalEntityTypes.IsKnown(entityType))
            throw new ArgumentException($"Onay kapsamı dışında varlık türü: {entityType}");

        // Aynı varlık için AÇIK süreç varsa ikincisi başlatılmaz (veritabanında da kısmi benzersiz indeks var).
        if (OpenInstanceId(conn, tx, s.CompanyId, entityType, entityId) is not null) return null;

        var zincir = UserHierarchyService.ResolveChain(conn, tx, s.CompanyId, initiatorUserId);
        if (zincir.Count == 0) return null;               // ⭐ zincir yok → mevcut davranış korunur

        var instanceId = Guid.NewGuid().ToString("N");
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO approval_instance(id, company_id, entity_type, entity_id, status, started_by, started_at,
    snapshot_at, closed_at, created_at, updated_at, version, is_deleted)
VALUES(@i,@c,@et,@eid,'pending',@by,@now,@now,NULL,@now,@now,1,0);";
            ins.AddWithValue("@i", instanceId);
            ins.AddWithValue("@c", s.CompanyId);
            ins.AddWithValue("@et", entityType);
            ins.AddWithValue("@eid", entityId);
            ins.AddWithValue("@by", initiatorUserId);
            ins.AddWithValue("@now", nowMs);
            ins.ExecuteNonQuery();
        }

        // ⭐ SNAPSHOT: adım sahipleri BURADA sabitlenir.
        for (int i = 0; i < zincir.Count; i++)
        {
            using var st = conn.CreateCommand();
            st.Transaction = tx;
            st.CommandText = @"
INSERT INTO approval_step(id, company_id, instance_id, step_no, approver_user_id, status,
    acted_by, acted_at, reason, created_at, updated_at, version, is_deleted)
VALUES(@i,@c,@inst,@no,@ap,'pending',NULL,NULL,NULL,@now,@now,1,0);";
            st.AddWithValue("@i", Guid.NewGuid().ToString("N"));
            st.AddWithValue("@c", s.CompanyId);
            st.AddWithValue("@inst", instanceId);
            st.AddWithValue("@no", (long)(i + 1));
            st.AddWithValue("@ap", zincir[i]);
            st.AddWithValue("@now", nowMs);
            st.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "approval_instance", instanceId,
            AuditActions.Create, s.UserId, AfterJson: $"{{\"entity\":\"{entityType}\",\"steps\":{zincir.Count}}}"), _clock);
        return instanceId;
    }

    // ══════════════════════════════════════ OKUMA ══════════════════════════════════════

    /// <summary>Varlığın AÇIK süreç kimliği (yoksa null). Tenant süzgeçlidir.</summary>
    public static string? OpenInstanceId(DbConnection conn, DbTransaction? tx, string companyId,
        string entityType, string entityId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id FROM approval_instance WHERE company_id=@c AND entity_type=@et AND entity_id=@eid " +
            "AND status='pending' AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@et", entityType);
        cmd.AddWithValue("@eid", entityId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Varlığın EN SON süreç durumu (yoksa null). <c>Receive</c> kapısı bunu kullanır.</summary>
    public static string? LatestStatus(DbConnection conn, DbTransaction? tx, string companyId,
        string entityType, string entityId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT status FROM approval_instance WHERE company_id=@c AND entity_type=@et AND entity_id=@eid " +
            "AND is_deleted=0 ORDER BY started_at DESC, id DESC;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@et", entityType);
        cmd.AddWithValue("@eid", entityId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? r.GetString(0) : null;
    }

    public ApprovalInstance? Instance(SessionContext s, string instanceId)
    {
        using var conn = _factory.Create();
        return Instance(conn, null, s.CompanyId, instanceId);
    }

    private static ApprovalInstance? Instance(DbConnection conn, DbTransaction? tx, string companyId, string instanceId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, company_id, entity_type, entity_id, status, started_by, started_at, snapshot_at, closed_at " +
            "FROM approval_instance WHERE id=@i AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@i", instanceId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ApprovalInstance(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5), Convert.ToInt64(r.GetValue(6)), Convert.ToInt64(r.GetValue(7)),
            r.IsDBNull(8) ? null : Convert.ToInt64(r.GetValue(8)));
    }

    /// <summary>Sürecin adımları (sıra ile). BAŞKA firmanın süreci ASLA dönmez.</summary>
    public IReadOnlyList<ApprovalStepRow> Steps(SessionContext s, string instanceId)
    {
        using var conn = _factory.Create();
        _ = Instance(conn, null, s.CompanyId, instanceId)
            ?? throw new ForbiddenException("Onay süreci bulunamadı veya başka firmaya ait.");
        return Steps(conn, null, s.CompanyId, instanceId);
    }

    private static List<ApprovalStepRow> Steps(DbConnection conn, DbTransaction? tx, string companyId, string instanceId)
    {
        var list = new List<ApprovalStepRow>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, company_id, instance_id, step_no, approver_user_id, status, acted_by, acted_at, reason " +
            "FROM approval_step WHERE instance_id=@i AND company_id=@c AND is_deleted=0 ORDER BY step_no;";
        cmd.AddWithValue("@i", instanceId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ApprovalStepRow(r.GetString(0), r.GetString(1), r.GetString(2),
                Convert.ToInt64(r.GetValue(3)), r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : Convert.ToInt64(r.GetValue(7)),
                r.IsDBNull(8) ? null : r.GetString(8)));
        return list;
    }

    /// <summary>
    /// ═══ ALT FAZ 3 — "ONAYLAMALARIM" VERİ KAYNAĞI ═══
    ///
    /// Kullanıcıya düşen ve <b>sırası gelmiş</b> bekleyen adımlar. Bir adım listede görünür ancak:
    /// (a) sürecin durumu <c>pending</c> ise, (b) adım kullanıcının <b>SNAPSHOT</b> edilmiş
    /// <c>approver_user_id</c>'sine aitse, (c) adım <c>pending</c> ise ve (d) kendisinden önce
    /// bekleyen adım kalmamışsa.
    ///
    /// <b>Kullanıcı ve firma DAİMA oturumdan</b> okunur — istemciden <c>approver_user_id</c> veya
    /// <c>company_id</c> ALINMAZ, bu yüzden başkasının kuyruğu hiçbir yoldan istenemez.
    ///
    /// <b>PERFORMANS (§14):</b> tek sorgu. Sıra kontrolü <c>NOT EXISTS</c> ile, toplam adım sayısı
    /// tek alt-sorguyla, belge/sipariş no ise <c>LEFT JOIN</c> ile SQL tarafında çözülür → satır
    /// başına ek sorgu (N+1) YOKTUR. (Önceki sürüm satır başına <c>IsCurrent</c> çağırıyordu.)
    ///
    /// <b>Yetki notu:</b> burada modül yetkisi ARANMAZ — liste zaten yalnız kullanıcının KENDİ
    /// adımlarını içerir, dolayısıyla veri sızıntısı olamaz. Gerçek onay/ret eylemi
    /// <see cref="Approve"/>/<see cref="Reject"/> içindeki mevcut kapılardan geçer
    /// (<c>request_approval</c> / <c>purchasing</c>). Listede görünmek onaylama yetkisi DEĞİLDİR.
    /// </summary>
    public IReadOnlyList<PendingApprovalRow> MyPending(SessionContext s)
    {
        var list = new List<PendingApprovalRow>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT i.id, s.id, i.entity_type, i.entity_id, s.step_no,
       (SELECT COUNT(1) FROM approval_step t WHERE t.instance_id = i.id AND t.is_deleted = 0),
       mr.doc_no, mr.request_date, po.order_no, po.order_date,
       i.started_by, i.started_at
FROM approval_step s
JOIN approval_instance i ON i.id = s.instance_id AND i.is_deleted = 0 AND i.status = 'pending'
LEFT JOIN material_requests mr
       ON i.entity_type = 'material_request' AND mr.id = i.entity_id AND mr.company_id = i.company_id
LEFT JOIN purchase_orders po
       ON i.entity_type = 'purchase_order'  AND po.id = i.entity_id AND po.company_id = i.company_id
WHERE s.company_id = @c AND s.approver_user_id = @u AND s.status = 'pending' AND s.is_deleted = 0
  AND NOT EXISTS (SELECT 1 FROM approval_step p
                  WHERE p.instance_id = i.id AND p.is_deleted = 0
                    AND p.status = 'pending' AND p.step_no < s.step_no)
ORDER BY i.started_at, s.step_no;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@u", s.UserId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var tur = r.GetString(2);
            var belgeNo = tur == ApprovalEntityTypes.MaterialRequest
                ? (r.IsDBNull(6) ? null : r.GetString(6))
                : (r.IsDBNull(8) ? null : r.GetString(8));
            long? tarih = tur == ApprovalEntityTypes.MaterialRequest
                ? (r.IsDBNull(7) ? null : Convert.ToInt64(r.GetValue(7)))
                : (r.IsDBNull(9) ? null : Convert.ToInt64(r.GetValue(9)));

            list.Add(new PendingApprovalRow(
                r.GetString(0), r.GetString(1), tur, r.GetString(3),
                Convert.ToInt64(r.GetValue(4)), Convert.ToInt64(r.GetValue(5)),
                belgeNo, tarih,
                r.IsDBNull(10) ? null : r.GetString(10), Convert.ToInt64(r.GetValue(11))));
        }
        return list;
    }

    // ══════════════════════════════════════ KARAR ══════════════════════════════════════

    /// <summary>Adımı ONAYLAR. Son adım da onaylanınca süreç tamamlanır ve varlık güncellenir.</summary>
    public void Approve(SessionContext s, string stepId) => Act(s, stepId, approve: true, reason: null);

    /// <summary>Adımı REDDEDER. Gerekçe ZORUNLUDUR (mevcut Malzeme Talebi davranışıyla aynı).
    /// Reddedilen süreç kapanır; kalan adımlar 'skipped' olur (silinmez — İK-10 görünürlük).</summary>
    public void Reject(SessionContext s, string stepId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Ret gerekçesi zorunlu.");
        Act(s, stepId, approve: false, reason: reason);
    }

    private void Act(SessionContext s, string stepId, bool approve, string? reason)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        // ── KAPI 1: adım bu firmanın mı, var mı? (istemciden gelen stepId'ye GÜVENİLMEZ) ──
        var (instanceId, stepNo, approverUserId, stepStatus) = LoadStep(conn, tx, s.CompanyId, stepId);

        // ── KAPI 2: süreç açık mı, bu firmanın mı? ──
        var inst = Instance(conn, tx, s.CompanyId, instanceId)
                   ?? throw new ForbiddenException("Onay süreci bulunamadı veya başka firmaya ait.");
        if (inst.Status != ApprovalStatus.Pending)
            throw new InvalidOperationException("Bu onay süreci kapanmış; yeniden işlem yapılamaz.");

        // ── KAPI 3: modül yetkisi (varlık türüne göre mevcut yetki sistemi KORUNUR) ──
        AccessControl.Require(s, ModuleOf(inst.EntityType), PermissionAction.Edit);

        // ── KAPI 4: adım sahipliği — SNAPSHOT'taki kişi. Ekip liderliği bunu BYPASS ETMEZ (ADR-188 §5) ──
        if (!string.Equals(approverUserId, s.UserId, StringComparison.Ordinal))
            throw new ForbiddenException("Bu onay adımı size atanmamış.");

        // ── KAPI 5: sıra — önceki adımlar tamamlanmadan sonraki adım işlenemez ──
        if (stepStatus != ApprovalStatus.Pending || !IsCurrent(conn, tx, instanceId, stepNo))
            throw new InvalidOperationException("Bu adım şu anda işleme açık değil (sırası gelmedi veya kapandı).");

        // ── KAPI 6: self-approval yalnız admin (İK-5). "admin" = mevcut AccessControl.IsAdmin ──
        if (string.Equals(inst.StartedBy, s.UserId, StringComparison.Ordinal) && !AccessControl.IsAdmin(s))
            throw new ForbiddenException("Kendi talebinizi onaylayamazsınız.");

        // ── EŞZAMANLILIK: yalnız 'pending' iken geçiş yapılır; ikinci eşzamanlı istek 0 satır etkiler ──
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText =
                "UPDATE approval_step SET status=@st, acted_by=@by, acted_at=@now, reason=@rs, " +
                "updated_at=@now, version=version+1 WHERE id=@i AND status='pending' AND is_deleted=0;";
            upd.AddWithValue("@st", approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected);
            upd.AddWithValue("@by", s.UserId);
            upd.AddWithValue("@now", now);
            upd.AddWithValue("@rs", (object?)reason ?? DBNull.Value);
            upd.AddWithValue("@i", stepId);
            if (upd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Bu adım başka bir işlemle güncellendi; tekrar deneyin.");
        }

        var bitti = false;
        var onaylandi = false;
        if (!approve)
        {
            // Ret: kalan bekleyen adımlar 'skipped' (fiziksel silme YOK).
            using (var atla = conn.CreateCommand())
            {
                atla.Transaction = tx;
                atla.CommandText =
                    "UPDATE approval_step SET status='skipped', updated_at=@now, version=version+1 " +
                    "WHERE instance_id=@i AND status='pending' AND is_deleted=0;";
                atla.AddWithValue("@i", instanceId);
                atla.AddWithValue("@now", now);
                atla.ExecuteNonQuery();
            }
            bitti = true;
        }
        else
        {
            bitti = KalanBekleyen(conn, tx, instanceId) == 0;
            onaylandi = bitti;
        }

        if (bitti)
        {
            using (var kapat = conn.CreateCommand())
            {
                kapat.Transaction = tx;
                kapat.CommandText =
                    "UPDATE approval_instance SET status=@st, closed_at=@now, updated_at=@now, version=version+1 " +
                    "WHERE id=@i AND status='pending';";
                kapat.AddWithValue("@st", onaylandi ? ApprovalStatus.Approved : ApprovalStatus.Rejected);
                kapat.AddWithValue("@now", now);
                kapat.AddWithValue("@i", instanceId);
                if (kapat.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("Onay süreci başka bir işlemle kapatıldı.");
            }
            // Varlığın kendi iş kaydı AYNI transaction'da güncellenir → yarım kalmış durum oluşmaz.
            if (_handlers.TryGetValue(inst.EntityType, out var handler))
                handler(conn, tx, s, inst.EntityType, inst.EntityId, onaylandi, reason, now);
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "approval_step", stepId,
            AuditActions.Update, s.UserId,
            AfterJson: $"{{\"status\":\"{(approve ? "approved" : "rejected")}\",\"instance\":\"{instanceId}\"}}"), _clock);
        tx.Commit();
    }

    // ══════════════════════════════════════ YARDIMCI ══════════════════════════════════════

    private static (string InstanceId, long StepNo, string ApproverUserId, string Status) LoadStep(
        DbConnection conn, DbTransaction tx, string companyId, string stepId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT instance_id, step_no, approver_user_id, status FROM approval_step " +
            "WHERE id=@i AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@i", stepId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Onay adımı bulunamadı veya başka firmaya ait.");
        return (r.GetString(0), Convert.ToInt64(r.GetValue(1)), r.GetString(2), r.GetString(3));
    }

    /// <summary>Bu adım SIRASI GELEN adım mı? (kendisinden önce bekleyen adım kalmamış olmalı)</summary>
    private static bool IsCurrent(DbConnection conn, DbTransaction? tx, string instanceId, long stepNo)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(1) FROM approval_step WHERE instance_id=@i AND is_deleted=0 " +
            "AND status='pending' AND step_no < @n;";
        cmd.AddWithValue("@i", instanceId);
        cmd.AddWithValue("@n", stepNo);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) == 0;
    }

    private static long KalanBekleyen(DbConnection conn, DbTransaction tx, string instanceId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(1) FROM approval_step WHERE instance_id=@i AND status='pending' AND is_deleted=0;";
        cmd.AddWithValue("@i", instanceId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
    }

    /// <summary>Varlık türünün MEVCUT yetki modülü — yeni yetki modülü icat edilmez (§17).</summary>
    private static string ModuleOf(string entityType) => entityType switch
    {
        ApprovalEntityTypes.MaterialRequest => "request_approval",   // mevcut Talep Onaylama yetkisi
        ApprovalEntityTypes.PurchaseOrder => "purchasing",           // mevcut Satın Alma yetkisi
        _ => throw new ArgumentException($"Onay kapsamı dışında varlık türü: {entityType}"),
    };
}
