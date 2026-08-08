using System.Collections.Generic;
using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Ortak tablo (Birim 4 + 2026-08-08) İSTEMCİ TARAFI filtre + sıralama çekirdeği — GridCell (görüntü Text +
/// HAM Num) üzerinden. Sayısal davranış HAM değere dayanır; biçimli görüntü ("₺ 12.345,67") sıralama/filtreyi
/// BOZMAZ. Bu mantık masaüstü GridController + web DwDataGrid'de AYNIDIR (web aynası); burada tek yerde doğrulanır.</summary>
public class GridDataViewTests
{
    private static readonly IReadOnlyList<ListColumn> Cols = new[]
    {
        new ListColumn("ad", "Ad", false),
        new ListColumn("sube", "Şube", false),
        new ListColumn("stok", "Stok", true),
    };

    private static GridCell T(string s) => new(s, null);              // metin hücre
    private static GridCell N(double v, string? disp = null) => new(disp ?? v.ToString(System.Globalization.CultureInfo.InvariantCulture), v);

    // Not: "stok" biçimli GÖRÜNTÜ ile verilir ("₺ 5,00" gibi) ama HAM değer Num'da → sayısal davranış korunur.
    private static IReadOnlyList<IReadOnlyList<GridCell>> Rows() => new IReadOnlyList<GridCell>[]
    {
        new[] { T("Filtre"),    T("Merkez"),    N(5, "₺ 5,00") },
        new[] { T("Yağ"),       T("Şantiye A"), N(15, "₺ 15,00") },
        new[] { T("Conta"),     T("Merkez"),    N(50, "₺ 50,00") },
        new[] { T("Bijon"),     T("Şantiye B"), N(0, "-") },          // değer 0, görüntüde "-"
        new[] { T("Amortisör"), T("Merkez"),    new GridCell("", null) },   // stok yok → Num null (sayısal filtre eler)
    };

    private static List<IReadOnlyList<GridCell>> Run(Dictionary<string, string> filters, string? sort = null, bool desc = false)
        => GridDataView.Compute(Cols, Rows(), filters, sort, desc);

    [Fact]
    public void MetinFiltre_IcerirVeBuyukKucukDuyarsiz()
    {
        var r = Run(new() { ["ad"] = "co" });
        Assert.Single(r);
        Assert.Equal("Conta", r[0][0].Text);
    }

    [Fact]
    public void SayisalFiltre_TamEslesme_BiçimliGorunumeRagmen()
    {
        // Görüntü "₺ 5,00" olsa da HAM değer 5 → "5" tam eşleşmesi yalnız onu bulur (15/50 değil).
        var r = Run(new() { ["stok"] = "5" });
        Assert.Single(r);
        Assert.Equal("Filtre", r[0][0].Text);
    }

    [Fact]
    public void SayisalFiltre_Karsilastirma_VeAralik()
    {
        Assert.Equal(2, Run(new() { ["stok"] = ">10" }).Count);          // 15, 50
        Assert.Equal(3, Run(new() { ["stok"] = "<=15" }).Count);         // 5, 15, 0
        Assert.Equal(2, Run(new() { ["stok"] = "5-15" }).Count);         // 5, 15
        Assert.Single(Run(new() { ["stok"] = ">=50" }));                 // 50
    }

    [Fact]
    public void SayisalFiltre_BosHucreyiEler_BosDegilSifirGecer()
    {
        // Num null (Amortisör) sayısal filtrede ELENİR; değeri 0 olan (Bijon, "-") >=0'da GEÇER (0 kalır).
        var r = Run(new() { ["stok"] = ">=0" });
        Assert.DoesNotContain(r, row => row[0].Text == "Amortisör");
        Assert.Contains(r, row => row[0].Text == "Bijon");
        Assert.Contains(Run(new() { ["ad"] = "amor" }), row => row[0].Text == "Amortisör");
    }

    [Fact]
    public void CokluFiltre_VE_MantigiyleBirlesir()
    {
        var r = Run(new() { ["sube"] = "Merkez", ["stok"] = ">=50" });
        Assert.Single(r);
        Assert.Equal("Conta", r[0][0].Text);
    }

    [Fact]
    public void Siralama_SayisalKolon_HamDegereGore_ArtanVeAzalan()
    {
        var asc = Run(new(), sort: "stok", desc: false);
        Assert.Equal("₺ 50,00", asc[^1][2].Text);   // en büyük sonda (HAM değer sıralaması, görüntü değil)
        var descRows = Run(new(), sort: "stok", desc: true);
        Assert.Equal("₺ 50,00", descRows[0][2].Text);
    }

    [Fact]
    public void Siralama_MetinKolon_KulturDuyarli()
    {
        var r = Run(new(), sort: "ad", desc: false);
        Assert.Equal("Amortisör", r[0][0].Text);
    }

    [Fact]
    public void BosFiltre_TumSatirlariGecirir()
    {
        Assert.Equal(5, Run(new()).Count);
        Assert.Equal(5, Run(new() { ["ad"] = "  " }).Count);
    }

    [Theory]
    [InlineData(5, "5", true, true)]
    [InlineData(15, "5", true, false)]
    [InlineData(1234.5, ">1000", true, true)]
    public void Match_Sayisal_HamDegerUzerinden(double num, string filter, bool numeric, bool expected)
        => Assert.Equal(expected, GridDataView.Match(new GridCell("₺ " + num, num), filter, numeric));

    [Fact]
    public void Match_MetinIcerir()
        => Assert.True(GridDataView.Match(new GridCell("Merkez", null), "merk", false));
}
