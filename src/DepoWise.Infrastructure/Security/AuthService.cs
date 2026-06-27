using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Security;

public sealed record LoginResult(
    bool Success,
    bool Locked,
    int SecondsRemaining,
    SessionContext? Session,
    string? Error = null);

/// <summary>
/// Kimlik doğrulama + brute-force kilidi. 5 ardışık hatalı denemeden sonra 5 dk kilit.
/// Başarılı login kilidi sıfırlar. company_id login isteğinden bağımsız; oturum onunla kurulur.
/// </summary>
public sealed class AuthService
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockWindow = TimeSpan.FromMinutes(5);

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public AuthService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public LoginResult Login(string companyId, string username, string password)
    {
        TenantGuard.Require(companyId);
        var now = _clock.UtcNow;
        using var conn = _factory.Create();

        // 1) Kilit kontrolü (consecutive failures since last success)
        var (failCount, lastFailMs) = ConsecutiveFailures(conn, companyId, username);
        if (failCount >= MaxFailures && lastFailMs is long lf)
        {
            var lockUntil = DateTimeOffset.FromUnixTimeMilliseconds(lf).Add(LockWindow);
            if (now < lockUntil)
                return new LoginResult(false, true, (int)Math.Ceiling((lockUntil - now).TotalSeconds), null);
        }

        // 2) Kullanıcı + parola
        var user = FindUser(conn, companyId, username);
        bool ok = user is not null && PasswordHasher.Verify(password, user.Value.PasswordHash);

        RecordAttempt(conn, companyId, username, ok);

        if (!ok)
            return new LoginResult(false, false, 0, null, "Kullanıcı adı veya parola hatalı.");

        // 3) Oturum + yetkiler
        var roles = LoadRoleKeys(conn, user!.Value.Id);
        var perms = LoadPermissions(conn, user.Value.Id);
        CreateSession(conn, user.Value.Id, companyId, now);
        var session = new SessionContext(user.Value.Id, companyId, roles, perms);
        return new LoginResult(true, false, 0, session);
    }

    /// <summary>
    /// Parola olmadan oturum kurar (yalnız "Beni Hatırla" token doğrulaması SONRASI çağrılır).
    /// Kullanıcı aktif değilse/yoksa null döner. Roller + yetkiler yüklenir.
    /// </summary>
    public SessionContext? CreateSessionForUser(string companyId, string userId)
    {
        TenantGuard.Require(companyId);
        using var conn = _factory.Create();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM users WHERE id=$id AND company_id=$c AND is_active=1 AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$id", userId);
            cmd.Parameters.AddWithValue("$c", companyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) return null;
        }
        var roles = LoadRoleKeys(conn, userId);
        var perms = LoadPermissions(conn, userId);
        return new SessionContext(userId, companyId, roles, perms);
    }

    private (int count, long? lastFailMs) ConsecutiveFailures(SqliteConnection conn, string companyId, string username)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT success, attempted_at FROM login_attempts " +
            "WHERE company_id = $c AND username = $u ORDER BY attempted_at DESC;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$u", username);
        using var r = cmd.ExecuteReader();
        int count = 0; long? lastFail = null;
        while (r.Read())
        {
            if (r.GetInt64(0) == 1) break; // son başarıya kadar say
            count++;
            lastFail ??= r.GetInt64(1);
        }
        return (count, lastFail);
    }

    private void RecordAttempt(SqliteConnection conn, string companyId, string username, bool success)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO login_attempts(id, company_id, username, success, attempted_at) " +
            "VALUES($id, $c, $u, $s, $t);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$s", success ? 1 : 0);
        cmd.Parameters.AddWithValue("$t", _clock.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    private readonly record struct UserRow(string Id, string PasswordHash);

    private static UserRow? FindUser(SqliteConnection conn, string companyId, string username)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, password_hash FROM users " +
            "WHERE company_id = $c AND username = $u AND is_active = 1 AND is_deleted = 0;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$u", username);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new UserRow(r.GetString(0), r.GetString(1)) : null;
    }

    private static List<string> LoadRoleKeys(SqliteConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id = ur.role_id " +
            "WHERE ur.user_id = $u AND r.is_deleted = 0;";
        cmd.Parameters.AddWithValue("$u", userId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static PermissionSet LoadPermissions(SqliteConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT module_key, can_view, can_create, can_edit, can_delete " +
            "FROM user_permissions WHERE user_id = $u;";
        cmd.Parameters.AddWithValue("$u", userId);
        var mods = new List<ModulePermission>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            mods.Add(new ModulePermission(r.GetString(0), r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1));
        return new PermissionSet(mods);
    }

    private void CreateSession(SqliteConnection conn, string userId, string companyId, DateTimeOffset now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO sessions(id, user_id, company_id, created_at, expires_at, revoked_at) " +
            "VALUES($id, $u, $c, $now, $exp, NULL);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$exp", now.AddHours(12).ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }
}
