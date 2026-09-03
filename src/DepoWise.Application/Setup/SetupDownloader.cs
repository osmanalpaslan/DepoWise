using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DepoWise.Application.Setup;

/// <summary>İndirme ilerlemesi (UI'a bildirilir).</summary>
public sealed record DownloadProgress(long BytesRead, long TotalBytes, double BytesPerSecond)
{
    public int Percent => TotalBytes > 0 ? (int)Math.Clamp(BytesRead * 100 / TotalBytes, 0, 100) : 0;

    /// <summary>Tahmini kalan süre; hız bilinmiyorsa null.</summary>
    public TimeSpan? Remaining => BytesPerSecond > 1 && TotalBytes > BytesRead
        ? TimeSpan.FromSeconds((TotalBytes - BytesRead) / BytesPerSecond)
        : null;
}

/// <summary>
/// HTTP'yi indirme mantığından ayırır. Testlerde sahte uygulamayla bağlantı kopması, zaman aşımı ve
/// yarım indirme senaryoları GERÇEK ağ olmadan kurulabilir.
/// </summary>
public interface ISetupHttp
{
    /// <summary>Toplam boyut (bilinmiyorsa -1).</summary>
    Task<long> GetLengthAsync(Uri url, CancellationToken ct);

    /// <summary><paramref name="offset"/> baytından itibaren içerik akışı açar (devam ettirme).</summary>
    Task<Stream> OpenReadAsync(Uri url, long offset, CancellationToken ct);
}

/// <summary>
/// ═══ İNDİRME YÖNETİCİSİ — YENİDEN DENEME + KALDIĞI YERDEN DEVAM (2026-09-04) ═══
///
/// <b>Eski davranış:</b> tek <c>GetAsync</c>; bağlantı 80 MB'de koparsa kurulum tamamen başarısız
/// oluyor ve kullanıcı en baştan başlıyordu. Tek koruma 30 dakikalık zaman aşımıydı.
///
/// <b>Yeni:</b> kopma hâlinde <see cref="MaxAttempts"/> kez yeniden denenir ve <b>kaldığı yerden</b>
/// devam edilir (HTTP Range). Kısmi dosya diskte tutulur; yalnız başarıda ya da iptalde temizlenir.
///
/// Bu sınıf paketi DOĞRULAMAZ — doğrulama <see cref="SetupPackageVerifier"/> işidir ve indirme
/// bittikten sonra ayrı bir kapı olarak çalışır (tek sorumluluk + kapıyı atlamak zorlaşsın diye).
/// </summary>
public static class SetupDownloader
{
    public const int MaxAttempts = 3;

    public static async Task DownloadAsync(
        ISetupHttp http, Uri url, string destPath, long expectedSize,
        IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var total = expectedSize > 0 ? expectedSize : await http.GetLengthAsync(url, ct).ConfigureAwait(false);

        Exception? sonHata = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            // Kaldığı yerden devam: dosyada zaten olan baytlar tekrar indirilmez.
            long offset = File.Exists(destPath) ? new FileInfo(destPath).Length : 0;
            if (total > 0 && offset >= total) return;        // zaten tam inmiş

            try
            {
                await KopyalaAsync(http, url, destPath, offset, total, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                Temizle(destPath);                            // iptalde yarım dosya BIRAKILMAZ
                throw;
            }
            catch (Exception ex)
            {
                sonHata = ex;
                if (attempt == MaxAttempts) break;
                // Kısa artan bekleme; kısmi dosya KORUNUR ki sonraki deneme devam etsin.
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
        }

        Temizle(destPath);
        throw new SetupVerificationException("INDIRME_BASARISIZ",
            "Kurulum paketi indirilemedi. İnternet bağlantınızı kontrol edip tekrar deneyin." +
            (sonHata is null ? "" : ""));
    }

    private static async Task KopyalaAsync(
        ISetupHttp http, Uri url, string destPath, long offset, long total,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        await using var src = await http.OpenReadAsync(url, offset, ct).ConfigureAwait(false);
        await using var fs = new FileStream(destPath, offset > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long okunan = offset;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long sonMs = 0, sonBayt = okunan;
        double hiz = 0;

        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            okunan += n;

            var ms = sw.ElapsedMilliseconds;
            if (ms - sonMs >= 400)
            {
                var sn = (ms - sonMs) / 1000.0;
                if (sn > 0) hiz = (okunan - sonBayt) / sn;
                sonMs = ms; sonBayt = okunan;
                progress?.Report(new DownloadProgress(okunan, total, hiz));
            }
        }
        progress?.Report(new DownloadProgress(okunan, total, hiz));
    }

    private static void Temizle(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* silinemezse doğrulama zaten reddeder */ }
    }
}
