using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;

namespace DepoWise.Infrastructure.Database;

public sealed record AuditLogRow(long CreatedAt, string User, string EntityType, string EntityId, string Action,
    // ⚠️ Ham anlık görüntüler API yanıtına KOYULMAZ: istemcinin ihtiyacı olan şey hazır fark listesidir
    // (Changes). Ham satırı göndermek, alan koruması olan sütunları da tarayıcıya taşırdı.
    [property: System.Text.Json.Serialization.JsonIgnore] string? BeforeJson = null,
    [property: System.Text.Json.Serialization.JsonIgnore] string? AfterJson = null)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");

    /// <summary>⭐ FAZ 4.3 — GÜN BAŞLIĞI: "bugün şunu, ertesi gün bunu yapmış" gruplaması buna göre yapılır.</summary>
    public string DayText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy");

    /// <summary>⭐ FAZ 4.3 — İŞLEM SAATİ (gün başlığının altında yalnız saat gösterilir).</summary>
    public string TimeText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("HH:mm:ss");

    public string ActionText => Action switch
    {
        "create" => "Oluşturma", "update" => "Güncelleme", "delete" => "Silme",
        "restore" => "Geri Yükleme", "reverse" => "Ters Kayıt", _ => Action
    };
    public string UserText => string.IsNullOrWhiteSpace(User) ? "—" : User;

    /// <summary>⭐ FAZ 4.3 — Varlık tipinin Türkçe adı ("vehicle" değil "Araç").</summary>
    public string EntityLabel => AuditFields.TipEtiket(EntityType);

    /// <summary>⭐ FAZ 4 FINAL QA (2026-09-06): kimlik → ad sözlüğü. Bağlantı alanlarında (şube,
    /// personel, malzeme…) 32 haneli kimlik yerine okunur ad gösterilir.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, string>? Names { get; init; }

    private IReadOnlyList<AuditChange>? _changes;
    /// <summary>⭐ FAZ 4.3 — ALAN BAZLI DEĞİŞİKLİKLER: "Sayaç: 10.000 → 155.000".</summary>
    public IReadOnlyList<AuditChange> Changes => _changes ??= AuditDiff.Hesapla(BeforeJson, AfterJson, Names);

    /// <summary>Liste satırında tek satırlık özet. Öncesi bilinmiyorsa boş kalır (uydurma yapılmaz).</summary>
    public string ChangeSummary => BeforeJson is null && Action != "create" ? "" : AuditDiff.Ozet(Changes);

    /// <summary>Öncesi bilinmiyor mu (sayfanın en eski satırı) — arayüz bunu açıkça yazar.</summary>
    public bool BeforeUnknown => BeforeJson is null && Action != "create";
}

/// <summary>Sistem Logu (audit_logs) salt-okuma. Loglar hiçbir rol tarafından SİLİNEMEZ (yalnız okunur).</summary>
public sealed class AuditLogService
{
    private const string Module = "audit";

    /// <summary>Sayfa içinde öncesi bulunamayan satırlar için yapılacak EK sorgu sayısı üst sınırı.
    /// Sınırsız bırakılsaydı 5000 satırlık bir log ekranı 5000 ek sorgu açabilirdi.</summary>
    private const int EkOncekiSorguSiniri = 60;

    private readonly IDbConnectionFactory _factory;
    public AuditLogService(IDbConnectionFactory factory) => _factory = factory;

    /// <summary>Sistem Logu filtreleri (madde 4, kullanıcı isteği 2026-08-06): Tarih Aralığı (fromMs/toMs, Unix
    /// ms, dahil) + kayıt sayısı (limit). Performans için limit 1-5000 arasına sıkıştırılır (StockService.
    /// SearchMovements ile AYNI desen) — filtre yokken de varsayılan 300 ile sınırsız sorgu asla çalışmaz.</summary>
    public IReadOnlyList<AuditLogRow> List(SessionContext s, long? fromMs = null, long? toMs = null, int limit = 300)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (limit < 1) limit = 1; if (limit > 5000) limit = 5000;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sb = new System.Text.StringBuilder(@"
SELECT a.created_at, COALESCE(NULLIF(u.full_name,''), u.username, a.user_id, ''), a.entity_type, a.entity_id, a.action,
       a.before_json, a.after_json
FROM audit_logs a LEFT JOIN users u ON u.id = a.user_id
WHERE a.company_id = @c");
        if (fromMs is not null) sb.Append(" AND a.created_at >= @from");
        if (toMs is not null) sb.Append(" AND a.created_at <= @to");
        sb.Append(" ORDER BY a.created_at DESC LIMIT @lim;");
        cmd.CommandText = sb.ToString();
        cmd.AddWithValue("@c", s.CompanyId);
        if (fromMs is not null) cmd.AddWithValue("@from", fromMs.Value);
        if (toMs is not null) cmd.AddWithValue("@to", toMs.Value);
        cmd.AddWithValue("@lim", limit);
        var list = Oku(cmd);
        return OncesiniBagla(conn, s.CompanyId, list);
    }

