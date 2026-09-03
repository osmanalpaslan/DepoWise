using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace DepoWise.Tests;

/// <summary>
/// ═══ GEÇİCİ VERİTABANI SIZINTISI — KAYNAKTA KAPATMA (2026-09-04) ═══
///
/// <b>Sorun (kullanıcının disk analiziyle ölçüldü):</b> <c>%TEMP%</c> içinde <b>47.710 dosya /
/// 60,2 GB</b> birikmişti. Tamamı test artığıydı — uygulama ya da kurulum aracı değil (kurulum aracı
/// hiç <c>.db</c> üretmiyor, doğrulandı).
///
/// <b>Neden bu kadar çok:</b>
/// <list type="bullet">
///   <item><b>191 test sınıfı</b> <c>Path.GetTempPath()</c> altında SQLite veritabanı açıyor.</item>
///   <item>xUnit her test <b>metodu</b> için sınıfı YENİDEN oluşturur → 3.290 test ≈ her tam koşuda
///         ~3.290 veritabanı.</item>
///   <item>WAL modunda her veritabanı <b>3 dosya</b> yapar (<c>.db</c> + <c>-wal</c> + <c>-shm</c>)
///         → koşu başına ~10.000 dosya.</item>
///   <item><b>191 sınıfın yalnız 38'i</b> <c>-wal</c>/<c>-shm</c> eşlikçilerini siliyordu; 153'ü
///         bırakıyordu. Silme hatası da <c>catch { }</c> ile sessizce yutuluyordu.</item>
/// </list>
///
/// <b>Neden BU çözüm:</b>
/// <list type="number">
///   <item><b>191 sınıfı tek tek düzeltmek</b> her test dosyasına dokunan, büyük ve riskli bir
///         değişiklik olurdu. Buradaki çözüm mevcut testlerin HİÇBİRİNİ değiştirmez.</item>
///   <item><b>Koşu SONUNDA</b> temizlemek yerine <b>koşu BAŞINDA</b> temizlenir: kapanış kancasının
///         süresi kısıtlıdır ve on binlerce dosya silinmeye yetişmeyebilir; başlangıçta ise bu baskı
///         yoktur. Sonuç aynıdır — artıklar bir sonraki koşuda mutlaka silinir, yani birikim
///         <b>tek bir koşuluk</b> (~2-3 GB) ile sınırlı kalır, 60 GB'a çıkamaz.</item>
///   <item>xUnit'in sürüme göre değişen genişletme API'sine <b>bağımlı değildir</b> (o yol denendi:
///         <c>XunitTestFramework.Dispose(bool)</c> bu sürümde sanal değil). Modül başlatıcı standart
///         C# özelliğidir.</item>
/// </list>
///
/// <b>Güvenlik:</b> yalnız <c>%TEMP%</c> içinde, yalnız <c>depowise_*</c> / <c>dw_*</c> ön ekli
/// dosyalar, yalnız <see cref="VarsayilanYas"/>'tan eski olanlar silinir. Kilitli dosya sessizce
/// atlanır → eşzamanlı çalışan başka bir test koşusu bozulmaz. Yaş eşiği, o an koşan testlerin
/// dosyalarına dokunulmamasını da garanti eder.
/// </summary>
public static class TempVeritabaniTemizligi
{
    /// <summary>Testlerin ürettiği geçici dosyaların ön ekleri.</summary>
    public static readonly string[] Onekler = { "depowise_", "dw_" };

    /// <summary>Bu yaştan eski artıklar silinir. Aktif koşuya dokunmamak için bilinçli olarak geniş.</summary>
    public static readonly TimeSpan VarsayilanYas = TimeSpan.FromMinutes(30);

    /// <summary>Test derlemesi yüklenirken ÖNCEKİ koşuların artıklarını temizler.</summary>
    [ModuleInitializer]
    internal static void Baslangicta() { try { Supur(VarsayilanYas); } catch { /* temizlik testi bozmaz */ } }

    /// <summary>Bu ön eklerden biriyle başlıyor mu? (yalnız dosya ADI'na bakar)</summary>
    public static bool Hedef(string fileName)
        => Onekler.Any(o => fileName.StartsWith(o, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <paramref name="yas"/>'tan eski test artıklarını siler; silinen öğe sayısını döndürür.
    /// Kilitli/erişilemeyen öğeler sessizce atlanır.
    /// </summary>
    public static int Supur(TimeSpan yas)
    {
        // Havuzdaki bağlantılar dosyayı kilitli tutabilir → önce serbest bırak.
        try { SqliteConnection.ClearAllPools(); } catch { }

        var kesim = DateTime.Now - yas;
        var temp = Path.GetTempPath();
        var silinen = 0;

        try
        {
            foreach (var yol in Directory.EnumerateFiles(temp))
            {
                if (!Hedef(Path.GetFileName(yol))) continue;
                try
                {
                    if (File.GetLastWriteTime(yol) >= kesim) continue;   // taze → aktif koşu olabilir
                    File.Delete(yol);
                    silinen++;
                }
                catch { /* kilitli → atla */ }
            }

            // Bazı testler geçici KLASÖR de açıyor (yedek/geri yükleme senaryoları).
            foreach (var klasor in Directory.EnumerateDirectories(temp))
            {
                if (!Hedef(Path.GetFileName(klasor))) continue;
                try
                {
                    if (Directory.GetLastWriteTime(klasor) >= kesim) continue;
                    Directory.Delete(klasor, recursive: true);
                    silinen++;
                }
                catch { /* kilitli → atla */ }
            }
        }
        catch (DirectoryNotFoundException) { /* %TEMP% yoksa yapacak bir şey yok */ }

        return silinen;
    }
}
