using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Update;

namespace DepoWise.Api;

/// <summary>
/// Sunucu kompozisyon kökü (Option A — masaüstüyle AYNI Application/Infrastructure). Migration + servisler +
/// oturum token deposu. Kimlik doğrulama AuthService ile; cihaz senkron token'ları SyncServer/EnrollmentService'te.
/// Kimlik = JWT (durum tutmaz); yetkiler her istekte sunucuda yeniden yüklenir. DB = SQLite (Postgres'e taşınabilir).
/// </summary>
public sealed class ServerServices
{
    public IDbConnectionFactory Factory { get; }
    public AuthService Auth { get; }
    public UserService Users { get; }
    public SyncServer Sync { get; }
    public ReleaseService Releases { get; }
    public EnrollmentService Enrollment { get; }
    public BackupStore Backups { get; }
    public ReleaseStore ReleasePackages { get; }

    public ServerServices(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        Factory = new SqliteConnectionFactory(Path.Combine(dataDir, "depowise-server.db"));
        new MigrationRunner(Factory).Run();

        var clock = new SystemClock();
        Auth = new AuthService(Factory, clock);
        Users = new UserService(Factory, clock);
        Sync = new SyncServer(Factory, clock);
        Releases = new ReleaseService(Factory, clock);
        Enrollment = new EnrollmentService(Factory, clock);
        Backups = new BackupStore(Path.Combine(dataDir, "backups"));
        ReleasePackages = new ReleaseStore(Path.Combine(dataDir, "releases"));

        EnsureSeedAdmins();
    }

    private void EnsureSeedAdmins()
    {
        using var conn = Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users;";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            Users.EnsureInitialAdmin("DEPOWISE", "admin", "admin123", RoleKeys.CompanyAdmin);

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE r.role_key=$k;";
        cmd2.Parameters.AddWithValue("$k", RoleKeys.SuperAdmin);
        if (Convert.ToInt64(cmd2.ExecuteScalar()) == 0)
            Users.EnsureInitialAdmin("DEPOWISE", "superadmin", "superadmin", RoleKeys.SuperAdmin);
    }

    /// <summary>JWT'den (userId+companyId) tam oturumu SUNUCUDA yeniden kurar — yetkiler token'dan değil DB'den.</summary>
    public SessionContext? SessionFor(string? companyId, string? userId)
        => string.IsNullOrEmpty(companyId) || string.IsNullOrEmpty(userId)
            ? null : Auth.CreateSessionForUser(companyId, userId);
}
