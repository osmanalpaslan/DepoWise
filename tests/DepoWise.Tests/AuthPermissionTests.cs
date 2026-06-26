using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

public class AuthPermissionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public AuthPermissionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_auth_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(TimeSpan t) => UtcNow = UtcNow.Add(t);
    }

    // ---- Parola hash ----
    [Fact]
    public void Parola_Hash_DogrulanirVeYanlisReddedilir()
    {
        var h = PasswordHasher.Hash("S3cret!");
        Assert.StartsWith("pbkdf2$sha256$", h);
        Assert.True(PasswordHasher.Verify("S3cret!", h));
        Assert.False(PasswordHasher.Verify("yanlis", h));
    }

    [Fact]
    public void Parola_FarkliSalt_FarkliHash()
        => Assert.NotEqual(PasswordHasher.Hash("ayni"), PasswordHasher.Hash("ayni"));

    // ---- Login + kilit ----
    [Fact]
    public void Login_BasariliVeYanlis()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);

        Assert.False(auth.Login("A", "admin", "yanlis").Success);
        var ok = auth.Login("A", "admin", "admin123");
        Assert.True(ok.Success);
        Assert.Equal("A", ok.Session!.CompanyId);
        Assert.True(ok.Session.IsCompanyAdmin);
    }

    [Fact]
    public void Login_5Hata_Sonra_Kilit_VeBasariSifirlar()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);

        for (int i = 0; i < AuthService.MaxFailures; i++)
            Assert.False(auth.Login("A", "admin", "yanlis").Success);

        // Doğru parola bile olsa kilitli
        var locked = auth.Login("A", "admin", "admin123");
        Assert.True(locked.Locked);
        Assert.True(locked.SecondsRemaining > 0);

        // Kilit süresi geçince tekrar denenebilir ve başarı kilidi sıfırlar
        _clock.Advance(AuthService.LockWindow + TimeSpan.FromSeconds(1));
        var ok = auth.Login("A", "admin", "admin123");
        Assert.True(ok.Success);
        for (int i = 0; i < AuthService.MaxFailures - 1; i++)
            auth.Login("A", "admin", "yanlis"); // başarıdan sonra tekrar 4 hata kilitlemez
        Assert.False(auth.Login("A", "admin", "yanlis2").Locked);
    }

    // ---- Deny-by-default ----
    [Fact]
    public void DenyByDefault_YetkiYoksaErisimYok()
    {
        var s = Session("A", "u1");
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.View));
        Assert.False(AccessControl.CanSeeMenu(s, "materials"));
        Assert.False(AccessControl.CanUseButton(s, SpecialButtons.Approve));
        Assert.Throws<ForbiddenException>(() => AccessControl.Require(s, "materials", PermissionAction.Create));
    }

    [Fact]
    public void Dashboard_HerkeseAcik_YalnizOkuma()
    {
        var s = Session("A", "u1");
        Assert.True(AccessControl.CanSeeMenu(s, AppModules.Dashboard));
        Assert.False(AccessControl.Can(s, AppModules.Dashboard, PermissionAction.Create));
    }

    [Fact]
    public void Yetki_Sadece_View_Verilince_MenuGorunur_YazmaReddedilir()
    {
        var s = Session("A", "u1", perms: new[]
        {
            new ModulePermission("materials", CanView: true, CanCreate: false, CanEdit: false, CanDelete: false),
        });
        Assert.True(AccessControl.CanSeeMenu(s, "materials"));
        Assert.False(AccessControl.Can(s, "materials", PermissionAction.Create)); // gizli + API reddi
        Assert.Throws<ForbiddenException>(() => AccessControl.Require(s, "materials", PermissionAction.Create));
    }

    [Fact]
    public void Admin_TamYetkili()
    {
        var admin = Session("A", "adm", roles: new[] { RoleKeys.CompanyAdmin });
        Assert.True(AccessControl.Can(admin, "materials", PermissionAction.Delete));
        Assert.True(AccessControl.CanUseButton(admin, SpecialButtons.ResetDatabase));
    }

    // ---- Tenant izolasyonu / firma değiştirme ----
    [Fact]
    public void Payload_FarkliCompany_Reddedilir()
    {
        var s = Session("A", "u1");
        Assert.Throws<ForbiddenException>(() => TenantAccessGuard.ResolveCompanyId(s, "B"));
        Assert.Equal("A", TenantAccessGuard.ResolveCompanyId(s, "A"));
        Assert.Equal("A", TenantAccessGuard.ResolveCompanyId(s, null));
    }

    [Fact]
    public void SuperAdmin_FirmaSecebilir()
    {
        var su = Session("A", "su", roles: new[] { RoleKeys.SuperAdmin });
        Assert.Equal("B", TenantAccessGuard.ResolveCompanyId(su, "B")); // çapraz firma
        Assert.Throws<ForbiddenException>(() => TenantAccessGuard.EnsureOwnership(Session("A", "x"), "B"));
        TenantAccessGuard.EnsureOwnership(su, "B"); // süper admin sahiplik kontrolünden muaf
    }

    // ---- Yetki yükseltme ----
    [Fact]
    public void AdminOlmayan_AdminRolu_Atayamaz()
    {
        var users = new UserService(_factory, _clock);
        // create yetkili ama admin olmayan müdür
        var manager = Session("A", "mgr", roles: new[] { RoleKeys.Manager }, perms: new[]
        {
            new ModulePermission("users", true, true, true, false),
        });
        Assert.Throws<ForbiddenException>(() => users.CreateUser(manager,
            new NewUser("yeni", "p12345", null, new[] { RoleKeys.CompanyAdmin })));
        Assert.Throws<ForbiddenException>(() => users.CreateUser(manager,
            new NewUser("yeni2", "p12345", null, new[] { RoleKeys.SuperAdmin })));
    }

    [Fact]
    public void FirmaAdmini_KullaniciOlusturur_KendiFirmasinda()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var auth = new AuthService(_factory, _clock);
        var admin = auth.Login("A", "admin", "admin123").Session!;

        // Farklı firma (B) istemek REDDEDİLİR (fail-closed; firma değiştiremez)
        Assert.Throws<ForbiddenException>(() => users.CreateUser(admin,
            new NewUser("depocu", "p12345", "Depo", new[] { RoleKeys.Warehouse }, CompanyId: "B")));

        // CompanyId verilmeyince kendi firmasında oluşturur
        var newId = users.CreateUser(admin,
            new NewUser("depocu", "p12345", "Depo", new[] { RoleKeys.Warehouse }));
        Assert.False(string.IsNullOrEmpty(newId));

        var login = new AuthService(_factory, _clock).Login("A", "depocu", "p12345");
        Assert.True(login.Success);
        Assert.Equal("A", login.Session!.CompanyId);
    }

    [Fact]
    public void SuperAdmin_SuperAdmin_Olusturabilir()
    {
        var users = new UserService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = new AuthService(_factory, _clock).Login("A", "root", "root123").Session!;
        var id = users.CreateUser(su, new NewUser("root2", "root234", null, new[] { RoleKeys.SuperAdmin }));
        Assert.False(string.IsNullOrEmpty(id));
    }

    private SessionContext Session(string company, string user,
        IEnumerable<string>? roles = null, IEnumerable<ModulePermission>? perms = null)
        => new(user, company, roles ?? Array.Empty<string>(), new PermissionSet(perms ?? Array.Empty<ModulePermission>()));

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
