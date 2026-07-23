using DepoWise.Application.Common;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database;

public sealed record BranchRecord(string Id, string CompanyId, string Name, string Kind, long CreatedAt);

/// <summary>
/// Çekirdek şema üzerinde tenant/soft-delete/keyset/audit kurallarını uygulayan referans repo.
/// Diğer modül repo'ları aynı deseni izler. Tüm okumalar TenantSql ile tenant'a kapanır.
/// </summary>
public sealed class BranchRepository
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public BranchRepository(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Add(ITenantContext tenant, string name, string kind = "branch", string? userId = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO branches(id, company_id, parent_id, name, kind, created_at, updated_at, version, is_deleted)
VALUES(@id, @companyId, NULL, @name, @kind, @now, @now, 1, 0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@companyId", tenant.CompanyId);
            cmd.AddWithValue("@name", name);
            cmd.AddWithValue("@kind", kind);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(tenant.CompanyId, "branch", id, AuditActions.Create, userId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Soft-delete: kayıt fiziksel silinmez, is_deleted=1 + version artar + audit.</summary>
    public void SoftDelete(ITenantContext tenant, string id, string? userId = null)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        int affected;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            // Tenant'a kapalı: yalnız kendi firmasının kaydını siler.
            cmd.CommandText =
                "UPDATE branches SET is_deleted = 1, version = version + 1, updated_at = @now " +
                "WHERE id = @id AND " + TenantSql.ScopePredicate();
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@companyId", tenant.CompanyId);
            affected = cmd.ExecuteNonQuery();
        }
        if (affected > 0)
            AuditWriter.Write(conn, tx, new AuditEntry(tenant.CompanyId, "branch", id, AuditActions.Delete, userId), _clock);
        tx.Commit();
    }

    /// <summary>Tenant + soft-delete filtreli keyset sayfası.</summary>
    public PagedResult<BranchRecord> List(ITenantContext tenant, PageRequest page)
    {
        var limit = page.NormalizedLimit();
        var hasCursor = Cursor.TryDecode(page.Cursor, out var cursor);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, name, kind, created_at FROM branches " +
            "WHERE " + TenantSql.ScopePredicate() +
            (hasCursor ? " AND " + TenantSql.KeysetAfterPredicate : "") +
            " " + TenantSql.KeysetOrderBy + " LIMIT @limit;";
        cmd.AddWithValue("@companyId", tenant.CompanyId);
        cmd.AddWithValue("@limit", limit + 1); // +1 → daha fazla var mı
        if (hasCursor)
        {
            cmd.AddWithValue("@cursorCreatedAt", cursor.CreatedAt);
            cmd.AddWithValue("@cursorId", cursor.Id);
        }

        var items = new List<BranchRecord>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                items.Add(new BranchRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4)));
        }

        string? next = null;
        if (items.Count > limit)
        {
            var last = items[limit - 1];
            items.RemoveAt(items.Count - 1);
            next = new Cursor(last.CreatedAt, last.Id).Encode();
        }
        return PagedResult<BranchRecord>.Of(items, next);
    }

    public void EnsureCompany(string companyId, string name)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted) " +
            "VALUES(@id, @name, @now, @now, 1, 0) ON CONFLICT DO NOTHING;";
        cmd.AddWithValue("@id", companyId);
        cmd.AddWithValue("@name", name);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }
}