    /// <summary>
    /// ⭐ LST-01 (2026-09-07) — AYNI FİLTREDEKİ GERÇEK TOPLAM.
    ///
    /// <see cref="List"/> en fazla <c>limit</c> satır döner. Ekran, dönen satır sayısını "toplam"
    /// diye yazarsa 10.000 kayıtlı bir firmada kullanıcı "300 kayıt var" sanır ve geri kalanı
    /// SESSİZCE kaybolur. Bu yüzden ekran gerçek toplamı buradan sorar ve tavana takıldığını
    /// kullanıcıya açıkça söyler. Sayım, listeyle AYNI koşulu kullanır — aksi hâlde iki sayı
    /// birbirini tutmaz.
    /// </summary>
    public int Sayim(SessionContext s, long? fromMs = null, long? toMs = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sb = new System.Text.StringBuilder("SELECT COUNT(*) FROM audit_logs a WHERE a.company_id = @c");
        if (fromMs is not null) sb.Append(" AND a.created_at >= @from");
        if (toMs is not null) sb.Append(" AND a.created_at <= @to");
        cmd.CommandText = sb.Append(';').ToString();
        cmd.AddWithValue("@c", s.CompanyId);
        if (fromMs is not null) cmd.AddWithValue("@from", fromMs.Value);
        if (toMs is not null) cmd.AddWithValue("@to", toMs.Value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// ⭐ LOG-01 (kullanıcı isteği 2026-08-27) — EKRANA ÖZEL KAYIT GEÇMİŞİ.
    ///
    /// Her ekranın kendi log düğmesi buradan beslenir ve YALNIZ o ekranın varlık tiplerini gösterir
    /// (<see cref="ScreenAuditMap"/>). Sistem Logu ekranından farkı budur: orası firmanın tamamıdır.
    ///
    /// <b>İKİ kapı birden uygulanır (deny-by-default):</b>
    /// <list type="number">
    ///   <item><see cref="SpecialButtons.ScreenLog"/> — kayıt geçmişini görme yetkisi.</item>
    ///   <item>Ekranın KENDİ modülünde <c>View</c> — göremediğiniz ekranın geçmişini de göremezsiniz.
    ///   Aksi halde log düğmesi, yetki sisteminde bir yan kapı olurdu.</item>
    /// </list>
    ///
    /// <b>Bilinmeyen/eşlemesiz modül:</b> boş liste döner — TÜM loga düşmez. Bir ekranın düğmesinin
    /// başka ekranın verisini açması, sessiz bir yetki sızıntısı olurdu.
    ///
    /// Gösterilen zaman <c>created_at</c>'tir: kaydın sisteme GERÇEKTEN girildiği an. İşlem tarihi
    /// (iş günü) geri/ileri alınmış olsa bile burası gerçek saati gösterir (TRH-01 ilkesi).
    /// </summary>
    public IReadOnlyList<AuditLogRow> ForModule(SessionContext s, string moduleKey, long? fromMs = null,
        long? toMs = null, int limit = 200)
    {
        AccessControl.RequireButton(s, SpecialButtons.ScreenLog);
        AccessControl.Require(s, moduleKey, PermissionAction.View);

        var tipler = ScreenAuditMap.EntityTypes(moduleKey);
        if (tipler.Count == 0) return Array.Empty<AuditLogRow>();

        if (limit < 1) limit = 1; if (limit > 2000) limit = 2000;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();

        var yerTutucular = string.Join(",", tipler.Select((_, i) => "@e" + i));
        var sb = new System.Text.StringBuilder(@"
SELECT a.created_at, COALESCE(NULLIF(u.full_name,''), u.username, a.user_id, ''), a.entity_type, a.entity_id, a.action,
       a.before_json, a.after_json
FROM audit_logs a LEFT JOIN users u ON u.id = a.user_id
WHERE a.company_id = @c AND a.entity_type IN (" + yerTutucular + ")");
        if (fromMs is not null) sb.Append(" AND a.created_at >= @from");
        if (toMs is not null) sb.Append(" AND a.created_at <= @to");
        sb.Append(" ORDER BY a.created_at DESC LIMIT @lim;");

        cmd.CommandText = sb.ToString();
        cmd.AddWithValue("@c", s.CompanyId);
        for (int i = 0; i < tipler.Count; i++) cmd.AddWithValue("@e" + i, tipler[i]);
        if (fromMs is not null) cmd.AddWithValue("@from", fromMs.Value);
        if (toMs is not null) cmd.AddWithValue("@to", toMs.Value);
        cmd.AddWithValue("@lim", limit);

        var list = Oku(cmd);
        return OncesiniBagla(conn, s.CompanyId, list);
    }

    /// <summary>
    /// ⭐ FAZ 4.3 (kullanıcı isteği 2026-09-06) — <b>TEK KAYDIN KENDİ LOG EKRANI.</b>
    ///
    /// Kullanıcı: <i>"ekranlarla beraber her kaydın kendine ait bir log ekranı olmalı"</i>. Bu uç,
    /// SEÇİLİ kaydın tüm geçmişini döner; satırlar en yeniden eskiye sıralıdır ve her satırın
    /// "öncesi", bir önceki (daha eski) satırın anlık görüntüsüdür → alan bazlı fark KESİNDİR
    /// (sayfa sınırından etkilenmez, çünkü kaydın tüm geçmişi okunur).
    ///
    /// <b>Yetki — iki kapı (deny-by-default):</b> <see cref="SpecialButtons.ScreenLog"/> düğme yetkisi
    /// VE kaydın ait olduğu ekranlardan en az birinde <c>View</c>. Göremediğiniz ekranın kaydının
    /// geçmişini de göremezsiniz; aksi hâlde bu ekran yetki sisteminin etrafından dolaşan bir yan
    /// kapı olurdu. Eşlemesi olmayan tipte <see cref="ForbiddenException"/> atılır (sessizce açılmaz).
    /// </summary>
    public IReadOnlyList<AuditLogRow> ForEntity(SessionContext s, string entityType, string entityId, int limit = 500)
    {
        AccessControl.RequireButton(s, SpecialButtons.ScreenLog);

        var moduller = ScreenAuditMap.ModulesForEntity(entityType);
        if (moduller.Count == 0)
            throw new ForbiddenException("Bu kayıt türü için kayıt geçmişi tanımlı değil.");
        if (!moduller.Any(m => AccessControl.Can(s, m, PermissionAction.View)))
            throw new ForbiddenException("Bu kaydın geçmişini görme yetkiniz yok.");

        if (string.IsNullOrWhiteSpace(entityId)) return Array.Empty<AuditLogRow>();
        if (limit < 1) limit = 1; if (limit > 2000) limit = 2000;

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT a.created_at, COALESCE(NULLIF(u.full_name,''), u.username, a.user_id, ''), a.entity_type, a.entity_id, a.action,
       a.before_json, a.after_json
FROM audit_logs a LEFT JOIN users u ON u.id = a.user_id
WHERE a.company_id = @c AND a.entity_type = @et AND a.entity_id = @eid
ORDER BY a.created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@et", entityType);
        cmd.AddWithValue("@eid", entityId);
        cmd.AddWithValue("@lim", limit);

        var list = Oku(cmd);
        return OncesiniBagla(conn, s.CompanyId, list);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static List<AuditLogRow> Oku(DbCommand cmd)
    {
        var list = new List<AuditLogRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditLogRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    /// <summary>
    /// ⭐ FAZ 4.3 — "ÖNCEKİ HÂL"İN TÜRETİLMESİ.
    ///
    /// Kayıtların önceki hâli ayrıca saklanmaz; <b>bir önceki log satırının anlık görüntüsü</b> zaten
    /// önceki hâldir. Bu yüzden liste eskiden yeniye taranır ve her satırın "öncesi", aynı kaydın bir
    /// önceki satırının "sonrası" olarak bağlanır. Böylece ek depolama olmadan alan bazlı fark çıkar.
    ///
    /// Sayfanın en eski satırının öncesi sayfada olmayabilir; bu satırlar için kayıt başına TEK bir
    /// hedefli sorgu yapılır (<see cref="EkOncekiSorguSiniri"/> ile sınırlı). Sınır aşılırsa o satır
    /// "öncesi bilinmiyor" olarak işaretlenir — <b>uydurma fark üretilmez.</b>
    /// </summary>
    private static IReadOnlyList<AuditLogRow> OncesiniBagla(DbConnection conn, string companyId, List<AuditLogRow> descRows)
    {
        if (descRows.Count == 0) return descRows;

        var sonGoruntu = new Dictionary<string, string?>(StringComparer.Ordinal);
        int ekSorgu = 0;

        // Eskiden yeniye: her kaydın bir önceki anlık görüntüsü elimizde birikir.
        for (int i = descRows.Count - 1; i >= 0; i--)
        {
            var row = descRows[i];
            var anahtar = row.EntityType + "|" + row.EntityId;

            if (row.BeforeJson is null)
            {
                if (sonGoruntu.TryGetValue(anahtar, out var onceki))
                {
                    descRows[i] = row with { BeforeJson = onceki };
                }
                else if (row.Action != "create" && ekSorgu < EkOncekiSorguSiniri)
                {
                    ekSorgu++;
                    var onceki2 = OncekiGoruntu(conn, companyId, row.EntityType, row.EntityId, row.CreatedAt);
                    if (onceki2 is not null) descRows[i] = row with { BeforeJson = onceki2 };
                }
            }

            if (descRows[i].AfterJson is not null) sonGoruntu[anahtar] = descRows[i].AfterJson;
        }
        return AdlariCoz(conn, companyId, descRows);
    }

    /// <summary>
    /// ⭐ FAZ 4 FINAL QA (2026-09-06) — <b>KİMLİK YERİNE AD.</b>
    ///
    /// Kayıt logunda <c>Şube: — → 0a795b41…</c> yazıyordu; kullanıcının isteği "hangi alanda NEYİ
    /// güncelledi ise görebilmeliyim" idi ve 32 haneli bir kimlik bunu karşılamıyordu.
    ///
    /// <b>Maliyet.</b> Satır başına sorgu YAPILMAZ: tüm sayfadaki bağlantı kimlikleri toplanır ve
    /// <b>tablo başına TEK</b> sorgu çalışır (en fazla ~30 tablo). Ad bulunamazsa ham değer kalır —
    /// uydurma ad yazılmaz. Sorgu firma sınırıyla çalışır: başka firmanın adı sızmaz.
    /// </summary>
    private static IReadOnlyList<AuditLogRow> AdlariCoz(DbConnection conn, string companyId, List<AuditLogRow> rows)
    {
        // 1) Sayfadaki tüm bağlantı kimliklerini tabloya göre topla.
        var tabloyaGore = new Dictionary<(string Tablo, string AdSutunu), HashSet<string>>();
        foreach (var r in rows)
            foreach (var json in new[] { r.BeforeJson, r.AfterJson })
            {
                if (string.IsNullOrWhiteSpace(json)) continue;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(json!);
                    if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                    foreach (var p in doc.RootElement.EnumerateObject())
                    {
                        if (p.Value.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                        var deger = p.Value.GetString();
                        if (string.IsNullOrWhiteSpace(deger)) continue;
                        if (AuditFields.BagliTablo(p.Name) is not { } hedef) continue;
                        if (!tabloyaGore.TryGetValue(hedef, out var kume))
                            tabloyaGore[hedef] = kume = new HashSet<string>(StringComparer.Ordinal);
                        kume.Add(deger!);
                    }
                }
                catch (System.Text.Json.JsonException) { }
            }
        if (tabloyaGore.Count == 0) return rows;

        // 2) Tablo başına TEK sorgu.
        var adlar = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ((tablo, adSutunu), kimlikler) in tabloyaGore)
        {
            if (kimlikler.Count == 0) continue;
            try
            {
                using var cmd = conn.CreateCommand();
                var liste = kimlikler.ToList();
                var yerTutucular = string.Join(",", liste.Select((_, i) => "@k" + i));
                // Tablo/sütun adları BEYAZ LİSTEDEN (AuditFields.BagliTablo) gelir; kimlikler parametreyle bağlanır.
                cmd.CommandText = $"SELECT id, {adSutunu} FROM {tablo} WHERE id IN ({yerTutucular}) AND company_id = @co;";
                for (int i = 0; i < liste.Count; i++) cmd.AddWithValue("@k" + i, liste[i]);
                cmd.AddWithValue("@co", companyId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (!r.IsDBNull(1) && r.GetString(1).Length > 0) adlar[r.GetString(0)] = r.GetString(1);
            }
            catch (DbException) { /* ad çözülemedi → ham kimlik gösterilir; log yine açılır */ }
        }
        if (adlar.Count == 0) return rows;

        for (int i = 0; i < rows.Count; i++) rows[i] = rows[i] with { Names = adlar };
        return rows;
    }

    /// <summary>Bu kaydın, verilen andan ÖNCEKİ en son anlık görüntüsü (sayfa dışında kalmış olabilir).</summary>
    private static string? OncekiGoruntu(DbConnection conn, string companyId, string entityType, string entityId, long beforeMs)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT after_json FROM audit_logs
WHERE company_id = @c AND entity_type = @et AND entity_id = @eid AND created_at < @ts AND after_json IS NOT NULL
ORDER BY created_at DESC LIMIT 1;";
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@et", entityType);
            cmd.AddWithValue("@eid", entityId);
            cmd.AddWithValue("@ts", beforeMs);
            var v = cmd.ExecuteScalar();
            return v is string s && s.Length > 0 ? s : null;
        }
        catch (DbException) { return null; }
    }
}
