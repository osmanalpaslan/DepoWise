using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-76 (AR-GE 2026-10-26, F-062) — BELGE KAYMASI + "HAKKINDA" YER TUTUCUSU ═══
///  BH1 — "Hakkında" iki ortamda gerçek içerik (uygulama adı, sürüm, firma, son not, Güncelleme Notları); "yakında" yer tutucusu yok
///  BH2 — eskiyen belgeler arşivde; belge haritası arşivdeki dosyaları "güncel" diye göstermiyor; liste kuralı tek kolon kataloğunu anlatır
/// </summary>
public class BelgeVeHakkindaTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void BH1_Hakkinda_Iki_Ortamda_Gercek_Icerik()
    {
        var ekran = Oku("src/DepoWise.Application/Security/AppScreens.cs");
        Assert.Contains("new AppScreen(\"about\", \"about\", \"Ayarlar\", \"Hakkında\", Both, \"about\", \"about\"", ekran);   // artık soon/about değil
        var web = Oku("src/DepoWise.Web/Components/Pages/About.razor");
        Assert.StartsWith("@page \"/about\"", web);
        var mas = Oku("src/DepoWise.Desktop/Views/AboutView.axaml") + Oku("src/DepoWise.Desktop/ViewModels/AboutViewModel.cs");
        Assert.DoesNotContain("yakında", mas, StringComparison.OrdinalIgnoreCase);
        foreach (var (w, m) in new[] { ("Masaüstü programı", "Program sürümü"), ("Firma", "Firma"), ("Son güncelleme", "Son güncelleme"), ("Güncelleme Notları", "Güncelleme Notları") })
        {
            Assert.Contains(w, web);
            Assert.Contains(m, mas);
        }
        Assert.Contains("NavigateCommand.Execute(\"about:notes\")", mas);
        Assert.Contains("Nav.NavigateTo(\"update-notes\")", web);
    }

    [Fact]
    public void BH2_Eskiyen_Belgeler_Arsivde_Harita_Guncel()
    {
        foreach (var ad in new[] { "UI_DESIGN_SPEC.md", "TEST_LISTESI.md", "TEST_ORTAMI.md" })
        {
            Assert.False(File.Exists(Path.Combine(Kok(), "docs", ad)), ad + " güncel klasörde kalmamalı");
            Assert.True(File.Exists(Path.Combine(Kok(), "docs", "arsiv", "kok", ad)), ad + " arşivde olmalı");
        }
        var harita = Oku("docs/README.md");
        var guncel = harita[..harita.IndexOf("## Arşiv", StringComparison.Ordinal)];
        Assert.DoesNotContain("UI_DESIGN_SPEC.md", guncel);
        Assert.DoesNotContain("TEST_ORTAMI.md", guncel);
        Assert.Contains("arge/08_avalonia_tasarim_sistemi.md", guncel);
        Assert.Contains(".claude/rules/testing.md", guncel);
        var kural = Oku(".claude/rules/list-screens.md");
        Assert.DoesNotContain("AYNASI `DepoWise.Web/Services/ListColumns.cs`", kural);
        Assert.False(File.Exists(Path.Combine(Kok(), "src/DepoWise.Web/Services/ListColumns.cs")));   // tek katalog: bağlı dosya
    }
}
