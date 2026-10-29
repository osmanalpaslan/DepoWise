using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MASAÜSTÜ "YENİ KAYIT" EKRANLARI — menü girişi + form yüksekliği ═══ (kullanıcı bildirimi 2026-10-12)
///
/// <b>1) MENÜ "YENİ" GİRİŞİ FORMU AÇMALI.</b> Menüdeki "Yeni Araç Ekle / Yeni Cari / Yeni Fatura / Yeni Hesap"
/// masaüstünde yalnız LİSTEYİ açıyordu (formu açan tek giriş "Malzemeler › Yeni Kayıt" idi); web aynı girişlerde
/// formu açıyor. Artık her ":new" anahtarı ekranın KENDİ "Yeni" komutunu çalıştırır (ayrı bir yol yok).
///
/// <b>2) FORM SABİT PİKSEL KUTUYA SIKIŞMAMALI.</b> Malzeme/Araç formları <c>ScrollViewer MaxHeight="470"</c> gibi
/// sabit bir kutudaydı: ekranın altı boşken form fare tekerleğiyle kaydırılıyordu. Artık form ya listenin esneyen
/// satırını kullanır ya da ekran yüksekliğinin ORANIYLA sınırlanır (<see cref="DepoWise.Application.Ui.FormListeOrani"/>).
/// Görsel doğrulama: <c>tests/DepoWise.Desktop.UiTests/GorselTaramaTests.cs</c> ("SIKIŞIK FORM" / "ULAŞILAMAZ" denetimi).
/// </summary>
public class MasaustuYeniKayitFormuTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Kaynak(params string[] yol) => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(yol).ToArray()));

    /// <summary>Shell'deki <c>case "anahtar":</c> satırından bir sonraki <c>break;</c>'e kadar olan blok.</summary>
    private static string CaseBlogu(string shell, string anahtar)
    {
        var bas = shell.IndexOf($"case \"{anahtar}\":", StringComparison.Ordinal);
        Assert.True(bas >= 0, $"ShellViewModel.Navigate içinde '{anahtar}' yok");
        var son = shell.IndexOf("break;", bas, StringComparison.Ordinal);
        return shell[bas..son];
    }

    public static IEnumerable<object[]> YeniAnahtarlari() =>
        AppScreens.All.Where(s => s.OnDesktop && s.DesktopNavKey is { } k && k.EndsWith(":new"))
                      .Select(s => new object[] { s.DesktopNavKey! });

    [Fact]
    public void YKF1_Menude_Yeni_Girisleri_Var()
    {
        // Katalog değişirse bu test bilinçli güncellenir; sessizce sıfıra düşüp aşağıdaki testi boşa çıkarmasın.
        Assert.True(YeniAnahtarlari().Count() >= 5);
    }

    [Theory]
    [MemberData(nameof(YeniAnahtarlari))]
    public void YKF2_Menu_Yeni_Girisi_Formu_Acar(string anahtar)
    {
        var blok = CaseBlogu(Kaynak("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"), anahtar);
        var formuAcar = blok.Contains("openAdd: true") || Regex.IsMatch(blok, @"\.\w+Command\.Execute\(null\)");
        Assert.True(formuAcar, $"'{anahtar}' menü girişi yalnız listeyi açıyor — yeni kayıt formu açılmalı");
    }

    [Theory]
    [MemberData(nameof(YeniAnahtarlari))]
    public void YKF3_Form_Yalniz_Yetki_Varsa_Acilir(string anahtar)
    {
        var blok = CaseBlogu(Kaynak("src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"), anahtar);
        if (blok.Contains("openAdd: true")) return;   // Malzeme: yetki kontrolü kurucuda (openAdd && CanWrite)
        Assert.Matches(@"if \((key == ""[\w:]+"" && )?vm\.Can(Write|Create)\) vm\.\w+Command\.Execute", blok);
    }

    /// <summary>Yeni kayıt formu bulunan ekranlarda formu SABİT piksel kaydırıcıya koymak yasak (kullanıcı bulgusu).
    /// İç tablolar (<c>Border Classes="Table" MaxHeight</c>) bu kurala girmez — yalnız ScrollViewer.</summary>
    [Theory]
    [InlineData("MaterialsView.axaml")]
    [InlineData("VehiclesView.axaml")]
    [InlineData("MaterialTemplatesView.axaml")]
    [InlineData("VehicleTemplatesView.axaml")]
    [InlineData("RequestsView.axaml")]
    [InlineData("MaintenanceView.axaml")]
    [InlineData("DailyActivityView.axaml")]
    [InlineData("UsersView.axaml")]
    [InlineData("StockEntryView.axaml")]
    public void YKF4_Form_Sabit_Piksel_Kaydiriciya_Sikismaz(string gorunum)
    {
        var x = Kaynak("src", "DepoWise.Desktop", "Views", gorunum);
        Assert.DoesNotMatch(@"<ScrollViewer\s+MaxHeight=""\d+""", x);
    }

    /// <summary>Liste formla BİRLİKTE görünen ekranlarda form, oran bağlamasıyla sınırlı kalmalı (listeyi ezmesin).</summary>
    [Theory]
    [InlineData("MaterialTemplatesView.axaml", "KokYerlesim")]
    [InlineData("VehicleTemplatesView.axaml", "KokYerlesim")]
    [InlineData("RequestsView.axaml", "TalepYerlesim")]
    [InlineData("MaintenanceView.axaml", "TanimYerlesim")]
    [InlineData("MaintenanceView.axaml", "BakimYerlesim")]
    [InlineData("DailyActivityView.axaml", "KokYerlesim")]
    [InlineData("UsersView.axaml", "KokYerlesim")]
    public void YKF5_Liste_Gorunurken_Form_Oranla_Sinirli(string gorunum, string kok)
    {
        var x = Kaynak("src", "DepoWise.Desktop", "Views", gorunum);
        Assert.Contains($"x:Name=\"{kok}\"", x);
        Assert.Contains($"MaxHeight=\"{{Binding #{kok}.Bounds.Height, Converter={{StaticResource FormUstSiniri}}}}\"", x);
        Assert.Contains("<conv:FormUstSiniriConverter x:Key=\"FormUstSiniri\"/>", x);
    }

    /// <summary>
    /// WEB eşi (2026-10-12, iki ortam denetimi bulgusu): <c>/…/new</c> adresi formu açan sayfalarda, formu açan metot
    /// YETKİ kapısı taşımalı. <c>/parties/new</c> ve <c>/finance/new</c> formu yetki sormadan açıyordu (kayıt sunucuda
    /// yine reddediliyordu ama yetkisiz kişi boş formu görüyordu). Masaüstünde form yetki yoksa hiç açılmaz.
    /// </summary>
    [Fact]
    public void YKF7_Web_New_Adresi_Formu_Yalniz_Yetkiliye_Acar()
    {
        var sayfalar = Directory.GetFiles(Path.Combine(RepoKok(), "src", "DepoWise.Web", "Components", "Pages"), "*.razor");
        var denetlenen = new List<string>();
        foreach (var yol in sayfalar)
        {
            var x = File.ReadAllText(yol);
            var m = Regex.Match(x, @"if \(string\.Equals\(Section, ""new"", StringComparison\.OrdinalIgnoreCase\)\)\s*(?:await\s+)?(?<metot>\w+)\(");
            if (!m.Success) continue;
            var metot = m.Groups["metot"].Value;
            var tanim = Regex.Match(x, @"private\s+(?:async\s+)?(?:void|Task)\s+" + metot + @"\([^)]*\)\s*\{(?<govde>.{0,600})", RegexOptions.Singleline);
            Assert.True(tanim.Success, $"{Path.GetFileName(yol)}: '{metot}' tanımı bulunamadı");
            Assert.Matches(@"Auth\.CanCreate\(""\w+""\)", tanim.Groups["govde"].Value);
            denetlenen.Add(Path.GetFileName(yol));
        }
        // Anlamlılık: Cari, Fatura, Kasa/Banka bu kalıbı kullanır — tarama onları gerçekten bulmalı.
        Assert.Contains("Parties.razor", denetlenen);
        Assert.Contains("Invoices.razor", denetlenen);
        Assert.Contains("Finance.razor", denetlenen);
    }

    /// <summary>Liste formla birlikte GİZLENEN ekranlarda form, listenin esneyen (*) satırına yerleşir.</summary>
    [Theory]
    [InlineData("MaterialsView.axaml", "Grid.Row=\"3\"")]
    [InlineData("VehiclesView.axaml", "Grid.Row=\"2\"")]
    public void YKF6_Liste_Gizliyken_Form_Esneyen_Satirda(string gorunum, string satir)
    {
        var x = Kaynak("src", "DepoWise.Desktop", "Views", gorunum);
        // 2026-10-29: iOS form kutusu ek özellik taşıyabilir (zemin/kenarlık) ve satır bölünebilir → öznitelik SIRASINA değil anlamına bakılır.
        Assert.Matches(new System.Text.RegularExpressions.Regex(
            "<Border[^>]*" + System.Text.RegularExpressions.Regex.Escape(satir) + "[^>]*Classes=\"Panel\"[^>]*VerticalAlignment=\"Top\"\\s+IsVisible=\"\\{Binding ShowAdd\\}\""), x);
        // liste aynı satırda, form açıkken gizli. Bilgi paneli 2026-10-29 kullanıcı isteğiyle 360 → 420 px.
        Assert.Contains($"{satir} Margin=\"0,16,0,0\" ColumnDefinitions=\"*,420\" IsVisible=\"{{Binding !ShowAdd}}\"", x);
    }
}
