using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ AR-GE 2026-10-25 (M-56 sırasında bulundu) — BAKIM MENÜ KISAYOLLARI DOĞRU SEKMEYİ AÇAR ═══
/// "Ekipman Bakımları" sekmesi (7b) sonradan 3. sıraya eklenince menüdeki "Uyarılar" kısayolu (sekme 2) ekipman sekmesini
/// açar olmuştu. Kilit: kısayolun verdiği sekme numarası, görünümdeki sekme sırasından OKUNUR — yeni sekme eklenirse test düşer.
///  BSK1 — maintenance:defs / records / alerts → Bakım Tanımları / Araç Bakımları / Uyarılar
/// </summary>
public class BakimSekmeKisayoluTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    private static int Sekme(string shell, string key)
    {
        var m = Regex.Match(shell, "case \"" + Regex.Escape(key) + "\":\\s*(?:case \"[^\"]+\":\\s*)*CurrentPage = new MaintenanceViewModel\\(_session, (\\d)\\)");
        Assert.True(m.Success, key + " kısayolu bulunamadı");
        return int.Parse(m.Groups[1].Value);
    }

    [Fact]
    public void BSK1_Kisayollar_Dogru_Sekme()
    {
        var basliklar = Regex.Matches(Oku("src/DepoWise.Desktop/Views/MaintenanceView.axaml"), "<TabItem Header=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).ToList();
        var shell = Oku("src/DepoWise.Desktop/ViewModels/ShellViewModel.cs");
        Assert.Equal("Bakım Tanımları", basliklar[Sekme(shell, "maintenance:defs")]);
        Assert.Equal("Araç Bakımları", basliklar[Sekme(shell, "maintenance:records")]);
        Assert.Equal("Uyarılar", basliklar[Sekme(shell, "maintenance:alerts")]);
        // derin bağlantı sekme numaraları da aynı sıraya bağlı (M-56: araç / ekipman bakımı kaynağı)
        var vm = Oku("src/DepoWise.Desktop/ViewModels/MaintenanceViewModel.cs");
        Assert.Equal(1, basliklar.IndexOf("Araç Bakımları"));
        Assert.Equal(2, basliklar.IndexOf("Ekipman Bakımları"));
        Assert.Contains("SelectedTab = 2;                       // Ekipman Bakımları", vm);
    }
}
