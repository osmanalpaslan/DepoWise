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

    /// <summary>9b — ⭐ YTK-06: WEB'DE DE YETİM EKRAN YOK. S9 aynı kilidi masaüstü için kuruyordu;
    /// web yönü <b>açıktı</b>. Yeni bir <c>.razor</c> sayfası eklenip kataloğa yazılmazsa ekran
    /// menüde çıkmaz, <b>yetki ağacından yönetilemez</b> ve platform yönetiminin dışında kalır —
    /// üstelik hiçbir test kırılmadığı için bu sessizce olur. Bu test o sessizliği bitirir.
    ///
    /// ⚠️ İstisna listesi bilinçlidir: bunlar menü öğesi DEĞİL — giriş/hata sayfaları ve başka
    /// ekranların içinden açılan hedeflerdir. Liste büyürse bilinçli karar gerekir (test kırılır).</summary>
    [Fact]
    public void S9b_Webde_Yetim_Ekran_Yok()
    {
        const string Isaret = "0001P0001";
        // ⚠️ Route parametrelidir (`materials/{Section}`), katalog ise SOMUTTUR (`materials/new`).
        // Bu yüzden düz metin karşılaştırması YANLIŞ sonuç verir; route'u kalıba çevirip kataloğun
        // o kalıba uyan bir kaydı var mı diye bakılır (S7'nin ters yönü).
        static Regex Kalip(string route)
            => new("^" + Regex.Escape(Regex.Replace(route.Trim('/'), @"\{[^}]*\}", Isaret)).Replace(Isaret, "[^/]+") + "$",
                   RegexOptions.IgnoreCase);

        var katalog = AppScreens.For(ScreenPlatform.Web).Select(s => s.WebRoute!.Trim('/')).ToList();

        // ⚠️ Her istisnanın gerekçesi katalogda ya da burada YAZILIDIR. Liste büyürse test kırılır
        //    ve yeni satır bilinçli bir karar gerektirir — sessizce büyüyemez.
        var menuDisiRotalar = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "",                     // ana ekran (dashboard) — menü öğesi değil, açılış hedefi
            "login",                // giriş sayfası — yetki ÖNCESİ çalışır
            "Error",                // ASP.NET Core hata sayfası — çerçevenin kendi sayfası, ekran değil
            // TAKMA ADLAR: grup adresine gidilince birincil alt ekran açılır. Katalog alt ekranları
            // tutar (fuel/dist, maintenance/defs); grubun kendisi ayrı bir ekran DEĞİLDİR.
            // Masaüstündeki S9 istisnalarının birebir karşılığı.
            "fuel",
            "maintenance",
            // MENÜDE OLMAYAN, ama web'de erişilebilen ekranlar — ikisi de katalogda BİLİNÇLİ olarak
            // masaüstü (D) işaretli ve gerekçesi katalog satırının üstünde yazılı:
            "material-templates",   // "web'de ekran var ama menüde listelenmiyor"
            "stock/distribute",     // STK-08 — "web'de Stok İşlemleri ekranından açılır"
        };

        var yetim = WebRoutes()
            .Where(r => !menuDisiRotalar.Contains(r.Trim('/')))
            .Where(r => { var k = Kalip(r); return !katalog.Any(c => k.IsMatch(c)); })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(yetim.Count == 0,
            "@page route'u olup KATALOGDA (AppScreens) OLMAYAN web ekranları — yetki ağacından "
            + "yönetilemezler: /" + string.Join(", /", yetim));
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

    /// <summary>13 — MASAÜSTÜ menüsü <b>VARSAYILAN ŞEMAYLA</b> birebir aynı olmalı: grup sırası +
    /// başlıklar + her grubun bağlantı anahtarları.
    ///
    /// ⚠️ 2026-08-19: beklenen değerler kullanıcının ilettiği NİHAİ ŞEMAYA göre YENİDEN yazıldı
    /// (ADR-114). Önceki değerler taşıma öncesi menüyü kilitliyordu; varsayılan menü bilinçli olarak
    /// değiştirildiği için referans da bilinçli olarak güncellendi. Toplam bağlantı sayısı DEĞİŞMEDİ
    /// (47) — yani hiçbir ekran kaybolmadı, yalnız gruplama ve sıra değişti.</summary>
    [Fact]
    public void S13_Masaustu_Menusu_Varsayilan_Semayla_Ayni()
    {
        var beklenen = new (string Grup, string[] Anahtarlar)[]
        {
            ("Uyarılar", new[] { "alerts" }),
            ("Malzemeler", new[] { "materials", "materials:new", "stock", "stock:movements", "stock:count", "material_templates:templates", "stock:distribute" }),
            ("Araçlar", new[] { "vehicles", "vehicles:new", "vehicle_templates:templates", "inspection" }),
            ("Ekipman", new[] { "equipment" }),   // EKP-01 (ADR-166)
            ("Zimmet", new[] { "assignments" }),   // ZMT-01 (ADR-167)
            ("Satın Alma", new[] { "purchasing" }),   // STN-01 (ADR-169)
            ("İş Emirleri", new[] { "work_orders" }),   // EMR-01 (ADR-170)
            ("Takvim", new[] { "calendar" }),   // TKV-01 (ADR-171)
            ("Günlük Faaliyet", new[] { "daily_activity" }),
            ("Bakım Takibi", new[] { "maintenance:defs", "maintenance:records" }),
            ("Yakıt", new[] { "fuel:dist", "fuel:depot", "fuel:summary" }),
            // ⭐ ARA İŞ 5 / ALT FAZ 3 (ADR-189): "Onaylamalarım" BİLİNÇLİ olarak eklendi — yeni yetki
            // modülü DEĞİL; mevcut request_approval modülüne bağlı AYRI ekran.
            ("Talepler", new[] { "requests:form", "requests:approve", "approvals", "request_ops:board" }),
            // G4-1 cari + G4-2 fatura + G4-3 kasa/banka. Her biri AYRI modüldür ("parties",
            // "invoices", "finance") ve menüde eklendikleri sırayla görünür.
            ("Ön Muhasebe", new[] { "parties", "parties:new", "invoices", "invoices:new",
                                    "finance", "finance:new", "payments", "cost_centers" }),   // MLY-01
            // ⭐ ARA İŞ 4 (ADR-186): "reports:designer" = Rapor Tasarımcısı. Aynı "reports" modülüne
            // bağlıdır (YENİ yetki modülü açılmadı); menüye BİLİNÇLİ olarak eklendi.
            ("Operasyon Raporları", new[] { "reports", "reports:designer" }),
            // RPR-07 (2026-08-25): Yönetici Raporları artık AYRI gezinme anahtarı kullanır
            // (eskiden "reports" ile aynı ekranı açıyordu; iki menü girişi fiilen tek ekrandı).
            ("Yönetici Raporları", new[] { "reports:manager" }),
            // PRJ-01 (ADR-164): Projeler ekranı — yetki anahtarı branches (PK-C4).
            ("Şube ve Personel", new[] { "branches", "projects", "personnel" }),
            // ⭐ ARA İŞ 5 / ALT FAZ 1 (ADR-187): "Ekipler" BİLİNÇLİ olarak eklendi — yeni yetki modülü DEĞİL,
            // ModuleKey="users" ile aynı modüle bağlı AYRI ekran (reports.designer ile aynı içtihat).
            ("Kullanıcı Yönetimi", new[] { "users", "users:teams", "permissions", "permission_templates" }),
            ("Evrak", new[] { "documents" }),   // EVR-01 (ADR-165)
            ("Duyurular", new[] { "announcements" }),   // DYR-01 (ADR-173)
            ("Denetim", new[] { "audit", "stock_change_log" }),
            ("Web Yönetimi", new[] { "companies", "releases", "machines", "server_backups" }),
            // "Yedekleme" grubu masaüstünde GÖRÜNMEZ: tek ekranı (Yedek Yönetimi) yalnız web'dedir.
            ("Çöp Kutusu", new[] { "trash" }),
            // 2026-09-03: Alan Ayarları (field_settings) bilinçli eklendi (ADR-198).
            ("Ayarlar", new[] { "definitions", "field_settings", "import_export", "settings:developer", "theme", "about" }),
        };

        var gercek = AppScreens.GroupsFor(ScreenPlatform.Desktop)
            .Select(g => (g.Title, AppScreens.ScreensOf(g.Title, ScreenPlatform.Desktop)
                .Select(s => s.DesktopNavKey!).ToArray()))
            .ToArray();

        Assert.Equal(beklenen.Select(x => x.Grup), gercek.Select(x => x.Item1));
        for (int i = 0; i < beklenen.Length; i++)
            Assert.Equal(beklenen[i].Anahtarlar, gercek[i].Item2);
        // ⭐ Toplam: 47 + PRJ/EVR/EKP/ZMT/MLY/STN/EMR/TKV/DYR = 56, + ARA İŞ 4 Rapor Tasarımcısı = 57,
        // + ARA İŞ 5 / ALT FAZ 1 Ekipler = 58, + ALT FAZ 3 Onaylamalarım = 59 (ADR-187/189).
        // Ekran kaybı yok; yeni ekran BİLİNÇLİ olarak eklendi (ADR-186).
        Assert.Equal(60, gercek.Sum(x => x.Item2.Length));   // 59 → 60: Alan Ayarları (2026-09-03, ADR-198)
    }

    /// <summary>14 — WEB menüsü <b>VARSAYILAN ŞEMAYLA</b> birebir aynı olmalı: grup sırası +
    /// route'lar + YETKİ ANAHTARLARI (sözde anahtarlar @admin/@super/@superr dahil).
    /// Toplam bağlantı sayısı DEĞİŞMEDİ (55).</summary>
    [Fact]
    public void S14_Web_Menusu_Varsayilan_Semayla_Ayni()
    {
        var beklenen = new (string Grup, (string Perm, string Route)[] Baglantilar)[]
        {
            ("Uyarılar", new[] { ("", "alerts") }),
            ("Malzemeler", new[] { ("materials", "materials"), ("materials", "materials/new"), ("stock", "stock"), ("stock", "stock/movements"), ("stock", "stock/count") }),
            ("Araçlar", new[] { ("vehicles", "vehicles"), ("vehicles", "vehicles/new"), ("vehicle_templates", "vehicle-templates"), ("inspection", "inspection") }),
            ("Ekipman", new[] { ("equipment", "equipment") }),   // EKP-01
            ("Zimmet", new[] { ("assignments", "assignments") }),   // ZMT-01
            ("Satın Alma", new[] { ("purchasing", "purchasing") }),   // STN-01
            ("İş Emirleri", new[] { ("work_orders", "work-orders") }),   // EMR-01
            ("Takvim", new[] { ("calendar", "calendar") }),   // TKV-01
            ("Günlük Faaliyet", new[] { ("daily_activity", "daily") }),
            ("Bakım Takibi", new[] { ("maintenance", "maintenance/defs"), ("maintenance", "maintenance/records") }),
            ("Yakıt", new[] { ("fuel", "fuel/dist"), ("fuel", "fuel/depot"), ("fuel", "fuel/summary") }),
            // ⭐ ARA İŞ 5 / ALT FAZ 3: web tarafında da aynı ekran — yetki "request_approval", rota "approvals".
            ("Talepler", new[] { ("requests", "requests"), ("requests", "requests/approve"), ("request_approval", "approvals"), ("request_ops", "request-operations") }),
            ("Ön Muhasebe", new[] { ("parties", "parties"), ("parties", "parties/new"),
                                    ("invoices", "invoices"), ("invoices", "invoices/new"),
                                    ("finance", "finance"), ("finance", "finance/new"),
                                    ("finance", "payments"),
                                    ("cost_centers", "cost-centers") }),   // G4 + MLY-01
            // ⭐ ARA İŞ 4 (ADR-186): web'de de aynı ekran — yetki anahtarı "reports", rota "reports/designer".
            ("Operasyon Raporları", new[] { ("reports", "reports"), ("reports", "reports/designer") }),
            // RPR-07: ayrı route → deep-link ve menü artık iki farklı ekranı gösterir.
            ("Yönetici Raporları", new[] { ("@admin", "reports/manager") }),
            ("Şube ve Personel", new[] { ("branches", "branches"), ("branches", "projects"), ("personnel", "personnel") }),   // PRJ-01: Projeler
            // ⭐ ARA İŞ 5 / ALT FAZ 1 (ADR-187): web'de de aynı ekran — yetki anahtarı "users", rota "teams".
            ("Kullanıcı Yönetimi", new[] { ("users", "users"), ("users", "teams"), ("permissions", "permissions"), ("permission_templates", "permission-templates") }),
            ("Evrak", new[] { ("files", "documents") }),   // EVR-01
            ("Duyurular", new[] { ("announcements", "announcements") }),   // DYR-01
            // 2026-09-06 (FAZ 4.4): Senkron Çakışmaları ekranı — yalnız web (masaüstünde pencere).
            ("Denetim", new[] { ("audit", "audit"), ("stock_change_log", "stock-change-log"), ("sync_conflicts", "sync-conflicts") }),
            ("Web Yönetimi", new[] { ("companies", "companies"), ("releases", "releases"), ("machines", "machines"), ("machine_backups", "machine-backups"), ("server_backups", "server-backups"), ("server_status", "server-status"), ("quota_monitor", "quota-monitor"), ("companies", "company-permissions"), ("purge_company", "purge-company"), ("@super", "reset-company-business"), ("local_reset", "local-reset"), ("screen_visibility", "screen-visibility") }),
            ("Yedekleme", new[] { ("@superr", "backup") }),
            ("Çöp Kutusu", new[] { ("trash", "trash") }),
            // SEC-03 (2026-08-25): "Geliştirici Modu" yetki anahtarı BİLİNÇLİ olarak "settings" → "@super"
            // yapıldı. Ekran süper admin yetkilerini taklit ettiği için devredilemez; paylaşılan "settings"
            // modülü onu firma personeline de açıyordu. Bağlantı SAYISI değişmedi (ekran kaybı yok).
            // 2026-09-03: Alan Ayarları (field_settings) bilinçli eklendi (ADR-198).
            ("Ayarlar", new[] { ("definitions", "definitions"), ("field_settings", "field-settings"), ("import_export", "import"), ("@super", "developer"), ("", "theme"), ("", "soon/about") }),
        };

        var gercek = AppScreens.GroupsFor(ScreenPlatform.Web)
            .Select(g => (g.Title, AppScreens.ScreensOf(g.Title, ScreenPlatform.Web)
                .Select(s => (s.WebPermKey, s.WebRoute!)).ToArray()))
            .ToArray();

        Assert.Equal(beklenen.Select(x => x.Grup), gercek.Select(x => x.Item1));
        for (int i = 0; i < beklenen.Length; i++)
            Assert.Equal(beklenen[i].Baglantilar, gercek[i].Item2);
        // ⭐ Toplam bağlantı sayısı şema değişikliğinden ÖNCEKİYLE aynı: 55.
        // A2 (2026-08-19): "Rol Yetki Kontrol" ekranı "Firma Yetki Paketi" içine SEKME olarak taşındı
        // → bağlantı sayısı bilinçli olarak 1 azaldı (ekran kaybı DEĞİL, birleşme).
        // PRJ/EVR/EKP/ZMT/MLY/STN/EMR/TKV/DYR → 63, + ARA İŞ 4 Rapor Tasarımcısı → 64 (ADR-186),
        // + ARA İŞ 5 / ALT FAZ 1 Ekipler → 65, + ALT FAZ 3 Onaylamalarım → 66 (ADR-187/189).
        // 67 → 68: Senkron Çakışmaları (2026-09-06, FAZ 4.4 — kullanıcı isteği).
        Assert.Equal(68, gercek.Sum(x => x.Item2.Length));
    }

    /// <summary>
    /// 14b (2026-08-19, ADR-114) — <b>VARSAYILAN ÜST GRUPLAR</b> kilidi. Kullanıcının nihai şeması
    /// artık kataloğun kendisindedir: hiçbir firma kaydı olmadan menü üç seviyeli çıkar.
    /// </summary>
    [Fact]
    public void S14b_Varsayilan_Ust_Gruplar_Semayla_Ayni()
    {
        Assert.Equal(
            new[] { "Malzeme ve Stok", "Operasyon", "Finans", "Raporlar", "Kurumsal Yönetim", "Sistem Yönetimi" },
            AppScreens.Sections.Select(x => x.Title).ToArray());

        var beklenen = new (string Grup, string? UstGrup)[]
        {
            ("Uyarılar", null),
            ("Malzemeler", "section:malzemestok"),
            ("Araçlar", "section:operasyon"),
            ("Ekipman", "section:operasyon"),   // EKP-01
            ("Zimmet", "section:operasyon"),   // ZMT-01
            ("Satın Alma", "section:operasyon"),   // STN-01
            ("İş Emirleri", "section:operasyon"),   // EMR-01
            ("Takvim", "section:operasyon"),   // TKV-01
            ("Günlük Faaliyet", "section:operasyon"),
            ("Bakım Takibi", "section:operasyon"),
            ("Yakıt", "section:operasyon"),
            ("Talepler", null),
            ("Ön Muhasebe", "section:finans"),
            ("Operasyon Raporları", "section:raporlar"),
            ("Yönetici Raporları", "section:raporlar"),
            ("Şube ve Personel", "section:kurumsal"),
            ("Kullanıcı Yönetimi", "section:kurumsal"),
            ("Evrak", "section:kurumsal"),   // EVR-01
            ("Duyurular", "section:kurumsal"),   // DYR-01
            ("Denetim", "section:kurumsal"),
            ("Web Yönetimi", "section:sistem"),
            ("Yedekleme", "section:sistem"),
            ("Çöp Kutusu", "section:sistem"),
            ("Ayarlar", null),
        };

        Assert.Equal(beklenen.Select(x => x.Grup), AppScreens.Groups.Select(g => g.Title));
        Assert.Equal(beklenen.Select(x => x.UstGrup), AppScreens.Groups.Select(g => g.Section));

        // Her üst grubun altında EN AZ BİR üst menü olmalı — boş üst grup varsayılanda bulunmaz.
        foreach (var sec in AppScreens.Sections)
            Assert.Contains(AppScreens.Groups, g => g.Section == sec.Key);
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
            // "sync_conflicts": FAZ 4.4 (2026-09-06) — masaüstünde karşılığı bir NAV EKRANI değil,
            // senkron uyarısından ve kabuk menüsünden açılan bir PENCEREdir (SyncConflictsWindow).
            "quota_monitor", "reset_company_business", "screen_visibility", "server_status", "sync_conflicts",
        }, yalnizWeb);

        // Geri kalanların hepsi iki platformda.
        Assert.Equal(AppScreens.All.Count - yalnizMasaustu.Length - yalnizWeb.Length,
            AppScreens.All.Count(s => s.Platforms == ScreenPlatform.Both));
    }
}
