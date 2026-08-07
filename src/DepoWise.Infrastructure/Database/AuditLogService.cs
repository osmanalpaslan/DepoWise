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
}
