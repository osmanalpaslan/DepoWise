using System.Net;
using System.Net.Http.Json;
using DepoWise.Api;
using DepoWise.Application.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G1 (2026-08-18) — GÜVENLİK KAPILARI.
///
/// <b>GUV-A1</b> — `/api/releases` (POST) paketi **yetki kontrolünden ÖNCE** diske yazıyordu:
/// `Session(ctx) is null` dışında kapı yoktu; süper admin kontrolü `Releases.Publish` içinde,
/// yani dosya yazıldıktan SONRA çalışıyordu. Kestrel istek sınırı 1 GB, sunucu diski ~974 MB →
/// herhangi bir oturum sahibi tek istekle diski doldurup **login dahil tüm API'yi 500'e** düşürebilirdi
/// (ADR-070; 12.07.2026'da fiilen yaşandı). Yayındaki paketi ezerek güncellemeyi de kırabilirdi.
///
/// <b>GUV-A2</b> — `/api/backup/list` yalnız "oturum var mı" bakıyordu; kardeşleri (create/download)
/// süper admin istiyordu. Her firmanın her kullanıcısı sunucu yedeklerinin adını/boyutunu/tarihini
/// görebiliyordu.
///
/// <b>DEN-F2</b> — "+" satır içi tanım ekleme yetkisi (<c>btn-add-lookup</c>) SUNUCUDA kapısızdı;
/// yalnız masaüstü arayüzünde uygulanıyordu → web'den atlatılabiliyordu.
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class ApiSecurityGateTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Co = "GUV-CO";
    private const string Super = "guv_super";
    private const string Staff = "guv_personel";
    private const string Pass = "Test!2026";

    private ServerServices _svc = null!;
    private HttpClient _super = null!, _staff = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _svc = _host.Services.GetRequiredService<ServerServices>();
        var uid = _svc.Users.EnsureInitialAdmin(Co, Super, Pass, RoleKeys.SuperAdmin);
        _svc.Users.EnsureInitialAdmin(Co, Staff, Pass, RoleKeys.Staff);
        // Giriş şube seçimi ister ("Tüm Şubeler" ayrı yetkidir) → test için bir şube açıp onunla girilir.
        var sa = new SessionContext(uid, Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        var sube = _svc.Branches.Create(sa, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _super = await _host.LoginAsync(Super, Pass, Co, sube);
        _staff = await _host.LoginAsync(Staff, Pass, Co, sube);
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    // ── GUV-A1 ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>Yetkisiz kullanıcı sürüm yükleyemez — ve KRİTİK olan: dosya diske hiç YAZILMAMALI.</summary>
    [Fact]
    public async Task Surum_Yukleme_Yetkisize_KAPALI_ve_dosya_yazilmaz()
    {
        var klasor = _svc.ReleasePackages.GetType()
            .GetField("_root", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_svc.ReleasePackages) as string;
        Assert.NotNull(klasor);
        var oncesi = Directory.Exists(klasor) ? Directory.GetFiles(klasor!, "*.pkg").Length : 0;

        using var form = new MultipartFormDataContent
        {
            { new StringContent("9.9.9"), "version" },
            { new StringContent(new string('a', 64)), "checksum" },
            { new StringContent("10"), "sizeBytes" },
            { new ByteArrayContent(new byte[1024]), "file", "DepoWise-9.9.9.pkg" },
        };
        var resp = await _staff.PostAsync("/api/releases", form);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var sonrasi = Directory.Exists(klasor) ? Directory.GetFiles(klasor!, "*.pkg").Length : 0;
        Assert.Equal(oncesi, sonrasi);   // ⭐ ASIL KORUMA: yetkisiz istek diske DOKUNAMADI
    }

    /// <summary>Boyut sınırı: süper admin bile diski dolduramaz.</summary>
    [Fact]
    public void Paket_Boyut_Siniri_Tanimli_Ve_Makul()
    {
        Assert.True(ReleaseStore.MaxPackageBytes > 0);
        Assert.True(ReleaseStore.MaxPackageBytes <= 500L * 1024 * 1024);   // diskten (974 MB) küçük olmalı
        Assert.True(ReleaseStore.MaxPackageBytes >= 150L * 1024 * 1024);   // gerçek paket ~86 MB → bol pay
    }

    // ── GUV-A2 ───────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Yedek_Listesi_Yetkisize_KAPALI()
    {
        var resp = await _staff.GetAsync("/api/backup/list");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Yedek_Listesi_SuperAdmine_ACIK()
    {
        var resp = await _super.GetAsync("/api/backup/list");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── DEN-F2 ───────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// "+" satır içi ekleme yetkisi olmayan kullanıcı sunucudan da eklememeli.
    /// Personel rolünde admin bypass yoktur ve <c>btn-add-lookup</c> açıkça verilmemiştir → 403.
    /// </summary>
    [Fact]
    public async Task SatirIci_Ekleme_Yetkisiz_Kullanicida_REDDEDILIR()
    {
        var resp = await _staff.PostAsJsonAsync("/api/lookups/units", new { name = "Kutu" });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    /// <summary>Süper adminde akış BOZULMAZ (admin bypass korunur).</summary>
    [Fact]
    public async Task SatirIci_Ekleme_SuperAdminde_CALISIR()
    {
        var resp = await _super.PostAsJsonAsync("/api/lookups/units", new { name = "Palet" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
