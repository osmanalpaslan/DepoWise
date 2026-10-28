using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ iOS TARZI TASARIM (2026-10-28, kullanıcı kararı: "tasarımı tamamen sana bırakıyorum … bazı butonları sağ tık içine
/// taşıyabilirsin") — masaüstü kaynak sözleşmeleri ═══
///  IOS-SAG1 — liste ekranlarında satır SAĞ TIK menüsü var; "Düzenle" içerir; her öğe ekrandaki karşılık düğmeyle AYNI komutu
///             ve AYNI görünürlük (yetki) koşulunu taşır; seçili kayıt yoksa öğeler pasif
///  IOS-SAG2 — sağ tık da satırı seçer (menü doğru kayda uygulansın); orta tuş vb. seçmez
/// </summary>
public class IosTasarimTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Ekran(string ad) => File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views", ad + ".axaml"));

    public static IEnumerable<object[]> SagTikEkranlari() => new[]
    {
        "VehiclesView", "MaterialsView", "EquipmentView", "PersonnelView", "WorkOrdersView", "MaterialTemplatesView",
        "VehicleTemplatesView", "DocumentsView", "ProjectsView", "CostCentersView", "BranchesView", "AnnouncementsView", "CalendarView",
        "RequestsView", "FuelView",
    }.Select(x => new object[] { x });

    [Theory]
    [MemberData(nameof(SagTikEkranlari))]
    public void IOSSAG1_Liste_Sag_Tik_Menusu_Ekrandaki_Dugmeyle_Ayni(string ekran)
    {
        var x = Ekran(ekran);
        var bas = x.IndexOf("<ContextMenu Classes=\"SatirMenusu\">", StringComparison.Ordinal);
        Assert.True(bas > 0, ekran + ": satır sağ tık menüsü yok");
        var menu = x[bas..x.IndexOf("</ContextMenu>", bas, StringComparison.Ordinal)];
        var ogeler = Regex.Matches(menu, @"<MenuItem Header=""([^""]+)"" Command=""\{Binding (\w+)\}""(?: IsVisible=""([^""]+)"")? IsEnabled=""([^""]+)""/>");
        Assert.Contains(ogeler, m => m.Groups[1].Value == "Düzenle");
        foreach (Match o in ogeler)
        {
            var komut = o.Groups[2].Value;
            Assert.Contains("{Binding Selected", o.Groups[4].Value);           // seçim yoksa pasif (ya da düğmenin kendi etkinlik koşulu)
            // Ekrandaki karşılık düğme (menü dışında) aynı komutu taşır; görünürlük koşulu varsa menüde de AYNISI.
            var disari = x[..bas] + x[x.IndexOf("</ContextMenu>", bas, StringComparison.Ordinal)..];
            var dugme = Regex.Match(disari, @"<(?:Button|MenuItem)\b[^>]*Command=""\{Binding " + komut + @"\}""[^>]*>");
            Assert.True(dugme.Success, $"{ekran}: {komut} için ekranda düğme yok");
            var kosul = Regex.Match(dugme.Value, @"\sIsVisible=""([^""]+)""");
            if (kosul.Success) Assert.Equal(kosul.Groups[1].Value, o.Groups[3].Value);
        }
        // Yıkıcı işlem (Sil) varsa en sonda ve ayıraçla ayrılmış
        var silIndex = menu.IndexOf("Header=\"Sil\"", StringComparison.Ordinal);
        if (silIndex > 0)
        {
            Assert.True(menu.LastIndexOf("<Separator/>", silIndex, StringComparison.Ordinal) > 0, ekran + ": Sil ayıraçla ayrılmalı");
            Assert.Equal(-1, menu.IndexOf("<MenuItem", silIndex, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void IOSSAG2_Sag_Tik_Satiri_Secer()
    {
        var k = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Controls", "TableRowSelect.cs"));
        Assert.Contains("if (!tus.IsLeftButtonPressed && !tus.IsRightButtonPressed) return;", k);
        Assert.DoesNotContain("if (!e.GetCurrentPoint(list).Properties.IsLeftButtonPressed) return;", k);
    }

    /// <summary>IOS-YEN1 — "Yenile" yazılı düğme kalmadı; hepsi simgeli kare düğme, ekran okuyucu adı "Yenile" ve ipucu "Yenile (F5)".</summary>
    [Fact]
    public void IOSYEN1_Yenile_Simgeli_Ve_Erisilebilir()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        int adet = 0;
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
        {
            var x = File.ReadAllText(f);
            Assert.DoesNotContain("Content=\"Yenile\"", x);
            foreach (Match m in Regex.Matches(x, @"<Button\s[^>]*YenileDugme[^>]*>"))
            {
                adet++;
                Assert.Contains("AutomationProperties.Name=\"Yenile\"", m.Value);
                Assert.Contains("ToolTip.Tip=\"Yenile (F5)\"", m.Value);
            }
        }
        Assert.True(adet >= 30, "simgeli yenile düğmesi: " + adet);
    }

    /// <summary>IOS-YIK1 — yıkıcı işlem düğmesi ("Sil", "İptal Et", "Kaydı İptal Et") sıradan gri (Secondary) stilde olmaz: kırmızı
    /// (Danger) ya da menüde. Tablo satırı içindeki sade (Ghost) satır işlemleri bilinçli olarak kapsam dışı.</summary>
    [Fact]
    public void IOSYIK1_Yikici_Dugme_Gri_Degil()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        var ihlal = new List<string>();
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), "<Button Classes=\"Secondary\"[^>]*Content=\"(Sil|İptal Et|Kaydı İptal Et)\""))
                ihlal.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value);
        Assert.True(ihlal.Count == 0, "gri yıkıcı düğme: " + string.Join(", ", ihlal));
    }

    /// <summary>IOS-HIZ1 — sabit genişlikli form alanı (FormField Width=…) DİKEY bir StackPanel içindeyse hizası açıkça belirtilir;
    /// yoksa Avalonia onu ortalar ve alan diğerlerinden sağa kayar (2026-10-29 görsel taramada Kullanıcı "Not", Bakım "depo").</summary>
    [Fact]
    public void IOSHIZ1_Sabit_Genislikli_Alan_Dikey_Sutunda_Kaymaz()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        var ihlal = new List<string>();
        var etiket = new Regex(@"<!--[\s\S]*?-->|<(/?)([A-Za-z_][\w:.]*)\b([^>]*?)(/?)>");
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
        {
            var yigin = new List<(string Ad, string Nit)>();
            foreach (Match m in etiket.Matches(File.ReadAllText(f)))
            {
                if (m.Value.StartsWith("<!--")) continue;
                var ad = m.Groups[2].Value; var nit = m.Groups[3].Value;
                if (m.Groups[1].Value == "/")
                {
                    while (yigin.Count > 0) { var son = yigin[^1]; yigin.RemoveAt(yigin.Count - 1); if (son.Ad == ad) break; }
                    continue;
                }
                if (ad == "ctrl:FormField" && Regex.IsMatch(nit, @"\sWidth=""\d+""") && !nit.Contains("HorizontalAlignment=") && !nit.Contains("Grid.Column=")
                    && yigin.Count > 0 && yigin[^1].Ad == "StackPanel" && !yigin[^1].Nit.Contains("Orientation=\"Horizontal\""))
                    ihlal.Add(Path.GetFileName(f) + ": " + Regex.Match(nit, @"Label=""([^""]*)""").Groups[1].Value);
                if (m.Groups[4].Value != "/") yigin.Add((ad, nit));
            }
        }
        Assert.True(ihlal.Count == 0, "dikey sütunda hizasız sabit genişlikli alan: " + string.Join(", ", ihlal));
    }

    /// <summary>IOS-BOS1 — madde 1 (2026-10-29): ana eylemi olan ekranlarda "kayıt yok" paneli aynı ana eylemi (metin + komut → aynı
    /// yetki) gösterir; kullanıcı boş ekranda ne yapacağını görür.</summary>
    [Fact]
    public void IOSBOS1_Bos_Ekranda_Ana_Eylem()
    {
        var dizin = Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views");
        int adet = 0; var ihlal = new List<string>();
        foreach (var f in Directory.GetFiles(dizin, "*.axaml"))
        {
            var x = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(x, @"<ctrl:StatePanel\s[^>]*Mode=""Empty""[^>]*>"))
            {
                var once = x[..m.Index];
                var tb = Regex.Matches(once, @"<ctrl:Toolbar\s[^>]*>").LastOrDefault();
                if (tb is null) continue;
                var komut = Regex.Match(tb.Value, "PrimaryActionCommand=\"([^\"]+)\"");
                var metin = Regex.Match(tb.Value, "PrimaryActionText=\"([^\"]+)\"");
                if (!komut.Success || !metin.Success) continue;
                adet++;
                if (!m.Value.Contains($"ActionCommand=\"{komut.Groups[1].Value}\"") || !m.Value.Contains($"ActionText=\"{metin.Groups[1].Value}\""))
                    ihlal.Add(Path.GetFileName(f));
            }
        }
        Assert.True(adet >= 20, "ana eylemli boş panel: " + adet);
        Assert.True(ihlal.Count == 0, "boş panelde ana eylem eksik/farklı: " + string.Join(", ", ihlal));
    }
}
