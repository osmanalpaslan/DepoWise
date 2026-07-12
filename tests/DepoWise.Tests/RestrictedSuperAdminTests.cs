using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Adım 2a — "Kısıtlı Süper Admin" rolü: yalnız süper admin atar; admin bypass'ı yok
/// (deny-by-default, yalnız açık yetkiler); firma admini bu kullanıcıyı yönetemez.</summary>
public class RestrictedSuperAdminTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly PermissionService _perms;
    private readonly AuthService _auth;

    public RestrictedSuperAdminTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_rsa_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _perms = new PermissionService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext Su() => _auth.Login("A", "root", "root123").Session!;

    [Fact]
    public void Rol_Seed_ve_Migration_ile_Mevcut()
    {
        Assert.Contains(RoleKeys.Seed, r => r.Key == RoleKeys.RestrictedSuperAdmin && r.Name == "Kısıtlı Süper Admin");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM roles WHERE role_key=$k AND is_deleted=0 AND company_id IS NULL;";
        cmd.Parameters.AddWithValue("$k", RoleKeys.RestrictedSuperAdmin);
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void RolAtama_YalnizSuperAdmin()
    {
        // Firma admini kısıtlı süper admin ATAYAMAZ
        var admin = new SessionContext("adm", "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() =>
            RoleAssignmentGuard.EnsureCanAssign(admin, new[] { RoleKeys.RestrictedSuperAdmin }));

        // Süper admin atayabilir (istisna yok)
        var su = new SessionContext("root", "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        RoleAssignmentGuard.EnsureCanAssign(su, new[] { RoleKeys.RestrictedSuperAdmin });
    }

    [Fact]
    public void AdminBypassYok_YalnizAcikYetkiler()
    {
        // Açık yetkisi olmayan kısıtlı süper admin → deny-by-default (admin gibi değil)
        var rsa = new SessionContext("k", "A", new[] { RoleKeys.RestrictedSuperAdmin }, PermissionSet.Empty);
        Assert.False(AccessControl.IsAdmin(rsa));
        Assert.False(AccessControl.Can(rsa, "materials", PermissionAction.View));
        Assert.False(AccessControl.Can(rsa, "users", PermissionAction.View));
    }

    [Fact]
    public void FirmaAdmini_KisitliSuperAdmini_Yonetemez()
    {
        var su = Su();
        var rsaId = _users.CreateUser(su, new NewUser("ksa", "p12345", null, new[] { RoleKeys.RestrictedSuperAdmin }, CompanyId: "A"));
        var adminId = _users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var admin = _auth.Login("A", "adm", "p12345").Session!;

        // Firma admini, kısıtlı süper adminin yetkisini düzenleyemez
        Assert.Throws<ForbiddenException>(() =>
            _perms.SaveForUser(admin, rsaId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>()));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
