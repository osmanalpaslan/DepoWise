using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MNU-B2 (2026-08-18) — <b>SÜPER ADMİN KENDİNİ KALICI OLARAK KİLİTLEYEBİLİYORDU.</b> ═══
///
/// <c>ScreenVisibilityService.Set</c> yalnız "bu ekran o platformda katalogda var mı" diye bakıyordu.
/// Bu yüzden süper admin <b>"Menü / Ekran Yönetimi"</b> ekranını WEB'de kapatabiliyordu. Kapattığı anda:
/// <list type="number">
///   <item>ekran menüden düşüyor (<c>NavMenu</c> platform süzgeci),</item>
///   <item>adresi elle yazmak da işe yaramıyor (<c>MainLayout</c> route koruması),</item>
///   <item>ekranın masaüstü karşılığı YOK (<c>AppScreens</c>: yalnız <c>W</c>).</item>
/// </list>
/// Sonuç: ayarı geri alacak hiçbir arayüz kalmıyordu — kurtarma yalnız veritabanına elle müdahaleyle
/// mümkündü. Aynı sınıf risk <c>users</c> ve <c>permissions</c> için de var (ikisi de kapatılırsa
/// firmada bir daha kullanıcı açılamaz, yetki verilemez).
///
/// Kural DAR tutulmuştur: <b>tek platformda kapatmak SERBEST</b> (diğeri kurtarma yolu olarak kalır);
/// yalnız "hepsi kapalı" hâli engellenir. Bkz. <see cref="AppScreens.Protected"/>.
/// </summary>
public class ProtectedScreenTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ScreenVisibilityService _svc;
    private readonly SessionContext _super;
    private const string Co = "PRT-CO";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public ProtectedScreenTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_prt_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','A',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        _svc = new ScreenVisibilityService(_factory, _clock);
        _super = new SessionContext("su", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        ScreenVisibilityService.InvalidateAll();
    }

    /// <summary>Korumalı liste keyfî değil — üçü de kilitlenme üretiyor. Liste kilitlenir.</summary>
    [Theory]
    [InlineData("screen_visibility")]
    [InlineData("users")]
    [InlineData("permissions")]
    public void P1_Kritik_Ekranlar_Korumali(string key)
    {
        Assert.True(AppScreens.IsProtected(key));
        Assert.NotNull(AppScreens.ByKey(key));   // katalogda gerçekten var
    }

    /// <summary>Sıradan ekranlar korumalı DEĞİL (kural gereksiz genişletilmedi).</summary>
    [Theory]
    [InlineData("reports")]
    [InlineData("materials.list")]
    [InlineData("trash")]
    public void P2_Siradan_Ekranlar_Korumali_Degil(string key)
        => Assert.False(AppScreens.IsProtected(key));

    /// <summary>⭐ ASIL HATA: yönetim ekranı web'de kapatılamaz (tek platformu odur → kilitlenme).</summary>
    [Fact]
    public void P3_Yonetim_Ekrani_Webde_Kapatilamaz()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _svc.Set(_super, "screen_visibility", desktop: null, web: false));
        Assert.Contains("kritik", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Ekran hâlâ açık olmalı.
        var ov = _svc.OverridesFor(Co);
        Assert.True(ScreenVisibility.IsEnabled("screen_visibility", ScreenPlatform.Web, ov));
    }

    /// <summary>Korumalı ekran TEK platformda kapatılabilir — diğeri kurtarma yolu olarak kalır.</summary>
    [Fact]
    public void P4_Tek_Platformda_Kapatma_Serbest()
    {
        _svc.Set(_super, "users", desktop: false, web: true);

        var ov = _svc.OverridesFor(Co);
        Assert.False(ScreenVisibility.IsEnabled("users", ScreenPlatform.Desktop, ov));
        Assert.True(ScreenVisibility.IsEnabled("users", ScreenPlatform.Web, ov));
    }

    /// <summary>⭐ Ama İKİSİ BİRDEN kapatılamaz.</summary>
    [Fact]
    public void P5_Iki_Platform_Birden_Kapatilamaz()
    {
        Assert.Throws<InvalidOperationException>(
            () => _svc.Set(_super, "users", desktop: false, web: false));

        var ov = _svc.OverridesFor(Co);
        Assert.True(ScreenVisibility.IsEnabled("users", ScreenPlatform.Desktop, ov)
                 || ScreenVisibility.IsEnabled("users", ScreenPlatform.Web, ov));
    }

    /// <summary>İkinci adımda kilitleme de engellenir (önce masaüstü kapatılmış, sonra web denenir).</summary>
    [Fact]
    public void P6_Adim_Adim_Kilitleme_De_Engellenir()
    {
        _svc.Set(_super, "permissions", desktop: false, web: true);
        Assert.Throws<InvalidOperationException>(
            () => _svc.Set(_super, "permissions", desktop: false, web: false));
        Assert.True(ScreenVisibility.IsEnabled("permissions", ScreenPlatform.Web, _svc.OverridesFor(Co)));
    }

    /// <summary>Korumasız ekran her iki platformda da kapatılabilir (davranış DEĞİŞMEDİ).</summary>
    [Fact]
    public void P7_Korumasiz_Ekran_Tamamen_Kapatilabilir()
    {
        _svc.Set(_super, "audit", desktop: false, web: false);

        var ov = _svc.OverridesFor(Co);
        Assert.False(ScreenVisibility.IsEnabled("audit", ScreenPlatform.Desktop, ov));
        Assert.False(ScreenVisibility.IsEnabled("audit", ScreenPlatform.Web, ov));
    }

    public void Dispose()
    {
        ScreenVisibilityService.InvalidateAll();
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
