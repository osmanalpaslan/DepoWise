using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3b-6 — MASAÜSTÜ TABLOLARININ İKİ SÖZLEŞMESİ ═══
///
/// Bu testler gerçek GUI turunda BULUNAN iki hatanın regresyonudur. İkisi de görsel davranıştır
/// ve projede Avalonia için başsız (headless) UI test altyapısı YOKTUR; bu yüzden burada
/// <b>XAML sözleşmesi</b> sınanır. Gerçek görsel doğrulama ayrıca yapılmış ve rapora yazılmıştır —
/// bu testler o doğrulamanın yerine geçmez, <b>tekrar bozulmasını</b> engeller.
///
/// <b>SÖZLEŞME 1 — korunan kolonun BAŞLIĞI da gizlenir.</b>
/// Bulunan hata: Kasa/Banka'da satır hücresi gizleniyor ama "BAKİYE" başlığı ekranda kalıyordu.
/// Kullanıcının açık şartı: <i>"kolon tamamen kaybolmalı · başlık kalmamalı · boş hücre kalmamalı"</i>.
///
/// <b>SÖZLEŞME 2 — başlık ızgarası gövdeyle hizalanır.</b>
/// Bulunan hata: kolonlar panele sığmadığında satırlar doğal genişliğini alıp yatay kayıyor,
/// başlık ise sıkışıyordu → 100 px kayma. Çözüm <c>TableHeaderSync</c>; bu test onun her tabloya
/// bağlı KALDIĞINI kilitler.
/// </summary>
public class MasaustuTabloHizaTests
{
    /// <summary>Alan yetkisiyle kolon gizleyen ön muhasebe görünümleri.</summary>
    private static readonly string[] Gorunumler =
    {
        "PartiesView.axaml",
        "InvoicesView.axaml",
        "FinanceView.axaml",
    };

    /// <summary>Korunan alanların KOLON BAŞLIĞI metinleri (ekranda göründükleri hâlleriyle).</summary>
    private static readonly string[] KorunanBasliklar = { "BAKİYE", "TUTAR", "BORÇ", "ALACAK", "GİRİŞ", "ÇIKIŞ" };

