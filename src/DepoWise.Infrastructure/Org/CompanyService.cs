using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Org;

public sealed record CompanyRecord(string Id, string Name, long CreatedAt);

/// <summary>
/// Firma yönetimi — YALNIZ Süper Admin (analiz §4). Normal admin başka firmayı göremez/oluşturamaz.
/// </summary>
public sealed class CompanyService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CompanyService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext session, string name)
    {
        if (!session.IsSuperAdmin)
            throw new ForbiddenException("Firma oluşturma yalnız Süper Admin yetkisindedir.");

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted) " +
                "VALUES($id,$n,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(id, "company", id, AuditActions.Create, session.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Süper Admin → tüm firmalar; diğer admin → yalnız kendi firması.</summary>
    public IReadOnlyList<CompanyRecord> List(SessionContext session)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        if (session.IsSuperAdmin)
        {
            cmd.CommandText = "SELECT id, name, created_at FROM companies WHERE is_deleted = 0 ORDER BY name;";
        }
        else
        {
            cmd.CommandText = "SELECT id, name, created_at FROM companies WHERE id = $c AND is_deleted = 0;";
            cmd.Parameters.AddWithValue("$c", session.CompanyId);
        }
        var list = new List<CompanyRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new CompanyRecord(r.GetString(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    /// <summary>Belirli bir firmaya erişimi fail-closed doğrular (süper admin hariç yalnız kendi firması).</summary>
    public void EnsureAccess(SessionContext session, string companyId)
        => TenantAccessGuard.EnsureOwnership(session, companyId);
}
