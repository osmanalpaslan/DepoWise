using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MAS-03 — MASAÜSTÜ "MALZEME GİRİŞ-ÇIKIŞ" TABLOSU GÖRÜLEMİYOR ═══ (kullanıcı bildirimi 2026-08-26)
///
/// <b>KULLANICININ GÖRDÜĞÜ.</b> Ekranda kayıt tablosu vardı ama bir "şerit" kadardı; oluşturulan
/// hareketler incelenemiyordu. Web'de aynı kayıtlar görülebiliyordu.
///
/// <b>KÖK NEDEN (kanıtlanmış — veri sorunu DEĞİL).</b> Masaüstü ile web <b>AYNI</b> veriyi,
/// <b>AYNI</b> metottan alıyor: web <c>GET /api/stock</c> → <c>svc.Stock.RecentMovements(s)</c>,
/// masaüstü doğrudan <c>DesktopServices.Stock.RecentMovements(_session)</c> (limit 200, aynı şube
/// kapsamı). Ekran altındaki "N hareket" sayacı da <c>Movements.Count</c>'tan gelir ve kullanıcının
/// ekran görüntüsünde <b>19</b> yazıyordu → koleksiyon DOLUYDU. Sorun tamamen yerleşimdeydi:
/// kök <c>Grid</c> <c>RowDefinitions="Auto,Auto,*,Auto"</c> idi; form <b>Auto</b> satırındaydı ve
/// istediği boyu (44 alan + 130 px arama + 180 px sepet + 44 px not ≈ 700 px) alıyordu, listeye
/// (<c>*</c>) yalnız artan ~50 px kalıyordu.
///
/// <b>SINIR — DÜRÜST BEYAN.</b> Bu projede Avalonia arayüzü otomatize edilemiyor (headless UI
/// koşucusu yok) ve test projesi <c>DepoWise.Desktop</c>'a referans vermiyor. Bu yüzden "piksel
/// ölçtüm" gibi <b>sahte bir GUI testi üretilmedi</b>. Bunun yerine:
/// <list type="number">
///   <item>Yerleşim kararının SAF matematiği <see cref="FormListeOrani"/>'ye taşındı ve burada
///         gerçek sayılarla sınanır (mutasyon bu testleri kırar).</item>
///   <item>Görünüm dosyasının o kararı GERÇEKTEN uyguladığı, XAML üzerinde mimari testlerle
///         doğrulanır (ScrollViewer + oran bağlaması + liste taban yüksekliği kaldırılırsa kırılır).</item>
///   <item>Verinin geldiği, web ile masaüstünün aynı kaynağı kullandığı ayrıca sınanır.</item>
/// </list>
/// </summary>
public class MasaustuFormListeYerlesimTests
{
    private static readonly string GorunumYolu =
        Path.Combine(RepoKok(), "src", "DepoWise.Desktop", "Views", "StockEntryView.axaml");

    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Gorunum() => File.ReadAllText(GorunumYolu);

    // ══════════════ A) KARAR MANTIĞI — GERÇEK SAYILARLA ══════════════

    /// <summary>⭐ ASIL DAVRANIŞ: kullanıcının penceresinde (947 px) listeye gerçekten yer kalır.
    /// Eski hâlde liste ~50 px idi; artık en az <see cref="FormListeOrani.ListeTabanYukseklik"/>.</summary>
    [Fact]
    public void YRL1_Kullanici_Penceresinde_Listeye_Yer_Kalir()
    {
        var sinir = FormListeOrani.FormUstSiniri(947);

        Assert.True(double.IsFinite(sinir), "947 px'lik pencerede forma sınır UYGULANMALI");
        var listeyeKalan = 947 - sinir;
        Assert.True(listeyeKalan >= FormListeOrani.ListeTabanYukseklik,
            $"listeye {listeyeKalan:0} px kaldı; taban {FormListeOrani.ListeTabanYukseklik} px olmalı");
        // Eski davranışın somut kıyası: form ~700 px isterken listeye ~50 px kalıyordu.
        Assert.True(listeyeKalan > 50 * 4, "düzeltme, eski ~50 px'lik şeride göre belirgin olmalı");
    }

