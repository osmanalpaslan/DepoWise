using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Firma Yetki Kontrol — ekran DÜZEYİ modeli (Serbest/Admin/Süper Admin). Süper admin belirler;
/// PermissionService grant-time uygular: admin düzeyi → hedef admin; süper-admin düzeyi → Kısıtlı Süper Admin.</summary>
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

    private static Dictionary<string, string> Lvl(string key, string level) => new() { [key] = level };

    [Fact]
    public void FirmaDuzeyi_Admin_YalnizSuperAdmin_PermissionServiceUygular()
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

        // Başta materials verilebilir (serbest)
        perms.SaveForUser(admin, staffId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());

        // Firma admini kontrol ekranına erişemez
        Assert.Throws<ForbiddenException>(() => grants.GetControl(admin, "A"));
        Assert.Throws<ForbiddenException>(() => grants.SetLevels(admin, "A", Lvl("materials", CompanyGrantService.LevelAdmin)));

        // Süper admin "materials"i A firmasında "Admin" düzeyine alır
        grants.SetLevels(su, "A", Lvl("materials", CompanyGrantService.LevelAdmin));
        var control = grants.GetControl(su, "A");
        Assert.Contains(control, r => r.ModuleKey == "materials" && r.Level == CompanyGrantService.LevelAdmin && !r.Grantable);

        // Artık firma admini Personel'e materials VEREMEZ
        var ex = Assert.Throws<InvalidOperationException>(() =>
            perms.SaveForUser(admin, staffId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>()));
        Assert.Contains("Admin", ex.Message);

        // Serbest'e alınınca yine verilebilir
        grants.SetLevels(su, "A", Lvl("materials", CompanyGrantService.LevelFree));
        perms.SaveForUser(admin, staffId, new[] { new ModulePermission("materials", true, false, false, false) }, Array.Empty<string>());
    }

    [Fact]
    public void FirmaDuzeyi_SuperAdmin_YalnizKisitliSuperAdmine_Verilebilir()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        var grants = new CompanyGrantService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        var staffId = users.CreateUser(su, new NewUser("per", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var rsaId = users.CreateUser(su, new NewUser("ksa", "p12345", null, new[] { RoleKeys.RestrictedSuperAdmin }, CompanyId: "A"));

        // Süper admin "fuel"i A firmasında "Süper Admin" düzeyine alır
        grants.SetLevels(su, "A", Lvl("fuel", CompanyGrantService.LevelSuper));
        var control = grants.GetControl(su, "A");
        Assert.Contains(control, r => r.ModuleKey == "fuel" && r.Level == CompanyGrantService.LevelSuper);

        // ⭐ B5 (kullanıcı kararı 2026-08-19): SÜPER ADMİN bu ekranı düz Personel'e de verebilir.
        // Firma düzeyi "Süper Admin" ayarı alt rollere karşı geçerliliğini korur; yalnız süper adminin
        // BİLEREK verdiği izin bu tavanı aşar.
        perms.SaveForUser(su, staffId, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>());
        var per = auth.Login("A", "per", "p12345").Session!;
        Assert.True(AccessControl.Can(per, "fuel", PermissionAction.View));

        // Kısıtlı Süper Admin'e VERİLEBİLİR
        perms.SaveForUser(su, rsaId, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>());
        var rsa = auth.Login("A", "ksa", "p12345").Session!;
        Assert.True(AccessControl.Can(rsa, "fuel", PermissionAction.View));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
