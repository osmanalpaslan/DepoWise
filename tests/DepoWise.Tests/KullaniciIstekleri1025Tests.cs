using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ KULLANICI İSTEKLERİ 2026-10-25 (küçük görünüm istekleri) — KAYNAK KİLİDİ ═══
///  KI1 — Web'de girişten önceki açılış animasyonu YOK (masaüstü değişmedi): çağrı, işlev, stil ve video dosyası kaldırıldı
///  KI2 — Menü ekran adları belirgin (tam opak, ana metin rengi, büyük) + üzerine gelince NET vurgu (kehribar zemin, kayma) — iki ortam;
///        web kuralları mobil katmanın (§18) DIŞINDA
///  KI3 — "Listeye Ekle" düğmeleri kehribar (dolu) — iki ortam
/// </summary>
public class KullaniciIstekleri1025Tests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void KI1_Web_Acilis_Animasyonu_Yok()
    {
        Assert.DoesNotContain("dwAcilisOynat", Oku("src/DepoWise.Web/Components/Pages/Login.razor"));
        Assert.DoesNotContain("dwAcilisOynat", Oku("src/DepoWise.Web/Components/App.razor"));
        Assert.DoesNotContain(".dw-acilis", Oku("src/DepoWise.Web/wwwroot/app.css"));
        Assert.False(File.Exists(Path.Combine(Kok(), "src/DepoWise.Web/wwwroot/acilis.webm")));
    }

    [Fact]
    public void KI2_Menu_Okunakli_Ve_Hover_Vurgusu()
    {
        var m = Oku("src/DepoWise.Desktop/App.axaml");
        var link = m.IndexOf("<Style Selector=\"Button.NavLink\">", StringComparison.Ordinal);
        var linkSon = m.IndexOf("</Style>", link, StringComparison.Ordinal);
        var blok = m[link..linkSon];
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"1\"/>", blok);
        Assert.Contains("<Setter Property=\"FontSize\" Value=\"13.5\"/>", blok);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource TextPrimaryBrush}\"/>", blok);
        var hover = m.IndexOf("<Style Selector=\"Button.NavLink:pointerover /template/ ContentPresenter\">", StringComparison.Ordinal);
        Assert.Contains("AccentSoftBrush", m[hover..m.IndexOf("</Style>", hover, StringComparison.Ordinal)]);
        Assert.Contains("<Setter Property=\"RenderTransform\" Value=\"translateX(3px)\"/>", m);

        var css = Oku("src/DepoWise.Web/wwwroot/app.css");
        var kural = css.IndexOf(".mud-navmenu .mud-nav-link:hover:not(.active):not(.mud-nav-link-active)", StringComparison.Ordinal);
        Assert.True(kural > 0, "web menü hover kuralı yok");
        Assert.True(kural < css.IndexOf("18) MOB-W", StringComparison.Ordinal), "hover kuralı mobil katmanın DIŞINDA (önünde) olmalı");
        Assert.Contains("translateX(3px)", css[kural..(kural + 400)]);
    }

    [Fact]
    public void KI3_Listeye_Ekle_Kehribar()
    {
        foreach (var v in new[] { "AssignmentsView", "StockCountView", "StockEntryView" })
            Assert.Contains("<Button Classes=\"Primary ListeyeEkle\" Content=\"+ Listeye Ekle\"", Oku($"src/DepoWise.Desktop/Views/{v}.axaml"));
        foreach (var w in new[] { "Assignments", "Stock" })
        {
            var s = Oku($"src/DepoWise.Web/Components/Pages/{w}.razor");
            Assert.Contains("Variant=\"Variant.Filled\" Color=\"Color.Warning\" Class=\"dw-listeye-ekle\"", s);
        }
    }
}
