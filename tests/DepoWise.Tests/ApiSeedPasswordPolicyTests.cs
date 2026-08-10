using System.Net.Http.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// GUV-01 — TOHUM HESAP PAROLA ZORUNLULUĞU, GERÇEK HTTP HATTI (2026-08-10).
///
/// Servis testleri (<see cref="SeedPasswordPolicyTests"/>) bayrağın veri katmanında yazıldığını kanıtlar.
/// Arayüzler bunu servisten değil, <c>/api/auth/login</c> yanıtındaki <c>mustChangePassword</c>
/// alanından öğrenir (Login.razor → 4. adım · LoginViewModel → ilk giriş ekranı). Bu testler o alanın
/// SUNUCU TOHUMLAMASINDAN sonra gerçekten geldiğini ve şifre belirlendikten sonra düştüğünü doğrular.
///
/// <see cref="ApiTestHost"/> üretim tohumlama yolunu (<c>ServerServices.EnsureSeedAdmins</c>) kullanır →
/// burada test edilen şey gerçek üretim davranışıdır.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiSeedPasswordPolicyTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();

    public Task InitializeAsync() { _ = _host.CreateClient(); return Task.CompletedTask; }
    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private async Task<System.Text.Json.JsonElement> LoginAsync(string user, string password)
    {
        var client = _host.CreateClient();
        var r = await client.PostAsJsonAsync("/api/auth/login",
            new { companyId = "DEPOWISE", username = user, password });
        r.EnsureSuccessStatusCode();
        return await ApiTestHost.JsonAsync(r);
    }

    [Fact]
    public async Task Tohumlanan_SuperAdmin_Ilk_Giriste_Sifre_Degisimi_Ister()
    {
        var body = await LoginAsync(ApiTestHost.SeedUser, ApiTestHost.SeedPassword);

        // Eskiden bu alan false geliyordu → geçici/tohum parola kurulumda kalıcı olabiliyordu.
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
        // Bayrak KİLİTLEMEZ: oturum yine kurulur, istemci şifre ekranını gösterir.
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Tohumlanan_FirmaAdmini_Ilk_Giriste_Sifre_Degisimi_Ister()
    {
        var body = await LoginAsync("admin", ApiTestHost.SeedPassword);

        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Sifre_Belirlendikten_Sonra_Zorunluluk_Duser_Ve_Eski_Parola_Gecersizdir()
    {
        var first = await LoginAsync(ApiTestHost.SeedUser, ApiTestHost.SeedPassword);
        Assert.True(first.GetProperty("mustChangePassword").GetBoolean());

        // Kullanıcı AYNI login ekranından yeni şifresini belirler (mevcut akış).
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", first.GetProperty("token").GetString());
        const string newPassword = "YeniKaliciParola!2026";
        var set = await client.PostAsJsonAsync("/api/auth/change-initial-password", new { newPassword });
        set.EnsureSuccessStatusCode();
        Assert.False((await ApiTestHost.JsonAsync(set)).GetProperty("mustChangePassword").GetBoolean());

        // Yeni parolayla giriş: artık sorulmaz.
        var second = await LoginAsync(ApiTestHost.SeedUser, newPassword);
        Assert.False(second.GetProperty("mustChangePassword").GetBoolean());

        // Konsola bir kez yazılan tohum parolası artık çalışmamalı.
        var old = await _host.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { companyId = "DEPOWISE", username = ApiTestHost.SeedUser, password = ApiTestHost.SeedPassword });
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, old.StatusCode);
    }
}
