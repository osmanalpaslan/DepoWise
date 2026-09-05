using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MNU-IKON (kullanıcı isteği 2026-09-05) — SİMGESİZ MENÜ KALMAYACAK ═══
///
/// <b>Bulgu (iki ortam da ölçüldü):</b>
/// <list type="bullet">
///   <item><b>70 alt menünün HİÇBİRİNDE ikon yoktu</b> — eksik kalmış değil, hiç tanımlanmamıştı:
///     masaüstü şablonunda ikon alanı bile yoktu, web'de <c>MudNavLink</c>'lere <c>Icon</c>
///     verilmiyordu.</item>
///   <item>Masaüstünde <b>7 üst menü</b> ikonsuzdu (Ekipman · Zimmet · Satın Alma · İş Emirleri ·
///     Takvim · Evrak · Duyurular).</item>
///   <item>Web'de eşleme tablosunda <b>beş eskimiş anahtar</b> vardı ("Personel", "Yönetim",
///     "Raporlar", "İmport / Export", "Kullanıcı") — grup adları değişince ikon SESSİZCE genel
///     klasöre düşmüştü.</item>
/// </list>
///
/// <b>Kök neden:</b> eşleme iki ayrı yerde elle tutuluyordu ve eşleşmeyen anahtar HATA VERMİYOR,
/// yalnız ikonu kaybediyordu. Bu testlerin varlık sebebi tam olarak budur: bir daha sessizce
/// eskiyemesin.
///
///  MIK1 — Katalogdaki HER ekranın simge kavramı var (nötre düşmüyor)
///  MIK2 — Katalogdaki HER üst menünün simge kavramı var
///  MIK3 — Katalogdaki HER üst grubun (section) simge kavramı var
///  MIK4 — Kullanılan HER kavramın masaüstü karşılığı var VE geometri gerçekten çizilmiş
///  MIK5 — Kullanılan HER kavramın web karşılığı var
///  MIK6 — Alt menüler iki platformda da ikon ÇİZİYOR (şablon kanıtı)
///  MIK7 — Eşleme tabloları katalogda OLMAYAN başlık taşımıyor (eskimiş anahtar kalmasın)
/// </summary>
public class MenuIkonTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string NavMenu() => Oku("src", "DepoWise.Web", "Components", "Layout", "NavMenu.razor");
    private static string DesktopIkonlar() => Oku("src", "DepoWise.Desktop", "DesktopIcons.cs");
    private static string IkonKaynagi() => Oku("src", "DepoWise.Desktop", "Themes", "Icons.axaml");
    private static string AnaPencere() => Oku("src", "DepoWise.Desktop", "Views", "MainWindow.axaml");

    // ══════════════ KATALOĞUN TAMAMI KAPSANIYOR MU ══════════════

    /// <summary>⭐ Yeni bir ekran eklenip simge kavramı unutulursa BURADA kırılır — eskiden bu
    /// sessizce ikonsuz bir menü satırı olarak geçiyordu.</summary>
    [Fact]
    public void MIK1_Her_Ekranin_Simge_Kavrami_Var()
    {
        var eksik = AppScreens.All
            .Where(s => MenuIcons.ForScreen(s.Key) == MenuIcons.Fallback)
            .Select(s => s.Key)
            .ToList();

        Assert.True(eksik.Count == 0,
            "Simge kavramı tanımlanmamış ekran(lar) — MenuIcons.ByScreen'e eklenmeli: " + string.Join(", ", eksik));
    }

    [Fact]
    public void MIK2_Her_Ust_Menunun_Simge_Kavrami_Var()
    {
        var eksik = AppScreens.Groups
            .Select(g => g.Title)
            .Distinct(StringComparer.Ordinal)
            .Where(t => MenuIcons.ForGroup(t) == "group")
            .ToList();

        Assert.True(eksik.Count == 0,
            "Simge kavramı tanımlanmamış üst menü(ler): " + string.Join(", ", eksik));
    }

    [Fact]
    public void MIK3_Her_Ust_Grubun_Simge_Kavrami_Var()
    {
        var eksik = AppScreens.Sections
            .Select(s => s.Title)
            .Where(t => MenuIcons.ForSection(t) == "group")
            .ToList();

        Assert.True(eksik.Count == 0,
            "Simge kavramı tanımlanmamış üst grup(lar): " + string.Join(", ", eksik));
    }

    // ══════════════ İKİ PLATFORM DA KAVRAMLARI ÇEVİREBİLİYOR MU ══════════════

    /// <summary>
    /// ⭐ Masaüstü: kavramın hem eşlemesi hem GEOMETRİSİ olmalı. Yalnız eşlemeye bakmak yetmez —
    /// Icons.axaml'de karşılığı çizilmemiş bir anahtar çalışma zamanında null döner ve satır
    /// yine ikonsuz kalır (yani kusur test yeşilken devam ederdi).
    /// </summary>
    [Fact]
    public void MIK4_Her_Kavramin_Masaustu_Karsiligi_Ve_Geometrisi_Var()
    {
        var kod = DesktopIkonlar();
        var kaynak = IkonKaynagi();
        var eslemesizler = new List<string>();
        var cizilmemisler = new List<string>();

        foreach (var kavram in MenuIcons.AllConcepts())
        {
            var m = Regex.Match(kod, "\\[\"" + Regex.Escape(kavram) + "\"\\]\\s*=\\s*\"(Icon[A-Za-z]+)\"");
            if (!m.Success) { eslemesizler.Add(kavram); continue; }

            var anahtar = m.Groups[1].Value;
            if (!kaynak.Contains("x:Key=\"" + anahtar + "\"", StringComparison.Ordinal))
                cizilmemisler.Add($"{kavram} → {anahtar}");
        }

        Assert.True(eslemesizler.Count == 0,
            "DesktopIcons.ByConcept'te karşılığı olmayan kavram(lar): " + string.Join(", ", eslemesizler));
        Assert.True(cizilmemisler.Count == 0,
            "Icons.axaml'de geometrisi ÇİZİLMEMİŞ anahtar(lar): " + string.Join(", ", cizilmemisler));
    }

    [Fact]
    public void MIK5_Her_Kavramin_Web_Karsiligi_Var()
    {
        var kod = NavMenu();
        var eksik = MenuIcons.AllConcepts()
            .Where(k => !Regex.IsMatch(kod, "\"" + Regex.Escape(k) + "\"\\s*=>\\s*Icons\\.Material\\."))
            .Where(k => k != "group")   // "group" web'de `_ =>` varsayılan daldan karşılanır
            .ToList();

        Assert.True(eksik.Count == 0,
            "NavMenu.KavramIkonu'nda karşılığı olmayan kavram(lar): " + string.Join(", ", eksik));
    }

    // ══════════════ ŞABLONLAR GERÇEKTEN İKON ÇİZİYOR MU ══════════════

    /// <summary>
    /// ⭐ Asıl kusur şablondaydı: eşleme olsa bile şablon ikonu çizmiyorsa menü yine ikonsuzdur.
    /// Bu test iki platformda da çizim satırını kilitler.
    /// </summary>
    [Fact]
    public void MIK6_Alt_Menuler_Iki_Platformda_Da_Ikon_Ciziyor()
    {
        // Web: alt menü bağlantısı ekranın kendi ikonunu alıyor.
        var web = NavMenu();
        Assert.Contains("Icon=\"@EkranIkonu(e.Screen.Key)\"", web);
        // Üst gruplar da artık hepsi aynı klasör ikonunu almıyor.
        Assert.DoesNotContain("Icon=\"@Icons.Material.Filled.FolderOpen\"", web);

        // Masaüstü: NavLinkVm ikon taşıyor ve şablon onu çiziyor.
        Assert.Contains("IconGeometry = DesktopIcons.ForScreenKey(e.Screen.Key)",
            Oku("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"));

        // Şablonun TAM kapsamı alınır (başlangıç işaretinden kendi </DataTemplate>'ine kadar) —
        // sabit karakter penceresi kullanmak yorum uzunluğuna bağımlı, kırılgan bir ölçüm olurdu.
        var pencere = AnaPencere();
        var bas = pencere.IndexOf("x:DataType=\"vm:NavLinkVm\"", StringComparison.Ordinal);
        Assert.True(bas > 0, "MainWindow.axaml içinde NavLinkVm şablonu bulunamadı.");
        var son = pencere.IndexOf("</DataTemplate>", bas, StringComparison.Ordinal);
        Assert.True(son > bas, "NavLinkVm şablonunun sonu bulunamadı.");
        var altSablon = pencere[bas..son];

        Assert.Contains("PathIcon", altSablon);
        Assert.Contains("Data=\"{Binding IconGeometry}\"", altSablon);
        Assert.Contains("IsVisible=\"{Binding HasIcon}\"", altSablon);
    }

    /// <summary>
    /// ⭐ ESKİMİŞ ANAHTAR KORUMASI — bu turun kök nedeni buydu. Web tablosunda katalogda artık
    /// bulunmayan beş başlık vardı ve kimse fark etmemişti. Eşleme tabloları yalnız GERÇEK
    /// katalog başlıklarını taşımalı; taşımayan satır ölü koddur ve yanlış güven verir.
    /// </summary>
    [Fact]
    public void MIK7_Eslemede_Katalogda_Olmayan_Baslik_Yok()
    {
        var gercekGruplar = AppScreens.Groups.Select(g => g.Title).ToHashSet(StringComparer.Ordinal);
        var gercekBolumler = AppScreens.Sections.Select(s => s.Title).ToHashSet(StringComparer.Ordinal);
        var gercekEkranlar = AppScreens.All.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);

        var kod = Oku("src", "DepoWise.Application", "Security", "MenuIcons.cs");
        var olu = new List<string>();

        // Sözlük anahtarları: ["<anahtar>"] = "<kavram>"
        foreach (Match m in Regex.Matches(kod, "\\[\"([^\"]+)\"\\]\\s*=\\s*\""))
        {
            var anahtar = m.Groups[1].Value;
            if (!gercekGruplar.Contains(anahtar) && !gercekBolumler.Contains(anahtar) && !gercekEkranlar.Contains(anahtar))
                olu.Add(anahtar);
        }

        Assert.True(olu.Count == 0,
            "MenuIcons'ta katalogda KARŞILIĞI OLMAYAN (eskimiş) anahtar(lar): " + string.Join(", ", olu));
    }
}
