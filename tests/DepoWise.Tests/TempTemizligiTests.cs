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

    [Fact]
    public void TMP3_Supurme_Artigi_Siler_Ilgisize_Dokunmaz()
    {
        var temp = Path.GetTempPath();
        var artik = Path.Combine(temp, "depowise_supurme_testi_" + Guid.NewGuid().ToString("N") + ".db");
        var korunan = Path.Combine(temp, "alpnex-korunmali-" + Guid.NewGuid().ToString("N") + ".zip");

        File.WriteAllText(artik, "artik");
        File.WriteAllText(korunan, "korunmali");

        try
        {
            // Yaş eşiği SIFIR: testte az önce yazılan dosya da hedefe girsin.
            // (Gerçek koşuda 30 dakikadır — o an çalışan başka bir koşunun dosyalarına dokunmamak için.)
            TempVeritabaniTemizligi.Supur(TimeSpan.Zero);

            Assert.False(File.Exists(artik));      // test artığı gitti
            Assert.True(File.Exists(korunan));     // ilgisiz dosya DURUYOR
        }
        finally
        {
            try { File.Delete(artik); } catch { }
            try { File.Delete(korunan); } catch { }
        }
    }
}
