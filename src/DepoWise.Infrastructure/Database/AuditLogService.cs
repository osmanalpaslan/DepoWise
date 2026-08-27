using DepoWise.Application.Security;

namespace DepoWise.Infrastructure.Database;

public sealed record AuditLogRow(long CreatedAt, string User, string EntityType, string EntityId, string Action)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string ActionText => Action switch
    {
        "create" => "Oluşturma", "update" => "Güncelleme", "delete" => "Silme",
        "restore" => "Geri Yükleme", "reverse" => "Ters Kayıt", _ => Action
    };
    public string UserText => string.IsNullOrWhiteSpace(User) ? "—" : User;
}

/// <summary>Sistem Logu (audit_logs) salt-okuma. Loglar hiçbir rol tarafından SİLİNEMEZ (yalnız okunur).</summary>
public sealed class AuditLogService
{
    private const string Module = "audit";
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
SELECT a.created_at, COALESCE(NULLIF(u.full_name,''), u.username, a.user_id, ''), a.entity_type, a.entity_id, a.action
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
        var list = new List<AuditLogRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditLogRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
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
SELECT a.created_at, COALESCE(NULLIF(u.full_name,''), u.username, a.user_id, ''), a.entity_type, a.entity_id, a.action
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

        var list = new List<AuditLogRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new AuditLogRow(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        return list;
    }
}
