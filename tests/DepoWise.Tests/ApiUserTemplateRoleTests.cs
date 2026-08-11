using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-07 + G6-09 (PRT-01 Grup 6, 2026-08-11) — YETKİ ŞABLONUNUN ROLÜ VE ŞABLON SEÇİCİNİN YETKİSİ.
///
/// Karar (KARAR-G6-B): masaüstündeki davranış doğru kabul edildi — şablon bir <c>roleKey</c> taşıyorsa
/// kullanıcıya o rol de atanır ve şablon seçici, kullanıcı oluşturma yetkisi olan HERKESE açıktır.
/// Web bunu yapmıyordu (rolü yok sayıyor, seçiciyi yalnız süper admine gösteriyordu) → iki platform
/// aynı şablondan FARKLI kullanıcı üretiyordu.
///
/// Bu testler kritik sınırı kilitler: şablon bir YETKİ YÜKSELTME KANALI DEĞİLDİR. Rol, kullanıcı
/// oluşturma isteğiyle birlikte gider ve sunucudaki <c>RoleAssignmentGuard</c> onu doğrular; aktörün
/// atayamayacağı bir rol için istek 403 ile reddedilir ve kullanıcı HİÇ OLUŞMAZ.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiUserTemplateRoleTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "TPL-A";
    private const string AdminA = "tpl_a";
    private const string CompanyB = "TPL-B";
    private const string AdminB = "tpl_b";
    private const string Pass = "Test!2026";

    private HttpClient _super = null!, _adminA = null!;
    private string _branchA = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        var uidA = svc.Users.EnsureInitialAdmin(CompanyA, AdminA, Pass, RoleKeys.CompanyAdmin);
        svc.Users.EnsureInitialAdmin(CompanyB, AdminB, Pass, RoleKeys.CompanyAdmin);

        var sa = new SessionContext(uidA, CompanyA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _branchA = svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("A Şube"));

        _super = await _host.LoginSeedAsync();
        _adminA = await _host.LoginAsync(AdminA, Pass, CompanyA);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    /// <summary>Şablon oluşturma YALNIZ süper admindedir (mevcut kural) → süper admin istemcisiyle kurulur.</summary>
    private async Task<string> CreateTemplateAsync(string name, string? roleKey, string? companyId, bool scopeAll)
    {
        var r = await _super.PostAsJsonAsync("/api/permission-templates", new
        {
            name, roleKey,
            modules = new[] { new { moduleKey = "fuel", canView = true, canCreate = false, canEdit = false, canDelete = false } },
            buttons = Array.Empty<string>(),
            companyId, scopeAll,
        });
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> CreateUserAsync(HttpClient c, string username, params string[] roleKeys)
        => c.PostAsJsonAsync("/api/users", new
        {
            username, password = "Kul!2026", fullName = (string?)null,
            roleKeys, companyId = (string?)null, branchId = _branchA, canViewAllBranches = false,
        });

    private async Task<bool> UserExistsAsync(HttpClient c, string username)
        => (await ApiTestHost.JsonAsync(await c.GetAsync("/api/users"))).EnumerateArray()
            .Any(u => u.GetProperty("username").GetString() == username);

    [Fact]
    public async Task Sablon_roleKey_Ucta_Geri_Doner()
    {
        // Web'in şablonun rolünü uygulayabilmesi için ucun roleKey'i döndürmesi ŞART.
        var id = await CreateTemplateAsync("Rollü Şablon", RoleKeys.Staff, CompanyA, scopeAll: false);

        var data = await ApiTestHost.JsonAsync(await _adminA.GetAsync($"/api/permission-templates/{id}"));

        Assert.Equal(RoleKeys.Staff, data.GetProperty("roleKey").GetString());
    }

    [Fact]
    public async Task Firma_Admini_Sablon_Listesini_GOREBILIR()
    {
        // G6-09: seçici artık süper adminle sınırlı değil; uç zaten users/Create istiyor.
        await CreateTemplateAsync("A Şablonu", null, CompanyA, scopeAll: false);
        await CreateTemplateAsync("Herkes Şablonu", null, null, scopeAll: true);
        await CreateTemplateAsync("B Şablonu", null, CompanyB, scopeAll: false);

        var raw = (await ApiTestHost.JsonAsync(await _adminA.GetAsync("/api/permission-templates/for-user"))).ToString();

        Assert.Contains("A Şablonu", raw);
        Assert.Contains("Herkes Şablonu", raw);
        Assert.DoesNotContain("B Şablonu", raw);   // tenant izolasyonu
    }

    [Fact]
    public async Task Sablonun_Izinli_Rolu_Kullaniciya_ATANIR()
    {
        await CreateTemplateAsync("Personel Şablonu", RoleKeys.Staff, CompanyA, scopeAll: false);

        // Web akışının yaptığı şey: şablonun rolü, kullanıcı oluşturma isteğine eklenir.
        (await CreateUserAsync(_adminA, "tpl_kullanici", RoleKeys.Staff)).EnsureSuccessStatusCode();

        var row = (await ApiTestHost.JsonAsync(await _adminA.GetAsync("/api/users"))).EnumerateArray()
            .First(u => u.GetProperty("username").GetString() == "tpl_kullanici");
        var id = row.GetProperty("id").GetString();
        var roles = await ApiTestHost.JsonAsync(await _adminA.GetAsync($"/api/users/{id}/roles"));
        Assert.Contains(roles.EnumerateArray(), e => e.GetString() == RoleKeys.Staff);
    }

    [Fact]
    public async Task Sablon_Yetki_YUKSELTME_Kanali_DEGILDIR_Kullanici_Hic_Olusmaz()
    {
        // Süper admin, süper-admin ROLÜ taşıyan bir şablonu A firmasına tanımlıyor.
        await CreateTemplateAsync("Tehlikeli Şablon", RoleKeys.SuperAdmin, CompanyA, scopeAll: false);

        // Firma admini bu şablonu uygularsa: rol isteğe eklenir → sunucu RoleAssignmentGuard ile REDDEDER.
        var r = await CreateUserAsync(_adminA, "kacak_kullanici", RoleKeys.SuperAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Contains("Yetki yükseltme reddedildi",
            (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
        // En kritik iddia: kullanıcı YARIM da olsa oluşmadı.
        Assert.False(await UserExistsAsync(_super, "kacak_kullanici"));
    }

    [Fact]
    public async Task Kisitli_Super_Admin_Rolu_De_Reddedilir()
    {
        await CreateTemplateAsync("Kısıtlı Şablon", RoleKeys.RestrictedSuperAdmin, CompanyA, scopeAll: false);

        var r = await CreateUserAsync(_adminA, "kacak2", RoleKeys.RestrictedSuperAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.False(await UserExistsAsync(_super, "kacak2"));
    }

    [Fact]
    public async Task Baska_Firmanin_Sablonu_Okunamaz()
    {
        var idB = await CreateTemplateAsync("B Gizli", RoleKeys.Staff, CompanyB, scopeAll: false);

        var r = await _adminA.GetAsync($"/api/permission-templates/{idB}");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Tum_Firma_Sablonu_Baska_Firmadan_Okunabilir()
    {
        // scope_all şablonlar bilinçli olarak her firmaya açıktır (tenant kuralının istisnası değil, tasarımı).
        var id = await CreateTemplateAsync("Ortak Şablon", RoleKeys.Staff, null, scopeAll: true);

        var r = await _adminA.GetAsync($"/api/permission-templates/{id}");

        r.EnsureSuccessStatusCode();
        Assert.Equal(RoleKeys.Staff, (await ApiTestHost.JsonAsync(r)).GetProperty("roleKey").GetString());
    }
}
