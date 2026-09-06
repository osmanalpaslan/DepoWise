using System;
using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ TARİH KUTUSU MANTIĞI ═══ (kullanıcı isteği 2026-09-06 — kompakt tarih alanı)
///
/// Bu testler <c>TarihKutusu</c> denetiminin ARKASINDAKİ mantığı korur. Denetim 25 ekranda
/// 43 alanda kullanılıyor; buradaki bir gerileme (regression) tüm tarih alanlarını aynı anda
/// bozar. En kritik madde <b>gerçek takvim doğrulamasıdır</b>: kullanıcının 31.02 gibi var
/// olmayan bir güne kayıt yazması engellenmelidir.
/// </summary>
public class TarihMetniTests
{
    // ── Biçimleme ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Bicimle_TarihYoksa_BosMetin()
        => Assert.Equal("", TarihMetni.Bicimle(null));

    [Fact]
    public void Bicimle_TekHaneliGunVeAy_BasindaSifirlaYazilir()
    {
        var t = new DateTimeOffset(new DateTime(2026, 2, 7), TimeSpan.Zero);
        Assert.Equal("07.02.2026", TarihMetni.Bicimle(t));
    }

    // ── Maskeleme (kullanıcı yalnız rakam yazar) ─────────────────────────────────────────
    [Theory]
    [InlineData("", "")]
    [InlineData("0", "0")]
    [InlineData("06", "06")]
    [InlineData("069", "06.9")]
    [InlineData("0609", "06.09")]
    [InlineData("06092", "06.09.2")]
    [InlineData("06092026", "06.09.2026")]
    public void Maskele_RakamlariNoktalarlaAyirir(string ham, string beklenen)
        => Assert.Equal(beklenen, TarihMetni.Maskele(ham));

    [Fact]
    public void Maskele_ZatenNoktaliMetinAyniKalir()
        => Assert.Equal("06.09.2026", TarihMetni.Maskele("06.09.2026"));

    [Fact]
    public void Maskele_SekizRakamdanFazlasiYokSayilir()
        => Assert.Equal("06.09.2026", TarihMetni.Maskele("0609202699"));

    [Fact]
    public void Maskele_HarfVeSimgeleriAtar()
        => Assert.Equal("06.09.2026", TarihMetni.Maskele("06/ab09-2026"));

    // ── Çözme ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Coz_BosMetin_BasariliVeTarihYok()
    {
        Assert.True(TarihMetni.Coz("", out var t));
        Assert.Null(t);
    }

    [Fact]
    public void Coz_SadeceBosluk_BasariliVeTarihYok()
    {
        Assert.True(TarihMetni.Coz("   ", out var t));
        Assert.Null(t);
    }

    [Fact]
    public void Coz_GecerliTarih_DogruDegerVeSaatSifir()
    {
        Assert.True(TarihMetni.Coz("06.09.2026", out var t));
        Assert.NotNull(t);
        Assert.Equal(new DateTime(2026, 9, 6), t!.Value.DateTime);
        Assert.Equal(TimeSpan.Zero, t.Value.Offset);
        Assert.Equal(TimeSpan.Zero, t.Value.TimeOfDay);
    }

    /// <summary>ASIL KORUMA: takvimde OLMAYAN günler reddedilir (CLAUDE.md §5).</summary>
    [Theory]
    [InlineData("31.02.2026")]   // Şubat 31 çekmez
    [InlineData("30.02.2024")]   // artık yılda bile Şubat 30 yok
    [InlineData("31.04.2026")]   // Nisan 30 çeker
    [InlineData("31.06.2026")]   // Haziran 30 çeker
    [InlineData("00.09.2026")]   // sıfırıncı gün yok
    [InlineData("06.00.2026")]   // sıfırıncı ay yok
    [InlineData("06.13.2026")]   // 13. ay yok
    public void Coz_TakvimdeOlmayanGun_Reddedilir(string metin)
    {
        Assert.False(TarihMetni.Coz(metin, out var t));
        Assert.Null(t);
    }

    /// <summary>29 Şubat yalnız artık yılda geçerlidir.</summary>
    [Fact]
    public void Coz_ArtikYilSubat29_Kabul()
        => Assert.True(TarihMetni.Coz("29.02.2024", out _));

    [Fact]
    public void Coz_ArtikOlmayanYilSubat29_Red()
        => Assert.False(TarihMetni.Coz("29.02.2026", out _));

    [Theory]
    [InlineData("6.9.2026")]      // tek haneli — belirsizliğe yol açar, kabul edilmez
    [InlineData("06.09.26")]      // iki haneli yıl
    [InlineData("2026-09-06")]    // farklı biçim
    [InlineData("06092026")]      // maskelenmemiş ham rakam
    [InlineData("abc")]
    [InlineData("06.09")]         // eksik
    public void Coz_BeklenenBicimDisi_Reddedilir(string metin)
    {
        Assert.False(TarihMetni.Coz(metin, out var t));
        Assert.Null(t);
    }

    /// <summary>Maskeleme ile çözme birlikte çalışır: kullanıcı rakam yazar, sonuç geçerli tarihtir.</summary>
    [Fact]
    public void MaskeleVeCoz_BirlikteCalisir()
    {
        var maskeli = TarihMetni.Maskele("29022024");
        Assert.Equal("29.02.2024", maskeli);
        Assert.True(TarihMetni.Coz(maskeli, out var t));
        Assert.Equal(new DateTime(2024, 2, 29), t!.Value.DateTime);
    }

    /// <summary>Gidiş-dönüş: biçimlenen metin yeniden çözülünce aynı günü verir.</summary>
    [Theory]
    [InlineData(2026, 1, 1)]
    [InlineData(2026, 12, 31)]
    [InlineData(2024, 2, 29)]
    [InlineData(1999, 7, 15)]
    public void BicimleSonraCoz_AyniGunuVerir(int yil, int ay, int gun)
    {
        var asil = new DateTimeOffset(new DateTime(yil, ay, gun), TimeSpan.Zero);
        Assert.True(TarihMetni.Coz(TarihMetni.Bicimle(asil), out var geri));
        Assert.Equal(asil, geri);
    }
}
