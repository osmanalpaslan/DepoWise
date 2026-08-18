using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G2/G6 — <see cref="AppScreens"/> TEK KAYNAK MİMARİSİNİN KİLİDİ (kullanıcı isteği 2026-08-12).
///
/// <b>AMAÇ:</b> "yeni ekran = tek satır" sözünü test zamanında ZORLAMAK. Bir katman kataloğu
/// beslemeyi bırakırsa ya da bir ekran yetim kalırsa buradaki testlerden biri KIRILIR.
///
/// ⚠️ Bu dosya <see cref="ScreenTreeParityTests"/>'in yerini ALMAZ; o dosya "uygulamadaki gerçek
/// durum" tarafını (route'lar, Navigate case'leri, mükerrerlik) tarar. İkisi birlikte iki yönü
/// kapatır: <b>katalog → uygulama</b> ve <b>uygulama → katalog</b>.
/// </summary>
public class AppScreensParityTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Depo kökü bulunamadı.");
    }

    private static string Read(string rel)
        => File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));

    private static List<string> WebRoutes()
    {
        var dir = Path.Combine(Root(), "src", "DepoWise.Web", "Components", "Pages");
        var routes = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.razor"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"^@page\s+""/([^""]*)""", RegexOptions.Multiline))
                routes.Add(m.Groups[1].Value);
        return routes;
    }

    private static List<string> DesktopNavigateCases()
    {
        var src = Read("src/DepoWise.Desktop/ViewModels/ShellViewModel.cs");
        var i = src.IndexOf("private void Navigate(string key)", StringComparison.Ordinal);
        Assert.True(i > 0, "ShellViewModel.Navigate bulunamadı.");
        return Regex.Matches(src[i..], @"case ""([a-z0-9_:]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 1–3 · KATALOĞUN KENDİ TUTARLILIĞI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>1 — Ekran anahtarı BENZERSİZ olmalı (aynı anahtar iki ekranı işaret edemez).</summary>
    [Fact]
    public void S1_Ekran_Anahtarlari_Benzersiz()
    {
        var dup = AppScreens.All.GroupBy(s => s.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "MÜKERRER ekran anahtarı: " + string.Join(", ", dup));
    }

    /// <summary>2 — Zorunlu alanlar: platform Web ise route, platform Desktop ise gezinme anahtarı
    /// DOLU olmalı; olmayan platformda ise BOŞ olmalı (yanlış platformda sızmasın).</summary>
    [Fact]
    public void S2_Platform_Alanlari_Tutarli()
    {
        foreach (var s in AppScreens.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Key), "Ekran anahtarı boş.");
            Assert.False(string.IsNullOrWhiteSpace(s.Label), $"{s.Key}: etiket boş.");
            Assert.False(string.IsNullOrWhiteSpace(s.ModuleKey), $"{s.Key}: modül anahtarı boş.");
            Assert.NotEqual(ScreenPlatform.None, s.Platforms);

            if (s.OnWeb) Assert.False(string.IsNullOrWhiteSpace(s.WebRoute), $"{s.Key}: web ekranı ama route YOK.");
            else Assert.True(string.IsNullOrEmpty(s.WebRoute), $"{s.Key}: web'de değil ama route TANIMLI.");

            if (s.OnDesktop) Assert.False(string.IsNullOrWhiteSpace(s.DesktopNavKey), $"{s.Key}: masaüstü ekranı ama gezinme anahtarı YOK.");
            else Assert.True(string.IsNullOrEmpty(s.DesktopNavKey), $"{s.Key}: masaüstünde değil ama gezinme anahtarı TANIMLI.");
        }
    }

    /// <summary>3 — Her ekranın grubu <see cref="AppScreens.Groups"/> içinde tanımlı olmalı
    /// (yazım hatası olan grup, menüde sessizce kaybolurdu).</summary>
    [Fact]
    public void S3_Ekran_Gruplari_Tanimli()
    {
        var gruplar = AppScreens.Groups.Select(g => g.Title).ToHashSet(StringComparer.Ordinal);
        var bilinmeyen = AppScreens.All.Select(s => s.Group).Distinct()
            .Where(g => !gruplar.Contains(g)).ToList();
        Assert.True(bilinmeyen.Count == 0, "Tanımsız menü grubu: " + string.Join(", ", bilinmeyen));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 4–6 · KATALOG → YETKİ AĞACI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>4 — Her ekranın modül anahtarı YETKİ KATALOĞUNDA olmalı. Olmayan bir anahtar,
    /// ekranın yetki ağacından yönetilememesi demektir (G2-B1'de "trash" tam olarak buydu).</summary>
    [Fact]
    public void S4_Her_Ekranin_Modulu_Yetki_Katalogunda_Var()
    {
        var moduller = AppModules.All.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        // Herkese açık modüller (Uyarılar/Tema/Hakkında) yetki ağacında YÖNETİLMEZ ve bilinçli olarak
        // AppModules.All içinde yer almaz — AppModules.IsPublic ile ayrıca ele alınırlar.
        var eksik = AppScreens.All
            .Where(s => !moduller.Contains(s.ModuleKey) && !AppModules.IsPublic(s.ModuleKey))
            .Select(s => $"{s.Key} → {s.ModuleKey}").ToList();
        Assert.True(eksik.Count == 0,
            "Yetki kataloğunda OLMAYAN modüle bağlı ekranlar (yetki ağacından yönetilemez): " + string.Join(", ", eksik));
    }

    /// <summary>5 — ⭐ G2-B1 DÜZELTMESİNİN KİLİDİ: Çöp Kutusu artık kataloğa dahil ve
    /// yetki ağacında yönetilebilir; yönetim düzeyi kısıtı korunuyor.</summary>
    [Fact]
    public void S5_Trash_Katalogda_Ve_Yonetim_Duzeyinde()
    {
        var trash = AppScreens.ByKey("trash");
        Assert.NotNull(trash);
        Assert.Equal("trash", trash!.ModuleKey);
        Assert.True(trash.OnDesktop && trash.OnWeb, "Çöp Kutusu iki platformda da olmalı (mevcut davranış).");

        Assert.Contains(AppModules.All, m => m.Key == "trash");          // yetki ağacında görünür
        Assert.True(AppModules.IsAdminRestricted("trash"), "Çöp Kutusu yönetim düzeyi olmalı.");
        Assert.False(AppModules.IsPublic("trash"));
        Assert.False(AppModules.IsSuperAdminOnly("trash"));
    }

    /// <summary>6 — Çöp Kutusu davranışı BOZULMADI: admin erişir, personel açık izin olmadan erişemez.</summary>
    [Fact]
    public void S6_Trash_Davranisi_Korundu()
    {
        var admin = new SessionContext("a", "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var personel = new SessionContext("p", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        var yetkili = new SessionContext("y", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("trash", true, false, false, false) }));

        Assert.True(AccessControl.Can(admin, "trash", PermissionAction.View));      // eskisi gibi (bypass)
        Assert.False(AccessControl.Can(personel, "trash", PermissionAction.View));  // eskisi gibi (deny-by-default)
        Assert.True(AccessControl.Can(yetkili, "trash", PermissionAction.View));    // ⭐ YENİ: artık devredilebilir
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 7–9 · KATALOG → UYGULAMA (route / gezinme)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>7 — Kataloğun her WEB ekranının gerçek bir <c>@page</c> route'u olmalı
    /// (parametreli route'lar desen olarak eşleşir) — yoksa menü kırık bağlantı üretir.</summary>
    [Fact]
    public void S7_Katalogtaki_Web_Ekranlarinin_Route_u_Var()
    {
        const string Isaret = "0001P0001";   // route parametresi yer tutucusu
        var kaliplar = WebRoutes()
            .Select(r => new Regex("^" + Regex.Escape(Regex.Replace(r, @"\{[^}]*\}", Isaret)).Replace(Isaret, "[^/]+") + "$",
                RegexOptions.IgnoreCase)).ToList();
        var eksik = AppScreens.For(ScreenPlatform.Web)
            .Where(s => !kaliplar.Any(k => k.IsMatch(s.WebRoute!)))
            .Select(s => $"{s.Key} → /{s.WebRoute}").ToList();
        Assert.True(eksik.Count == 0, "Katalogda olup @page route'u OLMAYAN web ekranları: " + string.Join(", ", eksik));
    }

    /// <summary>8 — Kataloğun her MASAÜSTÜ ekranının <c>Navigate</c> içinde karşılığı olmalı;
    /// yoksa kullanıcı menüde tıklar ve hiçbir şey açılmaz.</summary>
    [Fact]
    public void S8_Katalogtaki_Masaustu_Ekranlari_Gezinilebilir()
    {
        var cases = DesktopNavigateCases().ToHashSet(StringComparer.Ordinal);
        var eksik = AppScreens.For(ScreenPlatform.Desktop)
            .Where(s => !cases.Contains(s.DesktopNavKey!))
            .Select(s => $"{s.Key} → {s.DesktopNavKey}").ToList();
        Assert.True(eksik.Count == 0,
            "Katalogda olup Navigate() içinde karşılığı OLMAYAN masaüstü ekranları: " + string.Join(", ", eksik));
    }

    /// <summary>9 — YETİM EKRAN YOK: masaüstünde gezinilebilen her anahtarın katalogda karşılığı olmalı.
    /// Katalog dışı bir ekran = menüsüz, yetkisiz, platform yönetimi dışında kalan ekran.
    /// ⚠️ İstisna listesi bilinçlidir: bunlar menü öğesi değil, başka ekranların İÇİNDEN açılan hedeflerdir.</summary>
    [Fact]
    public void S9_Masaustunde_Yetim_Ekran_Yok()
    {
        var katalog = AppScreens.All.Where(s => s.OnDesktop).Select(s => s.DesktopNavKey!)
            .ToHashSet(StringComparer.Ordinal);

        // Menüde YER ALMAYAN ama Navigate ile açılabilen hedefler. Hepsi bilinçlidir ve
        // gerekçesi yazılıdır — liste büyürse bilinçli karar gerekir (test kırılır).
        var menuDisiHedefler = new HashSet<string>(StringComparer.Ordinal)
        {
            "dashboard",            // ana ekran — menü grubu değil, açılış/logo hedefi
            // Alt sekmesiz TAKMA ADLAR: grup ikonuna tıklanınca birincil alt ekrana düşer.
            "maintenance",          // = maintenance:defs
            "fuel",                 // = fuel:dist
            "requests",             // = requests:form
            "maintenance:alerts",   // Uyarılar ekranından bakım uyarılarına kısayol
            // Yedek Yönetimi masaüstü MENÜSÜNDEN 2026-07-26'da kaldırıldı (yalnız web'de kaldı),
            // ancak Navigate case'i duruyor → menüden erişilemez, yalnız kod içi çağrıyla açılır.
            "backup",
        };

        var yetim = DesktopNavigateCases()
            .Where(k => !katalog.Contains(k) && !menuDisiHedefler.Contains(k))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(yetim.Count == 0,
            "Navigate() içinde olup KATALOGDA OLMAYAN masaüstü ekranları: " + string.Join(", ", yetim));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 10–12 · MENÜLERİN GERÇEKTEN KATALOGDAN ÜRETİLDİĞİ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>10 — Masaüstü menüsü kataloğu KULLANMALI ve elle yazılmış grup listesi KALMAMALI.
    /// Biri eski desene dönerse bu test kırılır.</summary>
    [Fact]
    public void S10_Masaustu_Menusu_Katalogtan_Uretiliyor()
    {
        var src = Read("src/DepoWise.Desktop/ViewModels/ShellViewModel.cs");
        var i = src.IndexOf("BuildGroups(SessionContext s)", StringComparison.Ordinal);
        Assert.True(i > 0);
        var gövde = src[i..(i + 2000)];
        // MNU (2026-08-18): grup/ekran gezintisi artık MenuLayout.Build içinde yapılır (masaüstü ve
        // web AYNI kod). Garanti aynen sürüyor — menü KATALOGDAN üretilir, elle yazılmış listeden değil:
        // burada Build'in çağrıldığı, S17'de Build'in gerçekten AppScreens'i gezdiği doğrulanır.
        Assert.Contains("MenuLayout.Build(ScreenPlatform.Desktop", gövde);
        // Elle yazılmış menü kalıntısı olmamalı.
        Assert.DoesNotContain("new NavGroupVm(\"🔔\"", src);
        Assert.DoesNotContain("new NavLinkVm(\"Malzeme Listesi\"", src);
    }

    /// <summary>11 — Web menüsü kataloğu KULLANMALI ve elle yazılmış bağlantı listesi KALMAMALI.</summary>
    [Fact]
    public void S11_Web_Menusu_Katalogtan_Uretiliyor()
    {
        var src = Read("src/DepoWise.Web/Components/Layout/NavMenu.razor");
        // MNU: bkz. S10 — üretim MenuLayout.Build'e taşındı, katalog garantisi S17'de kilitli.
        Assert.Contains("MenuLayout.Build(ScreenPlatform.Web", src);
        Assert.DoesNotContain("new Link(\"Malzeme Listesi\"", src);
        Assert.DoesNotContain("new Link(\"Araç Listesi\"", src);
    }

    /// <summary>
    /// 17 (MNU, 2026-08-18) — <b>KATALOG GARANTİSİNİN ASIL KİLİDİ.</b> S10/S11 artık menülerin
    /// <c>MenuLayout.Build</c> çağırdığını doğruluyor; o hâlde Build'in gerçekten KATALOĞU gezdiği
    /// ayrıca kanıtlanmalı — yoksa iki test birden anlamsızlaşırdı.
    ///
    /// Kaynak metnine değil DAVRANIŞA bakılır: boş düzenle üretilen menü, kataloğun o platformdaki
    /// ekran kümesiyle BİREBİR aynı olmalı (sıra dahil).
    /// </summary>
    [Theory]
    [InlineData(ScreenPlatform.Desktop)]
    [InlineData(ScreenPlatform.Web)]
    public void S17_MenuLayout_Build_Katalogu_Birebir_Uretir(ScreenPlatform platform)
    {
        var üretilen = MenuLayout.Build(platform, MenuLayoutSet.Empty, _ => true);

        var beklenenGruplar = AppScreens.GroupsFor(platform).Select(g => g.Title).ToArray();
        Assert.Equal(beklenenGruplar, üretilen.Select(g => g.Key).ToArray());

        foreach (var g in üretilen)
        {
            var beklenen = AppScreens.ScreensOf(g.Key, platform).ToArray();
            Assert.Equal(beklenen.Select(s => s.Key), g.Entries.Select(e => e.Screen.Key));
            // Düzen tercihi yokken etiket de katalog etiketidir.
            Assert.Equal(beklenen.Select(s => s.Label), g.Entries.Select(e => e.Label));
            Assert.Equal(g.Key, g.Title);   // ad tercihi yok → başlık = anahtar
        }
    }

    /// <summary>12 — Katalog web projesinde de DERLENİYOR olmalı (paylaşılan kaynak dosya deseni);
    /// aksi halde web tekrar kendi aynasını tutmak zorunda kalır.</summary>
    [Fact]
    public void S12_Katalog_Web_Projesinde_Paylasilyor()
    {
        var csproj = Read("src/DepoWise.Web/DepoWise.Web.csproj");
        Assert.Contains("AppScreens.cs", csproj);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 13–15 · TAŞIMA REGRESYONU — menüler ESKİSİYLE BİREBİR mi?
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>13 — MASAÜSTÜ menüsü taşımadan ÖNCEKİ hâliyle birebir aynı olmalı:
    /// grup sırası + başlıklar + her grubun bağlantı anahtarları. Beklenen değerler taşımadan önce
    /// kaynaktan ÖLÇÜLMÜŞTÜR — kullanıcının alışkanlığı bozulmamalı.</summary>
    [Fact]
    public void S13_Masaustu_Menusu_Tasimadan_Once_Ile_Ayni()
    {
        var beklenen = new (string Grup, string[] Anahtarlar)[]
        {
            ("Uyarılar", new[] { "alerts" }),
            ("Malzemeler", new[] { "materials", "materials:new", "material_templates:templates", "stock", "stock:movements", "stock:count", "stock:distribute" }),
            ("Araçlar", new[] { "vehicles", "vehicles:new", "vehicle_templates:templates", "inspection" }),
            ("Personel", new[] { "personnel" }),
            ("Günlük Faaliyet", new[] { "daily_activity" }),
            ("Bakım Takibi", new[] { "maintenance:defs", "maintenance:records" }),
            ("Yakıt", new[] { "fuel:dist", "fuel:depot", "fuel:summary" }),
            ("Yönetim", new[] { "branches", "audit", "stock_change_log" }),
            ("Talepler", new[] { "requests:form", "requests:approve", "request_ops:board" }),
            ("Raporlar", new[] { "reports" }),
            ("Yönetici Raporları", new[] { "reports" }),
            ("İmport / Export", new[] { "import_export" }),
            // G4-1 cari + G4-2 fatura + G4-3 kasa/banka. Her biri AYRI modüldür ("parties",
            // "invoices", "finance") ve menüde eklendikleri sırayla görünür.
            ("Ön Muhasebe", new[] { "parties", "parties:new", "invoices", "invoices:new",
                                    "finance", "finance:new", "payments" }),
            ("Kullanıcı", new[] { "users", "permissions", "permission_templates" }),
            ("Ayarlar", new[] { "definitions", "settings:developer", "theme", "about" }),
            ("Web Yönetimi", new[] { "companies", "releases", "machines", "server_backups" }),
            ("Çöp Kutusu", new[] { "trash" }),
        };

        var gercek = AppScreens.GroupsFor(ScreenPlatform.Desktop)
            .Select(g => (g.Title, AppScreens.ScreensOf(g.Title, ScreenPlatform.Desktop)
                .Select(s => s.DesktopNavKey!).ToArray()))
            .ToArray();

        Assert.Equal(beklenen.Select(x => x.Grup), gercek.Select(x => x.Item1));
        for (int i = 0; i < beklenen.Length; i++)
            Assert.Equal(beklenen[i].Anahtarlar, gercek[i].Item2);
        // Taşımadan önce 40'tı; G4-1 Cari (+2), G4-2 Fatura (+2), G4-3 Kasa/Banka (+3) bilinçli
        // olarak eklendi → 47.
        Assert.Equal(47, gercek.Sum(x => x.Item2.Length));
    }

    /// <summary>14 — WEB menüsü taşımadan ÖNCEKİ hâliyle birebir aynı olmalı: grup sırası +
    /// route'lar + YETKİ ANAHTARLARI (sözde anahtarlar @admin/@super/@superr dahil).</summary>
    [Fact]
    public void S14_Web_Menusu_Tasimadan_Once_Ile_Ayni()
    {
        var beklenen = new (string Grup, (string Perm, string Route)[] Baglantilar)[]
        {
            ("Uyarılar", new[] { ("", "alerts") }),
            ("Malzemeler", new[] { ("materials", "materials"), ("materials", "materials/new"), ("stock", "stock"), ("stock", "stock/movements"), ("stock", "stock/count") }),
            ("Araçlar", new[] { ("vehicles", "vehicles"), ("vehicles", "vehicles/new"), ("vehicle_templates", "vehicle-templates"), ("inspection", "inspection") }),
            ("Personel", new[] { ("personnel", "personnel") }),
            ("Günlük Faaliyet", new[] { ("daily_activity", "daily") }),
            ("Bakım Takibi", new[] { ("maintenance", "maintenance/defs"), ("maintenance", "maintenance/records") }),
            ("Yakıt", new[] { ("fuel", "fuel/dist"), ("fuel", "fuel/depot"), ("fuel", "fuel/summary") }),
            ("Yönetim", new[] { ("branches", "branches"), ("audit", "audit"), ("stock_change_log", "stock-change-log"), ("@superr", "backup") }),
            ("Talepler", new[] { ("requests", "requests"), ("requests", "requests/approve"), ("request_ops", "request-operations") }),
            ("Raporlar", new[] { ("reports", "reports") }),
            ("Yönetici Raporları", new[] { ("@admin", "reports") }),
            ("Ön Muhasebe", new[] { ("parties", "parties"), ("parties", "parties/new"),
                                    ("invoices", "invoices"), ("invoices", "invoices/new"),
                                    ("finance", "finance"), ("finance", "finance/new"),
                                    ("finance", "payments") }),   // G4-1 + G4-2 + G4-3
            ("Kullanıcı", new[] { ("users", "users"), ("permissions", "permissions"), ("permission_templates", "permission-templates") }),
            ("Ayarlar", new[] { ("definitions", "definitions"), ("import_export", "import"), ("settings", "developer"), ("", "theme"), ("", "soon/about") }),
            ("Web Yönetimi", new[] { ("companies", "companies"), ("releases", "releases"), ("machines", "machines"), ("machine_backups", "machine-backups"), ("server_backups", "server-backups"), ("server_status", "server-status"), ("quota_monitor", "quota-monitor"), ("companies", "company-permissions"), ("role_permissions", "role-permissions"), ("purge_company", "purge-company"), ("@super", "reset-company-business"), ("local_reset", "local-reset"), ("screen_visibility", "screen-visibility") }),
            ("Çöp Kutusu", new[] { ("trash", "trash") }),
        };

        var gercek = AppScreens.GroupsFor(ScreenPlatform.Web)
            .Select(g => (g.Title, AppScreens.ScreensOf(g.Title, ScreenPlatform.Web)
                .Select(s => (s.WebPermKey, s.WebRoute!)).ToArray()))
            .ToArray();

        Assert.Equal(beklenen.Select(x => x.Grup), gercek.Select(x => x.Item1));
        for (int i = 0; i < beklenen.Length; i++)
            Assert.Equal(beklenen[i].Baglantilar, gercek[i].Item2);
        // Taşımadan önce ölçülen 46 bağlantı KORUNDU; G5 yönetim ekranı bilinçli olarak eklendi (+1).
        // 46 (taşıma) + 1 (G5 yönetim) + 2 (G4-1 Cari) + 2 (G4-2 Fatura) + 3 (G4-3 Kasa/Banka)
        //   + 1 (YET: Yerel Veri Sıfırlama — kullanıcı isteği 2026-08-18) = 55.
        Assert.Equal(55, gercek.Sum(x => x.Item2.Length));
    }

    /// <summary>15 — Çöp Kutusu'nun web yetki anahtarı artık sözde <c>@admin</c> DEĞİL, gerçek
    /// <c>trash</c> modülüdür (G2-B1). Davranış aynı kalır çünkü modül yönetim düzeyindedir.</summary>
    [Fact]
    public void S15_Trash_Web_Yetkisi_Artik_Gercek_Modul()
    {
        var trash = AppScreens.ByKey("trash")!;
        Assert.Equal("trash", trash.WebPermKey);
        Assert.Null(trash.WebPermOverride);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 16 · G5 HAZIRLIĞI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>16 — Platform farkları KAYIT ALTINDA: bugün tek platformda olan ekranlar açıkça
    /// işaretli. G5 bu alanı çalışma zamanında yönetilebilir yapacak; şimdi doğru veriyle başlıyoruz.</summary>
    [Fact]
    public void S16_Platform_Farklari_Kayit_Altinda()
    {
        var yalnizMasaustu = AppScreens.All.Where(s => s.OnDesktop && !s.OnWeb).Select(s => s.Key)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var yalnizWeb = AppScreens.All.Where(s => s.OnWeb && !s.OnDesktop).Select(s => s.Key)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "import_export", "material_templates", "stock.distribute" }, yalnizMasaustu);
        Assert.Equal(new[]
        {
            // "local_reset": YET (2026-08-18) — Kalıcı Silme / Firma İş Verisini Sıfırla ile aynı gruptaki
            // yönetim ekranı; kardeşleri gibi YALNIZ WEB'de sunulur (masaüstünde karşılığı yoktur).
            "backup", "company_permissions", "import", "local_reset", "machine_backups", "purge_company",
            "quota_monitor", "reset_company_business", "role_permissions", "screen_visibility", "server_status",
        }, yalnizWeb);

        // Geri kalanların hepsi iki platformda.
        Assert.Equal(AppScreens.All.Count - yalnizMasaustu.Length - yalnizWeb.Length,
            AppScreens.All.Count(s => s.Platforms == ScreenPlatform.Both));
    }
}
