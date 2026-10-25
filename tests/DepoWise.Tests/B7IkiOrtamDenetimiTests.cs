using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ B7 İKİ ORTAM DENETİMİ (AR-GE 2026-10-25) — 4 AYRIŞMANIN KİLİDİ ═══
/// Masaüstü ↔ web karşılaştırmasında bulunan ayrışmalar düzeltildi; kaynak sözleşmesiyle kilitlenir.
///  ID1 — web bakım derin bağlantısı kayıt ilk sayfada değilse aracın bakımlarında arar (masaüstü AracaDaralt karşılığı)
///  ID2 — Muayene/Personel (web) ve Personel/Maliyet Merkezi Yazdır (masaüstü) dışa aktarım yetkisiyle görünür
///  ID3 — masaüstü Raporlar Excel/PDF düğmeleri web gibi özel buton yetkisiyle görünür
///  ID4 — web Stok Hareketleri dışa aktarma hatası listeyi silmez (masaüstü gibi yalnız mesaj)
/// </summary>
public class B7IkiOrtamDenetimiTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void ID1_Web_Bakim_Baglantisi_Araca_Daraltir()
    {
        var web = Oku("src/DepoWise.Web/Components/Pages/Maintenance.razor");
        Assert.Contains("[SupplyParameterFromQuery(Name = \"arac\")]", web);
        Assert.Contains("hit = await AracBakimindaBul(AracId!, ViewId!)", web);
        Assert.Contains("/api/maintenance/grid?page={sayfa}&pageSize=200&vehicleId=", web);
        Assert.Contains("\"/maintenance?view=\" + vm.Id + \"&arac=\"", Oku("src/DepoWise.Infrastructure/Materials/StockService.Kaynak.cs"));
    }

    [Fact]
    public void ID2_Disa_Aktarim_Dugmeleri_Yetkiyle()
    {
        foreach (var sayfa in new[] { "Inspection", "Personnel" })
        {
            var s = Oku($"src/DepoWise.Web/Components/Pages/{sayfa}.razor");
            var i = s.IndexOf("@if (Auth.CanView(\"export\"))", StringComparison.Ordinal);
            Assert.True(i >= 0, sayfa + ": dışa aktarım yetki kapısı yok");
            var blok = s.Substring(i, Math.Min(900, s.Length - i));
            Assert.Contains("OnClick=\"ExportExcel\"", blok);
            Assert.Contains("OnClick=\"Yazdir\"", blok);
        }
        foreach (var gorunum in new[] { "PersonnelView", "CostCentersView" })
            Assert.Matches(new Regex("Content=\"Yazdır\" Command=\"\\{Binding YazdirCommand\\}\" IsVisible=\"\\{Binding CanExport\\}\""),
                Oku($"src/DepoWise.Desktop/Views/{gorunum}.axaml"));
    }

    [Fact]
    public void ID3_Masaustu_Rapor_Disa_Aktarma_Yetkiyle()
    {
        var v = Oku("src/DepoWise.Desktop/Views/ReportsView.axaml");
        Assert.Contains("Command=\"{Binding ExportExcelCommand}\" VerticalAlignment=\"Bottom\" IsVisible=\"{Binding CanExportReport}\"", v);
        Assert.Contains("Command=\"{Binding ExportPdfCommand}\" VerticalAlignment=\"Bottom\" IsVisible=\"{Binding CanExportReport}\"", v);
        Assert.Contains("[NotifyPropertyChangedFor(nameof(CanExportReport))]", Oku("src/DepoWise.Desktop/ViewModels/ReportsViewModel.cs"));
        Assert.Contains("SelectedReport.IsManager ? SpecialButtons.ExportManagerReports : SpecialButtons.ExportReports);",
            Oku("src/DepoWise.Desktop/ViewModels/ReportsViewModel.Gorunum.cs"));
    }

    [Fact]
    public void ID4_Web_Stok_Hareketleri_Disa_Aktarma_Hatasi_Listeyi_Silmez()
    {
        var s = Oku("src/DepoWise.Web/Components/Pages/StockMovements.razor");
        var i = s.IndexOf("private async Task Disari(bool pdf)", StringComparison.Ordinal);
        Assert.True(i >= 0);
        var govde = s.Substring(i, s.IndexOf("private async Task Load()", i, StringComparison.Ordinal) - i);
        Assert.DoesNotContain("_error =", govde);
        Assert.Contains("Snackbar.Add(", govde);
    }
}
