using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Ortak seçim alanı davranışı (madde 3, kullanıcı isteği 2026-08-06): arama boşken en fazla
/// MaxUnfiltered kayıt, arama doluyken sınırsız + Türkçe karakter-doğru eşleşme.</summary>
public class SelectionSearchTests
{
    private sealed record Item(string Name);

    [Fact]
    public void AramaBos_30KayittanIlkMaxUnfilteredTanesiDoner()
    {
        var items = Enumerable.Range(1, 30).Select(i => new Item($"Kayıt {i:00}")).ToList();
        var result = SelectionSearch.Apply(items, null, x => x.Name).ToList();
        Assert.Equal(SelectionSearch.MaxUnfiltered, result.Count);
        Assert.Equal(25, SelectionSearch.MaxUnfiltered);
        Assert.Equal("Kayıt 01", result[0].Name);
        Assert.Equal("Kayıt 25", result[^1].Name); // ilk 25, sıra korunur
    }

    [Fact]
    public void AramaBosVeAzKayit_TumunuDoner_SinirlamazZarar()
    {
        var items = new[] { new Item("A"), new Item("B"), new Item("C") };
        var result = SelectionSearch.Apply(items, "", x => x.Name).ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void AramaDoluysa_SinirKalkarTumEslesenlerDoner()
    {
        // 30 kayıt, hepsi "Kayıt" içeriyor -> arama ile 25 sınırı kalkıp 30'u da dönmeli.
        var items = Enumerable.Range(1, 30).Select(i => new Item($"Kayıt {i:00}")).ToList();
        var result = SelectionSearch.Apply(items, "Kayıt", x => x.Name).ToList();
        Assert.Equal(30, result.Count);
    }

    [Fact]
    public void AramaIcerirMantigiyla_KismiEslesmeBulur()
    {
        var items = new[] { new Item("Ankara Şube"), new Item("İstanbul Şube"), new Item("Depo") };
        var result = SelectionSearch.Apply(items, "şube", x => x.Name).ToList();
        Assert.Equal(2, result.Count);
    }

    // Türkçe İ/I/ı/i doğru eşleşme (kanıt: OrdinalIgnoreCase bunu yanlış eşler — bkz. Birim 5).
    [Fact]
    public void TurkceBuyukIile_KucukI_DoguEslesiyor()
    {
        var items = new[] { new Item("İstanbul") };
        var result = SelectionSearch.Apply(items, "istanbul", x => x.Name).ToList();
        Assert.Single(result);
    }

    [Fact]
    public void TurkceBuyukI_ileNoktasizKucukI_DogruEslesiyor()
    {
        var items = new[] { new Item("KIRAÇ Tedarik") };
        var result = SelectionSearch.Apply(items, "kıraç", x => x.Name).ToList();
        Assert.Single(result);
    }

    [Fact]
    public void EslesmeyenArama_BosDoner()
    {
        var items = new[] { new Item("Ankara"), new Item("İzmir") };
        var result = SelectionSearch.Apply(items, "xyz", x => x.Name).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void NullMetinSelector_BosStringGibiDavranir_HataAtmaz()
    {
        var items = new[] { new Item(null!) };
        var result = SelectionSearch.Apply(items, "abc", x => x.Name).ToList();
        Assert.Empty(result); // null ad, "abc" ile eşleşmez ama exception da atmaz
    }
}
