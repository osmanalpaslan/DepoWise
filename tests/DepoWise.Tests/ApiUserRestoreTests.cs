using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-03 (PRT-01 Grup 6, 2026-08-11) — SİLİNEN KULLANICI GERİ YÜKLENEBİLİR + KULLANICI ADI SERBEST KALIR.
///
/// Bulunan durum: kullanıcı silme SOFT'tu (<c>is_deleted=1</c>) ama Çöp Kutusu <c>users</c> tablosunu
/// kapsamıyordu → yanlışlıkla silinen kullanıcı KURTARILAMIYORDU. Üstelik Migration001'deki benzersizlik
/// indeksi silinmiş satırları da kapsadığı için o kullanıcı adı firmada KALICI olarak bloke kalıyor ve
/// aynı adla yeni kullanıcı denemesi jenerik 500 veriyordu.
///
/// Karar (KARAR-G6-A) uygulandı: Çöp Kutusu'na alındı + benzersizlik YALNIZ aktif kullanıcılara daraltıldı
/// (Migration063). Bu testler iki tenantlı GERÇEK HTTP hattı üzerinden çalışır.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiUserRestoreTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "GY-A";
    private const string AdminA = "gy_a";
    private const string CompanyB = "GY-B";
    private const string AdminB = "gy_b";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!, _b = null!, _super = null!;
    private string _branchA = "", _branchB = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        var uidA = svc.Users.EnsureInitialAdmin(CompanyA, AdminA, Pass, RoleKeys.CompanyAdmin);
        var uidB = svc.Users.EnsureInitialAdmin(CompanyB, AdminB, Pass, RoleKeys.CompanyAdmin);
        var sa = new SessionContext(uidA, CompanyA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sb = new SessionContext(uidB, CompanyB, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _branchA = svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("A Şube"));
        _branchB = svc.Branches.Create(sb, new DepoWise.Infrastructure.Organization.NewBranch("B Şube"));

        _a = await _host.LoginAsync(AdminA, Pass, CompanyA);
        _b = await _host.LoginAsync(AdminB, Pass, CompanyB);
        _super = await _host.LoginSeedAsync();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task<string> CreateUserAsync(HttpClient c, string username, string branchId)
    {
        var r = await c.PostAsJsonAsync("/api/users", new
        {
            username, password = "Kul!2026", fullName = (string?)null,
            roleKeys = new[] { RoleKeys.Staff }, companyId = (string?)null, branchId, canViewAllBranches = false,
        });
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> TrashAsync(HttpClient c, string password = Pass)
        => c.PostAsJsonAsync("/api/trash", new { password });

    private Task<HttpResponseMessage> RestoreAsync(HttpClient c, string id, string password = Pass)
        => c.PostAsJsonAsync("/api/trash/restore", new { table = "users", id, password });

    private static bool HasTrashUser(System.Text.Json.JsonElement trash, string id)
        => trash.EnumerateArray().Any(t => t.GetProperty("table").GetString() == "users"
                                        && t.GetProperty("id").GetString() == id);

    private async Task<bool> IsListedAsync(HttpClient c, string username)
        => (await ApiTestHost.JsonAsync(await c.GetAsync("/api/users"))).EnumerateArray()
            .Any(u => u.GetProperty("username").GetString() == username);

    [Fact]
    public async Task Silinen_Kullanici_Cop_Kutusunda_GORUNUR_Ve_GERI_YUKLENIR()
    {
        var id = await CreateUserAsync(_a, "silinecek", _branchA);
        (await _a.DeleteAsync($"/api/users/{id}")).EnsureSuccessStatusCode();
        Assert.False(await IsListedAsync(_a, "silinecek"));

        var trash = await ApiTestHost.JsonAsync(await TrashAsync(_a));
        Assert.True(HasTrashUser(trash, id));

        (await RestoreAsync(_a, id)).EnsureSuccessStatusCode();

        Assert.True(await IsListedAsync(_a, "silinecek"));                      // yeniden aktif
        Assert.False(HasTrashUser(await ApiTestHost.JsonAsync(await TrashAsync(_a)), id)); // çöpten düştü
    }

    [Fact]
    public async Task Silinen_Kullanici_Adi_YENIDEN_KULLANILABILIR()
    {
        var id = await CreateUserAsync(_a, "tekrar", _branchA);
        (await _a.DeleteAsync($"/api/users/{id}")).EnsureSuccessStatusCode();

        // Migration063 öncesi burada UNIQUE ihlali → jenerik 500 oluyordu.
        var id2 = await CreateUserAsync(_a, "tekrar", _branchA);

        Assert.NotEqual(id, id2);
        Assert.True(await IsListedAsync(_a, "tekrar"));
    }

    [Fact]
    public async Task AKTIF_Kullanici_Adi_Tekrar_Kullanilamaz_Ve_ANLASILIR_Hata_Doner()
    {
        await CreateUserAsync(_a, "mevcut", _branchA);

        var r = await _a.PostAsJsonAsync("/api/users", new
        {
            username = "mevcut", password = "Kul!2026", fullName = (string?)null,
            roleKeys = new[] { RoleKeys.Staff }, companyId = (string?)null, branchId = _branchA, canViewAllBranches = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);   // 500 DEĞİL
        Assert.Contains("zaten kullanılıyor", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Ad_Baska_AKTIF_Kullaniciya_Verildiyse_Geri_Yukleme_REDDEDILIR()
    {
        var id = await CreateUserAsync(_a, "cakisan", _branchA);
        (await _a.DeleteAsync($"/api/users/{id}")).EnsureSuccessStatusCode();
        await CreateUserAsync(_a, "cakisan", _branchA);   // ad yeniden kullanıldı

        var r = await RestoreAsync(_a, id);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("AKTİF", (await ApiTestHost.JsonAsync(r)).GetProperty("error").GetString());
        // Reddedilen istek kaydı diriltmemeli: hâlâ çöpte.
        Assert.True(HasTrashUser(await ApiTestHost.JsonAsync(await TrashAsync(_a)), id));
    }

    [Fact]
    public async Task Baska_Firmanin_Silinmis_Kullanicisi_NE_GORUNUR_NE_GERI_YUKLENIR()
    {
        var idB = await CreateUserAsync(_b, "b_kullanici", _branchB);
        (await _b.DeleteAsync($"/api/users/{idB}")).EnsureSuccessStatusCode();

        var trashA = await ApiTestHost.JsonAsync(await TrashAsync(_a));
        Assert.False(HasTrashUser(trashA, idB));
        Assert.DoesNotContain("b_kullanici", trashA.ToString());

        Assert.True(ApiTestHost.IsDenied(await RestoreAsync(_a, idB)));
        // B firmasında hâlâ silinmiş durumda (A'nın isteği hiçbir şeyi değiştirmedi).
        Assert.True(HasTrashUser(await ApiTestHost.JsonAsync(await TrashAsync(_b)), idB));
    }

    [Fact]
    public async Task Yanlis_Parolayla_Kullanici_Geri_Yuklenemez()
    {
        var id = await CreateUserAsync(_a, "parola_kapisi", _branchA);
        (await _a.DeleteAsync($"/api/users/{id}")).EnsureSuccessStatusCode();

        var r = await RestoreAsync(_a, id, "yanlis-parola");

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.True(HasTrashUser(await ApiTestHost.JsonAsync(await TrashAsync(_a)), id));
    }

    [Fact]
    public async Task Super_Admin_Kullanicisi_Firma_Admininin_Cop_Kutusunda_GORUNMEZ()
    {
        // Süper admin kayıtları kullanıcı listesinde olduğu gibi ÇÖP KUTUSUNDA da yalnız süper admine görünür.
        var svc = _host.Services.GetRequiredService<ServerServices>();
        var suId = svc.Users.EnsureInitialAdmin(CompanyA, "gizli_sa", Pass, RoleKeys.SuperAdmin);
        (await _super.DeleteAsync($"/api/users/{suId}")).EnsureSuccessStatusCode();

        var trashA = await ApiTestHost.JsonAsync(await TrashAsync(_a));

        Assert.False(HasTrashUser(trashA, suId));
        Assert.DoesNotContain("gizli_sa", trashA.ToString());
        Assert.True(ApiTestHost.IsDenied(await RestoreAsync(_a, suId)));
    }
}
