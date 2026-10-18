using System.Net;
using System.Security.Cryptography;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-13 (AR-GE 2026-10-18) — KÜÇÜK GÜVENLİK DÜZELTMELERİ (F-107, F-110, F-050) ═══
///  GKD1 — Yeni özet 600.000 tur; eski (100.000) özet doğrulanır ve "yeniden özetlenmeli" sayılır
///  GKD2 — Eski özetli kullanıcı doğru şifreyle girince özet yeni turla yazılır; yanlış şifre özete dokunmaz
///  GKD3 — Masaüstü senkron girişi paketi yeni özeti taşır (yerel kopya da güçlenir)
///  GKD4 — CORS yalnız web kökenine izin verir; yabancı kökene izin başlığı dönmez
///  GKD5 — 32 bayttan kısa JWT anahtarıyla API açılmaz
///  GKD6 — Log maskeleme: bağlantı dizesi parolası ve Bearer jetonu düz yazılmaz; maskeleme loglara bağlı (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class GuvenlikKucukDuzeltmeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "GKD-A", User = "gkd_kullanici", Pass = "Test!2026";
    private string _uid = "";

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
        _uid = Svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        await Task.CompletedTask;
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    /// <summary>M-13 öncesi PasswordHasher'ın ürettiği biçimle (100.000 tur) özet.</summary>
    private static string EskiOzet(string sifre)
    {
        var tuz = RandomNumberGenerator.GetBytes(16);
        var h = Rfc2898DeriveBytes.Pbkdf2(sifre, tuz, 100_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2$sha256$100000${Convert.ToBase64String(tuz)}${Convert.ToBase64String(h)}";
    }

    private string DbOzet()
    {
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM users WHERE id=@u;";
        cmd.AddWithValue("@u", _uid);
        return (string)cmd.ExecuteScalar()!;
    }

    private void OzetYaz(string ozet)
    {
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET password_hash=@h WHERE id=@u;";
        cmd.AddWithValue("@h", ozet);
        cmd.AddWithValue("@u", _uid);
        cmd.ExecuteNonQuery();
    }

    private static int Tur(string ozet) => int.Parse(ozet.Split('$')[2]);

    [Fact]
    public void GKD1_Yeni_Ozet_600Bin_Eski_Ozet_Gecerli()
    {
        var yeni = PasswordHasher.Hash(Pass);
        Assert.Equal(600_000, Tur(yeni));
        Assert.True(PasswordHasher.Verify(Pass, yeni));
        Assert.False(PasswordHasher.YenidenOzetlenmeli(yeni));

        var eski = EskiOzet(Pass);
        Assert.True(PasswordHasher.Verify(Pass, eski));                  // mevcut kullanıcılar girmeye devam eder
        Assert.False(PasswordHasher.Verify("yanlis", eski));
        Assert.True(PasswordHasher.YenidenOzetlenmeli(eski));
        Assert.False(PasswordHasher.YenidenOzetlenmeli("bozuk"));
    }

    [Fact]
    public void GKD2_Girişte_Eski_Ozet_Yenilenir_Yanlis_Sifre_Dokunmaz()
    {
        var eski = EskiOzet(Pass);
        OzetYaz(eski);

        Assert.False(Svc.Auth.Login(Company, User, "yanlis").Success);
        Assert.Equal(eski, DbOzet());                                     // yanlış şifre → özet aynı

        Assert.True(Svc.Auth.Login(Company, User, Pass).Success);
        var yeni = DbOzet();
        Assert.Equal(600_000, Tur(yeni));
        Assert.True(PasswordHasher.Verify(Pass, yeni));
        Assert.True(Svc.Auth.Login(Company, User, Pass).Success);       // yeni özetle giriş
        Assert.Equal(yeni, DbOzet());                                     // güncel özet tekrar yazılmaz
    }

    [Fact]
    public void GKD3_Senkron_Girisi_Paketi_Yeni_Ozeti_Tasir()
    {
        OzetYaz(EskiOzet(Pass));
        var paket = Svc.Auth.ExportForSync("", User, Pass);
        Assert.NotNull(paket);
        Assert.Equal(600_000, Tur(paket!.PasswordHash));
        Assert.Equal(DbOzet(), paket.PasswordHash);
        Assert.True(PasswordHasher.Verify(Pass, paket.PasswordHash));
    }

    [Theory]
    [InlineData("https://depowise-web.fly.dev", true)]
    [InlineData("https://kotu-site.example", false)]
    [InlineData("http://depowise-web.fly.dev", false)]
    public async Task GKD4_Cors_Yalniz_Web_Kokeni(string koken, bool izinli)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        req.Headers.Add("Origin", koken);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Headers", "content-type");
        var r = await _host.CreateClient().SendAsync(req);
        var izin = r.Headers.TryGetValues("Access-Control-Allow-Origin", out var v) ? v.Single() : null;
        if (izinli) Assert.Equal(koken, izin);
        else Assert.Null(izin);
    }

    /// <summary>Kısa anahtarlı ayrı sunucu (yalnız yapılandırma ayarı; süreç ortam değişkenleri değişmez).</summary>
    private sealed class KisaAnahtarHost : WebApplicationFactory<Program>
    {
        public readonly string Dizin = Path.Combine(Path.GetTempPath(), "depowise_kisaanahtar_" + Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(Dizin);
            builder.UseEnvironment(Environments.Development);
            builder.UseSetting("Jwt:Key", "kisa-anahtar-31-bayt-0123456789");   // 31 bayt
            Environment.SetEnvironmentVariable("DEPOWISE_SERVER_DATA", Dizin);
        }
    }

    [Fact]
    public void GKD5_Kisa_Jwt_Anahtariyla_Acilmaz()
    {
        Assert.Equal(31, System.Text.Encoding.UTF8.GetByteCount("kisa-anahtar-31-bayt-0123456789"));
        using var h = new KisaAnahtarHost();
        try
        {
            var ex = Assert.ThrowsAny<Exception>(() => h.CreateClient());
            var mesajlar = new List<string>();
            for (Exception? e = ex; e is not null; e = e.InnerException) mesajlar.Add(e.Message);
            Assert.Contains(mesajlar, m => m.Contains("en az 32 bayt", StringComparison.Ordinal));
        }
        finally { try { Directory.Delete(h.Dizin, true); } catch { } }
    }

    [Fact]
    public void GKD6_Log_Maskeleme()
    {
        var ham = "Npgsql.NpgsqlException: bağlantı hatası (Host=db.example;Username=app;Password=Gizli!Sifre123;Database=x) " +
                  "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc-DEF_ghi token=abc123";
        var m = LogRedactor.Redact(ham);
        Assert.DoesNotContain("Gizli!Sifre123", m);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", m);
        Assert.DoesNotContain("abc123", m);
        Assert.Contains("Host=db.example", m);                           // tanı için gerekli kısım kalır

        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir }.Concat(p).ToArray()));
        Assert.Contains("LogRedactor.Redact(ex.ToString())", Oku("src", "DepoWise.Api", "Program.cs"));
        Assert.Contains("LogRedactor.Redact(detail)", Oku("src", "DepoWise.Desktop", "SyncLog.cs"));
        Assert.Contains("LogRedactor.Redact(message)", Oku("src", "DepoWise.Infrastructure", "Update", "UpdateService.cs"));
    }
}
