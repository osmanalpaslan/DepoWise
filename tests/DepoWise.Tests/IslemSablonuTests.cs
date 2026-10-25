using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-63 (AR-GE 2026-10-25, F-128) — İŞLEM GİRİŞİ ŞABLONU (Şablon C) — WEB TARAFI KİLİDİ ═══
/// Masaüstü düzeni arayüz testleriyle (UIT23…) sınanır; web için yerel görsel test olmadığından kaynak sözleşmesi:
///  IS1 — Sayım: eylemler ALTTA sabit çubukta (dw-islem-alt), "Taslak Kaydet … Sayımı Tamamla"; kör sayımda fark sayısı gizli
/// </summary>
public class IslemSablonuTests
{
    private static string Oku(string rel)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return File.ReadAllText(Path.Combine(d!.FullName, rel));
    }

    [Fact]
    public void IS1_Web_Sayim_Alt_Eylem_Cubugu()
    {
        var s = Oku("src/DepoWise.Web/Components/Pages/StockCount.razor");
        var alt = s.IndexOf("Class=\"pa-3 mt-3 dw-islem-alt\"", StringComparison.Ordinal);
        Assert.True(alt > 0, "alt eylem çubuğu yok");
        var tablo = s.IndexOf("<MudTable Items=\"_rows\"", StringComparison.Ordinal);
        Assert.True(tablo > 0 && tablo < alt, "eylem çubuğu tablonun ALTINDA olmalı");
        var cubuk = s[alt..s.IndexOf("</MudPaper>", alt, StringComparison.Ordinal)];
        var taslak = cubuk.IndexOf("OnClick=\"TaslakKaydet\"", StringComparison.Ordinal);
        var kaydet = cubuk.IndexOf("OnClick=\"Save\"", StringComparison.Ordinal);
        Assert.True(taslak > 0 && kaydet > taslak, "Taslak Kaydet … Sayımı Tamamla (birincil en sağda)");
        Assert.Equal(1, s.Split("OnClick=\"Save\"").Length - 1);   // üstte ikinci bir Kaydet kalmadı
        Assert.Contains(": _kor ? $\"{_rows.Count} kalem listede\"", s);   // kör sayımda fark sayısı yok
        Assert.Contains(".dw-islem-alt { position: sticky; bottom: 0;", Oku("src/DepoWise.Web/wwwroot/app.css"));
    }
}
