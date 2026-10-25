using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-65 (AR-GE 2026-10-25, F-130) — ANA EKRAN "HIZLI İŞLEMLER" İKİ ORTAMDA AYNI ═══
///  HP1 — masaüstü (DashboardViewModel) ve web (Home.razor) katalogları aynı ad ve sırada; aynı ekranlara gider
///  HP2 — web düğmesi menüyle aynı erişim kararını kullanır (yetki + web'de açık); ayrı kural yok
/// </summary>
public class HizliIslemParitesiTests
{
    private static string Oku(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    [Fact]
    public void HP1_Masaustu_Web_Ayni_Katalog()
    {
        var masa = Regex.Matches(Oku("src/DepoWise.Desktop/ViewModels/DashboardViewModel.cs"), @"new HizliIslem\(""([^""]+)"", ""([^""]+)""\)")
            .Select(m => (Baslik: m.Groups[1].Value, Ekran: AppScreens.ByDesktopNavKey(m.Groups[2].Value)?.Key)).ToList();
        var web = Regex.Matches(Oku("src/DepoWise.Web/Components/Pages/Home.razor"), @"\(""([^""]+)"", ""([a-z_.]+)""\),")
            .Select(m => (Baslik: m.Groups[1].Value, Ekran: (string?)m.Groups[2].Value)).ToList();
        Assert.Equal(3, masa.Count);
        Assert.All(masa, h => Assert.NotNull(h.Ekran));
        Assert.Equal(masa, web);
        Assert.All(web, h => Assert.True(AppScreens.ByKey(h.Ekran!) is { OnWeb: true, OnDesktop: true }, h.Ekran + " iki ortamda da olmalı"));
    }

    [Fact]
    public void HP2_Web_Menu_Ile_Ayni_Erisim()
    {
        var s = Oku("src/DepoWise.Web/Components/Pages/Home.razor");
        Assert.Contains("Auth.CanView(e.WebPermKey) && Auth.PlatformOpen(e.Key)", s);
        Assert.Contains("Auth.PlatformOpen(sc.Key)", Oku("src/DepoWise.Web/Components/Layout/NavMenu.razor"));
    }
}
