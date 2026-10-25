using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-62 (2/4) (AR-GE 2026-10-25, F-095) — "KAYDEDİLDİ" BİLDİRİMİNİN BAĞLANTISI (kaynak sözleşmesi) ═══
/// Kural (Bildirimler.BasariMi) arayüz testinde (UIT17) sınanır; burada zincirin kopmadığı kilitlenir:
///  BB1 — kabuk açık ekranın Status değişimini dinler ve ekran değişince eski dinleyiciyi BIRAKIR (sızıntı yok)
///  BB2 — ana pencere bildirime abone olur, kapanınca aboneliği bırakır; bildirim UI iş parçacığında gösterilir
/// </summary>
public class BildirimBaglantisiTests
{
    private static string Oku(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    [Fact]
    public void BB1_Kabuk_Status_Dinler_Ve_Birakir()
    {
        var s = Oku("src/DepoWise.Desktop/ViewModels/ShellViewModel.cs");
        Assert.Contains("_currentPage.PropertyChanged -= SayfaDurumuDegisti;", s);
        Assert.Contains("value.PropertyChanged += SayfaDurumuDegisti;", s);
        Assert.Contains("e.PropertyName != \"Status\"", s);
        Assert.Contains("Bildirimler.Durum(durum)", s);
    }

    /// <summary>BB3 — M-62 (3/4): pencere kısayolları ana pencerede eşlenir; kayda dokunan kısayol (Ctrl+S, Del) YOK.</summary>
    [Fact]
    public void BB3_Klavye_Kisayollari_Eslenir()
    {
        var s = Oku("src/DepoWise.Desktop/Views/MainWindow.axaml.cs");
        foreach (var e in new[] { "(Avalonia.Input.Key.F5, false) => Kisayollar.Yenile(", "(Avalonia.Input.Key.N, true) => Kisayollar.AnaEylem(",
                                  "(Avalonia.Input.Key.F, true) => Kisayollar.AramayaOdaklan(", "(Avalonia.Input.Key.W, true) => Kisayollar.SekmeKapat(",
                                  "(Avalonia.Input.Key.Tab, true) => Kisayollar.SekmeGec(", "(Avalonia.Input.Key.F1, false) =>" })
            Assert.Contains(e, s);
        Assert.DoesNotContain("Avalonia.Input.Key.S, true", s);
        Assert.DoesNotContain("Avalonia.Input.Key.Delete", s);
        Assert.Contains("x:Name=\"SayfaAlani\"", Oku("src/DepoWise.Desktop/Views/MainWindow.axaml"));
    }

    [Fact]
    public void BB2_Ana_Pencere_Abone_Olur_Ve_Birakir()
    {
        var s = Oku("src/DepoWise.Desktop/Views/MainWindow.axaml.cs");
        Assert.Contains("Bildirimler.BasariGoster += BildirimGoster;", s);
        Assert.Contains("Bildirimler.BasariGoster -= BildirimGoster;", s);
        Assert.Contains("Dispatcher.UIThread.Post(", s);
        Assert.Contains("NotificationType.Success", s);
    }
}
