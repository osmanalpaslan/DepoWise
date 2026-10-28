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
}
