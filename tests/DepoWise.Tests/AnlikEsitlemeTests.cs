using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-04 — ANLIK EŞİTLEME (kullanıcı: "sunucu değişiklik algılarsa haber versin, 15 saniyede bir
/// eşitleme yapmak zorunda kalmasın").
/// ANL1 sürüm ileride → /api/sync/wait HEMEN döner · ANL2 bekleyen istek, firmada yazma olunca uyanır
/// (25 sn'lik üst sınırı beklemez) · ANL3 oturumsuz erişim yok · ANL4 başka firmanın yazması UYANDIRMAZ
/// (tenant) · ANL5 SyncNotifier birim davranışı.
/// </summary>
public class AnlikEsitlemeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string CoA = "ANL-A";
    private const string CoB = "ANL-B";
    private const string Pass = "Anl!2026";
    private ServerServices _svc = null!;
    private HttpClient _a = null!, _b = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        foreach (var (id, ad) in new[] { (CoA, "A Firmasi"), (CoB, "B Firmasi") })
        {
            using var conn = _svc.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
                              "VALUES(@c,@n,1,1,1,0,5,20,5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@c", id); cmd.AddWithValue("@n", ad);
            cmd.ExecuteNonQuery();
        }
        _svc.Users.EnsureInitialAdmin(CoA, "anl_admin_a", Pass, RoleKeys.CompanyAdmin);
        _svc.Users.EnsureInitialAdmin(CoB, "anl_admin_b", Pass, RoleKeys.CompanyAdmin);
        _a = await _host.LoginAsync("anl_admin_a", Pass, CoA);
        _b = await _host.LoginAsync("anl_admin_b", Pass, CoB);
    }

    public Task DisposeAsync() { _host.Dispose(); return Task.CompletedTask; }

    private static async Task<(long Version, bool Changed)> Oku(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("version").GetInt64(), doc.RootElement.GetProperty("changed").GetBoolean());
    }

    private Task<HttpResponseMessage> MalzemeEkle(HttpClient c, string kod) =>
        c.PostAsJsonAsync("/api/materials", new { code = kod, name = "Anlık " + kod, minStock = 0m, unitPrice = 0m, openingStock = 0m });

    private async Task<long> Surum(HttpClient c)
    {
        using var doc = JsonDocument.Parse(await (await c.GetAsync("/api/sync/business-version")).Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("version").GetInt64();
    }

    [Fact]
    public async Task ANL1_Surum_Ileride_Ise_Hemen_Doner()
    {
        (await MalzemeEkle(_a, "ANL1-M")).EnsureSuccessStatusCode();
        var sw = Stopwatch.StartNew();
        var (v, changed) = await Oku(await _a.GetAsync("/api/sync/wait?since=0"));
        Assert.True(changed);
        Assert.True(v > 0);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"beklemeden dönmeliydi ({sw.Elapsed})");
    }

    [Fact]
    public async Task ANL2_Bekleyen_Istek_Yazma_Olunca_Hemen_Uyanir()
    {
        (await MalzemeEkle(_a, "ANL2-ilk")).EnsureSuccessStatusCode();
        var simdi = await Surum(_a);

        var sw = Stopwatch.StartNew();
        var bekleme = _a.GetAsync($"/api/sync/wait?since={simdi}");
        await Task.Delay(800);                       // istek gerçekten beklemeye girsin
        Assert.False(bekleme.IsCompleted, "değişiklik yokken hemen dönmemeli");
        (await MalzemeEkle(_a, "ANL2-yeni")).EnsureSuccessStatusCode();

        var (v, changed) = await Oku(await bekleme);
        Assert.True(changed);
        Assert.True(v > simdi);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(8), $"yazmadan sonra hemen uyanmalıydı ({sw.Elapsed})");
    }

    [Fact]
    public async Task ANL3_Oturumsuz_Bekleme_Ucu_Kapali()
    {
        var r = await _host.CreateClient().GetAsync("/api/sync/wait?since=0");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ANL4_Baska_Firmanin_Yazmasi_Uyandirmaz()
    {
        (await MalzemeEkle(_a, "ANL4-ilk")).EnsureSuccessStatusCode();
        var simdi = await Surum(_a);
        var bekleme = _a.GetAsync($"/api/sync/wait?since={simdi}");
        await Task.Delay(500);
        (await MalzemeEkle(_b, "ANL4-B")).EnsureSuccessStatusCode();   // B firması yazdı
        await Task.Delay(1500);
        Assert.False(bekleme.IsCompleted, "başka firmanın yazması A'nın beklemesini bitirmemeli");
        (await MalzemeEkle(_a, "ANL4-A")).EnsureSuccessStatusCode();   // şimdi A yazdı → uyanır
        var (_, changed) = await Oku(await bekleme);
        Assert.True(changed);
    }

    [Fact]
    public async Task ANL5_Notifier_Uyandirir_Ve_Sure_Dolunca_False()
    {
        var bekle = SyncNotifier.WaitAsync("ANL5-firma", TimeSpan.FromSeconds(10), CancellationToken.None);
        SyncNotifier.Notify("ANL5-firma");
        Assert.True(await bekle);
        Assert.False(await SyncNotifier.WaitAsync("ANL5-firma", TimeSpan.FromMilliseconds(100), CancellationToken.None));
    }
}

/// <summary>Masaüstü kaynak taraması (sunucu gerektirmez).</summary>
public class AnlikEsitlemeKaynakTests
{
    private static string Kaynak(params string[] p)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DepoWise.sln"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(new[] { dir!, "src", "DepoWise.Desktop" }.Concat(p).ToArray()));
    }

    [Fact]
    public void ANK1_Anlik_Dongu_Baslatilir_Geri_Sayim_Kalkti()
    {
        var s = Kaynak("ViewModels", "ShellViewModel.cs");
        Assert.Contains("AnlikEsitlemeyiBaslat();", s);
        Assert.Contains("WaitForChangeAsync(", s);
        Assert.Contains("BusinessSyncPushService.YerelBekleyenVar", s);
        Assert.DoesNotContain("SyncCountdown", s);
        Assert.DoesNotContain("HalkaSayaciniBaslat", s);
        var w = Kaynak("Views", "MainWindow.axaml");
        Assert.DoesNotContain("SyncCountdown", w);
        Assert.Contains("Classes=\"DonenYay\"", w);
        Assert.Contains("{Binding AktarimSayilari}", w);
    }

    [Fact]
    public void ANK2_Hatali_Gonderimde_Istek_Yagmuru_Yok()
    {
        var s = Kaynak("ViewModels", "ShellViewModel.cs");
        Assert.Contains("if (bekleyen && !BusinessSyncPushService.LastPushFailed)", s);
        Assert.Contains("if (r is null) { await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(30)); continue; }", s);
    }
}
