using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Security;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Organization;

public sealed record BranchRow(string Id, string Name, string Kind, string? ParentId, string? ParentName, int UserCount,
    string? Code = null, bool HasPassword = false)
{
    public string KindDisplay => Kind == "site" ? "Şantiye" : "Şube";
    public string ParentDisplay => string.IsNullOrEmpty(ParentName) ? "—" : ParentName!;
    public string CodeDisplay => string.IsNullOrEmpty(Code) ? "—" : Code!;
    public string PasswordDisplay => HasPassword ? "•••• (tanımlı)" : "—";
}

public sealed record BranchUserRow(string Id, string Username, string? FullName, string Roles);

/// <summary>Şube tanımı — Password boş bırakılırsa (düzenlemede) mevcut şifre değişmez.</summary>
public sealed record NewBranch(string Name, string Kind = "branch", string? ParentId = null,
    string? Code = null, string? Password = null);

/// <summary>
/// Şube / Şantiye yönetimi (firma kapsamlı). CRUD + şubeye atanmış kullanıcılar + kullanıcıya şube atama.
/// Yetki: "branches" modülü (admin bypass). Tenant: yalnız oturumun firması.
/// </summary>
public sealed class BranchService
{
    private const string Module = "branches";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public BranchService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public IReadOnlyList<BranchRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT b.id, b.name, b.kind, b.parent_id, p.name,
       (SELECT COUNT(*) FROM users u WHERE u.branch_id = b.id AND u.is_deleted = 0),
       b.code, b.password_hash
FROM branches b
LEFT JOIN branches p ON p.id = b.parent_id
WHERE b.company_id = $c AND b.is_deleted = 0
ORDER BY b.name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        // #5: Şube kodu + şifresi yalnız Admin / Süper Admin'e görünür; Personel'de maskelenir.
        bool admin = AccessControl.IsAdmin(s);
        var list = new List<BranchRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BranchRow(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.GetInt32(5),
                admin ? (r.IsDBNull(6) ? null : r.GetString(6)) : null,
                admin && !r.IsDBNull(7) && !string.IsNullOrEmpty(r.GetString(7))));
        return list;
    }

    public string Create(SessionContext s, NewBranch dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Şube adı zorunlu.");
        // #5: Kod/şifre yalnız admin belirleyebilir; admin olmayan gönderse de yok sayılır.
        bool admin = AccessControl.IsAdmin(s);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        if (dto.ParentId is not null) EnsureBranchOwned(conn, tx, s.CompanyId, dto.ParentId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO branches(id, company_id, parent_id, name, kind, code, password_hash, created_at, updated_at, version, is_deleted) " +
                "VALUES($id,$c,$p,$n,$k,$code,$pw,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$p", (object?)dto.ParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$n", dto.Name.Trim());
            cmd.Parameters.AddWithValue("$k", dto.Kind == "site" ? "site" : "branch");
            cmd.Parameters.AddWithValue("$code", admin ? ((object?)Norm(dto.Code) ?? DBNull.Value) : DBNull.Value);
            cmd.Parameters.AddWithValue("$pw", admin && !string.IsNullOrWhiteSpace(dto.Password)
                ? DepoWise.Infrastructure.Security.PasswordHasher.Hash(dto.Password) : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "branch", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    public void Update(SessionContext s, string id, NewBranch dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Şube adı zorunlu.");
        if (dto.ParentId == id) throw new InvalidOperationException("Şube kendi üst şubesi olamaz.");
        bool admin = AccessControl.IsAdmin(s); // #5: kod/şifre yalnız admin değiştirir
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureBranchOwned(conn, tx, s.CompanyId, id);
        if (dto.ParentId is not null) EnsureBranchOwned(conn, tx, s.CompanyId, dto.ParentId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            // Admin: kod yazılır, şifre boşsa mevcut korunur (COALESCE). Admin DEĞİL: kod/şifre sütunlarına HİÇ dokunulmaz.
            cmd.CommandText = admin
                ? "UPDATE branches SET name=$n, kind=$k, parent_id=$p, code=$code, " +
                  "password_hash=COALESCE($pw, password_hash), version=version+1, updated_at=$now WHERE id=$id AND company_id=$c;"
                : "UPDATE branches SET name=$n, kind=$k, parent_id=$p, version=version+1, updated_at=$now WHERE id=$id AND company_id=$c;";
            cmd.Parameters.AddWithValue("$n", dto.Name.Trim());
            cmd.Parameters.AddWithValue("$k", dto.Kind == "site" ? "site" : "branch");
            cmd.Parameters.AddWithValue("$p", (object?)dto.ParentId ?? DBNull.Value);
            if (admin)
            {
                cmd.Parameters.AddWithValue("$code", (object?)Norm(dto.Code) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$pw", string.IsNullOrWhiteSpace(dto.Password)
                    ? DBNull.Value : DepoWise.Infrastructure.Security.PasswordHasher.Hash(dto.Password));
            }
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "branch", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureBranchOwned(conn, tx, s.CompanyId, id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            // Soft delete + atanmış kullanıcıların şubesini boşalt (dangling kalmasın)
            cmd.CommandText = @"
UPDATE branches SET is_deleted=1, updated_at=$now WHERE id=$id AND company_id=$c;
UPDATE users SET branch_id=NULL, updated_at=$now WHERE branch_id=$id AND company_id=$c;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "branch", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Şubeye atanmış kullanıcılar (şube detayında otomatik listelenir).</summary>
    public IReadOnlyList<BranchUserRow> GetUsers(SessionContext s, string branchId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT u.id, u.username, u.full_name,
  (SELECT GROUP_CONCAT(r.name, ', ') FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = u.id)
FROM users u
WHERE u.branch_id = $b AND u.company_id = $c AND u.is_deleted = 0
  AND ($all = 1 OR NOT EXISTS (
        SELECT 1 FROM user_roles ur JOIN roles r ON r.id = ur.role_id
        WHERE ur.user_id = u.id AND r.role_key = $sa))
ORDER BY u.username;";
        cmd.Parameters.AddWithValue("$b", branchId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$all", s.IsSuperAdmin ? 1 : 0);
        cmd.Parameters.AddWithValue("$sa", RoleKeys.SuperAdmin);
        var list = new List<BranchUserRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BranchUserRow(r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3)));
        return list;
    }

    /// <summary>Kullanıcıya şube atar/kaldırır (users modülü yetkisi gerekir).</summary>
    public void AssignUser(SessionContext s, string userId, string? branchId)
    {
        AccessControl.Require(s, "users", PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        if (branchId is not null) EnsureBranchOwned(conn, tx, s.CompanyId, branchId);
        // #8: Firma admini başka admin/süperadmine şube atayamaz (yalnız süper admin veya kendisi).
        if (!s.IsSuperAdmin && !string.Equals(userId, s.UserId, StringComparison.Ordinal))
        {
            using var chk = conn.CreateCommand();
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id " +
                "WHERE ur.user_id=$u AND r.role_key IN ($sa,$ca);";
            chk.Parameters.AddWithValue("$u", userId);
            chk.Parameters.AddWithValue("$sa", RoleKeys.SuperAdmin);
            chk.Parameters.AddWithValue("$ca", RoleKeys.CompanyAdmin);
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0)
                throw new ForbiddenException("Başka bir admin kullanıcıyı yalnız süper admin düzenleyebilir.");
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE users SET branch_id=$b, updated_at=$now WHERE id=$u AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$b", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Kullanıcı bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "user", userId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    private static string? Norm(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Login'de şube şifresi doğrulaması. Şubenin şifresi tanımlı DEĞİLSE true (şifre istenmiyor).
    /// Tanımlıysa bcrypt ile karşılaştırır. Yetki gerektirmez (login öncesi çağrılabilir).</summary>
    public bool VerifyBranchPassword(string companyId, string branchId, string? password)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM branches WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", branchId);
        cmd.Parameters.AddWithValue("$c", companyId);
        var hash = cmd.ExecuteScalar() as string;
        if (string.IsNullOrEmpty(hash)) return true; // şifre tanımlı değil → serbest
        return !string.IsNullOrEmpty(password) && DepoWise.Infrastructure.Security.PasswordHasher.Verify(password, hash);
    }

    /// <summary>Firmanın şubeleri (login şube seçimi için; yetki gerektirmez). id + ad + kod + şifre-var-mı.</summary>
    public IReadOnlyList<BranchRow> ListForLogin(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, kind, code, (password_hash IS NOT NULL AND password_hash<>'') " +
            "FROM branches WHERE company_id=$c AND is_deleted=0 ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", companyId);
        var list = new List<BranchRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BranchRow(r.GetString(0), r.GetString(1), r.GetString(2), null, null, 0,
                r.IsDBNull(3) ? null : r.GetString(3), r.GetInt64(4) == 1));
        return list;
    }

    private static void EnsureBranchOwned(SqliteConnection conn, SqliteTransaction tx, string companyId, string branchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", branchId);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException("Şube bulunamadı veya başka firmaya ait.");
    }
}
