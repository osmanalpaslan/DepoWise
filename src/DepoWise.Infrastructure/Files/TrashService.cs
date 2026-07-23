using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Files;

public sealed record TrashItem(string Table, string Id, string Label, long UpdatedAt);

/// <summary>
/// Çöp Kutusu — master-data soft-delete kayıtlarını listeler/geri yükler. Erişim admin/özel buton +
/// YENİDEN DOĞRULAMA (reauthenticated) ister (analiz §6.17/§9). Operasyonel kayıtlar burada DEĞİL
/// (onlar iptal/ters kayıt ile düzeltilir).
/// </summary>
public sealed class TrashService
{
    // Geri yüklenebilir master-data tabloları (is_deleted + label kolonu ile).
    private static readonly Dictionary<string, string> Tables = new(StringComparer.Ordinal)
    {
        ["materials"] = "name", ["vehicles"] = "internal_code", ["personnel"] = "full_name",
        ["branches"] = "name", ["suppliers"] = "name", ["brands"] = "name", ["units"] = "name",
        ["material_categories"] = "name", ["vehicle_templates"] = "name",
        ["vehicle_types"] = "name", ["vehicle_categories"] = "name", ["vehicle_models"] = "name",
        ["maintenance_definitions"] = "name",
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public TrashService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public IReadOnlyList<TrashItem> List(SessionContext s, bool reauthenticated)
    {
        RequireAccess(s, reauthenticated);
        var items = new List<TrashItem>();
        using var conn = _factory.Create();
        foreach (var (table, label) in Tables)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT id, {label}, updated_at FROM {table} WHERE company_id=@c AND is_deleted=1;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                items.Add(new TrashItem(table, r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetInt64(2)));
        }
        return items;
    }

    public void Restore(SessionContext s, string table, string id, bool reauthenticated)
    {
        RequireAccess(s, reauthenticated);
        if (!Tables.ContainsKey(table)) throw new ArgumentException($"Geri yüklenemez tablo: {table}");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        int affected;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET is_deleted=0, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            affected = cmd.ExecuteNonQuery();
        }
        if (affected == 0) throw new ForbiddenException("Kayıt bulunamadı veya başka firmaya ait.");
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Restore, s.UserId), _clock);
        tx.Commit();
    }

    private static void RequireAccess(SessionContext s, bool reauthenticated)
    {
        AccessControl.RequireButton(s, SpecialButtons.RestoreTrash); // admin bypass veya açık yetki
        if (!reauthenticated)
            throw new ForbiddenException("Çöp Kutusu için yeniden kimlik doğrulama gerekli.");
    }
}
