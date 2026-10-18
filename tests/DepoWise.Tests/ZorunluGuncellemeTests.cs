using System.Net;
using System.Text;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Application.Update;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-12 + M-83 (AR-GE 2026-10-18, K-17 a, F-042/F-156/F-184/F-185) — ZORUNLU GÜNCELLEME + SÜRÜM UYUMU ═══
///  ZG1 — Karar: güncelleme yok / ertelenebilir (kalan gün) / 7 gün sonra zorunlu / en düşük sürümün altı hemen zorunlu
///  ZG2 — Sunucu yazma kuralı: en düşük tanımsız → herkes; başlıksız istemci = 1.0.202; bozuk başlık kilitlemez
///  ZG3 — API: desteklenmeyen sürüm gönderimi 426 + anlaşılır neden; desteklenen sürüm yazar; çekme serbest
///  ZG4 — Ortak HTTP istemcisi sürüm başlığını ekler (masaüstü sürümü bildirdiyse)
///  ZG5 — Bekleyen migration listesi + etiketli (doğrulanmış) yedek
///  ZG6 — Masaüstü akışları: zorunluda erteleme yok, migration öncesi yedek, 426 nedeni gösterilir (kaynak kilidi)
/// </summary>
[Collection("PostgresSchema")]
public class ZorunluGuncellemeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "ZG-A", User = "zg_admin", Pass = "Test!2026";
    private HttpClient _client = null!;

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
        _client = await _host.LoginAsync(User, Pass, Company);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();

    private static UpdatePackage Paket(string v, string min = "0.0.0") => new(v, new string('a', 64), 1, min, null, false, "http://x");

    [Fact]
    public void ZG1_Karar()
    {
        var t0 = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        Assert.Equal(GuncellemeKarari.Yok, GuncellemePolitikasi.Karar("1.0.205", Paket("1.0.205"), t0, t0).Karar);
        Assert.Equal(GuncellemeKarari.Yok, GuncellemePolitikasi.Karar("1.0.206", Paket("1.0.205"), t0, t0).Karar);
        Assert.Equal(GuncellemeKarari.Yok, GuncellemePolitikasi.Karar("bozuk", Paket("1.0.205"), t0, t0).Karar);
        Assert.Equal(GuncellemeKarari.Yok, GuncellemePolitikasi.Karar("1.0.200", null, t0, t0).Karar);

        var ilk = GuncellemePolitikasi.Karar("1.0.200", Paket("1.0.205"), t0, t0);
        Assert.Equal(GuncellemeKarari.Serbest, ilk.Karar);
        Assert.Equal(7, ilk.KalanGun);
        var son = GuncellemePolitikasi.Karar("1.0.200", Paket("1.0.205"), t0, t0.AddDays(6.5));
        Assert.Equal(GuncellemeKarari.Serbest, son.Karar);
        Assert.Equal(1, son.KalanGun);
        var gecti = GuncellemePolitikasi.Karar("1.0.200", Paket("1.0.205"), t0, t0.AddDays(7));
        Assert.Equal(GuncellemeKarari.Zorunlu, gecti.Karar);
        Assert.Contains("7 günden uzun", gecti.Neden);

        var kritik = GuncellemePolitikasi.Karar("1.0.200", Paket("1.0.205", min: "1.0.205"), t0, t0);   // kritik yayın: hemen
        Assert.Equal(GuncellemeKarari.Zorunlu, kritik.Karar);
        Assert.Contains("desteklenmiyor", kritik.Neden);
        Assert.Equal(GuncellemeKarari.Serbest, GuncellemePolitikasi.Karar("1.0.204", Paket("1.0.205", min: "1.0.204"), t0, t0).Karar);
    }

    [Fact]
    public void ZG2_Yazma_Kurali()
    {
        Assert.True(GuncellemePolitikasi.YazmaIzinli(null, null));
        Assert.True(GuncellemePolitikasi.YazmaIzinli("1.0.100", "0.0.0"));
        Assert.True(GuncellemePolitikasi.YazmaIzinli("1.0.100", "bozuk"));
        Assert.True(GuncellemePolitikasi.YazmaIzinli(null, "1.0.202"));            // başlıksız = 1.0.202
        Assert.False(GuncellemePolitikasi.YazmaIzinli(null, "1.0.203"));
        Assert.True(GuncellemePolitikasi.YazmaIzinli("1.0.203", "1.0.203"));
        Assert.False(GuncellemePolitikasi.YazmaIzinli("1.0.202", "1.0.203"));
        Assert.True(GuncellemePolitikasi.YazmaIzinli("çöp", "1.0.203"));            // bozuk başlık kimseyi kilitlemez
    }

    private void Yayin(string surum, string min)
    {
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO app_releases(id, version, checksum_sha256, size_bytes, min_supported_version, release_notes, signed, download_url, published_at, created_at, is_deleted) " +
                          "VALUES(@id, @v, @cs, 1, @min, NULL, 0, 'http://x', 1, 1, 0);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@v", surum);
        cmd.AddWithValue("@cs", new string('a', 64));
        cmd.AddWithValue("@min", min);
        cmd.ExecuteNonQuery();
    }

    private async Task<HttpResponseMessage> Gonder(string? surum)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/sync/business-push")
        { Content = new StringContent("{\"tables\":{}}", Encoding.UTF8, "application/json") };
        if (surum is not null) req.Headers.TryAddWithoutValidation(GuncellemePolitikasi.SurumBasligi, surum);
        return await _client.SendAsync(req);
    }

    [Fact]
    public async Task ZG3_Api_Desteklenmeyen_Surum_Yazamaz()
    {
        Assert.Equal(HttpStatusCode.OK, (await Gonder(null)).StatusCode);              // yayın yok → bugünkü davranış
        Yayin("1.0.300", "1.0.300");                                                  // kritik yayın: en düşük = kendisi
        var red = await Gonder(null);
        Assert.Equal((HttpStatusCode)426, red.StatusCode);                           // eski (başlıksız) masaüstü
        Assert.Contains("artık desteklenmiyor", await red.Content.ReadAsStringAsync());
        Assert.Equal((HttpStatusCode)426, (await Gonder("1.0.299")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Gonder("1.0.300")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/api/sync/business-pull-v2?after=0")).StatusCode);   // okuma serbest
    }

    [Fact]
    public void ZG4_Http_Istemcisi_Surum_Basligi()
    {
        var onceki = DepoWise.Infrastructure.Net.HttpIstemci.UygulamaSurumu;
        try
        {
            DepoWise.Infrastructure.Net.HttpIstemci.UygulamaSurumu = "1.0.205";
            using var c = DepoWise.Infrastructure.Net.HttpIstemci.Yeni(TimeSpan.FromSeconds(5));
            Assert.Equal("1.0.205", c.DefaultRequestHeaders.GetValues(GuncellemePolitikasi.SurumBasligi).Single());
            DepoWise.Infrastructure.Net.HttpIstemci.UygulamaSurumu = null;
            using var c2 = DepoWise.Infrastructure.Net.HttpIstemci.Yeni(TimeSpan.FromSeconds(5));
            Assert.False(c2.DefaultRequestHeaders.Contains(GuncellemePolitikasi.SurumBasligi));
        }
        finally { DepoWise.Infrastructure.Net.HttpIstemci.UygulamaSurumu = onceki; }
    }

    private sealed class SahteMigration : IMigration
    {
        public int Version => 99_999;
        public string Name => "zg_sahte";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var c = conn.CreateCommand(); c.Transaction = tx; c.CommandText = "CREATE TABLE zg_sahte(a INTEGER);"; c.ExecuteNonQuery();
        }
    }

    [Fact]
    public void ZG5_Bekleyen_Migration_Ve_Etiketli_Yedek()
    {
        var yol = Path.Combine(Path.GetTempPath(), "depowise_zg_" + Guid.NewGuid().ToString("N") + ".db");
        var klasor = Path.Combine(Path.GetTempPath(), "depowise_zg_bak_" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = new SqliteConnectionFactory(yol);
            var runner = new MigrationRunner(f);
            Assert.NotEmpty(runner.Pending());
            runner.Run();
            Assert.Empty(runner.Pending());
            var arti = new MigrationRunner(f, MigrationCatalog.All().Append(new SahteMigration()));
            Assert.Equal(new[] { 99_999 }, arti.Pending());

            var yedek = new BackupService(f, backupFolder: klasor);
            var dosya = yedek.EtiketliYedek("guncelleme_oncesi_v104");
            Assert.EndsWith("_guncelleme_oncesi_v104.db", dosya);
            Assert.True(yedek.YedekGecerliMi(dosya, out _));
            Assert.Contains(yedek.ListBackups(), b => b.Path == dosya);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { yol, yol + "-wal", yol + "-shm" }) try { File.Delete(p); } catch { }
            try { Directory.Delete(klasor, true); } catch { }
        }
    }

    [Fact]
    public void ZG6_Masaustu_Akislari()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !File.Exists(Path.Combine(dir, "DepoWise.sln")); i++) dir = Directory.GetParent(dir)!.FullName;
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { dir }.Concat(p).ToArray()));

        var app = Oku("src", "DepoWise.Desktop", "App.axaml.cs");
        Assert.Contains("await AutoUpdateService.KararAsync(session.CompanyId)", app);
        Assert.Contains("if (AutoUpdateService.IsEnabled(session.CompanyId) || zorunlu)", app);
        Assert.Contains("var install = zorunlu || await ConfirmService.AskAsync(", app);
        var shell = Oku("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs");
        Assert.Contains("if (!zorunlu && DesktopServices.Settings.Get(_session.CompanyId, DashboardViewModel.AutoUpdateKey) == \"0\") return;", shell);
        Assert.Contains("var restart = zorunlu || await ConfirmService.AskAsync(", shell);
        Assert.Contains("BusinessSyncPushService.SurumUyarisi is { } surumUyarisi ? surumUyarisi", shell);
        Assert.Contains("MigrasyonlariYedekleCalistir(factory, new MigrationRunner(factory)", Oku("src", "DepoWise.Desktop", "DesktopBootstrap.cs"));
        Assert.Contains("(int)durum == 426", Oku("src", "DepoWise.Desktop", "BusinessSyncPushService.cs"));
        var hizmet = Oku("src", "DepoWise.Desktop", "DesktopServices.cs");
        Assert.Contains("DepoWise.Infrastructure.Net.HttpIstemci.UygulamaSurumu = sv.ToString();", hizmet);
    }
}
