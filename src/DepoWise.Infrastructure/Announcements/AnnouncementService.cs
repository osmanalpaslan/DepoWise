using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Announcements;

/// <summary>Duyuru satırı. Aktiflik TÜRETİLİR (durum alanı YOK — PK-J3): pencere içindeyse aktif.</summary>
public sealed record AnnouncementRow(string Id, string Title, string? Body, string Importance,
    string? BranchId, string? BranchName, long? PublishStart, long? PublishEnd,
    string CreatedByName, long CreatedAt, long Version)
{
    public bool IsImportant => Importance == "important";
    public string ImportanceDisplay => IsImportant ? "Önemli" : "Normal";
    public string BranchDisplay => string.IsNullOrEmpty(BranchName) ? "Tüm Firma" : BranchName!;
    public string PeriodDisplay => PublishStart is null && PublishEnd is null ? "Süresiz"
        : $"{Tarih(PublishStart)} – {Tarih(PublishEnd)}";
    public bool IsActive(long nowMs)
        => (PublishStart is not { } s || s <= nowMs) && (PublishEnd is not { } e || e >= nowMs);
    public string StatusDisplay(long nowMs)
        => PublishStart is { } s && s > nowMs ? "Yayında değil (gelecek)"
         : PublishEnd is { } e && e < nowMs ? "Yayın bitti"
         : "Yayında";
    private static string Tarih(long? ms) => ms is null ? "…"
        : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime.ToString("dd.MM.yyyy");
}

public sealed record NewAnnouncement(string Title, string? Body = null, string? Importance = null,
    string? BranchId = null, long? PublishStart = null, long? PublishEnd = null);

/// <summary>
/// ═══ DYR-01 (ADR-173, 2026-08-28) — DUYURU ═══
///
/// PK-J1: OKUMA herkese açık (<see cref="AppModules.IsPublicRead"/> → View herkese; Rol Yetki Kontrol
/// kapatması yine geçerli); YAZMA (Create/Edit/Delete) announcements yetkisiyle — kapalı gelir.
/// PK-J2: opsiyonel TEK şube hedefi — boşsa firma geneli; doluysa yalnız o şube KAPSAMINDAKİLER görür
/// (BranchAccess; yan kapı yok — bildirim kaynağı da AYNI listeden okur).
/// PK-J3: aktiflik yayın penceresinden TÜRETİLİR — durum alanı/paralel mekanizma YOK; pencere dışına
/// çıkan duyuru ekrandan (yönetici hariç) ve bildirimden kendiliğinden düşer.
/// OKUNDU: mevcut alert_reads imza mekanizması (BLD-01) — imza=version; duyuru DÜZENLENİNCE yeniden
/// okunmamış olur. <b>SİLME:</b> soft delete + Çöp Kutusu; fiziksel silme yok. TENANT: her sorgu company_id.
/// </summary>
public sealed class AnnouncementService
{
    public const string Module = "announcements";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public AnnouncementService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public bool CanManage(SessionContext s)
        => AccessControl.Can(s, Module, PermissionAction.Create)
           || AccessControl.Can(s, Module, PermissionAction.Edit)
           || AccessControl.Can(s, Module, PermissionAction.Delete);

