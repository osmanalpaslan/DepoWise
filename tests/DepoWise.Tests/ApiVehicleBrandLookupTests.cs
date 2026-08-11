using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-02 (PRT-01 Grup 6, 2026-08-11) — WEB'DE ARAÇ MARKASI DÜZELTİLEMİYOR/SİLİNEMİYORDU.
///
/// Bulunan durum: web Tanım Düzenle ekranı araç markaları için <c>vehicle_brands</c> takma adını kullanıyor.
/// Listeleme (özel GET rotası) ve ekleme (POST switch) bu takma adı çeviriyordu; ama yeniden adlandır / sil /
/// kilitle uçları çevirmeden <c>LookupService</c>'e veriyordu ve gerçek tablo <c>brands</c> olduğu için
/// "Bilinmeyen tanım tablosu: vehicle_brands" → 400 dönüyordu. Masaüstü doğrudan "brands" kullandığından
/// çalışıyordu → parite kırıktı.
///
/// Bu testler DÖRT şeyi birlikte kilitler: (1) dört işlemin de takma adla çalıştığını, (2) beyaz listenin
/// GEVŞEMEDİĞİNİ, (3) kilitli tanım korumasının sürdüğünü, (4) tenant + yetki kapılarının yerinde olduğunu.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiVehicleBrandLookupTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CompanyA = "MRK-A";
    private const string UserA = "mrk_a";
    private const string StaffA = "mrk_a_personel";
    private const string CompanyB = "MRK-B";
    private const string UserB = "mrk_b";
    private const string Pass = "Test!2026";

    private HttpClient _a = null!;
    private string _branchA = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        var uidA = svc.Users.EnsureInitialAdmin(CompanyA, UserA, Pass, RoleKeys.CompanyAdmin);
        svc.Users.EnsureInitialAdmin(CompanyA, StaffA, Pass, RoleKeys.Staff);   // yetkisiz (izin satırı yok)
        svc.Users.EnsureInitialAdmin(CompanyB, UserB, Pass, RoleKeys.CompanyAdmin);

        // Personel girişi "Tüm Şubeler" ile yapılamaz (o yetki yalnız süper adminde) → gerçek bir şube gerekir.
        var sa = new SessionContext(uidA, CompanyA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _branchA = svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("A Şube"));

        _a = await _host.LoginAsync(UserA, Pass, CompanyA);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task<string> AddBrandAsync(HttpClient c, string name)
    {
        var r = await c.PostAsJsonAsync("/api/lookups/vehicle_brands", new { name });
        r.EnsureSuccessStatusCode();
        return (await ApiTestHost.JsonAsync(r)).GetProperty("id").GetString()!;
    }

    private async Task<System.Text.Json.JsonElement> ListBrandsAsync(HttpClient c)
        => await ApiTestHost.JsonAsync(await c.GetAsync("/api/lookups/vehicle_brands"));

    [Fact]
    public async Task Arac_Markasi_Olusturulur_Ve_Listelenir()
    {
        await AddBrandAsync(_a, "Caterpillar");
        Assert.Contains("Caterpillar", (await ListBrandsAsync(_a)).ToString());
    }

    [Fact]
    public async Task Arac_Markasi_YENIDEN_ADLANDIRILIR()
    {
        var id = await AddBrandAsync(_a, "Komatzu");

        var r = await _a.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}", new { name = "Komatsu" });

        r.EnsureSuccessStatusCode();
        var raw = (await ListBrandsAsync(_a)).ToString();
        Assert.Contains("Komatsu", raw);
        Assert.DoesNotContain("Komatzu", raw);
    }

    [Fact]
    public async Task Arac_Markasi_SILINIR()
    {
        var id = await AddBrandAsync(_a, "Silinecek");

        (await _a.DeleteAsync($"/api/lookups/vehicle_brands/{id}")).EnsureSuccessStatusCode();

        Assert.DoesNotContain("Silinecek", (await ListBrandsAsync(_a)).ToString());
    }

    [Fact]
    public async Task Arac_Markasi_KILITLENIR_Ve_Kilit_Listede_GORUNUR()
    {
        var id = await AddBrandAsync(_a, "Sabit Marka");

        (await _a.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}/lock", new { locked = true }))
            .EnsureSuccessStatusCode();

        // Kilit bayrağı marka LİSTESİNDE de görünmeli — aksi halde arayüz düzenlenebilir sanır.
        var row = (await ListBrandsAsync(_a)).EnumerateArray()
            .First(e => e.GetProperty("id").GetString() == id);
        Assert.True(row.GetProperty("isLocked").GetBoolean());
    }

    [Fact]
    public async Task Kilitli_Arac_Markasi_Duzenlenemez_Ve_Silinemez()
    {
        var id = await AddBrandAsync(_a, "Kilitli");
        (await _a.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}/lock", new { locked = true }))
            .EnsureSuccessStatusCode();

        var rename = await _a.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}", new { name = "Yeni Ad" });
        var del = await _a.DeleteAsync($"/api/lookups/vehicle_brands/{id}");

        Assert.Equal(HttpStatusCode.BadRequest, rename.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, del.StatusCode);

        // Kilit AÇILINCA yeniden düzenlenebilmeli (kilit kalıcı bir engel değildir).
        (await _a.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}/lock", new { locked = false }))
            .EnsureSuccessStatusCode();
        (await _a.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}", new { name = "Yeni Ad" }))
            .EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Yetkisiz_Kullanici_Arac_Markasi_Yazamaz()
    {
        var id = await AddBrandAsync(_a, "Korunan");
        var staff = await _host.LoginAsync(StaffA, Pass, CompanyA, _branchA);

        Assert.True(ApiTestHost.IsDenied(await staff.PostAsJsonAsync("/api/lookups/vehicle_brands", new { name = "X" })));
        Assert.True(ApiTestHost.IsDenied(await staff.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}", new { name = "X" })));
        Assert.True(ApiTestHost.IsDenied(await staff.DeleteAsync($"/api/lookups/vehicle_brands/{id}")));
        // Kilit YALNIZ admin: personel değiştiremez.
        Assert.True(ApiTestHost.IsDenied(await staff.PutAsJsonAsync($"/api/lookups/vehicle_brands/{id}/lock", new { locked = true })));
    }

    [Fact]
    public async Task Baska_Firmanin_Arac_Markasi_Etkilenmez()
    {
        var b = await _host.LoginAsync(UserB, Pass, CompanyB);
        var idB = await AddBrandAsync(b, "B FIRMASI MARKA");

        // A firması B'nin markasını göremez ve silme/adlandırma isteği B'nin satırına DOKUNMAZ
        // (sorgular company_id ile süzülü → 0 satır etkilenir, hata da vermez).
        Assert.DoesNotContain("B FIRMASI MARKA", (await ListBrandsAsync(_a)).ToString());
        await _a.PutAsJsonAsync($"/api/lookups/vehicle_brands/{idB}", new { name = "CALINDI" });
        await _a.DeleteAsync($"/api/lookups/vehicle_brands/{idB}");

        var rawB = (await ListBrandsAsync(b)).ToString();
        Assert.Contains("B FIRMASI MARKA", rawB);
        Assert.DoesNotContain("CALINDI", rawB);
    }

    [Fact]
    public async Task Takma_Ad_Beyaz_Listeyi_GEVSETMEZ()
    {
        // Çeviri YALNIZ "vehicle_brands" içindir; uydurma tablo adı hâlâ reddedilir.
        var r = await _a.DeleteAsync("/api/lookups/users/abc");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);

        // Şube yazımının tanımlar üzerinden yapılamaması kuralı da yerinde kalır.
        var br = await _a.PutAsJsonAsync("/api/lookups/branches/abc", new { name = "X" });
        Assert.Equal(HttpStatusCode.Forbidden, br.StatusCode);
    }

    [Fact]
    public async Task Arac_Markasi_brand_type_ARAC_Olarak_Yazilir()
    {
        // Takma ad çevirisi brand_type ayrımını BOZMAMALI: araç markası, araç marka listesinde görünür.
        // (Malzeme marka listesinin /api/lookups/brands ile TÜM markaları döndürmesi ayrı bir konudur —
        //  G6-20 olarak raporlandı, bu turun kapsamında DEĞİL; bu yüzden burada iddia edilmiyor.)
        var id = await AddBrandAsync(_a, "SADECE ARAC");
        var vehicle = await ListBrandsAsync(_a);
        Assert.Contains(vehicle.EnumerateArray(), e => e.GetProperty("id").GetString() == id);
    }
}
