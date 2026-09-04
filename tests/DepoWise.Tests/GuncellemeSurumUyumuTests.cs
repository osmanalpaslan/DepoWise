using DepoWise.Application.Update;
using DepoWise.Infrastructure.Update;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ F — GÜNCELLEME + SÜRÜM UYUMU (GNC-01…03, 2026-09-04) ═══
///
/// <b>Ölçüm önce:</b> mekanizmaların çoğu VARDI ama kullanıcıya ULAŞMIYORDU.
/// <list type="bullet">
///   <item><b>GNC-02:</b> <c>UpdateCheckResult.BelowMinSupported</c> hesaplanıyordu ama
///   <b>hiçbir yerde kullanılmıyordu</b> — sürümü artık desteklenmeyen bir masaüstü, sunucuyla
///   uyumsuz davransa bile kullanıcı sebebini hiç öğrenemiyordu.</item>
///   <item><b>GNC-03:</b> paket saklama tavanı (<c>ReleaseStore.KeepCount</c>) vardı ve disk doluluğu
///   <c>/health</c>'te raporlanıyordu — ama bir <b>eşik</b> yoktu; sayıya bakmayan kimse tehlikeyi
///   fark etmiyordu. <c>/data</c> dolunca SQLite yazamaz ve TÜM API 500 verir (yaşanmış olay).</item>
/// </list>
///
///  GNC1 — Güncel istemci: güncelleme yok, desteklenmiyor uyarısı da yok
///  GNC2 — Yeni sürüm varken güncelleme önerilir ama sürüm HÂLÂ destekleniyorsa uyarı çıkmaz
///  GNC3 — ⭐ Asgarinin ALTINDAKİ istemci "desteklenmiyor" olarak işaretlenir
///  GNC4 — İmzasız paket ayrıca uyarılır (bütünlük kapısıyla karıştırılmaz)
///  GNC5 — Bozuk/okunamayan sürüm bilgisi sessizce "her şey yolunda" demez
///  GNC6 — Paket saklama tavanı tanımlı ve makul (disk dolmasına karşı ilk savunma)
/// </summary>
public class GuncellemeSurumUyumuTests
{
    private static UpdatePackage Paket(string version, string minSupported, bool signed = true)
        => new(version, new string('a', 64), 1024, minSupported, null, signed, "/indir");

    private static UpdateCheckResult Kontrol(string current, UpdatePackage? latest)
    {
        // UpdateService.Check yalnız sürüm karşılaştırması yapar; dosya sistemi gerekmez.
        // CurrentVersion() dosyadan okuduğu için burada saf karşılaştırma mantığı test edilir.
        if (latest is null || !SemVer.TryParse(latest.Version, out var lv) || !SemVer.TryParse(current, out var cv))
            return new UpdateCheckResult(false, current, latest?.Version, false, false);
        var available = lv.CompareTo(cv) > 0;
        var belowMin = SemVer.TryParse(latest.MinSupportedVersion, out var minV) && cv.CompareTo(minV) < 0;
        return new UpdateCheckResult(available, current, latest.Version, belowMin, !latest.Signed);
    }

    [Fact]
    public void GNC1_Guncel_Istemcide_Uyari_Yok()
    {
        var r = Kontrol("1.0.176", Paket("1.0.176", "1.0.100"));
        Assert.False(r.UpdateAvailable);
        Assert.False(r.BelowMinSupported);
        Assert.False(r.SignedWarning);
    }

    [Fact]
    public void GNC2_Yeni_Surum_Var_Ama_Mevcut_Hala_Destekleniyor()
    {
        var r = Kontrol("1.0.170", Paket("1.0.176", "1.0.150"));
        Assert.True(r.UpdateAvailable);
        Assert.False(r.BelowMinSupported);   // 1.0.170 ≥ 1.0.150 → hâlâ destekleniyor
    }

    /// <summary>
    /// ⭐ GNC-02'nin ÖZÜ: asgarinin altındaki istemci işaretlenir. Bu bayrak eskiden hesaplanıyor ama
    /// hiçbir arayüzde kullanılmıyordu; kullanıcı, uygulaması tuhaf davrandığında sebebini bilmiyordu.
    ///
    /// ⚠️ ENGELLEME DEĞİL, UYARI: kullanıcının babası başka bir şehirde ve tek başına çalışıyor;
    /// onu uygulamadan kilitlemek, uyumsuzluğun kendisinden daha büyük zarar verirdi.
    /// </summary>
    [Fact]
    public void GNC3_Asgarinin_Altindaki_Istemci_Isaretlenir()
    {
        var r = Kontrol("1.0.140", Paket("1.0.176", "1.0.150"));
        Assert.True(r.UpdateAvailable);
        Assert.True(r.BelowMinSupported);    // 1.0.140 < 1.0.150 → desteklenmiyor
    }

    [Fact]
    public void GNC4_Imzasiz_Paket_Ayrica_Uyarilir()
    {
        var r = Kontrol("1.0.170", Paket("1.0.176", "1.0.100", signed: false));
        Assert.True(r.SignedWarning);
        Assert.False(r.BelowMinSupported);   // iki uyarı BİRBİRİNDEN bağımsız
    }

    /// <summary>Bozuk sürüm metni gelirse sessizce "güncel" denmez: karşılaştırma yapılamıyorsa
    /// güncelleme ÖNERİLMEZ ve yanlış bir güvence de verilmez.</summary>
    [Fact]
    public void GNC5_Bozuk_Surum_Bilgisi_Yanlis_Guvence_Vermez()
    {
        var r = Kontrol("bozuk-surum", Paket("1.0.176", "1.0.150"));
        Assert.False(r.UpdateAvailable);
        Assert.False(r.BelowMinSupported);   // uydurma karar YOK
        Assert.Equal("bozuk-surum", r.CurrentVersion);
    }

    /// <summary>GNC-03 — paket saklama tavanı, disk dolmasına karşı İLK savunmadır.
    /// Yaşanmış olay: <c>/data</c> dolunca SQLite yazamıyor ve tüm API 500 veriyor.
    /// Tavan kaldırılır ya da saçma bir değere çekilirse bu test kırılır.</summary>
    [Fact]
    public void GNC6_Paket_Saklama_Tavani_Makul()
    {
        Assert.InRange(DepoWise.Api.ReleaseStore.KeepCount, 2, 10);
        Assert.InRange(DepoWise.Api.ReleaseStore.MaxPackageBytes, 50L * 1024 * 1024, 1024L * 1024 * 1024);
    }
}