    /// <summary>⭐ RESPONSIVE: pencere büyüyünce forma verilen pay da büyür — SABİT piksel değil.
    /// (Sabit bir üst sınır konsaydı büyük ekranda form gereksiz kırpılırdı.)</summary>
    [Fact]
    public void YRL2_Pencere_Buyuyunce_Pay_Da_Buyur()
    {
        var kucuk = FormListeOrani.FormUstSiniri(800);
        var orta = FormListeOrani.FormUstSiniri(1000);
        var buyuk = FormListeOrani.FormUstSiniri(1400);

        Assert.True(kucuk < orta, "800 → 1000: form payı artmalı");
        Assert.True(orta < buyuk, "1000 → 1400: form payı artmalı");
        // Oransal olmalı: 1400/800 = 1.75 kat → pay da yaklaşık o kadar artmalı (sabit değil).
        Assert.True(buyuk / kucuk > 1.5, "pay oransal büyümeli (sabit üst sınır davranışı DEĞİL)");
    }

    /// <summary>⭐ Pencere küçülünce liste tabanı KORUNUR — form daha da kısılır, tablo yok olmaz.
    ///
    /// ⚠️ <b>Neden 400 px'in ALTI da sınanır.</b> Varsayılan oranla (0,55) listeye kalan pay
    /// <c>0,45 × yükseklik</c>'tir; bu, ancak yükseklik ~400 px'in ALTINA inince tabandan küçük olur.
    /// Test yalnız 400 px ve üstünü deneseydi taban mantığı hiç çalışmaz, kaldırılsa bile fark
    /// edilmezdi — mutasyon turunda bu gerçekten yaşandı (M10 kaçmıştı) ve test bu yüzden
    /// güçlendirildi.</summary>
    [Fact]
    public void YRL3_Kucuk_Pencerede_Liste_Tabani_Korunur()
    {
        foreach (var yukseklik in new double[] { 250, 300, 350, 380, 400, 500, 600, 700, 768, 900, 1080 })
        {
            var sinir = FormListeOrani.FormUstSiniri(yukseklik);
            if (!double.IsFinite(sinir)) continue;   // sınır konamayacak kadar kısa pencere
            Assert.True(yukseklik - sinir >= FormListeOrani.ListeTabanYukseklik - 0.001,
                $"{yukseklik} px pencerede listeye en az {FormListeOrani.ListeTabanYukseklik} px kalmalı, " +
                $"kalan {yukseklik - sinir:0.##} px");
        }
    }

    /// <summary>⭐ TABAN MANTIĞININ KENDİSİ: oranın listeye taban kadar yer bırakmadığı bir yükseklikte
    /// (300 px → 0,45 × 300 = 135 px &lt; 180 px) form DAHA DA kısılmalı. Taban kaldırılırsa bu test kırılır.</summary>
    [Fact]
    public void YRL3b_Oran_Yetmediginde_Taban_Devreye_Girer()
    {
        const double yukseklik = 300;
        var sinir = FormListeOrani.FormUstSiniri(yukseklik);

        // Saf oran hesabı burada listeye yalnız 135 px bırakırdı — taban devreye girmeli.
        var safOran = yukseklik * FormListeOrani.VarsayilanOran;
        Assert.True(safOran > yukseklik - FormListeOrani.ListeTabanYukseklik,
            "kurgu hatası: bu yükseklikte oran zaten tabanı ihlal etmiyor");

        Assert.True(double.IsFinite(sinir));
        Assert.True(sinir < safOran, "taban korunması için form payı oran hesabından KÜÇÜK olmalı");
        Assert.Equal(yukseklik - FormListeOrani.ListeTabanYukseklik, sinir, 3);
    }

