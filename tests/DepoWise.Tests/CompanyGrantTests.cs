using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>#6 — Firma Yetki Kontrol: süper admin firmaya özel modül kısıtı ekler; PermissionService uygular.</summary>
public class CompanyGrantTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public CompanyGrantTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_cg_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Fact]
    public void FirmayaOzelKisit_YalnizSuperAdmin_PermissionServiceUygular()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        var grants = new CompanyGrantService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        var adminId = users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var staffId = users.CreateUser(su, new NewUser("per", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var admin = auth.Login("A", "adm", "p12345").Session!;

        // Başta materials verilebilir
        perms.SaveForUser(admin, staffId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());

        // Firma admini kontrol ekranına erişemez
        Assert.Throws<ForbiddenException>(() => grants.GetControl(admin, "A"));
        Assert.Throws<ForbiddenException>(() => grants.SetLimits(admin, "A", new[] { "materials" }));

        // Süper admin "materials"i A firmasına özel kısıtlar
        grants.SetLimits(su, "A", new[] { "materials" });
        var control = grants.GetControl(su, "A");
        Assert.Contains(control, r => r.ModuleKey == "materials" && r.CompanyRestricted && !r.Grantable);

        // Artık firma admini Personel'e materials VEREMEZ
        var ex = Assert.Throws<InvalidOperationException>(() =>
            perms.SaveForUser(admin, staffId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>()));
        Assert.Contains("Admin", ex.Message);

        // Kısıt kaldırılınca yine verilebilir
        grants.SetLimits(su, "A", Array.Empty<string>());
        perms.SaveForUser(admin, staffId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());
    }

    [Fact]
    public void GlobalKilit_YalnizSuperAdmin_TumFirmalariEtkiler_PermissionServiceUygular()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        var grants = new CompanyGrantService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        var adminId = users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var staffA = users.CreateUser(su, new NewUser("per", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var admin = auth.Login("A", "adm", "p12345").Session!;

        // Başta fuel verilebilir
        perms.SaveForUser(admin, staffA, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>());

        // Firma admini global kilidi değiştiremez
        Assert.Throws<ForbiddenException>(() => grants.SetGlobalLocks(admin, new[] { "fuel" }));

        // Süper admin "fuel"i GLOBAL kilitler → etkin ama SABİT değil (dinamik)
        grants.SetGlobalLocks(su, new[] { "fuel" });
        var ctrlA = grants.GetControl(su, "A");
        Assert.Contains(ctrlA, r => r.ModuleKey == "fuel" && r.GlobalRestricted && !r.GlobalHardLocked && !r.Grantable);
        // Firma bağımsız: hiç kaydı olmayan başka bir firma (B) için de global kilit etkin görünür.
        var ctrlB = grants.GetControl(su, "B");
        Assert.Contains(ctrlB, r => r.ModuleKey == "fuel" && r.GlobalRestricted && !r.GlobalHardLocked);

        // Artık firma admini Personel'e fuel VEREMEZ
        Assert.Throws<InvalidOperationException>(() =>
            perms.SaveForUser(admin, staffA, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>()));

        // Global kilit kaldırılınca yine verilebilir
        grants.SetGlobalLocks(su, Array.Empty<string>());
        perms.SaveForUser(admin, staffA, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>());
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
