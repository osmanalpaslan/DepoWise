using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ GEÇİCİ DOSYA SÜPÜRME — GÜVENLİK TESTLERİ (2026-09-04) ═══
///
/// Süpürme kancası %TEMP% içinde DOSYA SİLİYOR. Bu yüzden asıl kanıtlanması gereken şey ne sildiği
/// değil, <b>ne silmediğidir</b>: yanlış bir desen eşleşmesi kullanıcının başka dosyalarını götürür.
///
///  TMP1 — Test artığı desenleri tanınır
///  TMP2 — İLGİSİZ dosyalar ASLA hedef sayılmaz (asıl güvenlik kapısı)
///  TMP3 — Süpürme gerçekten siliyor ve ilgisiz dosyaya dokunmuyor
/// </summary>
public class TempTemizligiTests
{
    [Theory]
    [InlineData("depowise_ryetki_abc123.db")]
    [InlineData("depowise_actedit_x.db-wal")]
    [InlineData("dw_zmt_mig_9.db-shm")]
    [InlineData("dw_yed_kls_1")]
    public void TMP1_Test_Artigi_Desenleri_Taninir(string ad)
        => Assert.True(TempVeritabaniTemizligi.Hedef(ad));

    [Theory]
    [InlineData("onemli-belge.docx")]
    [InlineData("alpnex-1.0.171.zip")]       // kurulum aracının indirdiği paket — SİLİNMEMELİ
    [InlineData("alpnex-kurulum.log")]       // kurulum günlüğü — SİLİNMEMELİ
    [InlineData("depowise.txt")]             // ön ek "depowise_" DEĞİL → hedef değil
    [InlineData("mydw_test.db")]             // ortada geçiyor, başta değil
    [InlineData("DWG-cizim.dwg")]
    public void TMP2_Ilgisiz_Dosyalar_ASLA_Hedef_Degil(string ad)
        => Assert.False(TempVeritabaniTemizligi.Hedef(ad));

    /// <summary>
    /// Süpürme gerçekten siliyor mu ve ilgisize dokunuyor mu?
    ///
    /// ⚠️ <b>KENDİ İZOLE KLASÖRÜNDE koşar — gerçek %TEMP% üzerinde ASLA.</b> Bu test önce
    /// <c>Supur(TimeSpan.Zero)</c>'ı doğrudan <c>%TEMP%</c>'te çağırıyordu ve xUnit sınıfları
    /// PARALEL koştuğu için o an çalışan başka test sınıflarının CANLI dosya/klasörlerini
    /// siliyordu → tam süitte rastgele bir test <c>DirectoryNotFoundException</c> ile düşüyordu
    /// (2026-09-04'te PKT3'te yakalandı). Tek başına koşunca hep geçtiği için uzun süre
    /// "makine yükü" sanıldı. İzole klasör bu sınıfı kökten kapatır.
    /// </summary>
    [Fact]
    public void TMP3_Supurme_Artigi_Siler_Ilgisize_Dokunmaz()
    {
        var kok = Path.Combine(Path.GetTempPath(), "dw_tmp3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(kok);

        var artik = Path.Combine(kok, "depowise_supurme_testi.db");
        var artikKlasor = Path.Combine(kok, "dw_artik_klasor");
        var korunan = Path.Combine(kok, "alpnex-korunmali.zip");

        File.WriteAllText(artik, "artik");
        Directory.CreateDirectory(artikKlasor);
        File.WriteAllText(korunan, "korunmali");

        try
        {
            // Yaş eşiği SIFIR: testte az önce yazılan dosya da hedefe girsin. Gerçek koşuda 30
            // dakikadır. Süpürme YALNIZ bu izole klasörde çalışır → paralel testler etkilenmez.
            TempVeritabaniTemizligi.Supur(TimeSpan.Zero, kok);

            Assert.False(File.Exists(artik));            // test artığı gitti
            Assert.False(Directory.Exists(artikKlasor)); // artık KLASÖR de gitti
            Assert.True(File.Exists(korunan));           // ilgisiz dosya DURUYOR
        }
        finally
        {
            try { Directory.Delete(kok, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// ⭐ REGRESYON KİLİDİ — süpürme testi gerçek <c>%TEMP%</c>'i süpürmemeli.
    /// Bu kural bir kez ihlal edildi ve tam süiti rastgele kırdı (bkz. TMP3 notu). Kural koda
    /// yazılamaz (parametre isteğe bağlıdır, gerçek kullanımda kök verilmez) → kaynak metniyle
    /// kilitlenir: bu dosyada <c>Supur</c> daima İKİ argümanla çağrılmalıdır.
    /// </summary>
    [Fact]
    public void TMP4_Supurme_Testi_Gercek_Temp_Klasorunu_Supurmez()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        var kaynak = File.ReadAllText(Path.Combine(kok!.FullName, "tests", "DepoWise.Tests", "TempTemizligiTests.cs"));

        // ⚠ Aranan desen PARÇALI kurulur: tek parça yazılsaydı bu satırın KENDİSİ dosyada
        // eşleşir ve test daima kırmızı olurdu (ilk denemede tam olarak bu oldu).
        var tekArgumanli = "Supur(TimeSpan.Zero" + ");";
        Assert.DoesNotContain(tekArgumanli, kaynak);            // tek argüman = gerçek %TEMP%
        Assert.Contains("Supur(TimeSpan.Zero, kok)", kaynak);   // izole klasör
    }
}
