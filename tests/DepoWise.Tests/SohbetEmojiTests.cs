using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-30 — SOHBET EMOJİLERİ (kullanıcı isteği "mesaj kısmına emoji ve global emoji kısayolları"). Kural TEK kaynakta
/// (<see cref="EmojiKatalogu"/>); masaüstü ve web aynı dosyayı kullanır.
/// </summary>
public class SohbetEmojiTests
{
    [Theory]
    [InlineData("selam :) ", "selam 🙂 ")]
    [InlineData(":D ", "😀 ")]
    [InlineData("seni <3 ", "seni ❤️ ")]
    [InlineData("(y) tamam", "👍 tamam")]
    [InlineData(":kamyon: geldi", "🚚 geldi")]
    [InlineData(":uyarı: dikkat", "⚠️ dikkat")]     // Türkçe harfli
    [InlineData(":uyari: dikkat", "⚠️ dikkat")]     // harfsiz
    [InlineData(":TAMAM: ", "✅ ")]                  // büyük harf
    [InlineData(":+1: ", "👍 ")]
    [InlineData(":ok: ", "👌 ")]
    public void EMJ1_Biten_Kisayol_Yazarken_Doner(string yazilan, string beklenen)
        => Assert.Equal(beklenen, EmojiKatalogu.Donustur(yazilan, sonDahil: false));

    /// <summary>Yazarken SONDAKİ, henüz bitmemiş sözcük dönmez (kullanıcı ":))" yazıyor olabilir); gönderirken döner.</summary>
    [Fact]
    public void EMJ2_Sondaki_Kisayol_Yalniz_Gonderirken_Doner()
    {
        Assert.Equal("tamam :)", EmojiKatalogu.Donustur("tamam :)", sonDahil: false));
        Assert.Equal("tamam 🙂", EmojiKatalogu.Donustur("tamam :)", sonDahil: true));
    }

    /// <summary>Tek başına sözcük değilse ya da bilinmiyorsa dokunulmaz: adres, saat, kelime içi, bilinmeyen ad.</summary>
    [Theory]
    [InlineData("https://depowise-web.fly.dev/ adresi")]
    [InlineData("saat 10:30 toplantı")]
    [InlineData("a:)b ve x<3y")]
    [InlineData(":bilinmeyen: ad")]
    [InlineData("oran 3:1 oldu")]
    public void EMJ3_Kisayol_Olmayan_Metin_Bozulmaz(string metin)
        => Assert.Equal(metin, EmojiKatalogu.Donustur(metin, sonDahil: true));

    /// <summary>Liste sağlam: emoji ve ad tekrarı yok, hiçbir kısayol iki farklı emojiye gitmez, her öğenin ad kısayolu çalışır.</summary>
    [Fact]
    public void EMJ4_Liste_Tutarli()
    {
        var tumu = EmojiKatalogu.Tumu.ToList();
        Assert.True(tumu.Count >= 80, "liste beklenenden kısa: " + tumu.Count);
        Assert.Equal(tumu.Count, tumu.Select(o => o.Emoji).Distinct().Count());
        Assert.Equal(tumu.Count, tumu.Select(o => EmojiKatalogu.Sade(o.Ad)).Distinct().Count());
        foreach (var o in tumu)
        {
            Assert.Equal(o.Emoji, EmojiKatalogu.KisayolKarsiligi(":" + o.Ad + ":"));
            foreach (var k in o.Kisayollar)
                Assert.Equal(o.Emoji, EmojiKatalogu.KisayolKarsiligi(k));
        }
    }

    [Fact]
    public void EMJ5_Arama()
    {
        Assert.Equal(EmojiKatalogu.Kategoriler.Count, EmojiKatalogu.Ara("").Count);
        Assert.Contains(EmojiKatalogu.Ara("kamyon").Single().Ogeler, o => o.Emoji == "🚚");
        Assert.Contains(EmojiKatalogu.Ara("UYARI").Single().Ogeler, o => o.Emoji == "⚠️");
        Assert.Contains(EmojiKatalogu.Ara("<3").Single().Ogeler, o => o.Emoji == "❤️");
        Assert.Empty(EmojiKatalogu.Ara("zzzyok"));
    }

    /// <summary>İki ortam aynı kuralı kullanır; web'de "son kullanılanlar" OTURUMA özeldir (static değil — tek süreç tüm kullanıcılara hizmet eder).</summary>
    [Fact]
    public void EMJ6_Masaustu_Ve_Web_Ayni_Kaynak()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        var web = File.ReadAllText(Path.Combine(kok!.FullName, "src", "DepoWise.Web", "Components", "Layout", "ChatDock.razor"));
        var masa = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Desktop", "ViewModels", "ChatViewModel.cs"));
        var csproj = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Web", "DepoWise.Web.csproj"));
        Assert.Contains(@"<Compile Include=""..\DepoWise.Application\Ui\EmojiKatalogu.cs""", csproj);
        foreach (var kaynak in new[] { web, masa })
        {
            Assert.Contains("EmojiKatalogu.Donustur(", kaynak);
            Assert.Contains("sonDahil: true", kaynak);
            Assert.Contains("sonDahil: false", kaynak);
        }
        Assert.Contains("private readonly List<DepoWise.Application.Ui.EmojiOgesi> _sonEmojiler", web);
        Assert.DoesNotContain("static readonly List<DepoWise.Application.Ui.EmojiOgesi>", web);
    }
}
