using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MNU — MENÜ DÜZENİ UÇLARININ GÜVENLİĞİ (2026-08-18) ═══
///
/// CLAUDE.md §5: <i>"menü, işlem, alan ve özel buton yetkisi UI ile API'da aynı uygulanır."</i>
/// Arayüzde ekranı gizlemek güvenlik DEĞİLDİR — bu testler uçların <b>doğrudan çağrıldığında</b>
/// da fail-closed olduğunu kilitler. Ayrıca "kaydet → yeniden oku" turunun gerçekten kalıcı
/// olduğunu ve menü ucunun düzeni taşıdığını doğrular.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiMenuLayoutTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "MNUAPI-CO";
    private const string Super = "mnu_super";
    private const string Staff = "mnu_personel";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _super = null!, _staff = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        var uid = _svc.Users.EnsureInitialAdmin(Co, Super, Pass, RoleKeys.SuperAdmin);
        _ = _svc.Users.EnsureInitialAdmin(Co, Staff, Pass, RoleKeys.Staff);
        var sa = new SessionContext(uid, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var sube = _svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _super = await _host.LoginAsync(Super, Pass, Co, sube);
        _staff = await _host.LoginAsync(Staff, Pass, Co, sube);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    /// <summary>Süper adminin gördüğü tam durumu, tek bir ekran adı değiştirilmiş hâlde geri gönderir.</summary>
    private async Task<HttpResponseMessage> SaveWithRenameAsync(HttpClient c, string screenKey, string yeniAd)
    {
        var json = await ApiTestHost.JsonAsync(await _super.GetAsync("/api/screens/layout/manage"));

        var screens = new List<object>();
        foreach (var e in json.GetProperty("screens").EnumerateArray())
        {
            var key = e.GetProperty("screenKey").GetString()!;
            screens.Add(new
            {
                screenKey = key,
                label = key == screenKey ? yeniAd : e.GetProperty("label").GetString(),
                groupKey = e.GetProperty("groupKey").GetString(),
                sortOrder = e.GetProperty("sortOrder").GetInt32(),
            });
        }
        var groups = json.GetProperty("groups").EnumerateArray().Select(g => new
        {
            groupKey = g.GetProperty("groupKey").GetString(),
            title = g.GetProperty("title").GetString(),
            sortOrder = g.GetProperty("sortOrder").GetInt32(),
            isCustom = g.GetProperty("isCustom").GetBoolean(),
        }).ToList();

        return await c.PostAsJsonAsync("/api/screens/layout", new { screens, groups });
    }

    // ═══ Yetki ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>Oturumsuz istek uçlara ERİŞEMEZ.</summary>
    [Fact]
    public async Task A1_Oturumsuz_Erisemez()
    {
        var anon = _host.Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/screens/layout/manage")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync("/api/screens/layout", new { screens = Array.Empty<object>(), groups = Array.Empty<object>() })).StatusCode);
    }

    /// <summary>⭐ Yetkisiz personel yönetim listesini OKUYAMAZ.</summary>
    [Fact]
    public async Task A2_Yetkisiz_Personel_Okuyamaz()
    {
        var resp = await _staff.GetAsync("/api/screens/layout/manage");
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    /// <summary>⭐ Yetkisiz personel DOĞRUDAN uca yazarak menüyü değiştiremez.</summary>
    [Fact]
    public async Task A3_Yetkisiz_Personel_Yazamaz()
    {
        var resp = await SaveWithRenameAsync(_staff, "reports", "SIZMA");
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);

        // Menü GERÇEKTEN değişmemiş olmalı (yanıt kodu yeterli kanıt değil).
        var kontrol = await ApiTestHost.JsonAsync(await _super.GetAsync("/api/screens/layout/manage"));
        Assert.DoesNotContain(kontrol.GetProperty("screens").EnumerateArray(),
            e => e.GetProperty("label").GetString() == "SIZMA");
    }

    /// <summary>Yetkisiz personel varsayılana döndürme ucunu da çağıramaz.</summary>
    [Fact]
    public async Task A4_Yetkisiz_Personel_Sifirlayamaz()
    {
        var resp = await _staff.PostAsJsonAsync("/api/screens/layout/reset", new { });
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    // ═══ İşlevsellik ════════════════════════════════════════════════════════════════════════════

    /// <summary>Yönetim listesi süper admine tüm ekranları ve üst menüleri döndürür.</summary>
    [Fact]
    public async Task B1_Yonetim_Listesi_Doner()
    {
        var resp = await _super.GetAsync("/api/screens/layout/manage");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await ApiTestHost.JsonAsync(resp);

        Assert.Equal(AppScreens.All.Count, json.GetProperty("screens").GetArrayLength());
        Assert.True(json.GetProperty("groups").GetArrayLength() >= AppScreens.Groups.Count);
        // Kimlik alanları listede taşınıyor (arayüz route/yetkiyi gösterebilsin).
        var ilk = json.GetProperty("screens")[0];
        Assert.True(ilk.TryGetProperty("permissionKey", out _));
        Assert.True(ilk.TryGetProperty("isProtected", out _));
    }

    /// <summary>⭐ Kaydet → yeniden oku turu: değişiklik KALICI ve route/yetki DEĞİŞMEMİŞ.</summary>
    [Fact]
    public async Task B2_Kaydet_Ve_Yeniden_Oku()
    {
        Assert.Equal(HttpStatusCode.OK, (await SaveWithRenameAsync(_super, "reports", "Analiz Raporları")).StatusCode);

        var json = await ApiTestHost.JsonAsync(await _super.GetAsync("/api/screens/layout/manage"));
        var satir = json.GetProperty("screens").EnumerateArray()
            .First(e => e.GetProperty("screenKey").GetString() == "reports");

        Assert.Equal("Analiz Raporları", satir.GetProperty("label").GetString());
        Assert.Equal("Raporlar", satir.GetProperty("catalogLabel").GetString());   // özgün ad korunur
        Assert.Equal("reports", satir.GetProperty("webRoute").GetString());        // ⭐ adres AYNI
        Assert.Equal("reports", satir.GetProperty("permissionKey").GetString());   // ⭐ yetki AYNI
    }

    /// <summary>Menü ucu düzeni taşır → web menüsü yeni adı kullanabilir.</summary>
    [Fact]
    public async Task B3_Menu_Ucu_Duzeni_Tasir()
    {
        await SaveWithRenameAsync(_super, "trash", "Silinenler");

        var json = await ApiTestHost.JsonAsync(await _super.GetAsync("/api/screens/visibility"));
        Assert.True(json.TryGetProperty("layout", out var layout));
        Assert.Contains(layout.GetProperty("screens").EnumerateArray(),
            e => e.GetProperty("key").GetString() == "trash" && e.GetProperty("label").GetString() == "Silinenler");
    }

    /// <summary>Varsayılana döndürme çalışır ve düzen tamamen kalkar.</summary>
    [Fact]
    public async Task B4_Varsayilana_Donus()
    {
        await SaveWithRenameAsync(_super, "trash", "Silinenler");
        Assert.Equal(HttpStatusCode.OK, (await _super.PostAsJsonAsync("/api/screens/layout/reset", new { })).StatusCode);

        var json = await ApiTestHost.JsonAsync(await _super.GetAsync("/api/screens/layout/manage"));
        var satir = json.GetProperty("screens").EnumerateArray()
            .First(e => e.GetProperty("screenKey").GetString() == "trash");
        Assert.Equal("Çöp Kutusu Listesi", satir.GetProperty("label").GetString());
    }

    /// <summary>Boş gövde reddedilir (kazara tüm düzeni silen istek geçmesin).</summary>
    [Fact]
    public async Task B5_Bos_Govde_Reddedilir()
    {
        var resp = await _super.PostAsJsonAsync("/api/screens/layout",
            new { screens = Array.Empty<object>(), groups = Array.Empty<object>() });
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    /// <summary>
    /// ⭐ MNU-B1: <b>MASAÜSTÜNE İNME YOLU.</b> Ekran ayarları eskiden masaüstüne HİÇBİR yoldan
    /// ulaşmıyordu (ne iş senkronunda ne tanım senkronunda) → "Masaüstü" kutusu gerçek makinelerde
    /// etkisizdi. Tanım senkronu ucu artık üç bölümü de taşımalı; taşımazsa masaüstü yine sağır kalır.
    /// </summary>
    [Fact]
    public async Task B7_Tanim_Senkronu_Ekran_Ayarlarini_Tasir()
    {
        await SaveWithRenameAsync(_super, "audit", "Kayıt Defteri");
        Assert.Equal(HttpStatusCode.OK, (await _super.PostAsJsonAsync("/api/screens/visibility",
            new { screenKey = "audit", desktop = false, web = true })).StatusCode);

        var json = await ApiTestHost.JsonAsync(await _super.GetAsync("/api/lookups/sync"));

        Assert.True(json.TryGetProperty("screenVisibility", out var vis), "screenVisibility bölümü yok.");
        Assert.Contains(vis.EnumerateArray(), e => e.GetProperty("screen_key").GetString() == "audit");

        Assert.True(json.TryGetProperty("menuLayoutScreens", out var lay), "menuLayoutScreens bölümü yok.");
        Assert.Contains(lay.EnumerateArray(),
            e => e.GetProperty("screen_key").GetString() == "audit"
              && e.GetProperty("label_override").GetString() == "Kayıt Defteri");

        Assert.True(json.TryGetProperty("menuLayoutGroups", out _), "menuLayoutGroups bölümü yok.");
    }

    /// <summary>⭐ MNU-B2: korumalı yönetim ekranı uç üzerinden de kapatılamaz.</summary>
    [Fact]
    public async Task B6_Korumali_Ekran_Uctan_Kapatilamaz()
    {
        var resp = await _super.PostAsJsonAsync("/api/screens/visibility",
            new { screenKey = "screen_visibility", desktop = false, web = false });
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);

        var json = await ApiTestHost.JsonAsync(await _super.GetAsync("/api/screens/visibility"));
        var satir = json.GetProperty("screens").EnumerateArray()
            .First(e => e.GetProperty("key").GetString() == "screen_visibility");
        Assert.True(satir.GetProperty("web").GetBoolean());
    }
}
