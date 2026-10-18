using System.Text;
using DepoWise.Api;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YAYIN PAKETİ SAKLAMA POLİTİKASI ═══ (denetim 2026-08-26, dördüncü tur — **eksik test**)
///
/// <b>Neden bu test yazıldı.</b> <see cref="ReleaseStore"/> sınıf açıklaması bu mekanizmanın neden var
/// olduğunu anlatıyor: her paket ~85 MB, Fly.io kalıcı diski ~1 GB ve eski paketler temizlenmediği için
/// disk <b>12.07.2026'da DOLDU</b>; SQLite "database or disk is full" verdi ve <b>login dahil tüm API 500</b>
/// döndü (tam kesinti — ADR-070).
///
/// Buna rağmen taramada görüldü ki <c>PruneOld</c> için <b>hiçbir test yoktu</b>; yalnız
/// <c>MaxPackageBytes</c> sınırları kontrol ediliyordu. Yani sistemi bir kez durdurmuş olan hatanın
/// koruması <b>sessizce kaldırılabilirdi</b>.
///
/// Bu sınıf o boşluğu kapatır. Kapsam bilinçli olarak dardır: **davranış değiştirilmemiştir**, yalnız
/// mevcut davranış kilitlenmiştir.
/// </summary>
public class PaketSaklamaPolitikasiTests : IDisposable
{
    private readonly string _kok = Path.Combine(Path.GetTempPath(), $"dw_paket_{Guid.NewGuid():N}");
    private readonly ReleaseStore _depo;

    public PaketSaklamaPolitikasiTests()
    {
        Directory.CreateDirectory(_kok);
        _depo = new ReleaseStore(_kok);
    }

    private int _sira;

    private async Task Yayinla(string surum)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("paket-" + surum));
        var yol = await _depo.SaveAsync(surum, ms, CancellationToken.None);
        // ⚠️ "En yeni" = en son yazılan (LastWriteTimeUtc). Eskiden iki yayın arasında 40 ms GERÇEK bekleme vardı (Windows saat
        // adımı ~15,6 ms → aynı damga riski); tam koşuda iş parçacığı havuzu doluyken bu bekleme dakikalara uzuyordu
        // (PKT3 ≈ 20 dk, AR-GE 2026-10-18). Artık beklenmez: SaveAsync içindeki temizlik yeni dosyayı "şimdi" damgasıyla görür
        // (öncekilerin hepsinden yeni), ardından dosyaya geçmişte, KESİN ARTAN bir damga verilir → sonraki yayında da sıra
        // birebir "yayın sırası"dır. Doğrulanan kural (en yeni KeepCount kalır) değişmedi; saat çözünürlüğü kırılganlığı da kalktı.
        File.SetLastWriteTimeUtc(yol, Taban.AddSeconds(++_sira));
    }

    private static readonly DateTime Taban = DateTime.UtcNow.AddDays(-1);

    private string[] Paketler() => Directory.GetFiles(_kok, "DepoWise-*.pkg")
        .Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray()!;

    /// <summary>⭐ Sınırın ALTINDA hiçbir paket silinmez.</summary>
    [Fact]
    public async Task PKT1_Sinir_Altinda_Silme_Yok()
    {
        await Yayinla("1.0.1");
        await Yayinla("1.0.2");

        Assert.Equal(2, Paketler().Length);
    }

    /// <summary>⭐ Tam sınırda (KeepCount) hâlâ silme yok.</summary>
    [Fact]
    public async Task PKT2_Tam_Sinirda_Silme_Yok()
    {
        for (int i = 1; i <= ReleaseStore.KeepCount; i++) await Yayinla($"1.0.{i}");

        Assert.Equal(ReleaseStore.KeepCount, Paketler().Length);
    }

    /// <summary>⭐ ASIL KORUMA — sınır aşılınca disk büyümez: yalnız en yeni KeepCount paket kalır.</summary>
    [Fact]
    public async Task PKT3_Sinir_Asilinca_Disk_Buyumez()
    {
        for (int i = 1; i <= ReleaseStore.KeepCount + 4; i++) await Yayinla($"1.0.{i}");

        // Mesaj bilinçli olarak AYRINTILI: bu sayı tutmadığında tek başına "3 bekleniyordu, 4 geldi"
        // demek nedeni bulmaya yetmiyor. Kalan dosyalar yazılırsa "hangi silme başarısız oldu"
        // sorusu tek bakışta cevaplanır (PruneOld silme hatalarını sessizce yutar — bkz. ReleaseStore).
        var kalan = Paketler();
        Assert.True(kalan.Length == ReleaseStore.KeepCount,
            $"Saklama sınırı tutmadı: {ReleaseStore.KeepCount} paket kalmalıydı, {kalan.Length} kaldı. " +
            $"Kalanlar: {string.Join(", ", kalan)}");
    }

    /// <summary>⭐ Silinenler ESKİLER olmalı — güncelleyici daima EN SON sürümü indirir.</summary>
    [Fact]
    public async Task PKT4_En_Yeni_Paketler_Korunur()
    {
        for (int i = 1; i <= ReleaseStore.KeepCount + 2; i++) await Yayinla($"1.0.{i}");

        var kalan = Paketler();
        var enYeni = ReleaseStore.KeepCount + 2;

        Assert.Contains($"DepoWise-1.0.{enYeni}.pkg", kalan);          // en son sürüm KESİNLİKLE durmalı
        Assert.DoesNotContain("DepoWise-1.0.1.pkg", kalan);            // en eski gitmiş olmalı
        Assert.NotNull(_depo.PathFor($"1.0.{enYeni}"));                // indirme yolu çözülebiliyor
    }

    /// <summary>Silinen sürümün indirme yolu artık çözülmez (indirme ucu 404 verir).</summary>
    [Fact]
    public async Task PKT5_Silinen_Surum_Indirilemez()
    {
        for (int i = 1; i <= ReleaseStore.KeepCount + 2; i++) await Yayinla($"1.0.{i}");

        Assert.Null(_depo.PathFor("1.0.1"));
    }

    /// <summary>Politika sabiti anlamlı olmalı: geri dönüş için 1'den fazla, disk için makul.</summary>
    [Fact]
    public void PKT6_Saklama_Sayisi_Makul()
    {
        Assert.True(ReleaseStore.KeepCount >= 2, "geri dönüş ihtimali için 1'den fazla paket tutulmalı");
        Assert.True(ReleaseStore.KeepCount <= 5, "disk ~1 GB, paket ~85 MB → üst sınır makul kalmalı");
        // KeepCount × en büyük paket, kalıcı diski (~974 MB) aşmamalı.
        Assert.True(ReleaseStore.KeepCount * ReleaseStore.MaxPackageBytes <= 1024L * 1024 * 1024,
            "saklama × paket tavanı diskten büyük olursa ADR-070 sınıfı kesinti geri gelir");
    }

    public void Dispose()
    {
        try { Directory.Delete(_kok, recursive: true); } catch { }
    }
}
