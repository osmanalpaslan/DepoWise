using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G2/G6 — EKRAN/YETKİ AĞAÇLARININ HARİTASI VE PARİTE KİLİDİ (kullanıcı isteği 2026-08-12).
///
/// <b>SORUN:</b> bir ekran BİRDEN ÇOK yerde elle tanımlanıyor ve biri unutulunca ekran ya menüde
/// çıkmıyor, ya yetki ağacında görünmüyor, ya da tek platformda kalıyor. Web tarafındaki menü, masaüstü
/// menüsünün ELLE tutulan aynasıdır (<c>NavMenu.razor</c> içindeki kendi yorumu bunu söyler).
///
/// <b>BU DOSYA NE YAPAR:</b> tek kaynak (<c>AppScreens</c>) mimarisine geçmeden ÖNCE bugünkü ağaçları
/// kaynak koddan çıkarır ve aralarındaki tutarlılığı KİLİTLER. Böylece:
///   • bugünkü sapmalar ölçülür ve kayıt altına alınır,
///   • yeni bir ekran eklenirken bir katman unutulursa test KIRILIR (sessiz gerileme olmaz),
///   • tek kaynağa geçiş, davranışı bozmadığı kanıtlanarak yapılabilir.
///
/// ⚠️ Reflection ile "otomatik ekran keşfi" bilinçli olarak KULLANILMAZ: yeni bir ekran sessizce
/// yetkisiz açılabilir hâle gelir ve deny-by-default zayıflar. Bunun yerine bildirim + kaynak taraması.
/// </summary>
public class ScreenTreeParityTests
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

    // ── AĞAÇ 1: modül kataloğu (yetki ağacının kaynağı) ───────────────────────────────────────
    private static IReadOnlyList<string> ModuleKeys() => AppModules.All.Select(x => x.Key).ToList();

    // ── AĞAÇ 2: masaüstü menüsü ───────────────────────────────────────────────────────────────
    // G2/G6 SONRASI: menü artık kaynak koddan KAZINMAZ — AppScreens'ten ÜRETİLİR. Bu metotların
    // amacı değişti: eski dağınık yapıyı korumak değil, TEK KAYNAĞIN bütün sistemi beslediğini
    // doğrulamak. Aşağıdaki kontroller (route/gezinme/mükerrerlik/yetim) aynen sürüyor.
    private static (List<string> Groups, List<string> Links) DesktopNav()
        => (AppScreens.GroupsFor(ScreenPlatform.Desktop).Select(g => g.ModuleKey).ToList(),
            AppScreens.For(ScreenPlatform.Desktop).Select(s => s.DesktopNavKey!).ToList());

    // ── AĞAÇ 3: masaüstü gezinme anahtarları (key → View) ─────────────────────────────────────
    private static List<string> DesktopNavigateCases()
    {
        var src = Read("src/DepoWise.Desktop/ViewModels/ShellViewModel.cs");
        var i = src.IndexOf("private void Navigate(string key)", StringComparison.Ordinal);
        Assert.True(i > 0, "ShellViewModel.Navigate bulunamadı — masaüstü gezinme haritası değişmiş olabilir.");
        var body = src[i..];
        // Anahtarlar "modul" ya da "modul:altekran" biçiminde olabilir (ör. "stock:movements").
        return Regex.Matches(body, @"case ""([a-z0-9_:]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
    }

    // ── AĞAÇ 4: web menüsü (G2/G6 sonrası AppScreens'ten üretiliyor) ──────────────────────────
    private static List<(string Label, string Perm, string Route)> WebNav()
        => AppScreens.For(ScreenPlatform.Web)
            .Select(s => (s.Label, s.WebPermKey, s.WebRoute!)).ToList();

    // ── AĞAÇ 5: web route'ları ────────────────────────────────────────────────────────────────
    private static List<string> WebRoutes()
    {
        var dir = Path.Combine(Root(), "src", "DepoWise.Web", "Components", "Pages");
        var routes = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.razor"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"^@page\s+""/([^""]*)""", RegexOptions.Multiline))
                routes.Add(m.Groups[1].Value);
        return routes;
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // HARİTA — bugünkü ağaçların ölçüsü (değişirse test kırılır → bilinçli karar gerekir)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>1 — Beş ağacın tamamı kaynaktan OKUNABİLİYOR olmalı. Biri okunamazsa (yapı değiştiyse)
    /// parite kontrolleri sessizce boşa düşer — bu test onu yakalar.</summary>
    [Fact]
    public void A1_Bes_Agac_Da_Kaynaktan_Okunabiliyor()
    {
        Assert.True(ModuleKeys().Count >= 30, "Modül kataloğu beklenenden küçük.");
        var (groups, links) = DesktopNav();
        Assert.True(groups.Count >= 10, "Masaüstü menü grupları (AppScreens) okunamadı.");
        Assert.True(links.Count >= 30, "Masaüstü menü bağlantıları (AppScreens) okunamadı.");
        Assert.True(DesktopNavigateCases().Count >= 20, "Masaüstü gezinme anahtarları okunamadı.");
        Assert.True(WebNav().Count >= 30, "Web menüsü (AppScreens) okunamadı.");
        Assert.True(WebRoutes().Count >= 30, "Web route'ları okunamadı.");
    }

    /// <summary>2 — Masaüstü menüsündeki HER bağlantının gerçekten bir gezinme karşılığı olmalı;
    /// yoksa kullanıcı tıklar ve hiçbir şey açılmaz.</summary>
    [Fact]
    public void A2_Masaustu_Menu_Baglantilarinin_Hepsi_Gezinilebilir()
    {
        var (_, links) = DesktopNav();
        var cases = DesktopNavigateCases().ToHashSet(StringComparer.Ordinal);
        var eksik = links.Distinct().Where(k => !cases.Contains(k)).ToList();
        Assert.True(eksik.Count == 0,
            "Masaüstü menüsünde olup Navigate() içinde karşılığı OLMAYAN anahtarlar: " + string.Join(", ", eksik));
    }

    /// <summary>3 — Web menüsündeki HER bağlantının gerçek bir route'u olmalı (deep-link kırık olmasın).
    /// Parametreli route'lar (<c>@page "/materials/{Section}"</c>) desen olarak eşleştirilir.</summary>
    [Fact]
    public void A3_Web_Menu_Baglantilarinin_Hepsinin_Route_u_Var()
    {
        // "materials/{Section}" → ^materials/[^/]+$  ·  "materials" → ^materials$
        // Önce parametre segmentleri bir işaretçiye çevrilir, SONRA kaçış uygulanır (Regex.Escape
        // '{' karakterini kaçırır ama '}' karakterini kaçırmaz → ters sırada desen tutmaz).
        const string Isaret = "";
        var kaliplar = WebRoutes()
            .Select(r => new Regex(
                "^" + Regex.Escape(Regex.Replace(r, @"\{[^}]*\}", Isaret)).Replace(Isaret, "[^/]+") + "$",
                RegexOptions.IgnoreCase))
            .ToList();
        var eksik = WebNav().Select(x => x.Route).Distinct()
            .Where(r => !kaliplar.Any(k => k.IsMatch(r)))
            .ToList();
        Assert.True(eksik.Count == 0,
            "Web menüsünde olup @page route'u OLMAYAN adresler: " + string.Join(", ", eksik));
    }

    /// <summary>4 — Web menüsünün kullandığı HER yetki anahtarı modül kataloğunda olmalı.
    /// (Sözde anahtarlar — @admin/@super/@superr — bilinçli istisnadır ve burada kayıt altına alınır.)</summary>
    [Fact]
    public void A4_Web_Menu_Yetki_Anahtarlari_Katalogda_Var()
    {
        var known = ModuleKeys().ToHashSet(StringComparer.Ordinal);
        var pseudo = new[] { "", "@admin", "@super", "@superr" };
        var bilinmeyen = WebNav().Select(x => x.Perm).Distinct()
            .Where(p => !pseudo.Contains(p) && !known.Contains(p)).ToList();
        Assert.True(bilinmeyen.Count == 0,
            "Web menüsünde modül kataloğunda OLMAYAN yetki anahtarları: " + string.Join(", ", bilinmeyen));
    }

    /// <summary>
    /// 5 — ✅ G2-B1 KAPANDI (2026-08-12): masaüstü menüsünde olup YETKİ KATALOĞUNDA OLMAYAN modül
    /// ARTIK YOK. Önceden tek böyle ekran vardı — <b>"trash" (Çöp Kutusu)</b>: menüde ve
    /// <c>Navigate</c>'te vardı, web'de <c>@admin</c> sözde-anahtarıyla gösteriliyordu, ama
    /// <see cref="AppModules.All"/> içinde olmadığı için <b>yetki ağacından yönetilemiyordu</b>.
    /// Artık kataloğa alındı ve yönetim düzeyi (<see cref="AppModules.IsAdminRestricted"/>) olarak
    /// işaretlendi → mevcut davranış korunurken devredilebilir hâle geldi.
    ///
    /// Bu test listeyi <b>BOŞ</b> olarak kilitler: yeni bir ekran aynı hataya düşerse test KIRILIR.
    /// </summary>
    [Fact]
    public void A5_Katalog_Disi_Ekran_KALMADI()
    {
        var known = ModuleKeys().ToHashSet(StringComparer.Ordinal);
        var katalogDisi = DesktopNav().Groups.Distinct()
            .Where(g => !known.Contains(g) && !AppModules.IsPublic(g))
            .OrderBy(x => x, StringComparer.Ordinal).ToList();

        Assert.True(katalogDisi.Count == 0,
            "Yetki kataloğunda OLMAYAN menü modülleri (yetki ağacından yönetilemez): " + string.Join(", ", katalogDisi));
    }

    /// <summary>6 — Modül kataloğunda AYNI anahtar iki kez olmamalı (yetki ağacı çift satır göstermesin).</summary>
    [Fact]
    public void A6_Modul_Katalogunda_Mukerrer_Anahtar_Yok()
    {
        var dup = ModuleKeys().GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "Modül kataloğunda MÜKERRER anahtar: " + string.Join(", ", dup));
    }

    /// <summary>7 — Web'de AYNI route iki sayfada tanımlanmamalı (hangisinin açılacağı belirsizleşir).</summary>
    [Fact]
    public void A7_Web_Route_Mukerrer_Degil()
    {
        var dup = WebRoutes().GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "MÜKERRER web route: " + string.Join(", ", dup));
    }

    /// <summary>8 — Özel buton kataloğunda mükerrer anahtar olmamalı.</summary>
    [Fact]
    public void A8_Buton_Katalogunda_Mukerrer_Anahtar_Yok()
    {
        var dup = SpecialButtons.All.Select(x => x.Key)
            .GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "Buton kataloğunda MÜKERRER anahtar: " + string.Join(", ", dup));
    }

    /// <summary>9 — Kural listeleri (süper-admin-only / admin-kısıtlı) yalnız GERÇEK modüllere atıfta
    /// bulunmalı; yazım hatası olan bir anahtar sessizce ETKİSİZ kalır ve ekran korumasız açılır.</summary>
    [Fact]
    public void A9_Kural_Listeleri_Gercek_Modullere_Atif_Yapar()
    {
        var known = ModuleKeys().ToHashSet(StringComparer.Ordinal);
        var hatali = new List<string>();
        foreach (var key in known)
        {
            // Kurallar anahtar bazlı çalışıyor; katalogda olmayan bir anahtar için hiç çağrılmazlar.
            _ = AppModules.IsSuperAdminOnly(key);
            _ = AppModules.IsAdminRestricted(key);
        }
        // Kural fonksiyonlarındaki sabit metinleri kaynaktan çıkar ve katalogla karşılaştır.
        var src = Read("src/DepoWise.Application/Security/AppModules.cs");
        foreach (var blok in new[] { "IsSuperAdminOnly", "IsAdminRestricted", "IsPublic" })
        {
            var i = src.IndexOf(blok, StringComparison.Ordinal);
            if (i < 0) continue;
            var son = src.IndexOf(';', i);
            if (son < 0) continue;
            foreach (Match m in Regex.Matches(src[i..son], @"""([a-z_]+)"""))
                if (!known.Contains(m.Groups[1].Value) && m.Groups[1].Value is not ("alerts" or "about" or "theme"))
                    hatali.Add($"{blok}: {m.Groups[1].Value}");
        }
        Assert.True(hatali.Count == 0,
            "Kural listelerinde katalogda OLMAYAN modül anahtarları (sessizce etkisiz kalır): " + string.Join(", ", hatali));
    }

    /// <summary>10 — Yetki ağacında yönetilebilir HER modülün en az bir yerde (masaüstü ya da web)
    /// karşılığı olmalı. Karşılığı olmayan modül, kullanıcının hiç göremeyeceği bir yetki demektir.
    /// ⚠️ İSTİSNA listesi bilinçlidir: bunlar başka ekranların İÇİNDEN erişilen alt yetkilerdir.</summary>
    [Fact]
    public void A10_Yetki_Agacindaki_Moduller_Bir_Yerde_Kullaniliyor()
    {
        var desktopKeys = DesktopNavigateCases().Concat(DesktopNav().Groups).Concat(DesktopNav().Links)
            .ToHashSet(StringComparer.Ordinal);
        var webPerms = WebNav().Select(x => x.Perm).ToHashSet(StringComparer.Ordinal);

        // Menüsü olmayan ama gerçek olan yetkiler: başka ekranın içinden ya da özel akıştan kullanılırlar.
        var menusuz = new HashSet<string>(StringComparer.Ordinal)
        {
            "dashboard", "about", "theme",                 // herkese açık
            "export", "import_export", "files",            // liste ekranlarının içindeki butonlar
            "request_ops_warehouse", "request_ops_purchase",// Talep Operasyonları alt birimleri
            "definitions", "settings",                     // ayar/tanım akışları
            "permission_templates", "role_permissions",     // yalnız web/süper admin ekranları
            "quota_monitor", "server_status", "machine_backups", "server_backups",
            "purge_company", "companies", "releases", "machines",
            "stock_change_log", "audit", "backup",
            "material_templates", "vehicle_templates",
            "inspection", "request_approval", "request_ops",
        };

        var oksuz = ModuleKeys()
            .Where(k => !menusuz.Contains(k) && !desktopKeys.Contains(k) && !webPerms.Contains(k))
            .ToList();
        Assert.True(oksuz.Count == 0,
            "Yetki ağacında olup HİÇBİR menüde/gezinmede kullanılmayan modüller: " + string.Join(", ", oksuz));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // G3 — TABLO SATIR SEÇİMİ (kaynak kilidi)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>11 — G3 düzeltmesi yerinde mi: ortak tablo stili satır seçme davranışını bağlamalı
    /// ve davranış dosyası tünelleme kullanmalı. Biri kaldırılırsa 40+ ekranda hata geri döner.</summary>
    [Fact]
    public void G3_Tablo_Satir_Secme_Davranisi_Ortak_Stile_Bagli()
    {
        var tema = Read("src/DepoWise.Desktop/Themes/Components.axaml");
        Assert.Contains("ctrl:TableRowSelect.Enabled", tema);
        Assert.Contains("xmlns:ctrl=\"using:DepoWise.Desktop.Controls\"", tema);

        var davranis = Read("src/DepoWise.Desktop/Controls/TableRowSelect.cs");
        // Tünelleme ŞART: metin olayı tüketse bile satır seçilebilsin.
        Assert.Contains("RoutingStrategies.Tunnel", davranis);
        // Olay İŞARETLENMEMELİ: metin seçimi/kopyalama ve tooltip çalışmaya devam etsin.
        Assert.DoesNotContain("e.Handled = true", davranis);
        // Gerçek kontroller korunmalı.
        Assert.Contains("Button or CheckBox", davranis);
    }

    /// <summary>12 — Düzeltme TABLOYLA SINIRLI olmalı: genel bir "SelectableTextBlock tıklanamaz" kuralı
    /// eklenmemeli (tablo dışındaki metin kopyalama davranışı bozulmasın).</summary>
    [Fact]
    public void G3_Duzeltme_Tabloyla_Sinirli_Kalir()
    {
        var tema = Read("src/DepoWise.Desktop/Themes/Components.axaml");
        Assert.DoesNotContain("Selector=\"SelectableTextBlock\"", tema);
        var i = tema.IndexOf("ctrl:TableRowSelect.Enabled", StringComparison.Ordinal);
        var oncesi = tema[..i];
        var sonSelector = oncesi.LastIndexOf("<Style Selector=", StringComparison.Ordinal);
        Assert.Contains("ListBox.Table", tema[sonSelector..i]);
    }
}
