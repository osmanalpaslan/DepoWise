using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ WEB — SÜTUN DARALTMA/GENİŞLETME ═══ (kullanıcı bildirimi 2026-08-26)
///
/// <b>KULLANICININ GÖRDÜĞÜ.</b> "Web sitesinde hiçbir tablonun sütununda daraltma ve genişletme
/// yapamıyorum."
///
/// <b>KÖK NEDEN (tarayıcıda ölçülerek bulundu).</b> Sürükleme tutamağı ve JS <b>çalışıyordu</b> —
/// sürükleyince <c>&lt;th&gt;</c>'in satır içi stili gerçekten <c>width: 240px</c> oluyordu. Ama
/// hücrelerde metni "…" ile kesmek için kullanılan
/// <c>.dw-grid th, .dw-grid td { max-width: 0 }</c> kuralı bu genişliği <b>sıfıra kırpıyordu</b>.
/// Tarayıcı da kolon genişliklerini tümden yok sayıp tabloyu eşit bölüyordu: 8 kolon × 140 px
/// isteniyor, tablo 960 px'e sıkışıyor, her kolon 120 px oluyordu. Yani tutamak sürükleniyor,
/// ekranda <b>hiçbir şey değişmiyordu</b>.
///
/// <b>ÇÖZÜM.</b> Kolon genişliği artık hücreden değil <c>&lt;colgroup&gt;&lt;col&gt;</c>'dan gelir —
/// <c>&lt;col&gt;</c> genişliği <c>max-width</c>'ten etkilenmez ve sabit (fixed) tablo düzeninde
/// kolonun TEK belirleyicisidir. Ayrıca tablodaki <c>min-width: 100%</c> kaldırıldı: o kural tabloyu
/// daima kapsayıcıya yaydığı için genişlikler orantılı ölçekleniyor ve bir kolonu değiştirmek
/// diğerlerini de oynatıyordu.
///
/// <b>Tarayıcıda doğrulandı:</b> genişletme 140→320, daraltma 140→80, diğer kolonlar etkilenmiyor,
/// uzun değer kolonu şişirmiyor ("…" ile kesiliyor), tablo taşınca yatay kaydırma çalışıyor.
/// </summary>
public class WebKolonGenisligiTests
{
    /// <summary>Sütun genişliği ayarlanabilen web tabloları.</summary>
    public static TheoryData<string> Tablolar() => new()
    {
        "Components/Pages/Materials.razor",
        "Components/Pages/Vehicles.razor",
        "Components/Pages/Daily.razor",
        "Components/DwDataGrid.razor",
    };

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Web(string göreli)
        => File.ReadAllText(Path.Combine(RepoKok(), "src", "DepoWise.Web", göreli.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>⭐ Kolon genişliği <c>&lt;col&gt;</c>'dan gelmeli. Hücreye yazılan genişlik,
    /// <c>max-width: 0</c> yüzünden sessizce yok sayılır.</summary>
    [Theory]
    [MemberData(nameof(Tablolar))]
    public void WKG1_Tablolarda_Colgroup_Var(string dosya)
    {
        var x = Web(dosya);

        var tabloSayisi = Regex.Matches(x, @"<table[^>]*class=""dw-grid""").Count;
        var colgroupSayisi = Regex.Matches(x, @"<colgroup>").Count;

        Assert.True(tabloSayisi > 0, $"{dosya}: dw-grid tablosu bulunamadı.");
        Assert.Equal(tabloSayisi, colgroupSayisi);   // HER tabloda kolon tanımı olmalı
    }

    /// <summary>⭐ Tabloyu kapsayıcıya yayan <c>min-width: 100%</c> geri gelmemeli — kolon
    /// genişliklerini orantılı ölçekler ve tek kolonu değiştirmek hepsini oynatır.</summary>
    [Fact]
    public void WKG2_Tablo_Kapsayiciya_Zorla_Yayilmaz()
    {
        var css = Web("wwwroot/app.css");
        var kural = Regex.Match(css, @"\.dw-grid \{[^}]*\}", RegexOptions.Singleline);

        Assert.True(kural.Success, ".dw-grid kuralı bulunamadı.");
        // Yorumlar elenir: kuralın İÇİNDEKİ açıklama, kaldırılan kuralın adını anlatmak için
        // "min-width: 100%" yazısını içeriyor — bu bir bildirim değil, açıklamadır.
        var bildirimler = Regex.Replace(kural.Value, @"/\*.*?\*/", "", RegexOptions.Singleline);

        Assert.DoesNotContain("min-width: 100%", bildirimler);
        Assert.Contains("table-layout: fixed", bildirimler);
    }

    /// <summary>Metni "…" ile kesen kural KORUNDU — kaldırılırsa uzun değer kolonu şişirir
    /// (kullanıcının masaüstünde şikâyet ettiği taşmanın web karşılığı).</summary>
    [Fact]
    public void WKG3_Uzun_Deger_Kesme_Kurali_Korundu()
    {
        var css = Web("wwwroot/app.css");
        var kural = Regex.Match(css, @"\.dw-grid th, \.dw-grid td \{[^}]*\}", RegexOptions.Singleline);

        Assert.True(kural.Success);
        Assert.Contains("max-width: 0", kural.Value);
        Assert.Contains("text-overflow: ellipsis", kural.Value);
        Assert.Contains("overflow: hidden", kural.Value);
    }

    /// <summary>⭐ Sürükleme JS'i genişliği <c>&lt;col&gt;</c>'a yazmalı; <c>&lt;th&gt;</c>'e yazarsa
    /// eski hata (hiçbir şey olmuyor) geri gelir.</summary>
    [Fact]
    public void WKG4_Surukleme_Genisligi_Col_Ogesine_Yazar()
    {
        var app = Web("Components/App.razor");

        Assert.Contains("col.style.width =", app);
        Assert.DoesNotContain("th.style.width =", app);
        Assert.Contains("querySelectorAll('col')", app);   // başlığın sırasına karşılık gelen <col>
    }

    /// <summary>Tutamak hâlâ her başlıkta üretiliyor (yoksa sürüklenecek bir şey kalmaz).</summary>
    [Theory]
    [MemberData(nameof(Tablolar))]
    public void WKG5_Surukleme_Tutamagi_Duruyor(string dosya)
    {
        Assert.Contains("dw-col-grip", Web(dosya));
    }
}
