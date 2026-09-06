using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ KULLANICI İLETİŞİM ALANLARI — DOĞRULAMA ═══ (kullanıcı isteği 2026-09-06)
///
/// Kural TEK yerdedir ve hem sunucu (<c>UserService.UpdateProfile</c>) hem iki arayüz onu çağırır.
/// Buradaki bir gerileme ya geçerli adresleri reddeder (kullanıcı alanı dolduramaz) ya da bariz
/// yazım hatalarını kaçırır. Alanlar <b>zorunlu değildir</b> — boş bırakmak geçerli olmalıdır.
/// </summary>
public class IletisimDogrulamaTests
{
    // ── E-posta ──────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Eposta_BosBirakilabilir(string? s)
    {
        Assert.True(IletisimDogrulama.EpostaGecerli(s));
        Assert.Null(IletisimDogrulama.EpostaHatasi(s));
    }

    [Theory]
    [InlineData("ad@firma.com")]
    [InlineData("mustafa.alpaslan@gazinsaat.com.tr")]
    [InlineData("depo_1@firma.co")]
    [InlineData("a@b.io")]
    [InlineData("isim+etiket@firma.com")]
    public void Eposta_GecerliOrnekler(string s)
        => Assert.True(IletisimDogrulama.EpostaGecerli(s));

    [Theory]
    [InlineData("adfirma.com")]        // @ yok
    [InlineData("ad@@firma.com")]      // iki @
    [InlineData("@firma.com")]         // @ öncesi boş
    [InlineData("ad@firma")]           // nokta yok
    [InlineData("ad@firma.")]          // uzantı yok
    [InlineData("ad@firma.c")]         // uzantı tek harf
    [InlineData("ad@firma.123")]       // uzantı harf değil
    [InlineData("ad @firma.com")]      // boşluk
    public void Eposta_GecersizOrnekler(string s)
    {
        Assert.False(IletisimDogrulama.EpostaGecerli(s));
        Assert.NotNull(IletisimDogrulama.EpostaHatasi(s));
    }

    [Fact]
    public void Eposta_BastakiSondakiBosluklarKirpilir()
        => Assert.True(IletisimDogrulama.EpostaGecerli("  ad@firma.com  "));

    // ── Telefon ──────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Telefon_BosBirakilabilir(string? s)
    {
        Assert.True(IletisimDogrulama.TelefonGecerli(s));
        Assert.Null(IletisimDogrulama.TelefonHatasi(s));
    }

    /// <summary>Kullanıcılar numarayı çok farklı yazar; hepsi kabul edilmeli.</summary>
    [Theory]
    [InlineData("05001112233")]
    [InlineData("0500 111 22 33")]
    [InlineData("+90 500 111 22 33")]
    [InlineData("(0500) 111-22-33")]
    [InlineData("0212 555 44 33")]
    [InlineData("500 111 22 33")]        // baştaki 0 olmadan 10 rakam
    public void Telefon_GecerliYazimlar(string s)
        => Assert.True(IletisimDogrulama.TelefonGecerli(s));

    [Theory]
    [InlineData("123")]                  // çok kısa
    [InlineData("111 22 33")]            // 7 rakam
    [InlineData("0500111223344556")]     // 16 rakam — E.164 üstü
    [InlineData("0500 abc 22 33")]       // harf içeriyor
    public void Telefon_GecersizYazimlar(string s)
    {
        Assert.False(IletisimDogrulama.TelefonGecerli(s));
        Assert.NotNull(IletisimDogrulama.TelefonHatasi(s));
    }

    /// <summary>Hata mesajları kullanıcıya gösterilir: Türkçe ve örnekli olmalı.</summary>
    [Fact]
    public void HataMesajlari_TurkceVeOrnekli()
    {
        Assert.Contains("ad@firma.com", IletisimDogrulama.EpostaHatasi("bozuk"));
        Assert.Contains("0500", IletisimDogrulama.TelefonHatasi("123"));
    }
}
