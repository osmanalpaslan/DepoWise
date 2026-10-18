using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-11 (AR-GE 2026-10-18, 05 F-041 / F-051) — BİR KİŞİNİN HATASI TÜM WEB KULLANICILARINI KİLİTLEMESİN ═══
///  IAD1 — İmza: doğru anahtar + adres kabul; yanlış anahtar, değiştirilmiş adres, IP olmayan değer, anahtarsız → yok sayılır
///  IAD2 — Anahtar tanımlıyken giriş sınırı İLETİLEN adrese göre: A dolunca B girişe devam eder
///  IAD3 — Sahte imza / anahtar tanımsız → başlık yok sayılır (herkes aynı adres sayılır, bugünkü davranış)
///  IAD4 — sync-login aynı yanıtta çalışan jeton döner, paket alanları aynı; kilitli kullanıcıya jeton verilmez
///  IAD5 — Bağlantılar (web işleyici, App/Routes, masaüstü tek doğrulama) yerinde (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class IstemciAdresiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Anahtar = "m11-test-anahtari-0123456789";
    private const string Company = "IAD-A", User = "iad_kullanici", Pass = "Test!2026";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        using (var conn = Svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        Svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable(IstemciAdresiImzasi.AnahtarDegiskeni, null);
        await ((IAsyncLifetime)_host).DisposeAsync();
    }

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    private async Task<HttpStatusCode> HataliGiris(string? adres, string? imza)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        { Content = JsonContent.Create(new { username = "yok_boyle_biri", password = "yanlis" }) };
        if (adres is not null) req.Headers.TryAddWithoutValidation(IstemciAdresiImzasi.AdresBasligi, adres);
        if (imza is not null) req.Headers.TryAddWithoutValidation(IstemciAdresiImzasi.ImzaBasligi, imza);
        return (await _host.CreateClient().SendAsync(req)).StatusCode;
    }

    private static string Imza(string adres) => IstemciAdresiImzasi.Imzala(Anahtar, adres);

    [Fact]
    public void IAD1_Imza_Dogrulama()
    {
        Assert.Equal("203.0.113.7", IstemciAdresiImzasi.Dogrula(Anahtar, "203.0.113.7", Imza("203.0.113.7")));
        Assert.Equal("2001:db8::1", IstemciAdresiImzasi.Dogrula(Anahtar, " 2001:db8::1 ", Imza("2001:db8::1").ToLowerInvariant()));
        Assert.Null(IstemciAdresiImzasi.Dogrula("baska-anahtar-0123456789", "203.0.113.7", Imza("203.0.113.7")));
        Assert.Null(IstemciAdresiImzasi.Dogrula(Anahtar, "203.0.113.8", Imza("203.0.113.7")));      // adres değiştirilmiş
        Assert.Null(IstemciAdresiImzasi.Dogrula(Anahtar, "evil", Imza("evil")));                    // IP değil
        Assert.Null(IstemciAdresiImzasi.Dogrula(null, "203.0.113.7", Imza("203.0.113.7")));        // özellik kapalı
        Assert.Null(IstemciAdresiImzasi.Dogrula(Anahtar, "203.0.113.7", null));
        Assert.Null(IstemciAdresiImzasi.Dogrula(Anahtar, "203.0.113.7", "00"));

        Environment.SetEnvironmentVariable(IstemciAdresiImzasi.AnahtarDegiskeni, "kisa");
        Assert.Null(IstemciAdresiImzasi.Anahtar());                                                  // 16 karakterden kısa → kapalı
        Environment.SetEnvironmentVariable(IstemciAdresiImzasi.AnahtarDegiskeni, Anahtar);
        Assert.Equal(Anahtar, IstemciAdresiImzasi.Anahtar());
    }

    [Fact]
    public async Task IAD2_Sinir_Iletilen_Adrese_Gore()
    {
        Environment.SetEnvironmentVariable(IstemciAdresiImzasi.AnahtarDegiskeni, Anahtar);
        for (int i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, await HataliGiris("198.51.100.1", Imza("198.51.100.1")));
        Assert.Equal(HttpStatusCode.TooManyRequests, await HataliGiris("198.51.100.1", Imza("198.51.100.1")));   // A doldu
        Assert.Equal(HttpStatusCode.Unauthorized, await HataliGiris("198.51.100.2", Imza("198.51.100.2")));      // B etkilenmez
        Assert.Equal(HttpStatusCode.Unauthorized, await HataliGiris(null, null));                                // web sunucusunun kendisi de
        var c = await _host.LoginAsync(User, Pass, Company);                                                     // gerçek kullanıcı girer
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task IAD3_Sahte_Imza_Ve_Anahtarsiz_Yok_Sayilir()
    {
        // Anahtar tanımlı, imza sahte → başlık yok sayılır: 30 deneme ortak adrese yazılır, başlıksız istek de 429
        Environment.SetEnvironmentVariable(IstemciAdresiImzasi.AnahtarDegiskeni, Anahtar);
        for (int i = 0; i < 30; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, await HataliGiris($"192.0.2.{i + 1}", "DEADBEEF"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await HataliGiris(null, null));
        // Anahtar tanımsızken DOĞRU imza bile yok sayılır (özellik kapalı = bugünkü davranış)
        Environment.SetEnvironmentVariable(IstemciAdresiImzasi.AnahtarDegiskeni, null);
        Assert.Equal(HttpStatusCode.TooManyRequests, await HataliGiris("198.51.100.9", Imza("198.51.100.9")));
    }

    [Fact]
    public async Task IAD4_SyncLogin_Jeton_Doner_Kilitliye_Vermez()
    {
        var r = await _host.CreateClient().PostAsJsonAsync("/api/auth/sync-login", new { companyId = "", username = User, password = Pass });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var kok = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        foreach (var alan in new[] { "companyId", "userId", "username", "passwordHash", "roleKeys", "permissions", "buttons", "mustChangePassword" })
            Assert.True(kok.TryGetProperty(alan, out _), "paket alanı eksik: " + alan);              // eski masaüstleri bozulmaz
        var c = _host.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kok.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync("/api/auth/refresh", null)).StatusCode);

        // 5 hatalı /login → kullanıcı adı kilitli: sync-login paketi yine döner ama JETON VERİLMEZ (kilit atlanamaz)
        for (int i = 0; i < AuthServiceMaxFailures; i++)
            await _host.CreateClient().PostAsJsonAsync("/api/auth/login", new { companyId = Company, username = User, password = "yanlis" });
        var k = await _host.CreateClient().PostAsJsonAsync("/api/auth/sync-login", new { companyId = "", username = User, password = Pass });
        Assert.Equal(HttpStatusCode.OK, k.StatusCode);
        Assert.False(JsonDocument.Parse(await k.Content.ReadAsStringAsync()).RootElement.TryGetProperty("token", out _));
    }

    private const int AuthServiceMaxFailures = DepoWise.Infrastructure.Security.AuthService.MaxFailures;

    [Fact]
    public void IAD5_Baglantilar_Yerinde()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir }.Concat(p).ToArray()));

        var webProgram = Oku("src", "DepoWise.Web", "Program.cs");
        Assert.Contains("new IstemciAdresiIsleyici(sp.GetRequiredService<IstemciBaglami>(), istemciAnahtari", webProgram);
        Assert.Contains("builder.Services.AddScoped<IstemciBaglami>();", webProgram);
        Assert.Contains("<Routes IstemciAdresi=\"@IstemciAdresi\"", Oku("src", "DepoWise.Web", "Components", "App.razor"));
        Assert.Contains("Istemci.Adres = IstemciAdresi", Oku("src", "DepoWise.Web", "Components", "Routes.razor"));
        Assert.Contains("Common\\BaglantiIzleyici.cs", Oku("src", "DepoWise.Web", "DepoWise.Web.csproj"));   // imza kodu web'e bağlı
        Assert.Contains("IstemciAdresiImzasi.Dogrula(", Oku("src", "DepoWise.Api", "Program.cs"));

        Assert.Contains("StoreTokenAsync(baseUrl, username, password, YanittakiJeton(body))", Oku("src", "DepoWise.Desktop", "ServerAuthClient.cs"));
        Assert.Contains("ServerAuthClient.JetonFor(baseUrl, DesktopServices.Session?.UserId)", Oku("src", "DepoWise.Desktop", "LookupSyncService.cs"));
    }
}
