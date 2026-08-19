using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SEC — ÜST GRUP (menünün üçüncü seviyesi, kullanıcı isteği 2026-08-19) ═══
///
/// Menü artık <b>ÜST GRUP → ÜST MENÜ → EKRAN</b> olabilir. Bu testlerin ASIL GÖREVİ yeni yeteneği
/// göstermek değil, <b>mevcut menünün bozulmadığını</b> kilitlemektir:
/// <list type="bullet">
///   <item>üst grup TANIMLANMADIĞI sürece ağaç, bugünkü düz menünün BİREBİR karşılığıdır,</item>
///   <item><see cref="MenuLayout.Build"/> hiç değişmedi (S17 kilidi ayrıca duruyor),</item>
///   <item>yetim düğüm, döngü ve ikiden fazla derinlik oluşamaz (fail-closed).</item>
/// </list>
/// </summary>
public class MenuSectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MenuLayoutService _svc;
    private readonly SessionContext _super;
    private const string Co = "SEC-CO";
    private const string Digeri = "SEC-DIGER";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public MenuSectionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_sec_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','A',1,1,1,0);");
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Digeri}','B',1,1,1,0);");

        _svc = new MenuLayoutService(_factory, _clock);
        _super = new SessionContext("su", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        MenuLayoutService.InvalidateAll();
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private const string Ust = "section:test01";

    /// <summary>Mevcut düzeni girdiye çevirir (arayüzün "tam durum" gönderimini taklit eder).</summary>
    private (List<ScreenLayoutInput> Screens, List<GroupLayoutInput> Groups) Current()
    {
        var rows = _svc.List(_super, null);
        var groups = _svc.Groups(_super);
        var s = rows.Select(r => new ScreenLayoutInput(r.ScreenKey, r.EffectiveLabel, r.EffectiveGroupKey, r.SortOrder)).ToList();
        var g = groups.Select(x => new GroupLayoutInput(x.GroupKey, x.Title, x.SortOrder, x.IsCustom, x.ParentGroupKey)).ToList();
        return (s, g);
    }

    /// <summary>"Yakıt" ve "Araçlar" üst menülerini yeni bir ÜST GRUBUN altına taşır.</summary>
    private void IkiGrubuUstGrubaTasi(string ustAd = "Saha")
    {
        var (s, g) = Current();
        g.Add(new GroupLayoutInput(Ust, ustAd, g.Count, true));
        for (int i = 0; i < g.Count; i++)
            if (g[i].GroupKey is "Yakıt" or "Araçlar")
                g[i] = g[i] with { ParentGroupKey = Ust };
        _svc.Save(_super, s, g);
    }

    private IReadOnlyList<MenuNodeView> Agac()
        => MenuLayout.BuildTree(ScreenPlatform.Web, _svc.LayoutFor(Co), _ => true);

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 1 · GERİ UYUMLULUK — en önemli garanti
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ Firma kaydı YOKKEN ağaç, <b>KATALOG VARSAYILANININ</b> birebir karşılığıdır
    /// (2026-08-19: kullanıcının nihai şeması artık projenin varsayılan menüsüdür).
    ///
    /// Garanti şudur: düz menüdeki HER grup ağaçta tam bir kez bulunur, hiçbir ekran kaybolmaz,
    /// ve bir grubun üst grubu <b>katalogda yazan</b> üst gruptur — ne eksik ne fazla.
    /// </summary>
    [Theory]
    [InlineData(ScreenPlatform.Desktop)]
    [InlineData(ScreenPlatform.Web)]
    public void S01_Kayit_Yokken_Agac_Katalog_Varsayilani(ScreenPlatform platform)
    {
        var duz = MenuLayout.Build(platform, MenuLayoutSet.Empty, _ => true);
        var agac = MenuLayout.BuildTree(platform, MenuLayoutSet.Empty, _ => true);

        // 1) Hiçbir grup kaybolmaz, tekrar etmez ve sırası düz menüdeki sırayla aynıdır.
        var agactakiGruplar = agac.SelectMany(n => n.Groups).Select(g => g.Key).ToList();
        Assert.Equal(duz.Select(g => g.Key), agactakiGruplar);

        // 2) Her grubun ekranları birebir aynı.
        foreach (var d in duz)
        {
            var a = agac.SelectMany(n => n.Groups).Single(g => g.Key == d.Key);
            Assert.Equal(d.Entries.Select(e => e.Screen.Key), a.Entries.Select(e => e.Screen.Key));
        }

        // 3) Gruplama KATALOĞUN dediği gibi: üst grubu olan grup o üst grubun altında, olmayan en üstte.
        foreach (var node in agac)
            foreach (var g in node.Groups)
            {
                var beklenenUst = AppScreens.SectionOfGroup(g.Key);
                Assert.Equal(beklenenUst is null, !node.IsSection);
                if (beklenenUst is not null) Assert.Equal(beklenenUst, node.Key);
            }
    }

    /// <summary>Migration 071 sonrası kayıt yoksa düzen hâlâ BOŞ → menü katalog varsayılanı.</summary>
    [Fact]
    public void S02_Migration_Sonrasi_Duzen_Bos()
        => Assert.True(_svc.LayoutFor(Co).IsEmpty);

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 2 · TEMEL YETENEK
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ İki üst menü bir ÜST GRUBUN altında toplanır; diğerleri en üst seviyede kalır.</summary>
    [Fact]
    public void S03_Ust_Grup_Altinda_Toplanir()
    {
        IkiGrubuUstGrubaTasi();

        var agac = Agac();
        var ustGrup = agac.SingleOrDefault(n => n.Key == Ust);
        Assert.NotNull(ustGrup);
        Assert.True(ustGrup!.IsSection);
        Assert.Equal("Saha", ustGrup.Title);
        Assert.Equal(new[] { "Araçlar", "Yakıt" }, ustGrup.Groups.Select(g => g.Key).OrderBy(x => x).ToArray());

        // Üst gruba girenler artık en üst seviyede TEK BAŞINA görünmez.
        Assert.DoesNotContain(agac.Where(n => !n.IsSection), n => n.Key is "Yakıt" or "Araçlar");
        // Dokunulmayanlar yerinde: Malzemeler katalog üst grubunda kalmalı (elle taşınmadı).
        var malzeme = agac.SelectMany(n => n.Groups).Single(g => g.Key == "Malzemeler");
        Assert.NotNull(malzeme);
        Assert.Equal("section:malzemestok",
            agac.Single(n => n.Groups.Any(g => g.Key == "Malzemeler")).Key);
    }

    /// <summary>Ekranlar kaybolmaz: ağaçtaki toplam ekran sayısı düz menüyle AYNI.</summary>
    [Fact]
    public void S04_Hicbir_Ekran_Kaybolmaz()
    {
        var oncekiSayi = MenuLayout.Build(ScreenPlatform.Web, MenuLayoutSet.Empty, _ => true).Sum(g => g.Entries.Count);
        IkiGrubuUstGrubaTasi();
        var sonrakiSayi = Agac().SelectMany(n => n.Groups).Sum(g => g.Entries.Count);
        Assert.Equal(oncekiSayi, sonrakiSayi);
    }

    /// <summary>Üst grup, İLK ÜYESİNİN bulunduğu yerde açılır (ikinci bir sıralama alanı yok).</summary>
    [Fact]
    public void S05_Ust_Grup_Ilk_Uyesinin_Yerinde_Acilir()
    {
        IkiGrubuUstGrubaTasi();
        var agac = Agac();

        // Katalogda Araçlar, Yakıt'tan önce gelir → üst grup Araçlar'ın yerinde açılmalı.
        var duz = MenuLayout.Build(ScreenPlatform.Web, MenuLayoutSet.Empty, _ => true).Select(g => g.Key).ToList();
        var beklenenYer = duz.IndexOf("Araçlar");
        var gercekYer = agac.ToList().FindIndex(n => n.Key == Ust);
        Assert.Equal(beklenenYer, gercekYer);
    }

    /// <summary>Üst grubun adı değiştirilebilir; anahtarı sabit kalır.</summary>
    [Fact]
    public void S06_Ust_Grup_Adi_Degisir_Anahtar_Sabit()
    {
        IkiGrubuUstGrubaTasi();
        var (s, g) = Current();
        int i = g.FindIndex(x => x.GroupKey == Ust);
        g[i] = g[i] with { Title = "Saha Operasyonu" };
        _svc.Save(_super, s, g);

        var ustGrup = Agac().Single(n => n.Key == Ust);
        Assert.Equal("Saha Operasyonu", ustGrup.Title);
        Assert.Equal(Ust, ustGrup.Key);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 3 · BÜTÜNLÜK VE GÜVENLİK (fail-closed)
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ Üst grup BAŞKA bir üst grubun altına konulamaz → menü ikiden fazla derinleşmez.</summary>
    [Fact]
    public void S07_Ust_Grup_Ust_Gruba_Konulamaz()
    {
        var (s, g) = Current();
        g.Add(new GroupLayoutInput(Ust, "Saha", g.Count, true));
        g.Add(new GroupLayoutInput("section:test02", "Ofis", g.Count, true, Ust));
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>Var olmayan üst gruba bağlanamaz (yetim düğüm oluşmaz).</summary>
    [Fact]
    public void S08_Var_Olmayan_Ust_Gruba_Baglanamaz()
    {
        var (s, g) = Current();
        int i = g.FindIndex(x => x.GroupKey == "Yakıt");
        g[i] = g[i] with { ParentGroupKey = "section:yok" };
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>Bir üst menü kendi kendisinin altına konulamaz (döngü olmaz).</summary>
    [Fact]
    public void S09_Kendine_Baglanamaz()
    {
        var (s, g) = Current();
        int i = g.FindIndex(x => x.GroupKey == "Yakıt");
        g[i] = g[i] with { ParentGroupKey = "Yakıt" };
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>Üst menü, üst grup OLMAYAN bir gruba bağlanamaz (grup içinde grup yok).</summary>
    [Fact]
    public void S10_Siradan_Gruba_Baglanamaz()
    {
        var (s, g) = Current();
        int i = g.FindIndex(x => x.GroupKey == "Yakıt");
        g[i] = g[i] with { ParentGroupKey = "Araçlar" };
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>Adsız üst grup reddedilir.</summary>
    [Fact]
    public void S11_Adsiz_Ust_Grup_Reddedilir()
    {
        var (s, g) = Current();
        g.Add(new GroupLayoutInput(Ust, "   ", g.Count, true));
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>
    /// ⭐ FAIL-SAFE: üst grup veritabanından silinmiş olsa bile ona bağlı üst menü KAYBOLMAZ —
    /// sessizce en üst seviyeye döner. (Servis bunu zaten reddeder; bu, elle bozulmuş veriye karşı
    /// çözümleyicinin savunmasıdır.)
    /// </summary>
    [Fact]
    public void S12_Ust_Grup_Silinse_Bile_Menu_Kaybolmaz()
    {
        IkiGrubuUstGrubaTasi();
        Sql($"DELETE FROM menu_group_layout WHERE company_id='{Co}' AND group_key='{Ust}';");
        MenuLayoutService.Invalidate(Co);

        var agac = Agac();
        Assert.DoesNotContain(agac, n => n.Key == Ust);                      // silinen üst grup yok
        Assert.Contains(agac, n => n.Key == "Yakıt");                        // ⭐ üst menü geri döndü
        Assert.Contains(agac, n => n.Key == "Araçlar");
    }

    /// <summary>Kalıcılık: kayıt yeni bir servis örneğinde de okunur.</summary>
    [Fact]
    public void S13_Kalici()
    {
        IkiGrubuUstGrubaTasi();
        MenuLayoutService.InvalidateAll();
        var yeni = new MenuLayoutService(_factory, _clock);
        var set = yeni.LayoutFor(Co);
        Assert.Equal(Ust, MenuLayout.SectionKeyOf("Yakıt", set));
    }

    /// <summary>⭐ TENANT: bir firmanın üst grubu diğerini ETKİLEMEZ.</summary>
    [Fact]
    public void S14_Firma_Ust_Grubu_Digerine_Sizmaz()
    {
        IkiGrubuUstGrubaTasi();
        Assert.True(_svc.LayoutFor(Digeri).IsEmpty);
        Assert.DoesNotContain(MenuLayout.BuildTree(ScreenPlatform.Web, _svc.LayoutFor(Digeri), _ => true),
            n => n.Key == Ust);
    }

    /// <summary>Yetkisiz kullanıcı üst grup oluşturamaz (mevcut yetki kapısı aynen geçerli).</summary>
    [Fact]
    public void S15_Yetkisiz_Ust_Grup_Olusturamaz()
    {
        var (s, g) = Current();
        g.Add(new GroupLayoutInput(Ust, "Saha", g.Count, true));
        var personel = new SessionContext("p", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _svc.Save(personel, s, g));
    }

    /// <summary>"Varsayılan düzene dön" üst grupları da kaldırır.</summary>
    [Fact]
    public void S16_Varsayilana_Donus_Ust_Gruplari_Kaldirir()
    {
        IkiGrubuUstGrubaTasi();
        _svc.ResetToDefaults(_super);

        Assert.True(_svc.LayoutFor(Co).IsEmpty);
        Assert.DoesNotContain(Agac(), n => n.Key == Ust);
        // Katalog varsayılanı geri gelir: Yakıt yeniden Operasyon üst grubunun altındadır.
        Assert.Equal("section:operasyon", Agac().Single(n => n.Groups.Any(g => g.Key == "Yakıt")).Key);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 4 · BOŞ TANIM SAKLANMAZ (kullanıcı kuralı 2026-08-19)
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ Altında ekran kalmayan ÜST MENÜNÜN tanımı kaydedilmez (boş tanım birikmez).</summary>
    [Fact]
    public void S18_Ekrani_Kalmayan_Grup_Tanimi_Yazilmaz()
    {
        var (s, g) = Current();
        // "Yakıt" grubundaki tüm ekranlar "Araçlar" altına taşınır → "Yakıt" boş kalır.
        for (int i = 0; i < s.Count; i++)
            if (s[i].GroupKey == "Yakıt") s[i] = s[i] with { GroupKey = "Araçlar" };
        // Boş kalan grup, arayüz onu yine de gönderse bile kaydedilmemeli.
        _svc.Save(_super, s, g);

        MenuLayoutService.InvalidateAll();
        Assert.DoesNotContain("Yakıt", _svc.LayoutFor(Co).Groups.Keys);
        Assert.DoesNotContain(Agac(), n => n.Title == "Yakıt");
        // Ekranlar KAYBOLMAZ — hepsi yeni grubunda durur.
        // Sayı katalogdan okunur: ekran eklenip çıkarıldığında test sabit sayı yüzünden kırılmasın,
        // ama "hiçbir ekran düşmedi" garantisi aynen sürsün.
        Assert.Equal(AppScreens.All.Count, _svc.List(_super, null).Count);
    }

    /// <summary>⭐ Altında üst menü kalmayan ÜST GRUBUN tanımı da kaydedilmez.</summary>
    [Fact]
    public void S19_Alti_Bos_Ust_Grup_Tanimi_Yazilmaz()
    {
        var (s, g) = Current();
        g.Add(new GroupLayoutInput(Ust, "Bos Ust Grup", g.Count, true));   // altına hiç grup bağlanmadı
        _svc.Save(_super, s, g);

        MenuLayoutService.InvalidateAll();
        Assert.DoesNotContain(Ust, _svc.LayoutFor(Co).Groups.Keys);
        Assert.DoesNotContain(Agac(), n => n.Key == Ust);
    }

    /// <summary>Dolu ÜST GRUP korunur — kural yalnız BOŞ tanımı eler.</summary>
    [Fact]
    public void S20_Dolu_Ust_Grup_Korunur()
    {
        IkiGrubuUstGrubaTasi();
        MenuLayoutService.InvalidateAll();
        Assert.Contains(Ust, _svc.LayoutFor(Co).Groups.Keys);
        Assert.Contains(Agac(), n => n.IsSection && n.Title == "Saha");
    }

    public void Dispose()
    {
        MenuLayoutService.InvalidateAll();
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
