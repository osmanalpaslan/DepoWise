using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G5 — WEB / MASAÜSTÜ EKRAN PLATFORM GÖRÜNÜRLÜĞÜ (kullanıcı isteği 2026-08-12).
///
/// <b>MODEL:</b> <c>ERİŞİM = PLATFORM_AKTİF &amp;&amp; YETKİ_VAR</c>. Platform görünürlüğü yetki VERMEZ
/// ve yetkiyi BYPASS ETMEZ; ikisi ayrı kavramdır ve testler bunu ayrı ayrı kanıtlar.
///
/// <b>VARSAYILAN:</b> veritabanında kayıt yoksa <see cref="AppScreens"/> derleme-zamanı değeri geçerlidir
/// → migration sonrası hiçbir ekran kapanmaz (regresyon testi aşağıda).
///
/// <b>YALNIZ DARALTIR:</b> katalogda o platformda bulunmayan bir ekran, veritabanı kaydıyla AÇILAMAZ.
/// </summary>
public class ScreenPlatformVisibilityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ScreenVisibilityService _svc;
    private readonly UserService _users;
    private const string CoA = "A";
    private const string CoB = "B";

    public ScreenPlatformVisibilityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g5_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _svc = new ScreenVisibilityService(_factory, _clock);
        _users = new UserService(_factory, _clock);
        ScreenVisibilityService.InvalidateAll();   // testler arası önbellek sızmasın
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static SessionContext Super(string co = CoA)
        => new("su", co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    private static SessionContext Admin(string co = CoA)
        => new("ad", co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    private static SessionContext Staff(string co = CoA, params string[] modules)
        => new("st", co, new[] { RoleKeys.Staff },
            new PermissionSet(modules.Select(m => new ModulePermission(m, true, true, true, true))));

    /// <summary>Her okumada taze harita (önbellek testi ayrıca yapılır).</summary>
    private IReadOnlyDictionary<string, ScreenVisibilityOverride> Ov(string co = CoA)
    {
        ScreenVisibilityService.Invalidate(co);
        return _svc.OverridesFor(co);
    }

    private static AppScreen Screen(string key) => AppScreens.ByKey(key)!;

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 1–3 · VARSAYILAN DAVRANIŞ (migration hiçbir ekranı kapatmaz)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>1 — Kayıt YOKKEN her ekranın etkin platformu katalog varsayılanına eşittir.
    /// ⭐ Bu, "migration sonrası hiçbir şey kapanmaz" garantisidir.</summary>
    [Fact]
    public void G5_01_Kayit_Yokken_Katalog_Varsayilani_Gecerli()
    {
        var ov = Ov();
        Assert.Empty(ov);
        foreach (var sc in AppScreens.All)
            Assert.Equal(sc.Platforms, ScreenVisibility.Effective(sc, ov));
    }

    /// <summary>2 — Null harita (okuma başarısız / eski şema) da varsayılana düşer, ekran kapatmaz.</summary>
    [Fact]
    public void G5_02_Null_Harita_Varsayilana_Duser()
    {
        foreach (var sc in AppScreens.All)
            Assert.Equal(sc.Platforms, ScreenVisibility.Effective(sc, null));
    }

    /// <summary>3 — Bilinmeyen ekran anahtarı KAPALI sayılır (deny-by-default).</summary>
    [Fact]
    public void G5_03_Bilinmeyen_Ekran_Kapali()
    {
        Assert.False(ScreenVisibility.IsEnabled("boyle-bir-ekran-yok", ScreenPlatform.Desktop, Ov()));
        Assert.False(ScreenVisibility.IsEnabled("boyle-bir-ekran-yok", ScreenPlatform.Web, Ov()));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 4–8 · A/B/C/D SENARYOLARI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>4 — A: iki platform da AÇIK (varsayılan).</summary>
    [Fact]
    public void G5_04_A_Iki_Platform_Acik()
    {
        var sc = Screen("materials.list");
        Assert.True(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Desktop, Ov()));
        Assert.True(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Web, Ov()));
    }

    /// <summary>5 — B: Web açık, MASAÜSTÜ kapalı.</summary>
    [Fact]
    public void G5_05_B_Masaustu_Kapali_Web_Acik()
    {
        _svc.Set(Super(), "materials.list", desktop: false, web: true);
        var sc = Screen("materials.list");
        Assert.False(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Desktop, Ov()));
        Assert.True(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Web, Ov()));
    }

    /// <summary>6 — C: WEB kapalı, masaüstü açık (senin örneğin: "Malzemeler — Desktop açık, Web kapalı").</summary>
    [Fact]
    public void G5_06_C_Web_Kapali_Masaustu_Acik()
    {
        _svc.Set(Super(), "materials.list", desktop: true, web: false);
        var sc = Screen("materials.list");
        Assert.True(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Desktop, Ov()));
        Assert.False(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Web, Ov()));
    }

    /// <summary>7 — D: iki platform da KAPALI → hiçbir yerden erişilemez.</summary>
    [Fact]
    public void G5_07_D_Iki_Platform_Kapali()
    {
        _svc.Set(Super(), "materials.list", desktop: false, web: false);
        var sc = Screen("materials.list");
        Assert.False(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Desktop, Ov()));
        Assert.False(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Web, Ov()));
        Assert.Equal(ScreenPlatform.None, ScreenVisibility.Effective(sc, Ov()));
    }

    /// <summary>8 — null geçmek kaydı SİLER → katalog varsayılanına döner.</summary>
    [Fact]
    public void G5_08_Null_Kaydi_Siler_Varsayilana_Doner()
    {
        _svc.Set(Super(), "materials.list", desktop: false, web: false);
        Assert.Equal(ScreenPlatform.None, ScreenVisibility.Effective(Screen("materials.list"), Ov()));

        _svc.Set(Super(), "materials.list", desktop: null, web: null);
        Assert.Empty(Ov());
        Assert.Equal(ScreenPlatform.Both, ScreenVisibility.Effective(Screen("materials.list"), Ov()));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 9–11 · YALNIZ DARALTIR
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>9 — ⭐ Katalogda o platformda OLMAYAN ekran, kayıtla AÇILAMAZ. Açılabilseydi menüde
    /// karşılığı olmayan bir giriş belirir, tıklanınca hiçbir yere gitmezdi.</summary>
    [Fact]
    public void G5_09_Katalogda_Olmayan_Platform_Acilamaz()
    {
        // "quota_monitor" yalnız WEB'de var.
        var ex = Assert.Throws<InvalidOperationException>(
            () => _svc.Set(Super(), "quota_monitor", desktop: true, web: true));
        Assert.Contains("masaüstünde bulunmuyor", ex.Message);

        // "stock.distribute" yalnız MASAÜSTÜNDE var.
        var ex2 = Assert.Throws<InvalidOperationException>(
            () => _svc.Set(Super(), "stock.distribute", desktop: true, web: true));
        Assert.Contains("web'de bulunmuyor", ex2.Message);
    }

    /// <summary>10 — Elle veritabanına "açık" yazılsa bile katalog varsayılanı KAZANIR (savunma katmanı).</summary>
    [Fact]
    public void G5_10_DB_Kaydi_Katalogu_Genisletemez()
    {
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO screen_platform_visibility(id,company_id,screen_key,platform,enabled,created_at,updated_at) " +
                              "VALUES('x1','A','quota_monitor','desktop',1,1,1);";
            cmd.ExecuteNonQuery();
        }
        // Katalogda masaüstü YOK → etkin platform yine yalnız Web.
        Assert.Equal(ScreenPlatform.Web, ScreenVisibility.Effective(Screen("quota_monitor"), Ov()));
    }

    /// <summary>11 — Bilinmeyen ekran anahtarına ayar yazılamaz.</summary>
    [Fact]
    public void G5_11_Bilinmeyen_Ekrana_Ayar_Yazilamaz()
        => Assert.Throws<ArgumentException>(() => _svc.Set(Super(), "yok-boyle", false, false));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 12–15 · PLATFORM + YETKİ BİRLİKTE
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>12 — ⭐ Platform KAPALI + yetki VAR → erişim YOK.</summary>
    [Fact]
    public void G5_12_Platform_Kapali_Yetki_Var_Erisim_Yok()
    {
        _svc.Set(Super(), "materials.list", desktop: false, web: true);
        var kullanici = Staff(CoA, "materials");
        Assert.True(AccessControl.Can(kullanici, "materials", PermissionAction.View));   // yetki VAR
        Assert.False(ScreenVisibility.CanOpen(kullanici, Screen("materials.list"), ScreenPlatform.Desktop, Ov()));
    }

    /// <summary>13 — Platform AÇIK + yetki YOK → erişim YOK (platform yetki VERMEZ).</summary>
    [Fact]
    public void G5_13_Platform_Acik_Yetki_Yok_Erisim_Yok()
    {
        var kullanici = Staff(CoA);   // hiç yetki yok
        Assert.False(ScreenVisibility.CanOpen(kullanici, Screen("materials.list"), ScreenPlatform.Desktop, Ov()));
    }

    /// <summary>14 — Platform AÇIK + yetki VAR → erişim VAR.</summary>
    [Fact]
    public void G5_14_Platform_Acik_Yetki_Var_Erisim_Var()
    {
        var kullanici = Staff(CoA, "materials");
        Assert.True(ScreenVisibility.CanOpen(kullanici, Screen("materials.list"), ScreenPlatform.Desktop, Ov()));
        Assert.True(ScreenVisibility.CanOpen(kullanici, Screen("materials.list"), ScreenPlatform.Web, Ov()));
    }

    /// <summary>15 — ⭐ ADMIN ve SÜPER ADMIN platform kapısından MUAF DEĞİLDİR. Yetki bypass'ı vardır,
    /// platform bypass'ı YOKTUR — aksi halde "kapattım ama hâlâ açık" durumu oluşurdu.</summary>
    [Fact]
    public void G5_15_Admin_Ve_Super_Admin_Platform_Kapisindan_Muaf_Degil()
    {
        _svc.Set(Super(), "materials.list", desktop: false, web: true);
        Assert.False(ScreenVisibility.CanOpen(Admin(), Screen("materials.list"), ScreenPlatform.Desktop, Ov()));
        Assert.False(ScreenVisibility.CanOpen(Super(), Screen("materials.list"), ScreenPlatform.Desktop, Ov()));
        // Web'de açık olduğu için orada erişebilirler (yetki bypass'ı yerinde).
        Assert.True(ScreenVisibility.CanOpen(Admin(), Screen("materials.list"), ScreenPlatform.Web, Ov()));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 16–18 · GEZİNME / ROUTE KAPILARI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>16 — Masaüstü gezinme anahtarı kapısı (alt-ekran anahtarları dahil).</summary>
    [Fact]
    public void G5_16_Masaustu_Gezinme_Kapisi()
    {
        var k = Staff(CoA, "stock");
        Assert.True(ScreenVisibility.CanOpenDesktop(k, "stock:movements", Ov()));
        _svc.Set(Super(), "stock.movements", desktop: false, web: true);
        Assert.False(ScreenVisibility.CanOpenDesktop(k, "stock:movements", Ov()));
        // Aynı modülün BAŞKA ekranı etkilenmez (kapatma ekran bazlıdır, modül bazlı değil).
        Assert.True(ScreenVisibility.CanOpenDesktop(k, "stock:count", Ov()));
    }

    /// <summary>17 — Web route kapısı (deep-link). Katalogda olmayan route platform yönetimi dışındadır.</summary>
    [Fact]
    public void G5_17_Web_Route_Kapisi()
    {
        var k = Staff(CoA, "stock");
        Assert.True(ScreenVisibility.CanOpenWeb(k, "stock/movements", Ov()));
        _svc.Set(Super(), "stock.movements", desktop: true, web: false);
        Assert.False(ScreenVisibility.CanOpenWeb(k, "stock/movements", Ov()));
        Assert.True(ScreenVisibility.CanOpenWeb(k, "bilinmeyen/route", Ov()));   // katalog dışı → platform yönetimi yok
    }

    /// <summary>18 — Katalogda olmayan masaüstü gezinme hedefleri (ör. "dashboard") engellenmez.</summary>
    [Fact]
    public void G5_18_Katalog_Disi_Gezinme_Hedefi_Engellenmez()
        => Assert.True(ScreenVisibility.CanOpenDesktop(Staff(CoA), "dashboard", Ov()));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 19–21 · FİRMA İZOLASYONU
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>19 — ⭐ Bir firmanın ayarı DİĞER firmayı etkilemez.</summary>
    [Fact]
    public void G5_19_Firma_Izolasyonu()
    {
        _svc.Set(Super(CoA), "materials.list", desktop: false, web: true);

        Assert.False(ScreenVisibility.IsEnabled(Screen("materials.list"), ScreenPlatform.Desktop, Ov(CoA)));
        Assert.True(ScreenVisibility.IsEnabled(Screen("materials.list"), ScreenPlatform.Desktop, Ov(CoB)));
        Assert.Empty(Ov(CoB));
    }

    /// <summary>20 — İki firma AYNI ekranı farklı ayarlayabilir; kayıtlar karışmaz.</summary>
    [Fact]
    public void G5_20_Iki_Firma_Farkli_Ayarlayabilir()
    {
        _svc.Set(Super(CoA), "reports", desktop: false, web: true);
        _svc.Set(Super(CoB), "reports", desktop: true, web: false);

        Assert.Equal(ScreenPlatform.Web, ScreenVisibility.Effective(Screen("reports"), Ov(CoA)));
        Assert.Equal(ScreenPlatform.Desktop, ScreenVisibility.Effective(Screen("reports"), Ov(CoB)));
    }

    /// <summary>21 — Aynı (firma, ekran, platform) için MÜKERRER kayıt oluşmaz; tekrar yazma günceller.</summary>
    [Fact]
    public void G5_21_Mukerrer_Kayit_Olusmaz()
    {
        _svc.Set(Super(), "reports", desktop: false, web: true);
        _svc.Set(Super(), "reports", desktop: false, web: false);
        _svc.Set(Super(), "reports", desktop: true, web: false);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM screen_platform_visibility WHERE company_id='A' AND screen_key='reports';";
        Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar()));   // platform başına TEK satır
        Assert.Equal(ScreenPlatform.Desktop, ScreenVisibility.Effective(Screen("reports"), Ov()));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 22–24 · YETKİ · ÖNBELLEK · AUDIT
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>22 — Yönetim ekranı YALNIZ süper admindir; firma admini ve personel erişemez
    /// (<c>screen_visibility</c> süper-admin-only).</summary>
    [Fact]
    public void G5_22_Yonetim_Yalniz_Super_Admin()
    {
        Assert.Throws<ForbiddenException>(() => _svc.List(Admin()));
        Assert.Throws<ForbiddenException>(() => _svc.Set(Admin(), "reports", false, true));
        Assert.Throws<ForbiddenException>(() => _svc.List(Staff(CoA, "screen_visibility")));
        Assert.Throws<ForbiddenException>(() => _svc.Set(Staff(CoA, "screen_visibility"), "reports", false, true));

        _ = _svc.List(Super());   // süper admin geçer
        Assert.True(AppModules.IsSuperAdminOnly("screen_visibility"));
    }

    /// <summary>23 — ⭐ ÖNBELLEK: yazma anında düşürülür → yönetici kapattığı an etkili olur (bayat veri yok).</summary>
    [Fact]
    public void G5_23_Yazma_Onbellegi_Aninda_Duserir()
    {
        _ = _svc.OverridesFor(CoA);                       // önbelleğe alındı (boş)
        _svc.Set(Super(), "reports", desktop: false, web: true);

        // Invalidate ÇAĞIRMADAN okuyoruz: yazma zaten düşürmüş olmalı.
        var taze = _svc.OverridesFor(CoA);
        Assert.True(taze.ContainsKey("reports"));
        Assert.False(ScreenVisibility.IsEnabled(Screen("reports"), ScreenPlatform.Desktop, taze));
    }

    /// <summary>24 — Değişiklik AUDIT'e yazılır (kim, hangi ekran, ne yaptı).</summary>
    [Fact]
    public void G5_24_Degisiklik_Audit_Edilir()
    {
        _svc.Set(Super(), "reports", desktop: false, web: true);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type='screen_platform_visibility' AND entity_id='reports';";
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 1);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 25–27 · YÖNETİM LİSTESİ + MİGRATION + REGRESYON
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>25 — Yönetim listesi TÜM ekranları, varsayılan + etkin değerleriyle döndürür ve
    /// "o platformda yok" bilgisini taşır (arayüz kutuyu kapalı gösterebilsin).</summary>
    [Fact]
    public void G5_25_Yonetim_Listesi_Dogru()
    {
        _svc.Set(Super(), "reports", desktop: false, web: true);
        var rows = _svc.List(Super());

        Assert.Equal(AppScreens.All.Count, rows.Count);

        var reports = rows.Single(r => r.ScreenKey == "reports");
        Assert.True(reports.DefaultDesktop);          // katalogda masaüstünde VAR
        Assert.False(reports.EffectiveDesktop);       // ama kapatıldı
        Assert.True(reports.EffectiveWeb);
        Assert.Equal("Yalnız Web", reports.StatusText);
        Assert.NotNull(reports.UpdatedAt);

        var quota = rows.Single(r => r.ScreenKey == "quota_monitor");
        Assert.True(quota.DesktopUnavailable);        // masaüstünde hiç yok → kutu kapalı
        Assert.False(quota.WebUnavailable);
        Assert.Null(quota.UpdatedAt);                 // hiç ayar yapılmadı
    }

    /// <summary>26 — Migration idempotenttir: yeniden çalıştırmak veriyi bozmaz.</summary>
    [Fact]
    public void G5_26_Migration_Idempotent()
    {
        _svc.Set(Super(), "reports", desktop: false, web: true);

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            new Migration065_ScreenPlatformVisibility().Up(conn, tx);   // ikinci kez
            tx.Commit();
        }

        Assert.Equal(ScreenPlatform.Web, ScreenVisibility.Effective(Screen("reports"), Ov()));
    }

    /// <summary>27 — ⭐ REGRESYON: migration sonrası TÜM ekranların davranışı değişmedi —
    /// yetkili bir kullanıcı bugün erişebildiği her ekrana erişmeye devam ediyor.</summary>
    [Fact]
    public void G5_27_Migration_Sonrasi_Hicbir_Ekran_Kapanmadi()
    {
        var ov = Ov();
        foreach (var sc in AppScreens.All)
        {
            var yetkili = Staff(CoA, sc.ModuleKey);
            if (sc.OnDesktop)
                Assert.True(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Desktop, ov), $"{sc.Key} masaüstünde kapandı!");
            if (sc.OnWeb)
                Assert.True(ScreenVisibility.IsEnabled(sc, ScreenPlatform.Web, ov), $"{sc.Key} web'de kapandı!");
        }
    }

    /// <summary>28 — Yönetim ekranının KENDİSİ katalogda ve yalnız web'de; kapatılarak kilitlenme
    /// riski açıkça kayıt altında (süper admin kendi yönetim ekranını web'de kapatabilir — bu bilinçli
    /// bir karardır ve veritabanından geri alınabilir).</summary>
    [Fact]
    public void G5_28_Yonetim_Ekrani_Katalogda()
    {
        var sc = AppScreens.ByKey("screen_visibility");
        Assert.NotNull(sc);
        Assert.False(sc!.OnDesktop);
        Assert.True(sc.OnWeb);
        Assert.Equal("screen_visibility", sc.ModuleKey);
    }

    public void Dispose()
    {
        ScreenVisibilityService.InvalidateAll();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