    /// <summary>Ölçü henüz yokken (ilk yerleşim turunda Bounds.Height = 0) SINIR UYGULANMAZ.
    /// Aksi hâlde form bir kare boyunca 0 px'e ezilir ve ekran boş görünürdü.</summary>
    [Fact]
    public void YRL4_Olcu_Yokken_Sinir_Uygulanmaz()
    {
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(0)));
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(-100)));
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(double.NaN)));
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(double.PositiveInfinity)));
    }

    /// <summary>Anlamsız oran verilirse eski (sınırsız) davranışa dönülür — ekran asla boş kalmaz.</summary>
    [Fact]
    public void YRL5_Gecersiz_Oran_Eski_Davranisa_Doner()
    {
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(900, 0)));
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(900, 1)));
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(900, -0.5)));
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(900, 1.5)));
        Assert.True(double.IsPositiveInfinity(FormListeOrani.FormUstSiniri(900, double.NaN)));
    }

    /// <summary>Politika sabitleri anlamlı kalmalı: form yarıdan biraz fazlasını alabilir, liste
    /// tabanı başlık + birkaç satırı gerçekten gösterecek kadar olmalı.</summary>
    [Fact]
    public void YRL6_Politika_Sabitleri_Makul()
    {
        Assert.InRange(FormListeOrani.VarsayilanOran, 0.35, 0.7);
        Assert.True(FormListeOrani.ListeTabanYukseklik >= 120,
            "taban, başlık + birkaç satırı göstermeye yetmeli (yoksa yine 'şerit' olur)");
    }

    // ══════════════ B) GÖRÜNÜM O KARARI GERÇEKTEN UYGULUYOR MU ══════════════

    /// <summary>⭐ Liste satırı hâlâ <c>*</c> (esneyen) — form satırı gibi <c>Auto</c> yapılırsa
    /// tablo tekrar içeriği kadar küçülür.</summary>
    [Fact]
    public void YRL7_Liste_Satiri_Esneyen_Kalir()
    {
        Assert.Contains("RowDefinitions=\"Auto,Auto,*,Auto\"", Gorunum());
    }

    /// <summary>⭐ Form bir <c>ScrollViewer</c> içinde ve ORAN bağlamasıyla sınırlı olmalı.
    /// Bu kaldırılırsa form yine tüm boyu yer ve hata geri gelir.</summary>
    [Fact]
    public void YRL8_Form_Sinirli_Ve_Kendi_Icinde_Kayar()
    {
        var x = Gorunum();
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", x);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", x);
        Assert.Contains("MaxHeight=\"{Binding #KokYerlesim.Bounds.Height", x);
        Assert.Contains("Converter={StaticResource FormUstSiniri}", x);
    }

    /// <summary>⭐ Liste alanının taban yüksekliği görünümde de uygulanmış olmalı ve
    /// <see cref="FormListeOrani.ListeTabanYukseklik"/> ile TUTARLI olmalı (iki ayrı sayı olmasın).</summary>
    [Fact]
    public void YRL9_Liste_Taban_Yuksekligi_Gorunumde_De_Var()
    {
        var beklenen = $"MinHeight=\"{FormListeOrani.ListeTabanYukseklik:0}\"";
        Assert.Contains(beklenen, Gorunum());
    }

    /// <summary>Mevcut tasarım BOZULMADI: tablo, başlık satırı, iptal butonu ve durum yazısı yerinde.
    /// (Düzeltme yalnız yükseklik paylaşımını değiştirdi; ekranın işlevi aynı kaldı.)</summary>
    [Fact]
    public void YRL10_Mevcut_Tasarim_Korundu()
    {
        var x = Gorunum();
        Assert.Contains("Text=\"TARİH\"", x);
        Assert.Contains("Text=\"MALZEME\"", x);
        Assert.Contains("ItemsSource=\"{Binding Movements}\"", x);
        Assert.Contains("ReverseMovementCommand", x);       // İptal (ters kayıt) akışı duruyor
        Assert.Contains("Grid.Row=\"3\" Text=\"{Binding Status}\"", x);
        Assert.Contains("Title=\"Hareket yok\"", x);        // 0 kayıt durumu paneli duruyor
        Assert.Contains("Title=\"Yüklenemedi\"", x);        // hata durumu paneli duruyor
    }

    /// <summary>ScrollViewer'ın YATAY kaydırması kapalı: form alanları zaten sığar, yatay çubuk
    /// açık kalsaydı her açılışta gereksiz kaydırma çubuğu görünürdü.</summary>
    [Fact]
    public void YRL11_Formda_Yatay_Kaydirma_Kapali()
    {
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", Gorunum());
    }
}
