using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Rol Yetki Kontrol — süper adminin bir ekranı belirli bir ROLE kapatması.
/// Kapalı ekran: yetki ağacında görünmez, grant'te reddedilir, oturumda erişim kapanır (admin bypass'ı dahil).</summary>
public class RoleGrantTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    public RoleGrantTests()
    {
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "depowise_rg_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static Dictionary<string, IReadOnlyList<string>> Block(string roleKey, params string[] modules)
        => new() { [roleKey] = modules };

    [Fact]
    public void RoleKapali_Personel_AgactaGorunmez_GrantReddedilir_ErisimKapanir()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        var roles = new RoleGrantService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;

        var staffId = users.CreateUser(su, new NewUser("per", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));

        // Başta serbest: fuel verilebilir ve erişilebilir.
        perms.SaveForUser(su, staffId, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>());
        Assert.True(AccessControl.Can(auth.Login("A", "per", "p12345").Session!, "fuel", PermissionAction.View));

        // Süper admin "fuel"i PERSONEL rolüne kapatır.
        roles.SetMatrix(su, Block(RoleKeys.Staff, "fuel"));

        // 1) Yetki ağacında görünmez (hedefin rolüne kapalı).
        Assert.Contains("fuel", perms.BlockedModulesForUser(su, staffId));

        // 2) Grant reddedilir — süper admin bile veremez.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            perms.SaveForUser(su, staffId, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>()));
        Assert.Contains("Rol Yetki Kontrol", ex.Message);

        // 3) ÖNCEDEN verilmiş izin olsa bile oturumda erişim kapanır (izin satırı DB'de duruyor).
        var staff = auth.Login("A", "per", "p12345").Session!;
        Assert.Contains("fuel", staff.BlockedModules);
        Assert.False(AccessControl.Can(staff, "fuel", PermissionAction.View));
        Assert.False(AccessControl.CanSeeMenu(staff, "fuel"));
    }

    [Fact]
    public void RoleKapali_Admin_BypassiAsamaz_SuperAdminMuaf()
    {
        var users = new UserService(_factory, _clock);
        var roles = new RoleGrantService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;
        users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));

        // Admin normalde her ekrana erişir (bypass).
        Assert.True(AccessControl.Can(auth.Login("A", "adm", "p12345").Session!, "reports", PermissionAction.View));

        // Süper admin "reports"u ADMIN rolüne kapatır → admin bypass'ı bunu AŞAMAZ.
        roles.SetMatrix(su, Block(RoleKeys.CompanyAdmin, "reports"));
        var admin = auth.Login("A", "adm", "p12345").Session!;
        Assert.False(AccessControl.Can(admin, "reports", PermissionAction.View));
        Assert.False(AccessControl.Can(admin, "reports", PermissionAction.Edit));

        // Süper admin MUAF (aksi halde platform sahibi kendini kilitler).
        var su2 = auth.Login("A", "root", "root123").Session!;
        Assert.Empty(su2.BlockedModules);
        Assert.True(AccessControl.Can(su2, "reports", PermissionAction.View));
    }

    [Fact]
    public void YalnizSuperAdmin_Yonetir_YapisalKilitler_Sabit()
    {
        var users = new UserService(_factory, _clock);
        var roles = new RoleGrantService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;
        users.CreateUser(su, new NewUser("adm", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var admin = auth.Login("A", "adm", "p12345").Session!;

        // Firma admini bu ekrana erişemez.
        Assert.Throws<ForbiddenException>(() => roles.GetControl(admin));
        Assert.Throws<ForbiddenException>(() => roles.SetMatrix(admin, Block(RoleKeys.Staff, "fuel")));

        var control = roles.GetControl(su);

        // ⭐ B5 (kullanıcı kararı 2026-08-19): yapısal kilitler SÜPER ADMİN için BAĞLAYICI DEĞİL.
        // Matris yalnız süper admine açık; "yetki tamamen süper adminin elinde" olmalı. Bu yüzden
        // hiçbir hücre "değiştirilemez" (Hard) gelmez — kilit yalnız BAŞLANGIÇ değerini belirler.
        var machines = control.First(r => r.ModuleKey == "machines");
        Assert.False(machines.Cells.First(c => c.RoleKey == RoleKeys.CompanyAdmin).Hard);
        Assert.False(machines.Cells.First(c => c.RoleKey == RoleKeys.Staff).Hard);
        // Başlangıçta yine KAPALI görünür (öneri korunur): süper-admin-only ekran alt rollere kapalı başlar.
        Assert.True(machines.Cells.First(c => c.RoleKey == RoleKeys.CompanyAdmin).Blocked);
        Assert.True(machines.Cells.First(c => c.RoleKey == RoleKeys.Staff).Blocked);
        Assert.False(machines.Cells.First(c => c.RoleKey == RoleKeys.RestrictedSuperAdmin).Blocked); // devredilebilir

        // Admin-kısıtlı ekran (users): Personel'e kapalı BAŞLAR, Admin'e serbest — ama ikisi de kilitli değil.
        var usersRow = control.First(r => r.ModuleKey == "users");
        Assert.True(usersRow.Cells.First(c => c.RoleKey == RoleKeys.Staff).Blocked);
        Assert.False(usersRow.Cells.First(c => c.RoleKey == RoleKeys.Staff).Hard);
        Assert.False(usersRow.Cells.First(c => c.RoleKey == RoleKeys.CompanyAdmin).Blocked);

        // Public modüller (Ana Ekran/Tema/Hakkında) matriste yok — kapatılamaz.
        Assert.DoesNotContain(control, r => r.ModuleKey == AppModules.Dashboard);
    }

    [Fact]
    public void Matris_TamDegistirir_AcilinceErisimGeriGelir()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        var roles = new RoleGrantService(_factory, _clock);
        users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var auth = new AuthService(_factory, _clock);
        var su = auth.Login("A", "root", "root123").Session!;
        var staffId = users.CreateUser(su, new NewUser("per", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));

        roles.SetMatrix(su, Block(RoleKeys.Staff, "fuel", "vehicles"));
        Assert.Equal(2, perms.BlockedModulesForUser(su, staffId).Count);

        // Boş matris = tümü serbest (tam değiştirir).
        roles.SetMatrix(su, new Dictionary<string, IReadOnlyList<string>>());
        Assert.Empty(perms.BlockedModulesForUser(su, staffId));

        // Açılınca yeniden verilebilir + erişilebilir.
        perms.SaveForUser(su, staffId, new[] { new ModulePermission("fuel", true, false, false, false) }, Array.Empty<string>());
        Assert.True(AccessControl.Can(auth.Login("A", "per", "p12345").Session!, "fuel", PermissionAction.View));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); System.IO.File.Delete(_dbPath); } catch { }
    }
}
