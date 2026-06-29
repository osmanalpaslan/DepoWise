using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace DepoWise.Infrastructure.Files;

public sealed record BackupUploadResult(bool Ok, string Message);

/// <summary>
/// Yerel yedek dosyasını bulut yedek sunucusuna yükler (multipart POST). Sunucu yedekleri HİÇ silmez;
/// her makinenin (machine) tüm günlük yedekleri firma + makine bazında saklanır. Backend ayrı kurulur
/// (sözleşme: docs/SERVER_BACKUP_CONTRACT.md). Yapılandırma yoksa sessizce atlanır.
/// </summary>
public sealed class BackupUploadService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public bool IsConfigured(string? url) => !string.IsNullOrWhiteSpace(url);

    public async Task<BackupUploadResult> UploadAsync(
        string url, string? token, string companyId, string machine, string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return new(false, "Sunucu adresi tanımlı değil.");
        if (!File.Exists(filePath)) return new(false, "Yedek dosyası bulunamadı: " + filePath);

        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(companyId), "company");
            form.Add(new StringContent(machine), "machine");
            form.Add(new StringContent(Path.GetFileName(filePath)), "filename");

            await using var fs = File.OpenRead(filePath);
            var fileContent = new StreamContent(fs);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", Path.GetFileName(filePath));

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
            if (!string.IsNullOrWhiteSpace(token))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await Http.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                return new(true, $"Yüklendi → {machine}/{Path.GetFileName(filePath)}");

            var body = await resp.Content.ReadAsStringAsync(ct);
            return new(false, $"Sunucu hatası {(int)resp.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return new(false, "Yüklenemedi: " + ex.Message);
        }
    }
}
