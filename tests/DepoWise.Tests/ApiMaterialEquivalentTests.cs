using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// MUADİL UZLAŞTIRMASI — GERÇEK HTTP HATTI (G2-03, 2026-08-10).
///
/// Servis testleri (<see cref="MaterialTests"/>: çift yönlülük, kendine muadil, döngü, tenant) çekirdeği
/// kanıtlar. Ama <c>PUT /api/materials/{id}</c> muadil listesini UZUN SÜRE HİÇ İŞLEMİYORDU: web tam
/// düzenleme formunda muadil ekleyip kaydetmek sessizce hiçbir şey yapmıyordu. Bu testler zincirin
/// tamamını kapsar: JSON gövdedeki <c>equivalentIds</c> bağlanıyor mu, uzlaştırma doğru mu, çift yön
/// temizleniyor mu, tenant koruması sürüyor mu, düzenleme kilidi (G2-02) bypass ediliyor mu.
///
/// ⚠️ EN KRİTİK KURAL — <b>null ≠ boş liste</b>:
///   null → muadillere DOKUNMA (hızlı düzenleme pencereleri bu alanı göndermez)
///   []   → TÜM muadilleri kaldır
/// Bu ayrım bozulursa her hızlı kaydetme kullanıcının muadillerini siler.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiMaterialEquivalentTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "MUADIL-A";
    private const string Other = "MUADIL-B";
    private const string User = "muadil_kullanici";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;
    private ServerServices _svc = null!;
    private SessionContext _s = null!;
    private string _m1 = "", _m2 = "", _m3 = "", _m4 = "", _yabanci = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();

        Company_(Company);
        Company_(Other);

        var uid = _svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _m1 = _svc.Materials.Create(_s, new NewMaterial("EQ-1", "Bir"));
        _m2 = _svc.Materials.Create(_s, new NewMaterial("EQ-2", "İki"));
        _m3 = _svc.Materials.Create(_s, new NewMaterial("EQ-3", "Üç"));
        _m4 = _svc.Materials.Create(_s, new NewMaterial("EQ-4", "Dört"));

        // Başka firmanın malzemesi (tenant testi için)
        var otherUid = _svc.Users.EnsureInitialAdmin(Other, "muadil_b", Pass, RoleKeys.CompanyAdmin);
        var os = new SessionContext(otherUid, Other, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _yabanci = _svc.Materials.Create(os, new NewMaterial("EQ-X", "Yabancı"));

        // Başlangıç: M1 ↔ M2 ve M1 ↔ M3 (DOĞRUDAN bağlar — transitif belirsizlik olmasın)
        _svc.Materials.AddEquivalent(_s, _m1, _m2);
        _svc.Materials.AddEquivalent(_s, _m1, _m3);

        _client = await _host.LoginAsync(User, Pass, Company);
    }

    private void Company_(string id)
    {
        using var conn = _svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    /// <summary>Servisten okunan muadil grubu (kaydın kendisi hariç) — sıralı, karşılaştırılabilir.</summary>
    private string[] Group(string materialId) =>
        _svc.Materials.GetEquivalentGroup(materialId).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private async Task<long> VersionAsync(string materialId)
    {
        var r = await _client.GetAsync($"/api/materials/{materialId}");
        r.EnsureSuccessStatusCode();
        var j = await ApiTestHost.JsonAsync(r);
        return j.GetProperty("version").GetInt64();
    }

    private async Task<string> NameAsync(string materialId)
    {
        var r = await _client.GetAsync($"/api/materials/{materialId}");
        r.EnsureSuccessStatusCode();
        var j = await ApiTestHost.JsonAsync(r);
        return j.GetProperty("name").GetString() ?? "";
    }

    /// <summary>Web tam düzenleme formunun gönderdiği gövde. <paramref name="equivalentIds"/> null
    /// verilirse alan JSON'da yine null gider → sunucu "dokunma" olarak yorumlar.</summary>
    private Task<HttpResponseMessage> PutAsync(string name, string[]? equivalentIds, long? version = null) =>
        _client.PutAsJsonAsync($"/api/materials/{_m1}", new
        {
            code = "EQ-1", name, type = (string?)null, categoryId = (string?)null, unitId = (string?)null,
            brandId = (string?)null, supplierId = (string?)null, minStock = 0m, unitPrice = 0m,
            description = (string?)null, openingStock = 0m,
            vehicleIds = (string[]?)null, equivalentIds, templateId = (string?)null, version
        });

    // ── T1: null → DOKUNMA (hızlı düzenleme penceresinin gövdesi budur) ────────────────────

    [Fact]
    public async Task Muadil_NULL_gonderilirse_mevcut_muadiller_KORUNUR()
    {
        var oncesi = Group(_m1);
        Assert.Equal(2, oncesi.Length);   // M2 + M3

        (await PutAsync("Ad degisti", equivalentIds: null)).EnsureSuccessStatusCode();

        Assert.Equal(oncesi, Group(_m1));            // muadiller AYNEN duruyor
        Assert.Equal("Ad degisti", await NameAsync(_m1));   // ana güncelleme yine de uygulandı
    }

    // ── T2: boş liste → HEPSİNİ KALDIR (iki yönde) ────────────────────────────────────────

    [Fact]
    public async Task Muadil_BOS_liste_gonderilirse_hepsi_KALKAR_ve_ters_yon_de_temizlenir()
    {
        (await PutAsync("Ad", equivalentIds: System.Array.Empty<string>())).EnsureSuccessStatusCode();

        Assert.Empty(Group(_m1));
        Assert.Empty(Group(_m2));   // ters yön de silindi
        Assert.Empty(Group(_m3));
    }

    // ── T3: uzlaştırma — [M2,M3] → [M2,M4] ────────────────────────────────────────────────

    [Fact]
    public async Task Muadil_listesi_UZLASTIRILIR_kalan_korunur_cikan_silinir_yeni_eklenir()
    {
        (await PutAsync("Ad", new[] { _m2, _m4 })).EnsureSuccessStatusCode();

        Assert.Equal(new[] { _m2, _m4 }.OrderBy(x => x, StringComparer.Ordinal).ToArray(), Group(_m1));
        Assert.Contains(_m1, Group(_m2));    // M2 korundu (çift yönlü)
        Assert.Contains(_m1, Group(_m4));    // M4 eklendi (çift yönlü)
        Assert.Empty(Group(_m3));            // M3 çıkarıldı — ters yön de temizlendi
    }

    [Fact]
    public async Task Muadil_ayni_id_iki_kez_gonderilse_de_tek_baglanti_olusur()
    {
        (await PutAsync("Ad", new[] { _m2, _m2, _m2 })).EnsureSuccessStatusCode();
        Assert.Equal(new[] { _m2 }, Group(_m1));
    }

    // ── T4: firma izolasyonu — hiçbir şey yazılmamalı ─────────────────────────────────────

    [Fact]
    public async Task Muadil_BASKA_FIRMA_malzemesi_reddedilir_ve_HICBIRI_yazilmaz()
    {
        var oncesi = Group(_m1);

        // Geçerli bir id (M4) ile yabancı id birlikte gönderilir: atomiklik kanıtı.
        var r = await PutAsync("Ad", new[] { _m4, _yabanci });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        Assert.Equal(oncesi, Group(_m1));       // eski liste bozulmadı
        Assert.DoesNotContain(_m4, Group(_m1)); // geçerli olan da YAZILMADI (yarım liste yok)
    }

    // ── T5: düzenleme kilidi (G2-02) bypass edilmiyor ─────────────────────────────────────

    [Fact]
    public async Task Muadil_ESKI_surumle_gonderilirse_409_doner_ve_HICBIR_sey_degismez()
    {
        var eskiSurum = await VersionAsync(_m1);
        var oncekiAd = await NameAsync(_m1);
        var oncekiGrup = Group(_m1);

        // Başkası araya giriyor (sürümsüz kaydeder → sürüm artar)
        (await PutAsync("B kaydetti", equivalentIds: null)).EnsureSuccessStatusCode();

        // Biz hâlâ eski sürümle, üstelik muadilleri de değiştirmek istiyoruz
        var r = await PutAsync("Bizim eski verimiz", new[] { _m4 }, version: eskiSurum);
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);

        // Muadiller değişmedi (Update önce çalışıp 409 attığı için SetEquivalents'a HİÇ gelinmedi)
        Assert.Equal(oncekiGrup, Group(_m1));
        Assert.DoesNotContain(_m4, Group(_m1));
        // Ana alan da bizim verimizle EZİLMEDİ (araya girenin verisi duruyor)
        Assert.Equal("B kaydetti", await NameAsync(_m1));
        Assert.NotEqual(oncekiAd, await NameAsync(_m1));
    }
}
