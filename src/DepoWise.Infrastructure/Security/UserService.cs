using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Security;

public sealed record NewUser(
    string Username,
    string Password,
    string? FullName,
    IReadOnlyList<string> RoleKeys,
    string? CompanyId = null,
    IReadOnlyList<ModulePermission>? Permissions = null,
    string? BranchId = null);

public sealed record UserRow(string Id, string Username, string? FullName, bool IsActive, string Roles, string? BranchId, string? BranchName)
{
    public string BranchDisplay => string.IsNullOrEmpty(BranchName) ? "—" : BranchName!;
    public string StatusText => IsActive ? "Aktif" : "Pasif";
}

/// <summary>
/// Kullanıcı oluşturma — yetki yükseltme + tenant kuralları fail-closed (analiz §4/§9).
/// Tek transaction: user + user_roles + user_permissions + audit.
/// </summary>
public sealed class UserService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public UserService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Kullanıcı listesi (tenant: Süper Admin tümünü, diğerleri kendi firmasını görür).</summary>
    public IReadOnlyList<UserRow> ListUsers(SessionContext actor)
    {
        AccessControl.Require(actor, "users", PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT u.id, u.username, u.full_name, u.is_active,
  (SELECT GROUP_CONCAT(r.name, ', ') FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = u.id),
  u.branch_id, b.name
FROM users u
LEFT JOIN branches b ON b.id = u.branch_id
WHERE u.is_deleted = 0 AND ($all = 1 OR u.company_id = $c)
ORDER BY u.username;";
        cmd.Parameters.AddWithValue("$all", actor.IsSuperAdmin ? 1 : 0);
        cmd.Parameters.AddWithValue("$c", actor.CompanyId);
        var list = new List<UserRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UserRow(r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt64(3) == 1,
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6)));
        return list;
    }

    public string CreateUser(SessionContext actor, NewUser dto)
    {
        // 1) Yetki: users modülünde 'create' (admin bypass)
        AccessControl.Require(actor, "users", PermissionAction.Create);
        // 2) Tenant: Firma Admini kendi firmasına kilitli; Süper Admin firma seçebilir
        var companyId = RoleAssignmentGuard.ResolveTargetCompany(actor, dto.CompanyId);
        // 3) Yetki yükseltme: admin/süper-admin rolü atama koruması
        RoleAssignmentGuard.EnsureCanAssign(actor, dto.RoleKeys);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var userId = Guid.NewGuid().ToString("N");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        Insert(conn, tx,
            "INSERT INTO users(id, company_id, username, password_hash, full_name, branch_id, is_active, created_at, updated_at, version, is_deleted) " +
            "VALUES($id,$c,$u,$h,$f,$b,1,$now,$now,1,0);",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$id", userId);
                cmd.Parameters.AddWithValue("$c", companyId);
                cmd.Parameters.AddWithValue("$u", dto.Username);
                cmd.Parameters.AddWithValue("$h", PasswordHasher.Hash(dto.Password));
                cmd.Parameters.AddWithValue("$f", (object?)dto.FullName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$b", (object?)dto.BranchId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$now", now);
            });

        foreach (var roleKey in dto.RoleKeys.Distinct())
        {
            var roleId = ResolveRoleId(conn, tx, companyId, roleKey)
                ?? throw new InvalidOperationException($"Rol bulunamadı: {roleKey}");
            Insert(conn, tx,
                "INSERT OR IGNORE INTO user_roles(user_id, role_id) VALUES($u,$r);",
                cmd => { cmd.Parameters.AddWithValue("$u", userId); cmd.Parameters.AddWithValue("$r", roleId); });
        }

        foreach (var p in dto.Permissions ?? Array.Empty<ModulePermission>())
        {
            Insert(conn, tx,
                "INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version) " +
                "VALUES($id,$c,$u,$m,$v,$cr,$e,$d,$now,$now,1);",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    cmd.Parameters.AddWithValue("$c", companyId);
                    cmd.Parameters.AddWithValue("$u", userId);
                    cmd.Parameters.AddWithValue("$m", p.ModuleKey);
                    cmd.Parameters.AddWithValue("$v", p.CanView ? 1 : 0);
                    cmd.Parameters.AddWithValue("$cr", p.CanCreate ? 1 : 0);
                    cmd.Parameters.AddWithValue("$e", p.CanEdit ? 1 : 0);
                    cmd.Parameters.AddWithValue("$d", p.CanDelete ? 1 : 0);
                    cmd.Parameters.AddWithValue("$now", now);
                });
        }

        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Create, actor.UserId), _clock);
        tx.Commit();
        return userId;
    }

    /// <summary>İlk kurulum: sistem düzeyinde Süper Admin/Firma Admini oluşturur (actor yok).</summary>
    public string EnsureInitialAdmin(string companyId, string username, string password, string roleKey)
    {
        TenantGuard.Require(companyId);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var userId = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Firma kaydı yoksa oluştur
        Insert(conn, tx,
            "INSERT OR IGNORE INTO companies(id, name, created_at, updated_at, version, is_deleted) VALUES($id,$n,$now,$now,1,0);",
            cmd => { cmd.Parameters.AddWithValue("$id", companyId); cmd.Parameters.AddWithValue("$n", companyId); cmd.Parameters.AddWithValue("$now", now); });

        Insert(conn, tx,
            "INSERT INTO users(id, company_id, username, password_hash, full_name, is_active, created_at, updated_at, version, is_deleted) " +
            "VALUES($id,$c,$u,$h,$f,1,$now,$now,1,0);",
            cmd =>
            {
                cmd.Parameters.AddWithValue("$id", userId);
                cmd.Parameters.AddWithValue("$c", companyId);
                cmd.Parameters.AddWithValue("$u", username);
                cmd.Parameters.AddWithValue("$h", PasswordHasher.Hash(password));
                cmd.Parameters.AddWithValue("$f", DBNull.Value);
                cmd.Parameters.AddWithValue("$now", now);
            });

        var roleId = ResolveRoleId(conn, tx, companyId, roleKey)
            ?? throw new InvalidOperationException($"Rol bulunamadı: {roleKey}");
        Insert(conn, tx,
            "INSERT OR IGNORE INTO user_roles(user_id, role_id) VALUES($u,$r);",
            cmd => { cmd.Parameters.AddWithValue("$u", userId); cmd.Parameters.AddWithValue("$r", roleId); });

        tx.Commit();
        return userId;
    }

    private static string? ResolveRoleId(SqliteConnection conn, SqliteTransaction tx, string companyId, string roleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Önce firmaya özel, yoksa sistem rolü (company_id IS NULL)
        cmd.CommandText =
            "SELECT id FROM roles WHERE role_key = $k AND (company_id = $c OR company_id IS NULL) AND is_deleted = 0 " +
            "ORDER BY company_id IS NULL LIMIT 1;";
        cmd.Parameters.AddWithValue("$k", roleKey);
        cmd.Parameters.AddWithValue("$c", companyId);
        return cmd.ExecuteScalar() as string;
    }

    private static void Insert(SqliteConnection conn, SqliteTransaction tx, string sql, Action<SqliteCommand> bind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind(cmd);
        cmd.ExecuteNonQuery();
    }
}
