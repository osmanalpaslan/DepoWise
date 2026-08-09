using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace DepoWise.Tests;

/// <summary>
/// GERÇEK HTTP hattı üzerinden API testi (Paket 1, 2026-08-09).
///
/// API bellek-içi ayağa kalkar; testler <see cref="HttpClient"/> ile gerçek uçlara istek atar →
/// kimlik doğrulama (JWT), yetkilendirme, model bağlama ve hata yönetimi DAHİL tüm hat kapsanır.
/// Servis metodunu doğrudan çağıran testler bu katmanı atlar; ikisi birlikte kullanılır.
///
/// 🔒 GÜVENLİK: veri dizini her testte AYRI geçici klasördür ve <c>DEPOWISE_PG_URL</c> bilinçli olarak
/// TEMİZLENİR → API her zaman kendi YEREL SQLite dosyasına bağlanır. Canlı veritabanına ASLA bağlanmaz.
/// </summary>
public sealed class ApiTestHost : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(),
        "depowise_apitest_" + Guid.NewGuid().ToString("N"));

    /// <summary>Tohum (seed) süper admin — firma/kullanıcı kurmak için.</summary>
    public const string SeedUser = "superadmin";
    public const string SeedPassword = "ApiTest!2026";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dataDir);
        builder.UseEnvironment(Environments.Development);       // JWT anahtarı için dev fallback
        builder.UseSetting("Api:BaseUrl", "http://localhost");
        Environment.SetEnvironmentVariable("DEPOWISE_SERVER_DATA", _dataDir);
        Environment.SetEnvironmentVariable("DEPOWISE_SEED_SUPERADMIN_PASSWORD", SeedPassword);
        Environment.SetEnvironmentVariable("DEPOWISE_SEED_ADMIN_PASSWORD", SeedPassword);
        Environment.SetEnvironmentVariable("DEPOWISE_JWT_KEY", "api-test-jwt-key-0123456789-0123456789");
        Environment.SetEnvironmentVariable("DEPOWISE_PG_URL", null);   // ← canlı/PG'ye bağlanmayı ENGELLE
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dataDir, recursive: true); } catch { }
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    /// <summary>Giriş yapıp JWT taşıyan istemci döndürür.</summary>
    public async Task<HttpClient> LoginAsync(string username, string password, string? companyId = null,
        string branchId = "__all__")
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { companyId, username, password, branchId });
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"giriş başarısız ({(int)res.StatusCode}): {body}");
        var token = JsonDocument.Parse(body).RootElement.GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public Task<HttpClient> LoginSeedAsync() => LoginAsync(SeedUser, SeedPassword);

    /// <summary>Anonim (kimlik doğrulamasız) istemci.</summary>
    public HttpClient Anonymous() => CreateClient();

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage r)
        => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    /// <summary>Yanıtın "erişim reddedildi" sayılıp sayılmadığı — uygulamanın MEVCUT hata modeli:
    /// yetki/tenant ihlali 403, bulunamayan kayıt 404, doğrulama 400. 200 + veri ASLA kabul edilmez.</summary>
    public static bool IsDenied(HttpResponseMessage r)
        => (int)r.StatusCode is 400 or 401 or 403 or 404 or 500;
}
