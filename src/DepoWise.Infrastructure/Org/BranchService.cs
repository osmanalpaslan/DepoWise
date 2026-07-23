using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Org;

public sealed record BranchInfo(string Id, string CompanyId, string Name, string Kind, string? ParentId, long CreatedAt);

/// <summary>
/// Şube/şantiye yönetimi — tenant + permission + kapsam fail-closed. Liste yalnız kullanıcının
/// erişebildiği şubeleri döndürür (ScopeResolver). "branches" modül izinleri uygulanır.
/// </summary>
public sealed class BranchService
{
    private const string Module = "branches";
    private readonly IDbConnectionFactory _factory;
    private readonly ScopeResolver _scope;
    private readonly IClock _clock;

    public BranchService(IDbConnectionFactory factory, ScopeResolver scope, IClock? clock = null)
    {
        _factory = factory;
        _scope = scope;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext session, string name, string kind = "branch", string? parentId = null)
    {
        AccessControl.Require(session, Module, PermissionAction.Create);
        if (parentId is not null) _scope.EnsureBranchAllowed(session, parentId);

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO branches(id, company_id, parent_id, name, kind, created_at, updated_at, version, is_deleted) " +
                "VALUES($id,$c,$p,$n,$k,$now,$now,1,0);";
            cmd.AddWithValue("$id", id);
            cmd.AddWithValue("$c", session.CompanyId);
            cmd.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
            cmd.AddWithValue("$n", name);
            cmd.AddWithValue("$k", kind);
            cmd.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(session.CompanyId, "branch", id, AuditActions.Create, session.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Yalnız kullanıcının KAPSAMINDAKİ (tenant + scope) şubeler — seçim listeleri bunu kullanır.</summary>
    public IReadOnlyList<BranchInfo> ListInScope(SessionContext session)
    {
        AccessControl.Require(session, Module, PermissionAction.View);
        var allowed = _scope.AllowedBranchIds(session);
        if (allowed.Count == 0) return Array.Empty<BranchInfo>();

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, name, kind, parent_id, created_at FROM branches " +
            "WHERE company_id = $c AND is_deleted = 0;";
        cmd.AddWithValue("$c", session.CompanyId);
        var list = new List<BranchInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetString(0);
            if (!allowed.Contains(id)) continue; // kapsam dışına TAŞMAZ
            list.Add(new BranchInfo(id, r.GetString(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetInt64(5)));
        }
        return list;
    }

    public void SoftDelete(SessionContext session, string id)
        => SetDeleted(session, id, true, AuditActions.Delete);

    public void Restore(SessionContext session, string id)
        => SetDeleted(session, id, false, AuditActions.Restore);

    private void SetDeleted(SessionContext session, string id, bool deleted, string action)
    {
        AccessControl.Require(session, Module, deleted ? PermissionAction.Delete : PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        int affected;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE branches SET is_deleted = $d, version = version + 1, updated_at = $now " +
                "WHERE id = $id AND company_id = $c;";
            cmd.AddWithValue("$d", deleted ? 1 : 0);
            cmd.AddWithValue("$now", now);
            cmd.AddWithValue("$id", id);
            cmd.AddWithValue("$c", session.CompanyId);
            affected = cmd.ExecuteNonQuery();
        }
        if (affected > 0)
            AuditWriter.Write(conn, tx, new AuditEntry(session.CompanyId, "branch", id, action, session.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Kapsamlı kullanıcı için şube atar (admin işlemi).</summary>
    public void AssignScope(SessionContext admin, string userId, string branchId)
    {
        AccessControl.Require(admin, "permissions", PermissionAction.Edit);
        _scope.EnsureBranchAllowed(admin, branchId);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT OR IGNORE INTO user_scopes(user_id, company_id, branch_id) VALUES($u,$c,$b);";
        cmd.AddWithValue("$u", userId);
        cmd.AddWithValue("$c", admin.CompanyId);
        cmd.AddWithValue("$b", branchId);
        cmd.ExecuteNonQuery();
    }
}
