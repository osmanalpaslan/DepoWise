using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MNU — MENÜ / EKRAN YÖNETİMİ (kullanıcı isteği 2026-08-18) ═══
///
/// Ekranın menüdeki <b>adı · üst menüsü · sırası</b> firma bazında yönetilebilir hâle geldi.
/// Bu testlerin ASIL GÖREVİ yeni özelliği doğrulamak değil, <b>mevcut sistemin bozulmadığını</b>
/// kilitlemektir:
/// <list type="bullet">
///   <item>kayıt yokken menü katalogla BİREBİR aynı (migration sonrası hiçbir şey değişmez),</item>
///   <item>route · ekran anahtarı · yetki anahtarı HİÇ değişmez,</item>
///   <item>yetkisiz kullanıcı ne okuyabilir ne yazabilir (fail-closed),</item>
///   <item>yetim ekran ve kilitlenme oluşamaz.</item>
/// </list>
/// </summary>
public class MenuLayoutTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MenuLayoutService _svc;
    private readonly SessionContext _super, _personel;
    private const string Co = "MNU-CO";
    private const string Digeri = "MNU-DIGER";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public MenuLayoutTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_mnu_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','A',1,1,1,0);");
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Digeri}','B',1,1,1,0);");

        _svc = new MenuLayoutService(_factory, _clock);
        _super = new SessionContext("su", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        _personel = new SessionContext("p", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        MenuLayoutService.InvalidateAll();
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Count(string table)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table};";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>Bir ekranın o anki (çözümlenmiş) satırı.</summary>
    private MenuLayoutRow Row(string screenKey)
        => _svc.List(_super, null).First(r => r.ScreenKey == screenKey);

    /// <summary>Mevcut düzeni girdi listesine çevirir (arayüzün "tam durum" gönderimini taklit eder).</summary>
    private (List<ScreenLayoutInput> Screens, List<GroupLayoutInput> Groups) Current()
    {
        var rows = _svc.List(_super, null);
        var groups = _svc.Groups(_super);
        var s = rows.Select(r => new ScreenLayoutInput(r.ScreenKey, r.EffectiveLabel, r.EffectiveGroupKey, r.SortOrder)).ToList();
        var g = groups.Select(x => new GroupLayoutInput(x.GroupKey, x.Title, x.SortOrder, x.IsCustom)).ToList();
        return (s, g);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 1 · GERİ UYUMLULUK — migration sonrası HİÇBİR ŞEY değişmemeli
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ Kayıt yokken düzen BOŞ olmalı → menüler katalog varsayılanıyla çizilir.</summary>
    [Fact]
    public void M01_Kayit_Yokken_Duzen_Bos()
    {
        Assert.True(_svc.LayoutFor(Co).IsEmpty);
        Assert.Equal(0, Count("screen_menu_layout"));
        Assert.Equal(0, Count("menu_group_layout"));
    }

    /// <summary>⭐ Kayıt yokken üretilen menü kataloğun BİREBİR aynısı (grup sırası + ekran sırası + etiket).</summary>
    [Theory]
    [InlineData(ScreenPlatform.Desktop)]
    [InlineData(ScreenPlatform.Web)]
    public void M02_Menu_Katalogla_Birebir_Ayni(ScreenPlatform platform)
    {
        var uretilen = MenuLayout.Build(platform, _svc.LayoutFor(Co), _ => true);
        Assert.Equal(AppScreens.GroupsFor(platform).Select(g => g.Title), uretilen.Select(g => g.Key));
        foreach (var g in uretilen)
            Assert.Equal(AppScreens.ScreensOf(g.Key, platform).Select(s => s.Label),
                         g.Entries.Select(e => e.Label));
    }

    /// <summary>
    /// ⭐ REGRESYON (bu testler yakaladı): birleşik maske <c>Desktop|Web</c> "İKİSİNDE DE olan" değil
    /// <b>"en az BİRİNDE olan"</b> anlamına gelmeli. <c>HasFlag</c> kullanıldığında yalnız tek platformda
    /// bulunan 14 ekran (Kota İzleme, Malzeme Şablonları, Yedek Yönetimi…) yönetim listesinden sessizce
    /// düşüyordu → yönetici onları hiç göremiyor, düzenleyemiyordu.
    /// </summary>
    [Fact]
    public void M02b_Yonetim_Listesi_TUM_Ekranlari_Icerir()
    {
        var hepsi = MenuLayout.Build(ScreenPlatform.Desktop | ScreenPlatform.Web, MenuLayoutSet.Empty, _ => true);
        Assert.Equal(AppScreens.All.Count, hepsi.Sum(g => g.Entries.Count));

        var listede = _svc.List(_super, null).Select(r => r.ScreenKey).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(AppScreens.All.Count, listede.Count);
        // Tek platformlu örnekler gerçekten var mı?
        Assert.Contains("quota_monitor", listede);        // yalnız web
        Assert.Contains("material_templates", listede);   // yalnız masaüstü
    }

    /// <summary>Katalog varsayılanına EŞİT düzen kaydedilirse tabloya satır YAZILMAZ (gereksiz veri yok).</summary>
    [Fact]
    public void M03_Varsayilanla_Ayni_Kayit_Satir_Yazmaz()
    {
        var (s, g) = Current();
        var r = _svc.Save(_super, s, g);
        Assert.Equal(0, r.ScreensChanged);
        Assert.Equal(0, r.GroupsChanged);
        Assert.Equal(0, Count("screen_menu_layout"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 2 · TEMEL YETENEKLER
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Ekranın GÖRÜNEN adı değişir; kimliği (anahtar · route · yetki) DEĞİŞMEZ.</summary>
    [Fact]
    public void M04_Ekran_Adi_Degisir_Kimlik_Degismez()
    {
        var once = Row("materials.list");
        var (s, g) = Current();
        s[s.FindIndex(x => x.ScreenKey == "materials.list")] =
            new ScreenLayoutInput("materials.list", "Stok / Malzemeler", once.EffectiveGroupKey, once.SortOrder);
        _svc.Save(_super, s, g);

        var sonra = Row("materials.list");
        Assert.Equal("Stok / Malzemeler", sonra.EffectiveLabel);
        Assert.Equal("Malzeme Listesi", sonra.CatalogLabel);      // özgün ad korunur
        Assert.Equal(once.WebRoute, sonra.WebRoute);              // ⭐ adres DEĞİŞMEDİ
        Assert.Equal(once.PermissionKey, sonra.PermissionKey);    // ⭐ yetki anahtarı DEĞİŞMEDİ
        Assert.Equal(once.ModuleKey, sonra.ModuleKey);
        // Katalog nesnesi de dokunulmamış olmalı.
        Assert.Equal("Malzeme Listesi", AppScreens.ByKey("materials.list")!.Label);
        Assert.Equal("materials", AppScreens.ByKey("materials.list")!.WebRoute);
    }

    /// <summary>Üst menünün adı değişir; ANAHTARI değişmez → hiçbir referans kırılmaz.</summary>
    [Fact]
    public void M05_Grup_Adi_Degisir_Anahtar_Sabit()
    {
        var (s, g) = Current();
        int i = g.FindIndex(x => x.GroupKey == "Ön Muhasebe");
        g[i] = new GroupLayoutInput("Ön Muhasebe", "Muhasebe", g[i].SortOrder, false);
        _svc.Save(_super, s, g);

        var set = _svc.LayoutFor(Co);
        Assert.Equal("Muhasebe", MenuLayout.GroupTitleOf("Ön Muhasebe", set));
        // Ekranlar hâlâ AYNI anahtara bağlı — taşınmadılar.
        Assert.Equal("Ön Muhasebe", MenuLayout.GroupKeyOf(AppScreens.ByKey("accounting.parties")!, set));
        // Menüde yeni başlıkla görünür.
        var web = MenuLayout.Build(ScreenPlatform.Web, set, _ => true);
        Assert.Contains(web, x => x.Key == "Ön Muhasebe" && x.Title == "Muhasebe");
    }

    /// <summary>Ekran başka üst menüye taşınır.</summary>
    [Fact]
    public void M06_Ekran_Baska_Gruba_Tasinir()
    {
        var (s, g) = Current();
        int i = s.FindIndex(x => x.ScreenKey == "inspection");
        s[i] = new ScreenLayoutInput("inspection", "Muayene / Sigorta", "Yönetim", 99);
        _svc.Save(_super, s, g);

        Assert.Equal("Yönetim", Row("inspection").EffectiveGroupKey);
        var web = MenuLayout.Build(ScreenPlatform.Web, _svc.LayoutFor(Co), _ => true);
        Assert.Contains(web.First(x => x.Key == "Yönetim").Entries, e => e.Screen.Key == "inspection");
        Assert.DoesNotContain(web.First(x => x.Key == "Araçlar").Entries, e => e.Screen.Key == "inspection");
    }

    /// <summary>Üst menü sırası değişir.</summary>
    [Fact]
    public void M07_Grup_Sirasi_Degisir()
    {
        var (s, g) = Current();
        // "Çöp Kutusu" en başa alınır.
        var cop = g.First(x => x.GroupKey == "Çöp Kutusu");
        g.Remove(cop);
        g.Insert(0, cop);
        for (int i = 0; i < g.Count; i++) g[i] = g[i] with { SortOrder = i };
        _svc.Save(_super, s, g);

        var web = MenuLayout.Build(ScreenPlatform.Web, _svc.LayoutFor(Co), _ => true);
        Assert.Equal("Çöp Kutusu", web[0].Key);
    }

    /// <summary>Grup içindeki ekran sırası değişir.</summary>
    [Fact]
    public void M08_Ekran_Sirasi_Degisir()
    {
        var (s, g) = Current();
        // Yakıt grubunda "Özet" en başa alınsın (varsayılan: dist, depot, summary).
        SetOrder(s, "fuel.summary", 0);
        SetOrder(s, "fuel.dist", 1);
        SetOrder(s, "fuel.depot", 2);
        _svc.Save(_super, s, g);

        var yakit = MenuLayout.Build(ScreenPlatform.Web, _svc.LayoutFor(Co), _ => true)
            .First(x => x.Key == "Yakıt");
        Assert.Equal(new[] { "fuel.summary", "fuel.dist", "fuel.depot" },
                     yakit.Entries.Select(e => e.Screen.Key).ToArray());
    }

    private static void SetOrder(List<ScreenLayoutInput> s, string key, int order)
    {
        int i = s.FindIndex(x => x.ScreenKey == key);
        s[i] = s[i] with { SortOrder = order };
    }

    /// <summary>Kullanıcının oluşturduğu yeni üst menü + oraya ekran taşıma.</summary>
    [Fact]
    public void M09_Kullanici_Grubu_Olusturulur()
    {
        var (s, g) = Current();
        g.Add(new GroupLayoutInput("custom:depo01", "Depo İşleri", g.Count, true));
        int i = s.FindIndex(x => x.ScreenKey == "stock.count");
        s[i] = new ScreenLayoutInput("stock.count", "Stok Sayım", "custom:depo01", 0);
        _svc.Save(_super, s, g);

        var set = _svc.LayoutFor(Co);
        Assert.Equal("Depo İşleri", MenuLayout.GroupTitleOf("custom:depo01", set));
        var web = MenuLayout.Build(ScreenPlatform.Web, set, _ => true);
        var grup = web.First(x => x.Key == "custom:depo01");
        Assert.Equal("Depo İşleri", grup.Title);
        Assert.Single(grup.Entries);
        Assert.Equal("stock.count", grup.Entries[0].Screen.Key);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 3 · GÜVENLİK VE BÜTÜNLÜK
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ Yetkisiz kullanıcı listeyi OKUYAMAZ (UI'da gizlemek yeterli sayılmaz).</summary>
    [Fact]
    public void M10_Yetkisiz_Okuyamaz()
    {
        Assert.Throws<ForbiddenException>(() => _svc.List(_personel, null));
        Assert.Throws<ForbiddenException>(() => _svc.Groups(_personel));
    }

    /// <summary>⭐ Yetkisiz kullanıcı YAZAMAZ — servis katmanında fail-closed.</summary>
    [Fact]
    public void M11_Yetkisiz_Yazamaz()
    {
        var (s, g) = Current();
        Assert.Throws<ForbiddenException>(() => _svc.Save(_personel, s, g));
        Assert.Throws<ForbiddenException>(() => _svc.ResetToDefaults(_personel));
        Assert.Equal(0, Count("screen_menu_layout"));
    }

    /// <summary>⭐ YETİM EKRAN: var olmayan gruba taşıma REDDEDİLİR ve HİÇBİR ŞEY yazılmaz (atomik).</summary>
    [Fact]
    public void M12_Var_Olmayan_Gruba_Tasima_Reddedilir()
    {
        var (s, g) = Current();
        // Aynı pakette geçerli bir değişiklik de var — reddin ATOMİK olduğu görülsün.
        s[s.FindIndex(x => x.ScreenKey == "personnel")] =
            new ScreenLayoutInput("personnel", "Personel Girişi", "custom:yok", 0);
        s[s.FindIndex(x => x.ScreenKey == "alerts")] =
            new ScreenLayoutInput("alerts", "Bildirimler", "Uyarılar", 0);

        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
        Assert.Equal(0, Count("screen_menu_layout"));                 // kısmi kayıt YOK
        Assert.Equal("Uyarılar", Row("alerts").EffectiveLabel);       // geçerli değişiklik de yazılmadı
    }

    /// <summary>Bilinmeyen ekran anahtarı reddedilir (katalog dışı ekran uydurulmaz).</summary>
    [Fact]
    public void M13_Bilinmeyen_Ekran_Reddedilir()
    {
        var (s, g) = Current();
        s.Add(new ScreenLayoutInput("uydurma.ekran", "Sızma", "Yönetim", 0));
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>Katalog dışı ve "custom:" önekli olmayan grup anahtarı reddedilir.</summary>
    [Fact]
    public void M14_Kacak_Grup_Anahtari_Reddedilir()
    {
        var (s, g) = Current();
        g.Add(new GroupLayoutInput("uydurma-grup", "Sızma", g.Count, true));
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>Aşırı uzun ad reddedilir (menü taşmasın).</summary>
    [Fact]
    public void M15_Cok_Uzun_Ad_Reddedilir()
    {
        var (s, g) = Current();
        int i = s.FindIndex(x => x.ScreenKey == "reports");
        s[i] = s[i] with { Label = new string('X', MenuLayoutService.MaxLabelLength + 1) };
        Assert.Throws<ArgumentException>(() => _svc.Save(_super, s, g));
    }

    /// <summary>Satır sonu / sekme temizlenir; boş ad varsayılana döner.</summary>
    [Fact]
    public void M16_Ad_Temizlenir_Bos_Ad_Varsayilana_Doner()
    {
        var (s, g) = Current();
        SetLabel(s, "reports", "Yeni\r\nRapor\tEkranı");
        SetLabel(s, "personnel", "   ");
        _svc.Save(_super, s, g);

        Assert.Equal("Yeni Rapor Ekranı", Row("reports").EffectiveLabel);
        Assert.Equal("Personel Girişi", Row("personnel").EffectiveLabel);   // katalog varsayılanı
    }

    private static void SetLabel(List<ScreenLayoutInput> s, string key, string label)
    {
        int i = s.FindIndex(x => x.ScreenKey == key);
        s[i] = s[i] with { Label = label };
    }

    /// <summary>⭐ TENANT: bir firmanın düzeni diğerini ETKİLEMEZ.</summary>
    [Fact]
    public void M17_Firma_Duzeni_Digerine_Sizmaz()
    {
        var (s, g) = Current();
        SetLabel(s, "reports", "A Firması Raporları");
        _svc.Save(_super, s, g);

        Assert.True(_svc.LayoutFor(Digeri).IsEmpty);
        var digerininMenusu = MenuLayout.Build(ScreenPlatform.Web, _svc.LayoutFor(Digeri), _ => true);
        Assert.Contains(digerininMenusu.SelectMany(x => x.Entries), e => e.Label == "Raporlar");
        Assert.DoesNotContain(digerininMenusu.SelectMany(x => x.Entries), e => e.Label == "A Firması Raporları");
    }

    /// <summary>Silinmiş/bilinmeyen gruba işaret eden ESKİ kayıt menüyü bozmaz — ekran özgün grubuna döner.</summary>
    [Fact]
    public void M18_Bilinmeyen_Gruba_Isaret_Eden_Kayit_Yetim_Birakmaz()
    {
        // Servisin reddettiği durumu VERİTABANINA elle yazarak simüle et (grup satırı olmadan).
        Sql($"INSERT INTO screen_menu_layout(id,company_id,screen_key,label_override,group_key_override," +
            $"sort_order,created_at,updated_at) VALUES('x1','{Co}','personnel',NULL,'custom:silinmis',0,1,1);");
        MenuLayoutService.Invalidate(Co);

        var set = _svc.LayoutFor(Co);
        Assert.Equal("Personel", MenuLayout.GroupKeyOf(AppScreens.ByKey("personnel")!, set));

        var web = MenuLayout.Build(ScreenPlatform.Web, set, _ => true);
        Assert.Contains(web.SelectMany(x => x.Entries), e => e.Screen.Key == "personnel");   // ⭐ kaybolmadı
        Assert.DoesNotContain(web, x => x.Key == "custom:silinmis");
    }

    /// <summary>Aynı sıra iki kayda verilse bile sonuç DETERMİNİSTİK (her çağrıda aynı).</summary>
    [Fact]
    public void M19_Cakisan_Sira_Deterministik()
    {
        foreach (var (key, i) in new[] { ("fuel.dist", 0), ("fuel.depot", 0), ("fuel.summary", 0) })
            Sql($"INSERT INTO screen_menu_layout(id,company_id,screen_key,label_override,group_key_override," +
                $"sort_order,created_at,updated_at) VALUES('{key}','{Co}','{key}',NULL,NULL,{i},1,1);");
        MenuLayoutService.Invalidate(Co);

        var set = _svc.LayoutFor(Co);
        var ilk = MenuLayout.Build(ScreenPlatform.Web, set, _ => true).First(x => x.Key == "Yakıt")
            .Entries.Select(e => e.Screen.Key).ToArray();
        for (int i = 0; i < 5; i++)
            Assert.Equal(ilk, MenuLayout.Build(ScreenPlatform.Web, set, _ => true)
                .First(x => x.Key == "Yakıt").Entries.Select(e => e.Screen.Key).ToArray());
        Assert.Equal(3, ilk.Length);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 4 · KALICILIK VE GERİ ALMA
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Kaydedilen düzen yeni bir servis örneğinde (uygulama yeniden başlatma / sayfa yenileme) korunur.</summary>
    [Fact]
    public void M20_Kayit_Yenilemeden_Sonra_Korunur()
    {
        var (s, g) = Current();
        SetLabel(s, "vehicles.list", "Araç Kartları");
        _svc.Save(_super, s, g);

        MenuLayoutService.InvalidateAll();
        var yeni = new MenuLayoutService(_factory, _clock);
        Assert.Equal("Araç Kartları",
            MenuLayout.LabelOf(AppScreens.ByKey("vehicles.list")!, yeni.LayoutFor(Co)));
    }

    /// <summary>"Varsayılan düzene dön" tüm düzen tercihlerini kaldırır.</summary>
    [Fact]
    public void M21_Varsayilana_Donus_Temizler()
    {
        var (s, g) = Current();
        SetLabel(s, "vehicles.list", "Araç Kartları");
        _svc.Save(_super, s, g);
        Assert.True(Count("screen_menu_layout") > 0);

        _svc.ResetToDefaults(_super);

        Assert.True(_svc.LayoutFor(Co).IsEmpty);
        Assert.Equal(0, Count("screen_menu_layout"));
        Assert.Equal(0, Count("menu_group_layout"));
        Assert.Equal("Araç Listesi", Row("vehicles.list").EffectiveLabel);
    }

    /// <summary>Yazma sonrası önbellek ANINDA düşer (yönetici bayat menü görmez).</summary>
    [Fact]
    public void M22_Yazmada_Onbellek_Duser()
    {
        _ = _svc.LayoutFor(Co);                       // önbelleğe al
        var (s, g) = Current();
        SetLabel(s, "audit", "Kayıt Defteri");
        _svc.Save(_super, s, g);
        Assert.Equal("Kayıt Defteri", MenuLayout.LabelOf(AppScreens.ByKey("audit")!, _svc.LayoutFor(Co)));
    }

    /// <summary>Değişiklik audit'e yazılır (kim · ne zaman · ne değişti).</summary>
    [Fact]
    public void M23_Degisiklik_Audit_e_Yazilir()
    {
        var (s, g) = Current();
        SetLabel(s, "audit", "Kayıt Defteri");
        _svc.Save(_super, s, g);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM audit_logs WHERE entity_type='menu_layout' AND company_id='{Co}';";
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) > 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // 5 · ÇEVRİMDIŞI / BOZUK ŞEMA DAYANIKLILIĞI (masaüstü)
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>⭐ Tablo hiç yoksa (eski yerel veritabanı) menü ÇÖKMEZ — katalog varsayılanı geçerli.</summary>
    [Fact]
    public void M24_Tablo_Yoksa_Menu_Cokmez()
    {
        Sql("DROP TABLE screen_menu_layout;");
        Sql("DROP TABLE menu_group_layout;");
        MenuLayoutService.Invalidate(Co);

        var set = _svc.LayoutFor(Co);
        Assert.True(set.IsEmpty);
        var web = MenuLayout.Build(ScreenPlatform.Web, set, _ => true);
        Assert.NotEmpty(web);
        Assert.Equal(AppScreens.GroupsFor(ScreenPlatform.Web).Select(g => g.Title), web.Select(g => g.Key));
    }

    public void Dispose()
    {
        MenuLayoutService.InvalidateAll();
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