    /// <summary>
    /// Duyuru listesi. HERKES çağırabilir (PK-J1); yönetici olmayanlar YALNIZ AKTİF (pencere içi)
    /// duyuruları görür — <paramref name="includeInactive"/> istese de (fail-closed).
    /// Şube hedefli duyuru yalnız kapsamdakilere; şubesiz herkese (sınıf kuralı).
    /// </summary>
    public IReadOnlyList<AnnouncementRow> List(SessionContext s, bool includeInactive = false, string? search = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);   // IsPublicRead → herkes; blocked rol takılır
        var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        var list = new List<AnnouncementRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT a.id, a.title, a.body, a.importance, a.branch_id, b.name, a.publish_start, a.publish_end,
       COALESCE(u.username,''), a.created_at, a.version
FROM announcements a
LEFT JOIN branches b ON b.id = a.branch_id
LEFT JOIN users u ON u.id = a.created_by
WHERE a.company_id=@c AND a.is_deleted=0
ORDER BY a.created_at DESC;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new AnnouncementRow(r.GetString(0), r.GetString(1), N(r, 2), r.GetString(3),
                    N(r, 4), N(r, 5), r.IsDBNull(6) ? null : r.GetInt64(6), r.IsDBNull(7) ? null : r.GetInt64(7),
                    r.GetString(8), r.GetInt64(9), r.GetInt64(10)));
        }

        // ŞUBE KAPSAMI: hedefli duyuru yalnız kapsamdakilere; şubesiz gizlenmez (yan kapı yok).
        var izinli = BranchAccess.Allowed(s);
        if (izinli is not null)
        {
            var set = izinli.ToHashSet(StringComparer.Ordinal);
            list = list.Where(a => a.BranchId is null || set.Contains(a.BranchId)).ToList();
        }

        // PK-J3: yönetici olmayan yalnız AKTİF duyuruları görür (pencere dışı kendiliğinden düşer).
        if (!includeInactive || !CanManage(s))
            list = list.Where(a => a.IsActive(nowMs)).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(a =>
                a.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (a.Body?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (a.BranchName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return list;
    }

    public string Create(SessionContext s, NewAnnouncement dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        Dogrula(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureBranch(s, conn, tx, dto.BranchId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO announcements(id, company_id, branch_id, title, body, importance, publish_start, publish_end,
    created_by, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@b,@t,@bd,@imp,@ps,@pe,@u,@now,@now,1,0);";
            Alanlar(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@u", s.UserId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "announcement", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"title\":{System.Text.Json.JsonSerializer.Serialize(dto.Title.Trim())}}}"), _clock);
        tx.Commit();
        return id;
    }

    public void Update(SessionContext s, string id, NewAnnouncement dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        Dogrula(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Getir(s, conn, tx, id, expectedVersion);
        EnsureBranch(s, conn, tx, dto.BranchId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            // version+1: senkron LWW + okundu imzası (BLD-01) — düzenleme herkes için yeniden okunmamış yapar.
            cmd.CommandText = "UPDATE announcements SET title=@t, body=@bd, importance=@imp, branch_id=@b, " +
                "publish_start=@ps, publish_end=@pe, updated_at=@now, version=version+1 " +
                "WHERE id=@id AND company_id=@c;";
            Alanlar(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "announcement", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Soft delete (fiziksel silme YOK) — Çöp Kutusu'ndan geri yüklenir.</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Getir(s, conn, tx, id, null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE announcements SET is_deleted=1, updated_at=@now, version=version+1 " +
                "WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "announcement", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<AnnouncementRow> rows, long nowMs)
        => new("Duyurular",
            new[] { "Başlık", "Önem", "Hedef", "Yayın", "Durum", "Oluşturan" },
            rows.Select(a => (IReadOnlyList<object?>)new object?[]
                { a.Title, a.ImportanceDisplay, a.BranchDisplay, a.PeriodDisplay, a.StatusDisplay(nowMs), a.CreatedByName }).ToList());

    // ── yardımcılar ──

    private static void Dogrula(NewAnnouncement dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new ArgumentException("Duyuru başlığı zorunlu.");
        if (dto.PublishStart is { } a && dto.PublishEnd is { } b && b < a)
            throw new ArgumentException("Yayın bitişi başlangıçtan önce olamaz.");
    }

    private static void Alanlar(DbCommand cmd, NewAnnouncement dto)
    {
        cmd.AddWithValue("@t", dto.Title.Trim());
        cmd.AddWithValue("@bd", string.IsNullOrWhiteSpace(dto.Body) ? DBNull.Value : dto.Body!.Trim());
        cmd.AddWithValue("@imp", dto.Importance == "important" ? "important" : "normal");
        cmd.AddWithValue("@b", string.IsNullOrWhiteSpace(dto.BranchId) ? DBNull.Value : (object)dto.BranchId!);
        // PLAN tarihleri (ADR-162 emsali: Takvim/İş Emri planları) — geri-tarih kapısına GİRMEZ.
        cmd.AddWithValue("@ps", (object?)dto.PublishStart ?? DBNull.Value);
        cmd.AddWithValue("@pe", (object?)dto.PublishEnd ?? DBNull.Value);
    }

    /// <summary>Tenant + kapsam + (verilmişse) düzenleme kilidi.</summary>
    private static void Getir(SessionContext s, DbConnection conn, DbTransaction tx, string id, long? expectedVersion)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT branch_id, version FROM announcements WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ArgumentException("Duyuru bulunamadı.");
        if (!r.IsDBNull(0)) BranchAccess.Require(s, r.GetString(0), "duyuru");
        if (expectedVersion is { } ev && r.GetInt64(1) != ev) throw new ConcurrencyException(ev, r.GetInt64(1));
    }

    private static void EnsureBranch(SessionContext s, DbConnection conn, DbTransaction tx, string? branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId)) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", branchId!);
        cmd.AddWithValue("@c", s.CompanyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ArgumentException("Şantiye/Saha bulunamadı veya bu firmaya ait değil.");
        BranchAccess.Require(s, branchId, "duyuru");
    }

    private static string? N(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