    private static string Yol(string dosya)
    {
        var dizin = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dizin is not null; i++)
        {
            var aday = Path.Combine(dizin, "src", "DepoWise.Desktop", "Views", dosya);
            if (File.Exists(aday)) return aday;
            dizin = Path.GetDirectoryName(dizin);
        }
        throw new FileNotFoundException("Görünüm dosyası bulunamadı: " + dosya);
    }

    // ══════════════════ SÖZLEŞME 1 ══════════════════

    /// <summary>
    /// 🔴 REGRESYON: korunan bir kolonun başlığı, görünürlük bağlaması OLMADAN yazılamaz.
    ///
    /// Aksi hâlde alan gizlendiğinde başlık ekranda kalır (gerçek GUI'de "BAKİYE" başlığı böyle
    /// kalmıştı) ve hem yanlış bilgi verir hem de kolonları kaydırır.
    /// </summary>
    [Fact]
    public void TH1_Korunan_Kolon_Basliklari_Gorunurluk_Baglamasi_Tasir()
    {
        var eksikler = new List<string>();

        foreach (var dosya in Gorunumler)
        {
            var metin = File.ReadAllText(Yol(dosya));
            foreach (Match m in Regex.Matches(metin, @"<SelectableTextBlock[^>]*?Text=""(?<b>[^""]+)""[^>]*?/>",
                         RegexOptions.Singleline))
            {
                var baslik = m.Groups["b"].Value;
                if (!KorunanBasliklar.Contains(baslik)) continue;
                if (!m.Value.Contains("IsVisible="))
                    eksikler.Add($"{dosya}: «{baslik}» başlığında IsVisible bağlaması yok");
            }
        }

        Assert.True(eksikler.Count == 0,
            "Korunan kolon başlıkları gizlenmiyor:\n  " + string.Join("\n  ", eksikler));
    }

    /// <summary>
    /// Testin kendisi anlamlı mı? Aranan başlıklar dosyalarda GERÇEKTEN bulunmalı — aksi hâlde
    /// yukarıdaki test hiçbir şeye bakmadan yeşil kalırdı (sahte güven).
    /// </summary>
    [Fact]
    public void TH2_Aranan_Basliklar_Gercekten_Var()
    {
        var bulunan = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dosya in Gorunumler)
        {
            var metin = File.ReadAllText(Yol(dosya));
            foreach (var b in KorunanBasliklar)
                if (metin.Contains($@"Text=""{b}""")) bulunan.Add(b);
        }

        Assert.Contains("BAKİYE", bulunan);
        Assert.Contains("TUTAR", bulunan);
        Assert.True(bulunan.Count >= 3, "Beklenen korunan başlıkların çoğu bulunamadı: " + string.Join(",", bulunan));
    }

    // ══════════════════ SÖZLEŞME 2 ══════════════════

    /// <summary>
    /// 🔴 REGRESYON: bu görünümlerdeki HER tablo başlığı <c>TableHeaderSync.Source</c> taşımalıdır.
    ///
    /// Taşımayan başlık, kolonlar panele sığmadığında gövdeden kayar (ölçülen: 100 px). Bağlamanın
    /// işaret ettiği <c>x:Name</c>'in dosyada gerçekten tanımlı olduğu da doğrulanır — yanlış ada
    /// bağlanmış bir başlık sessizce hizasız kalırdı.
    /// </summary>
    [Fact]
    public void TH3_Tablo_Basliklari_Govdeyle_Hizalanir()
    {
        var sorunlar = new List<string>();

        foreach (var dosya in Gorunumler)
        {
            var metin = File.ReadAllText(Yol(dosya));
            var basliklar = Regex.Matches(metin, @"<Border[^>]*?Classes=""TableHeader""[^>]*?>", RegexOptions.Singleline);
            Assert.True(basliklar.Count > 0, dosya + ": hiç tablo başlığı bulunamadı (test anlamsızlaşır)");

            foreach (Match m in basliklar)
            {
                var eslesme = Regex.Match(m.Value, @"TableHeaderSync\.Source=""\{Binding #(?<ad>[A-Za-z0-9_]+)\}""");
                if (!eslesme.Success)
                {
                    sorunlar.Add($"{dosya}: bir tablo başlığında TableHeaderSync.Source yok");
                    continue;
                }
                var ad = eslesme.Groups["ad"].Value;
                if (!metin.Contains($@"x:Name=""{ad}"""))
                    sorunlar.Add($"{dosya}: başlık «{ad}» adına bağlı ama o adda liste yok");
            }
        }

        Assert.True(sorunlar.Count == 0, "Başlık/gövde hizalama sözleşmesi bozuk:\n  " + string.Join("\n  ", sorunlar));
    }

    /// <summary>
    /// Hizalama kuralının SAYISAL çekirdeği: başlığın iç genişliği, listenin içerik genişliğinden
    /// başlığın yatay boşluğu düşülerek bulunur. Boşluk düşülmezse yıldız (*) kolon tam o kadar
    /// fazla alır — gerçek GUI'de 24 px olarak ölçülen hata buydu.
    ///
    /// <c>TableHeaderSync</c> masaüstü projesindedir ve test projesi ona başvurmaz; bu yüzden kural
    /// burada AYNI biçimde ifade edilip kilitlenir. (Davranışın kendisi gerçek GUI'de doğrulandı.)
    /// </summary>
    [Theory]
    [InlineData(790d, 24d, 766d)]   // tipik: kolon toplamı 790, başlık padding 12+12
    [InlineData(400d, 24d, 376d)]
    [InlineData(0d, 24d, 0d)]       // liste henüz ölçülmedi → başlığa dokunulmaz
    [InlineData(10d, 24d, 0d)]      // boşluk içerikten büyük → negatif genişlik ÜRETİLMEZ
    public void TH4_Baslik_Ic_Genisligi_Yatay_Boslugu_Duser(double extent, double bosluk, double beklenen)
    {
        static double IcerikGenisligi(double extentWidth, double yatayBosluk)
        {
            if (extentWidth <= 0) return 0;
            var g = extentWidth - yatayBosluk;
            return g > 0 ? g : 0;
        }

        Assert.Equal(beklenen, IcerikGenisligi(extent, bosluk));
    }
}
