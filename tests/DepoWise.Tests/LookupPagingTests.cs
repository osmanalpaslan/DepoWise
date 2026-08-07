using System.Collections.Generic;
using System.Linq;
using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Lookup açılır liste çekirdeği (Prompt 1, 2026-08-08): 25'lik sayfalama + Türkçe-doğru "içerir".</summary>
public class LookupPagingTests
{
    private static List<string> Items(int n) => Enumerable.Range(1, n).Select(i => "Kayit " + i).ToList();
    private static LookupPaging.Result<string> Run(List<string> all, string? search, int page, int size = 25)
        => LookupPaging.Apply(all, x => x, search, page, size);

    [Fact]
    public void AramaYok_IlkSayfa25Kayit()
    {
        var r = Run(Items(60), null, 1);
        Assert.Equal(25, r.Items.Count);
        Assert.Equal(3, r.TotalPages);   // 60 → 25/25/10
        Assert.Equal(60, r.TotalCount);
        Assert.Equal("Kayit 1", r.Items[0]);
    }

    [Fact]
    public void IkinciSayfa_DogruDilim()
    {
        var r = Run(Items(60), null, 2);
        Assert.Equal("Kayit 26", r.Items[0]);
        Assert.Equal(25, r.Items.Count);
    }

    [Fact]
    public void UcuncuSayfa_KalanKayitlar()
    {
        var r = Run(Items(60), null, 3);
        Assert.Equal(10, r.Items.Count);
        Assert.Equal("Kayit 51", r.Items[0]);
    }

    [Fact]
    public void SayfaTasarsa_SonGecerliSayfayaCekilir()
    {
        var r = Run(Items(60), null, 99);
        Assert.Equal(3, r.Page);
        Assert.Equal(10, r.Items.Count);
    }

    [Fact]
    public void Arama_25Sinirini_Korur_VeSayfalar()
    {
        // 100 kaydın hepsi "Kayit" içerir → filtre sonrası yine 100, sayfa başına 25.
        var r = Run(Items(100), "kayit", 1);
        Assert.Equal(25, r.Items.Count);
        Assert.Equal(4, r.TotalPages);
        Assert.Equal(100, r.TotalCount);
    }

    [Fact]
    public void Arama_TurkceDuyarli()
    {
        var all = new List<string> { "İSTANBUL", "izmir", "Ankara" };
        // "istanbul" araması İSTANBUL'u tr-TR ile yakalar (Ordinal yakalayamazdı).
        var r = Run(all, "istanbul", 1);
        Assert.Single(r.Items);
        Assert.Equal("İSTANBUL", r.Items[0]);
    }

    [Fact]
    public void BosListe_TekSayfa_BosItems()
    {
        var r = Run(new List<string>(), null, 1);
        Assert.Empty(r.Items);
        Assert.Equal(1, r.TotalPages);
        Assert.Equal(0, r.TotalCount);
    }
}
