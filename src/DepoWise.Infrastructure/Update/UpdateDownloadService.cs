using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DepoWise.Infrastructure.Update;

/// <summary>
/// Güncelleme paketini web sunucusundan indirir (yüzde bildirimiyle). Yalnız indirir; checksum doğrulama
/// ve kurulum <see cref="UpdateService"/>'te. Veritabanına ASLA dokunmaz (paket yalnız uygulama dizinine kurulur).
/// </summary>
public sealed class UpdateDownloadService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };

    /// <summary>URL'den paketi indirir; progress 0–100 (Content-Length yoksa yalnız başlangıç/son bildirilir).</summary>
    public async Task<byte[]> DownloadAsync(string url, Action<int>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Güncelleme indirme adresi tanımlı değil.");
        progress?.Invoke(0);

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream(total > 0 ? (int)total : 1 << 20);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await ms.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Invoke((int)(read * 100 / total));
        }
        progress?.Invoke(100);
        return ms.ToArray();
    }
}
