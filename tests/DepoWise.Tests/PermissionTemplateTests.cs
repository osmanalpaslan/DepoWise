using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Adım 4 — Yetki Şablonları firma-kapsamlı: şablon bir firmaya veya TÜM firmalara tanımlanır;
/// kullanıcı-oluşturma yetkili aktör KENDİ firması + tüm-firma şablonlarını görür (tenant izolasyonu).</summary>
public class PermissionTemplateTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly UserService _users;
    private readonly CompanyService _companies;
    private readonly PermissionTemplateService _templates;
    private readonly AuthService _auth;

    public PermissionTemplateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_tpl_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _users = new UserService(_factory, _clock);
        _companies = new CompanyService(_factory, _clock);
        _templates = new PermissionTemplateService(_factory, _clock);
        _auth = new AuthService(_factory, _clock);
        _users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext Su() => _auth.Login("A", "root", "root123").Session!;
    private static ModulePermission[] M(string key) => new[] { new ModulePermission(key, true, false, false, false) };

    [Fact]
    public void FirmayaOzel_ve_TumFirmalar_Gorunurluk()
    {
        var su = Su();
        var b = _companies.Create(su, new NewCompany("Firma B"));
        var adminBId = _users.CreateUser(su, new NewUser("admb", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: b));
        var adminB = _auth.Login(b, "admb", "p12345").Session!;

        var tplA = _templates.Create(su, "A Şablonu", null, M("materials"), Array.Empty<string>(), targetCompanyId: su.CompanyId);
        var tplB = _templates.Create(su, "B Şablonu", null, M("vehicles"), Array.Empty<string>(), targetCompanyId: b);
        var tplAll = _templates.Create(su, "Ortak Şablon", null, M("reports"), Array.Empty<string>(), scopeAll: true);

        // B firması admini: yalnız B'ye özel + tüm-firma şablonunu görür (A'yı GÖRMEZ)
        var forB = _templates.ListForUserCreation(adminB).Select(t => t.Id).ToHashSet();
        Assert.Contains(tplB, forB);
        Assert.Contains(tplAll, forB);
        Assert.DoesNotContain(tplA, forB);

        // Süper admin yönetim listesi: tümünü + kapsamı görür
        var all = _templates.List(su);
        Assert.Contains(all, t => t.Id == tplAll && t.ScopeAll);
        Assert.Contains(all, t => t.Id == tplB && !t.ScopeAll && t.CompanyName == "Firma B");
    }

    [Fact]
    public void ListForUserCreation_KullaniciOlusturmaYetkisi_Ister()
    {
        // users/Create yetkisi olmayan personel şablonları göremez
        var staff = new SessionContext("s", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _templates.ListForUserCreation(staff));
    }

    [Fact]
    public void GetData_BaskaFirmaSablonu_Erisemez()
    {
        var su = Su();
        var b = _companies.Create(su, new NewCompany("Firma B"));
        var tplB = _templates.Create(su, "B Şablonu", null, M("vehicles"), Array.Empty<string>(), targetCompanyId: b);

        // A firması admini B'nin şablonunu okuyamaz
        var adminAId = _users.CreateUser(su, new NewUser("adma", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var adminA = _auth.Login("A", "adma", "p12345").Session!;
        Assert.Throws<ForbiddenException>(() => _templates.GetData(adminA, tplB));

        // Süper admin okuyabilir
        Assert.NotEmpty(_templates.GetData(su, tplB).Modules);
    }

    [Fact]
    public void Create_Silme_YalnizSuperAdmin()
    {
        var su = Su();
        var adminAId = _users.CreateUser(su, new NewUser("adma", "p12345", null, new[] { RoleKeys.CompanyAdmin }, CompanyId: "A"));
        var adminA = _auth.Login("A", "adma", "p12345").Session!;
        Assert.Throws<ForbiddenException>(() =>
            _templates.Create(adminA, "X", null, M("materials"), Array.Empty<string>()));
        var tpl = _templates.Create(su, "X", null, M("materials"), Array.Empty<string>());
        Assert.Throws<ForbiddenException>(() => _templates.Delete(adminA, tpl));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
