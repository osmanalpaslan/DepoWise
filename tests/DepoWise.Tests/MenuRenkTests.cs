using System.Globalization;
using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 2 (ADR-221, 2026-09-05) — MENÜ HİYERARŞİ RENK SİSTEMİ ═══
///
/// Kullanıcının kabul kriterleri tek tek ölçülür:
/// <list type="number">
///   <item>Renk ekran bazında HARDCODE EDİLMEZ — hiyerarşiden miras alınır,</item>
///   <item>Sistem bugünkü 70 ekran için değil, YENİ eklenenler için de çalışır,</item>
///   <item>Web ve masaüstü AYNI hiyerarşik anlamı taşır (hex eşitliği şart değil),</item>
///   <item>Renk TEK BAŞINA anlam taşımaz + kontrast yeterli,</item>
///   <item>Üst grup / üst menü / ekran görsel olarak ayrışır,</item>
///   <item>Mevcut menü ve yetki davranışı DEĞİŞMEZ.</item>
/// </list>
///
/// <b>Kontrast göz kararı değildir:</b> WCAG 2.1 bağıl ışıklılık formülü burada HESAPLANIR ve
/// metin dışı arayüz bileşeni eşiği (1.4.11 → <b>3:1</b>) doğrulanır.
/// </summary>
public class MenuRenkTests
{
    // ── WCAG bağıl ışıklılık ────────────────────────────────────────────────────────────────

