using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G6-20 (PRT-01 Grup 6, 2026-08-11) — MALZEME MARKA LİSTESİ TÜRE GÖRE SÜZÜLMÜYORDU.
///
/// Bulunan durum: <c>GET /api/lookups/brands</c> genel <c>/api/lookups/{table}</c> rotasına düşüyor,
/// o da <c>LookupService.List</c> ile TÜM <c>brands</c> satırlarını döndürüyordu → web'de "Malzemeler →
/// Marka" listesinde ARAÇ markaları da görünüyordu. Masaüstü aynı ekranda <c>ListBrands(s, "material")</c>
/// kullandığı için doğru davranıyordu → web ↔ masaüstü parite hatası.
///
/// Düzeltme, dosyadaki mevcut emsalin simetriğidir: araç markaları için zaten özel bir rota vardı
/// (<c>/api/lookups/vehicle_brands</c> → <c>ListBrands(s, "vehicle")</c>); malzeme için de aynısı eklendi.
/// Servis sözleşmesi (<c>List</c>) DEĞİŞMEDİ; yalnız bu ucun hangi metodu çağırdığı düzeltildi.
///
/// Bu testler dört şeyi birlikte kilitler: (1) malzeme listesinde yalnız malzeme markaları,
/// (2) araç markasının sızmaması, (3) TÜRSÜZ eski kayıtların kaybolmaması, (4) araç marka ekranının
/// davranışının bozulmaması.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiMaterialBrandLookupTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "MMRK";
    private const string Admin = "mmrk_admin";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _c = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        _svc.Users.EnsureInitialAdmin(Company, Admin, Pass, RoleKeys.CompanyAdmin);
        _c = await _host.LoginAsync(Admin, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task AddMaterialBrandAsync(string name)
        => (await _c.PostAsJsonAsync("/api/lookups/brands", new { name })).EnsureSuccessStatusCode();

    private async Task AddVehicleBrandAsync(string name)
        => (await _c.PostAsJsonAsync("/api/lookups/vehicle_brands", new { name })).EnsureSuccessStatusCode();

    private async Task<List<string>> MaterialBrandNamesAsync()
        => (await ApiTestHost.JsonAsync(await _c.GetAsync("/api/lookups/brands")))
            .EnumerateArray().Select(e => e.GetProperty("name").GetString() ?? "").ToList();

    private async Task<List<string>> VehicleBrandNamesAsync()
        => (await ApiTestHost.JsonAsync(await _c.GetAsync("/api/lookups/vehicle_brands")))
            .EnumerateArray().Select(e => e.GetProperty("name").GetString() ?? "").ToList();

    [Fact]
    public async Task Malzeme_Marka_Listesi_ARAC_Markasini_ICERMEZ()
    {
        await AddMaterialBrandAsync("Bosch");
        await AddVehicleBrandAsync("Caterpillar");

        var material = await MaterialBrandNamesAsync();

        Assert.Contains("Bosch", material);
        Assert.DoesNotContain("Caterpillar", material);   // G6-20'nin ta kendisi
    }

    [Fact]
    public async Task Arac_Marka_Listesi_MALZEME_Markasini_ICERMEZ()
    {
        // Simetri kontrolü: düzeltme araç tarafını da bulandırmamalı (zaten süzülüydü, bozulmadığı kilitlenir).
        await AddMaterialBrandAsync("Bosch");
        await AddVehicleBrandAsync("Caterpillar");

        var vehicle = await VehicleBrandNamesAsync();

        Assert.Contains("Caterpillar", vehicle);
        Assert.DoesNotContain("Bosch", vehicle);
    }

    [Fact]
    public async Task Mevcut_Malzeme_Markalari_Listelenmeye_DEVAM_EDER()
    {
        await AddMaterialBrandAsync("Bosch");
        await AddMaterialBrandAsync("Makita");
        await AddMaterialBrandAsync("Hilti");
        await AddVehicleBrandAsync("Komatsu");

        var material = await MaterialBrandNamesAsync();

        Assert.Equal(new[] { "Bosch", "Hilti", "Makita" }, material.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Ayni_Ad_Hem_Malzeme_Hem_Arac_Markasi_Olabilir_Ve_KARISMAZ()
    {
        // Şema aynı adın iki türde ayrı satır olmasına izin verir:
        // ux_brands(company_id, brand_type, name) — Migration005. Süzme SATIR bazında çalışmalı,
        // ad bazında değil; iki liste de kendi kaydını göstermeli.
        await AddMaterialBrandAsync("Ortak Ad");
        await AddVehicleBrandAsync("Ortak Ad");

        var material = (await ApiTestHost.JsonAsync(await _c.GetAsync("/api/lookups/brands")))
            .EnumerateArray().Where(e => e.GetProperty("name").GetString() == "Ortak Ad").ToList();
        var vehicle = (await ApiTestHost.JsonAsync(await _c.GetAsync("/api/lookups/vehicle_brands")))
            .EnumerateArray().Where(e => e.GetProperty("name").GetString() == "Ortak Ad").ToList();

        Assert.Single(material);   // her listede TEK kayıt (karşı türün satırı sızmıyor)
        Assert.Single(vehicle);
        Assert.NotEqual(material[0].GetProperty("id").GetString(), vehicle[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Malzeme_Marka_Listesi_Kilit_Bayragini_Tasir()
    {
        // Tanım Düzenle ekranı "sabit tanım" rozetini bu alandan okur; süzme eklenirken kaybolmamalı.
        await AddMaterialBrandAsync("Sabitlenecek");
        var row = (await ApiTestHost.JsonAsync(await _c.GetAsync("/api/lookups/brands")))
            .EnumerateArray().First(e => e.GetProperty("name").GetString() == "Sabitlenecek");
        var id = row.GetProperty("id").GetString();
        Assert.False(row.GetProperty("isLocked").GetBoolean());

        (await _c.PutAsJsonAsync($"/api/lookups/brands/{id}/lock", new { locked = true })).EnsureSuccessStatusCode();

        var after = (await ApiTestHost.JsonAsync(await _c.GetAsync("/api/lookups/brands")))
            .EnumerateArray().First(e => e.GetProperty("id").GetString() == id);
        Assert.True(after.GetProperty("isLocked").GetBoolean());
    }

    [Fact]
    public async Task Malzeme_Markasi_Silinince_Listeden_Duser()
    {
        // Yazma uçları genel rotada kaldı (POST/PUT/DELETE) — yeni GET rotası onları etkilememeli.
        await AddMaterialBrandAsync("Silinecek");
        var id = (await ApiTestHost.JsonAsync(await _c.GetAsync("/api/lookups/brands")))
            .EnumerateArray().First(e => e.GetProperty("name").GetString() == "Silinecek").GetProperty("id").GetString();

        (await _c.DeleteAsync($"/api/lookups/brands/{id}")).EnsureSuccessStatusCode();

        Assert.DoesNotContain("Silinecek", await MaterialBrandNamesAsync());
    }
}
