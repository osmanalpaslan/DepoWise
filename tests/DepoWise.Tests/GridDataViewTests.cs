using System.Collections.Generic;
using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Ortak tablo (Birim 4) İSTEMCİ TARAFI filtre + sıralama çekirdeği — Excel-benzeri davranış.
/// Bu mantık masaüstü GridController + web DwDataGrid'de AYNIDIR (web aynası); burada tek yerde doğrulanır.</summary>
public class GridDataViewTests
{
    private static readonly IReadOnlyList<ListColumn> Cols = new[]
    {
        new ListColumn("ad", "Ad", false),
        new ListColumn("sube", "Şube", false),
        new ListColumn("stok", "Stok", true),
    };

    private static IReadOnlyList<IReadOnlyList<string>> Rows() => new IReadOnlyList<string>[]
    {
        new[] { "Filtre", "Merkez", "5" },
        new[] { "Yağ", "Şantiye A", "15" },
        new[] { "Conta", "Merkez", "50" },
        new[] { "Bijon", "Şantiye B", "0" },
        new[] { "Amortisör", "Merkez", "" },   // stok yok (sayısal filtre elemeli)
    };

    private static List<IReadOnlyList<string>> Run(Dictionary<string, string> filters, string? sort = null, bool desc = false)
        => GridDataView.Compute(Cols, Rows(), filters, sort, desc);

    [Fact]
    public void MetinFiltre_IcerirVeBuyukKucukDuyarsiz()
    {
        var r = Run(new() { ["ad"] = "co" });   // "Conta" — büyük/küçük duyarsız
        Assert.Single(r);
        Assert.Equal("Conta", r[0][0]);
    }

    [Fact]
    public void SayisalFiltre_TamEslesme_15i25i50yiYakalamaz()
    {
        // Kullanıcının klasik derdi: "sadece 5 olanlar" — "içerir" 15/50'yi de yakalardı; sayısal tam eşleşme yakalamaz.
        var r = Run(new() { ["stok"] = "5" });
        Assert.Single(r);
        Assert.Equal("Filtre", r[0][0]);
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
    public void SayisalFiltre_BosHucreyiEler()
    {
        // "Amortisör" stok boş → herhangi bir sayısal filtrede ELENİR (metin filtresinde değil).
        Assert.DoesNotContain(Run(new() { ["stok"] = ">=0" }), row => row[0] == "Amortisör");
        Assert.Contains(Run(new() { ["ad"] = "amor" }), row => row[0] == "Amortisör");
    }

    [Fact]
    public void CokluFiltre_VE_MantigiyleBirlesir()
    {
        var r = Run(new() { ["sube"] = "Merkez", ["stok"] = ">=50" });
        Assert.Single(r);
        Assert.Equal("Conta", r[0][0]);
    }

    [Fact]
    public void Siralama_SayisalKolon_ArtanVeAzalan()
    {
        var asc = Run(new(), sort: "stok", desc: false);   // boş hariç sayısal: 0,5,15,50 (boş "0" gibi mi? TryNum boşta false → string karşılaştırma değil, sayısal)
        // Sayısal kolonda boş hücre TryNum=false → 0 sayılır (value=0). Sıra: 0(boş), 0, 5, 15, 50 — ilk ikisi 0.
        Assert.Equal("50", asc[^1][2]);
        var descRows = Run(new(), sort: "stok", desc: true);
        Assert.Equal("50", descRows[0][2]);
    }

    [Fact]
    public void Siralama_MetinKolon_KulturDuyarli()
    {
        var r = Run(new(), sort: "ad", desc: false);
        Assert.Equal("Amortisör", r[0][0]);   // alfabetik ilk
    }

    [Fact]
    public void BosFiltre_TumSatirlariGecirir()
    {
        Assert.Equal(5, Run(new()).Count);
        Assert.Equal(5, Run(new() { ["ad"] = "  " }).Count);   // yalnız boşluk = filtre yok
    }

    [Theory]
    [InlineData("5", "5", true, true)]
    [InlineData("15", "5", true, false)]     // tam eşleşme: 15 ≠ 5
    [InlineData("Merkez", "merk", false, true)]
    [InlineData("1.234,50", ">1000", true, true)]   // TR binlik/ondalık ayrıştırma
    public void Match_CesitliDurumlar(string cell, string filter, bool numeric, bool expected)
        => Assert.Equal(expected, GridDataView.Match(cell, filter, numeric));
}