    private static double Kanal(double c)
        => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    /// <summary>WCAG 2.1 bağıl ışıklılık (0 = siyah, 1 = beyaz).</summary>
    private static double Isiklilik(string hex)
    {
        var h = hex.TrimStart('#');
        var r = int.Parse(h[..2], NumberStyles.HexNumber) / 255.0;
        var g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber) / 255.0;
        var b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber) / 255.0;
        return 0.2126 * Kanal(r) + 0.7152 * Kanal(g) + 0.0722 * Kanal(b);
    }

    private static double Kontrast(string hex1, string hex2)
    {
        var a = Isiklilik(hex1);
        var b = Isiklilik(hex2);
        var (buyuk, kucuk) = a > b ? (a, b) : (b, a);
        return (buyuk + 0.05) / (kucuk + 0.05);
    }

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    // ══════════════ 1) RENK HİYERARŞİDEN GELİR, HARDCODE DEĞİL ══════════════

    /// <summary>
    /// ⭐ EN ÖNEMLİ KABUL KRİTERİ: ekranın kendi rengi YOKTUR.
    ///
    /// Katalogdaki 70 ekranın her biri, ait olduğu üst menünün ailesiyle AYNI aileyi almalıdır.
    /// Bir ekran grubundan farklı bir aile alıyorsa, renk bir yerde ekrana özel yazılmış demektir.
    /// </summary>
    [Fact]
    public void RNK1_Her_Ekran_Ailesini_Ust_Menusunden_Miras_Alir()
    {
        var sapanlar = new List<string>();

        foreach (var ekran in AppScreens.All)
        {
            var ekranAilesi = MenuPalette.ForScreen(ekran.Key);
            var grupAilesi = MenuPalette.ForGroup(ekran.Group);
            if (ekranAilesi != grupAilesi)
                sapanlar.Add($"{ekran.Key} → {ekranAilesi} (grubu '{ekran.Group}' → {grupAilesi})");
        }

        Assert.True(sapanlar.Count == 0,
            "Ekran, üst menüsünün ailesinden SAPMIŞ (renk bir yerde ekrana özel yazılmış):\n  " +
            string.Join("\n  ", sapanlar));
    }

    /// <summary>⭐ Üst menü de ailesini ÜST GRUBUNDAN alır — zincirin ikinci halkası.</summary>
    [Fact]
    public void RNK2_Ust_Menu_Ailesini_Ust_Grubundan_Miras_Alir()
    {
        var sapanlar = new List<string>();

        foreach (var grup in AppScreens.Groups.Where(g => g.Section is { Length: > 0 }))
        {
            var bolum = AppScreens.Sections.FirstOrDefault(s => s.Key == grup.Section);
            if (bolum is null) continue;

            var grupAilesi = MenuPalette.ForGroup(grup.Title);
            var bolumAilesi = MenuPalette.ForSection(bolum.Title);
            if (grupAilesi != bolumAilesi)
                sapanlar.Add($"{grup.Title} → {grupAilesi} (üst grubu '{bolum.Title}' → {bolumAilesi})");
        }

        Assert.True(sapanlar.Count == 0,
            "Üst menü, üst grubunun ailesinden SAPMIŞ:\n  " + string.Join("\n  ", sapanlar));
    }

    /// <summary>
    /// ⭐ HİÇBİR YERDE EKRANA/MENÜYE ÖZEL RENK YAZILMAMIŞ olmalı.
    ///
    /// Kaynak taranır: ekran anahtarının yanında bir renk sabiti (hex) geçiyorsa desen bozulmuş
    /// demektir. Bu test "ScreenX = Blue" tarzı sızıntıyı yakalar.
    /// </summary>
    [Fact]
    public void RNK3_Kaynakta_Ekrana_Ozel_Renk_Yok()
    {
        // Aile eşlemesi YALNIZ bu iki dosyada olmalı; başka yerde aile→renk kararı verilemez.
        var palet = Oku("src", "DepoWise.Application", "Security", "MenuPalette.cs");
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6}"), palet);   // ortak katman RENK TAŞIMAZ

        // Masaüstü çözücüsü de ham renk taşımaz — yalnız kaynak ANAHTARI.
        var masaustu = Oku("src", "DepoWise.Desktop", "DesktopMenuColors.cs");
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6}"), masaustu);
    }

    // ══════════════ 2) YENİ EKLENENLER İÇİN DE ÇALIŞIR ══════════════

    /// <summary>
    /// ⭐ YENİ EKRAN — geliştirici renk tanımlamaz.
    ///
    /// Katalogdaki bir üst menüye yarın yeni bir ekran eklenirse, o ekran ailesini otomatik alır.
    /// Burada bu, "ekranın ailesi = grubunun ailesi" kuralının katalogdan BAĞIMSIZ çalıştığı
    /// gösterilerek kanıtlanır: henüz var olmayan bir anahtar, grubu üzerinden çözülür.
    /// </summary>
    [Fact]
    public void RNK4_Yeni_Ekran_Rengi_Otomatik_Alir()
    {
        // ── (a) EKRANA ÖZEL RENK TABLOSU YOK ────────────────────────────────────────────────
        // Asıl kanıt budur: yeni ekran eklerken "renk kaydı" yapılacak bir yer BULUNMUYOR.
        // MenuPalette'te ekran anahtarıyla eşleşen HİÇBİR sözlük girdisi olmamalı.
        var kaynak = Oku("src", "DepoWise.Application", "Security", "MenuPalette.cs");
        var ekranAnahtarlari = AppScreens.All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        var kaynaktaGecen = Regex.Matches(kaynak, @"\[""([^""]+)""\]")
                                 .Select(m => m.Groups[1].Value)
                                 .Where(ekranAnahtarlari.Contains)
                                 .ToList();

        Assert.True(kaynaktaGecen.Count == 0,
            "MenuPalette'te EKRANA ÖZEL renk girdisi var — miras deseni bozulmuş: " +
            string.Join(", ", kaynaktaGecen));

        // ── (b) 70 ekranın tamamı GRUBU üzerinden çözülüyor ────────────────────────────────
        // Yani renk, ekran satırından değil hiyerarşiden geliyor: yarın eklenecek 71. ekran da
        // hiçbir renk tanımı yapılmadan grubunun ailesini alır.
        foreach (var ekran in AppScreens.All)
            Assert.Equal(MenuPalette.ForGroup(ekran.Group), MenuPalette.ForScreen(ekran.Key));

        // ── (c) Katalogda olmayan ekran → nötr (sessizce YANLIŞ bir aileye düşmez) ─────────
        Assert.Equal(MenuPalette.Neutral, MenuPalette.ForScreen("henuz-yok-" + Guid.NewGuid().ToString("N")));
    }

    /// <summary>
    /// ⭐ YENİ ÜST MENÜ — eşlemede karşılığı olmasa bile RENKSİZ KALMAZ ve renk KAYMAZ.
    ///
    /// Kullanıcı şartı: "sistem otomatik olarak doğru renk tokenını belirleyebilmeli".
    /// Belirlenimci dağıtım kullanılır; aynı ad her zaman aynı aileyi verir (uygulama yeniden
    /// başlatıldığında menü renkleri değişmez).
    /// </summary>
    [Fact]
    public void RNK5_Yeni_Ust_Menu_Belirlenimci_Aile_Alir()
    {
        var yeniAdlar = new[] { "Kalite Kontrol", "Lojistik", "İnsan Kaynakları", "Üretim Planlama", "Ar-Ge" };

        foreach (var ad in yeniAdlar)
        {
            var aile = MenuPalette.ForGroup(ad);

            // (a) Renksiz kalmaz ve bilinen bir ailedir.
            Assert.Contains(aile, MenuPalette.AllFamilies);
            // (b) Nötre düşmez — yeni menü gerçek bir aile alır.
            Assert.NotEqual(MenuPalette.Neutral, aile);
            // (c) BELİRLENİMCİ: aynı ad, aynı sonuç (100 kez).
            for (int i = 0; i < 100; i++)
                Assert.Equal(aile, MenuPalette.ForGroup(ad));
        }

        // Farklı adlar aynı aileye düşebilir (6 aile, sonsuz ad) — ama dağılım tek aileye
        // yığılmamalı; aksi hâlde "otomatik renk" işe yaramaz.
        var dagilim = Enumerable.Range(0, 60)
            .Select(i => MenuPalette.ForGroup("Deneme Menüsü " + i))
            .Distinct()
            .Count();
        Assert.True(dagilim >= 4, $"60 yeni menü yalnız {dagilim} aileye dağıldı — dağılım zayıf.");
    }

    // ══════════════ 3) İKİ PLATFORM AYNI ANLAMI TAŞIR ══════════════

    /// <summary>
    /// ⭐ Her ailenin İKİ platformda da karşılığı olmalı.
    ///
    /// Hex eşitliği şart değildir (kullanıcı şartı), ama bir aile bir platformda çevrilemiyorsa
    /// o menü orada renksiz kalır ve iki arayüz ayrışır.
    /// </summary>
    [Fact]
    public void RNK6_Her_Aile_Iki_Platformda_Da_Cevrilebiliyor()
    {
        var masaustu = Oku("src", "DepoWise.Desktop", "DesktopMenuColors.cs");
        var palet = Oku("src", "DepoWise.Desktop", "Themes", "Palette.axaml");
        var webCss = Oku("src", "DepoWise.Web", "wwwroot", "app.css");
        var navMenu = Oku("src", "DepoWise.Web", "Components", "Layout", "NavMenu.razor");

        var eksikMasaustu = new List<string>();
        var cizilmemis = new List<string>();
        var eksikWeb = new List<string>();

        foreach (var aile in MenuPalette.AllFamilies)
        {
            // Masaüstü: aile → fırça anahtarı → Palette.axaml'de gerçekten TANIMLI mı?
            var m = Regex.Match(masaustu, @"\[MenuPalette\.\w+\]\s*=\s*""(MenuFamily\w+Brush)""");
            var anahtarlar = Regex.Matches(masaustu, @"=\s*""(MenuFamily\w+Brush)""")
                                  .Select(x => x.Groups[1].Value).ToList();
            if (!masaustu.Contains($"[MenuPalette.{AileSabiti(aile)}]", StringComparison.Ordinal))
                eksikMasaustu.Add(aile);

            // Web: CSS değişkeni ve sınıfı var mı?
            if (!webCss.Contains($"--dw-menu-{aile}", StringComparison.Ordinal)) eksikWeb.Add(aile + " (değişken)");
            if (!webCss.Contains($".dw-fam-{aile}", StringComparison.Ordinal)) eksikWeb.Add(aile + " (sınıf)");
        }

        // Masaüstündeki her fırça anahtarı Palette.axaml'de İKİ temada da tanımlı olmalı.
        foreach (var anahtar in Regex.Matches(masaustu, @"=\s*""(MenuFamily\w+Brush)""")
                                     .Select(x => x.Groups[1].Value).Distinct())
        {
            var adet = Regex.Matches(palet, $"x:Key=\"{anahtar}\"").Count;
            if (adet < 2) cizilmemis.Add($"{anahtar} ({adet} temada tanımlı, 2 olmalı)");
        }

        Assert.True(eksikMasaustu.Count == 0, "Masaüstünde karşılığı olmayan aile: " + string.Join(", ", eksikMasaustu));
        Assert.True(cizilmemis.Count == 0, "Palette.axaml'de eksik fırça: " + string.Join(", ", cizilmemis));
        Assert.True(eksikWeb.Count == 0, "Web'de karşılığı olmayan aile: " + string.Join(", ", eksikWeb));

        // Sınıf üretimi iki platformda AYNI seviye adlarını kullanır (section/group/screen).
        foreach (var seviye in new[] { "section", "group", "screen" })
            Assert.Contains($"dw-fam-{seviye}", navMenu + webCss);
    }

    private static string AileSabiti(string aile) => aile switch
    {
        MenuPalette.Stock => "Stock",
        MenuPalette.Operations => "Operations",
        MenuPalette.Finance => "Finance",
        MenuPalette.Reports => "Reports",
        MenuPalette.Corporate => "Corporate",
        MenuPalette.System => "System",
        _ => "Neutral",
    };

    // ══════════════ 4) KONTRAST — ÖLÇÜLEREK ══════════════

    /// <summary>
    /// ⭐ MASAÜSTÜ KONTRASTI — WCAG 1.4.11 (metin dışı bileşen) eşiği <b>3:1</b>.
    ///
    /// Aile çubuğu kenar menüsü zemini üzerine çizilir. Koyu temada zemin <c>#0F1524</c>,
    /// açık temada <c>#F1F3F8</c>'dir (Palette.axaml → SidebarBackgroundBrush).
    /// Renkler göz kararı seçilmedi; burada HESAPLANIR.
    /// </summary>
    [Fact]
    public void RNK7_Masaustu_Aile_Renkleri_Kontrasti_Saglar()
    {
        var palet = Oku("src", "DepoWise.Desktop", "Themes", "Palette.axaml");

        // Tema bloklarını ayır (Dark önce, Light sonra — dosyadaki sıra).
        var darkBas = palet.IndexOf("x:Key=\"Dark\"", StringComparison.Ordinal);
        var lightBas = palet.IndexOf("x:Key=\"Light\"", StringComparison.Ordinal);
        Assert.True(darkBas > 0 && lightBas > darkBas, "Palette.axaml tema blokları bulunamadı.");

        var koyu = palet[darkBas..lightBas];
        var acik = palet[lightBas..];

        var zeminler = new[]
        {
            ("KOYU", koyu, Zemin(koyu)),
            ("AÇIK", acik, Zemin(acik)),
        };

        var zayiflar = new List<string>();
        foreach (var (ad, blok, zemin) in zeminler)
        {
            foreach (Match m in Regex.Matches(blok, @"x:Key=""(MenuFamily\w+Brush)""\s+Color=""(#[0-9A-Fa-f]{6})"""))
            {
                var oran = Kontrast(m.Groups[2].Value, zemin);
                if (oran < 3.0)
                    zayiflar.Add($"{ad} · {m.Groups[1].Value} {m.Groups[2].Value} → {oran:F2}:1 (zemin {zemin})");
            }
        }

        Assert.True(zayiflar.Count == 0,
            "WCAG 3:1 altında kalan aile rengi:\n  " + string.Join("\n  ", zayiflar));
    }

    private static string Zemin(string temaBloku)
    {
        var m = Regex.Match(temaBloku, @"x:Key=""SidebarBackgroundBrush""\s+Color=""(#[0-9A-Fa-f]{6})""");
        Assert.True(m.Success, "SidebarBackgroundBrush bulunamadı.");
        return m.Groups[1].Value;
    }

    /// <summary>
    /// ⭐ WEB KONTRASTI — web'de tek renk değeri İKİ temada da kullanılır (CSS'te koyu/açık
    /// kapsam yok; tüm renkler MudBlazor değişkenlerinden gelir). Bu yüzden aile renkleri
    /// HEM beyaza yakın HEM koyu çekmece zemininde 3:1 vermelidir — dar bir pencere.
    /// Test bu pencereyi ölçer; renk seçimi tahmine bırakılmaz.
    /// </summary>
    [Fact]
    public void RNK8_Web_Aile_Renkleri_Iki_Zeminde_De_Kontrast_Saglar()
    {
        var css = Oku("src", "DepoWise.Web", "wwwroot", "app.css");

        // 🔴 BU DEĞERLER TAHMİN DEĞİL, ÖLÇÜMDÜR (2026-09-05, izole QA sunucusu :5285).
        // Tarayıcıda `getComputedStyle(.mud-drawer).backgroundColor` ile okundu.
        //
        // İlk hâlinde açık zemin "#FFFFFF" VARSAYILMIŞTI ve test geçiyordu; oysa gerçek çekmece
        // #F1F3F8'dir ve o zeminde `finance` rengi 2.97:1 ile eşiğin ALTINDA kalıyordu. Yani test
        // yeşilken arayüzde gerçek bir erişilebilirlik kusuru vardı. Varsayım yerine ölçüm.
        const string acikZemin = "#F1F3F8";   // açık tema çekmece zemini (ölçüldü)
        const string koyuZemin = "#0F1524";   // koyu tema çekmece zemini (ölçüldü)

        var zayiflar = new List<string>();
        foreach (Match m in Regex.Matches(css, @"--dw-menu-(\w+):\s*(#[0-9A-Fa-f]{6});"))
        {
            var renk = m.Groups[2].Value;
            var a = Kontrast(renk, acikZemin);
            var k = Kontrast(renk, koyuZemin);
            if (a < 3.0 || k < 3.0)
                zayiflar.Add($"{m.Groups[1].Value} {renk} → açık {a:F2}:1 · koyu {k:F2}:1");
        }

        Assert.True(zayiflar.Count == 0,
            "Web aile rengi iki zeminden birinde 3:1 altında:\n  " + string.Join("\n  ", zayiflar));
    }

    /// <summary>⭐ Aileler BİRBİRİNDEN de ayrışmalı: iki aile aynı rengi kullanamaz.</summary>
    [Fact]
    public void RNK9_Aileler_Birbirinden_Ayrisir()
    {
        var css = Oku("src", "DepoWise.Web", "wwwroot", "app.css");
        var renkler = Regex.Matches(css, @"--dw-menu-\w+:\s*(#[0-9A-Fa-f]{6});")
                           .Select(m => m.Groups[1].Value.ToUpperInvariant()).ToList();

        Assert.Equal(MenuPalette.AllFamilies.Count, renkler.Count);
        Assert.Equal(renkler.Count, renkler.Distinct().Count());   // hepsi FARKLI
    }

    // ══════════════ 5) HİYERARŞİ GÖRSEL OLARAK AYRIŞIR ══════════════

    /// <summary>
    /// ⭐ Üç seviye yalnız RENKLE değil, GEOMETRİYLE de ayrışır (kullanıcı şartı 5 ve 4).
    ///
    /// Renk körlüğünde ya da tek renkli baskıda bile "üst grup > üst menü > ekran" okunabilmeli:
    /// çubuk kalınlığı ve opaklık kademeli azalır, ikon boyu ve girinti farklıdır.
    /// </summary>
    [Fact]
    public void RNK10_Uc_Seviye_Renk_Disinda_Da_Ayrisir()
    {
        var pencere = Oku("src", "DepoWise.Desktop", "Views", "MainWindow.axaml");
        var css = Oku("src", "DepoWise.Web", "wwwroot", "app.css");

        // Masaüstü: üç farklı çubuk kalınlığı/opaklığı
        Assert.Contains("Width=\"3\" CornerRadius=\"2\"", pencere);     // üst grup — en kalın
        Assert.Contains("Opacity=\"0.85\"", pencere);                   // üst menü
        Assert.Contains("Opacity=\"0.55\"", pencere);                   // ekran — en yumuşak

        // Web: aynı kademeler CSS'te
        Assert.Contains(".dw-fam-section::before { width: 3px; opacity: 1;", css);
        Assert.Contains(".dw-fam-group::before   { width: 2px; opacity: .85;", css);
        Assert.Contains(".dw-fam-screen::before  { width: 2px; opacity: .55;", css);

        // Renk dışındaki ipuçları duruyor: ikon + girinti + tipografi
        Assert.Contains("PathIcon", pencere);
        Assert.Contains("FontWeight=\"Bold\"", pencere);                // üst seviyeler kalın
        Assert.Contains("Margin=\"10,2,0,6\"", pencere);                // ekranlar içeriden başlar
    }

    // ══════════════ 6) MEVCUT DAVRANIŞ KORUNUYOR ══════════════

    /// <summary>
    /// ⭐ RENK YETKİYE DOKUNMAZ (kullanıcı şartı 7 · Faz 1 mührü).
    ///
    /// Renk katmanı hiçbir erişim kararı vermemeli. Kaynakta yetki çağrısı geçmemeli ve
    /// <c>MenuPalette</c> yalnız saf string döndürmeli.
    /// </summary>
    [Fact]
    public void RNK11_Renk_Katmani_Yetkiye_Dokunmaz()
    {
        foreach (var dosya in new[]
                 {
                     Oku("src", "DepoWise.Application", "Security", "MenuPalette.cs"),
                     Oku("src", "DepoWise.Desktop", "DesktopMenuColors.cs"),
                 })
        {
            // ⚠️ YORUM SATIRLARI AYIKLANIR. İlk hâli ham metni tarıyordu ve dosyanın kendi
            // açıklaması ("... AccessControl'a dokunmaz") testi kırıyordu — yani test, KODU değil
            // METNİ ölçüyordu. Aranan şey gerçek KULLANIMDIR.
            var kod = string.Join("\n", dosya.Split('\n')
                .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)
                         && !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

            Assert.DoesNotContain("AccessControl", kod);
            Assert.DoesNotContain("PermissionAction", kod);
            Assert.DoesNotContain("CanSeeMenu", kod);
            Assert.DoesNotContain("BranchAccess", kod);
            Assert.DoesNotContain("SessionContext", kod);   // oturum bile görmüyor
        }
    }

    /// <summary>
    /// ⭐ HİÇBİR MENÜ KAYBOLMADI: katalogdaki her üst grup, üst menü ve ekran bir aile alır —
    /// yani renk çözümü hiçbir öğeyi eleyemez (renk görünürlük kararı VERMEZ).
    /// </summary>
    [Fact]
    public void RNK12_Katalogdaki_Her_Oge_Aile_Alir()
    {
        foreach (var b in AppScreens.Sections)
            Assert.Contains(MenuPalette.ForSection(b.Title), MenuPalette.AllFamilies);

        foreach (var g in AppScreens.Groups)
            Assert.Contains(MenuPalette.ForGroup(g.Title), MenuPalette.AllFamilies);

        foreach (var e in AppScreens.All)
            Assert.Contains(MenuPalette.ForScreen(e.Key), MenuPalette.AllFamilies);

        // Sayılar: 6 üst grup · 24 üst menü · 71 ekran (katalog büyürse bu satır da büyür).
        // 70 → 71: Senkron Çakışmaları (2026-09-06, FAZ 4.4 — kullanıcı isteği). Denetim grubunda,
        // yalnız web rotası; masaüstünde karşılığı bir penceredir (SyncConflictsWindow).
        Assert.Equal(6, AppScreens.Sections.Count);
        Assert.Equal(24, AppScreens.Groups.Select(g => g.Title).Distinct().Count());
        Assert.Equal(71, AppScreens.All.Count);
    }

    /// <summary>⭐ Token adı üretimi tek biçimdir — iki platform isimlendirmede ayrışamaz.</summary>
    [Fact]
    public void RNK13_Token_Adi_Tek_Bicimde_Uretilir()
    {
        Assert.Equal("menu-stock-section", MenuPalette.TokenName(MenuPalette.Stock, MenuPalette.Level.Section));
        Assert.Equal("menu-operations-group", MenuPalette.TokenName(MenuPalette.Operations, MenuPalette.Level.Group));
        Assert.Equal("menu-finance-screen", MenuPalette.TokenName(MenuPalette.Finance, MenuPalette.Level.Screen));
    }
}
