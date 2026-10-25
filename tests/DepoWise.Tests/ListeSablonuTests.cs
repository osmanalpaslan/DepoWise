using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-64 (AR-GE 2026-10-25, F-129) — LİSTE ŞABLONU (Şablon B) — WEB TARAFI KİLİDİ ═══
/// Masaüstü düzeni arayüz testleriyle (UIT27…) sınanır; web için yerel görsel test olmadığından kaynak sözleşmesi:
///  - sayfalama listenin HEMEN ALTINDA ortak ListePager ile (sayfaya özel "◀ Önceki / Sonraki ▶" yok)
///  - ekranda TEK dolu (Filled) ana düğme var; seçili kayda ait eylemler ikincil
/// </summary>
public class ListeSablonuTests
{
    private static string Oku(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    private static void SablonB(string rel, string anaDugme)
    {
        var s = Oku(rel);
        var tablo = s.IndexOf("</MudTable>", StringComparison.Ordinal);
        var pager = s.IndexOf("<ListePager ", StringComparison.Ordinal);
        Assert.True(tablo > 0 && pager > tablo, rel + ": ListePager tablonun ALTINDA olmalı");
        Assert.Equal(1, s.Split("<ListePager ").Length - 1);
        Assert.DoesNotContain("◀ Önceki", s);
        Assert.DoesNotContain("Sonraki ▶", s);
        // Dolu düğmelerin yazısı (etiket içindeki "=>" yüzünden düzenli ifade yerine: son '>' sonrası metin).
        // "@(...)" ile başlayanlar form içi Kaydet düğmeleridir (form açıkken tek eylem) — sayılmaz.
        var dolu = Regex.Matches(s, "<MudButton\\b(?<g>.*?)</MudButton>", RegexOptions.Singleline)
            .Select(m => m.Groups["g"].Value)
            .Where(g => g.Contains("Variant=\"Variant.Filled\""))
            .Select(g => g[(g.LastIndexOf('>') + 1)..].Trim())
            .Where(m => !m.StartsWith('@')).ToArray();
        Assert.Equal(new[] { anaDugme }, dolu);
    }

    /// <summary>LS1 — Cari (web): ortak sayfalayıcı + tek ana düğme "Yeni Cari".</summary>
    [Fact]
    public void LS1_Web_Cari_Liste_Sablonu() => SablonB("src/DepoWise.Web/Components/Pages/Parties.razor", "Yeni Cari");

    /// <summary>LS0 — Web ListePager özeti masaüstü Pager.Ozet ile aynı biçimi üretir ("51–100 / 1.240", boşsa "0 kayıt").</summary>
    [Fact]
    public void LS0_Web_Pager_Ozet_Bicimi()
    {
        var s = Oku("src/DepoWise.Web/Components/ListePager.razor");
        Assert.Contains("return \"0 kayıt\";", s);
        Assert.Contains("var bas = (Math.Max(1, sayfa) - 1) * boyut + 1;", s);
        Assert.Contains("Math.Min(toplam, bas + boyut - 1)", s);
        Assert.Contains("Disabled=\"@(Page <= 1 || Busy)\"", s);
        Assert.Contains("Disabled=\"@(Page >= SayfaSayisi(Total, PageSize) || Busy)\"", s);
    }
}
