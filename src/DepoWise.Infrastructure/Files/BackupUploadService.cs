using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DepoWise.Infrastructure.Files;

public sealed record BackupUploadResult(bool Ok, string Message);

/// <summary>Sunucudaki bir yedek kaydı (listeleme/silme için).</summary>
public sealed record ServerBackupItem(string Machine, string FileName, DateTimeOffset Date, long SizeBytes);

public sealed record ServerListResult(bool Ok, string Message, IReadOnlyList<ServerBackupItem> Items);

public sealed record ServerDeleteResult(bool Ok, string Message, int Deleted);

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

    private static string RangeUrl(string baseUrl, string companyId, DateOnly from, DateOnly to)
    {
        var sep = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{sep}company={WebUtility.UrlEncode(companyId)}" +
               $"&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
    }

    private static void Auth(HttpRequestMessage req, string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>İki tarih arası sunucu yedeklerini listeler (Süper Admin ekranı).</summary>
    public async Task<ServerListResult> ListAsync(
        string url, string? token, string companyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return new(false, "Sunucu adresi tanımlı değil.", Array.Empty<ServerBackupItem>());
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RangeUrl(url, companyId, from, to));
            Auth(req, token);
            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new(false, $"Sunucu hatası {(int)resp.StatusCode}: {body}", Array.Empty<ServerBackupItem>());

            var items = new List<ServerBackupItem>();
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "[]" : body);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var machine = e.TryGetProperty("machine", out var m) ? m.GetString() ?? "" : "";
                var file = e.TryGetProperty("filename", out var f) ? f.GetString() ?? "" : "";
                var date = e.TryGetProperty("date", out var d) && d.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(d.GetString(), out var dt) ? dt : DateTimeOffset.MinValue;
                var size = e.TryGetProperty("sizeBytes", out var s) && s.TryGetInt64(out var sb) ? sb : 0L;
                items.Add(new ServerBackupItem(machine, file, date, size));
            }
            return new(true, $"{items.Count} kayıt", items);
        }
        catch (Exception ex) { return new(false, "Listelenemedi: " + ex.Message, Array.Empty<ServerBackupItem>()); }
    }

    /// <summary>İki tarih arası sunucu yedeklerini TOPLU siler (Süper Admin; geri alınamaz).</summary>
    public async Task<ServerDeleteResult> DeleteRangeAsync(
        string url, string? token, string companyId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return new(false, "Sunucu adresi tanımlı değil.", 0);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Delete, RangeUrl(url, companyId, from, to));
            Auth(req, token);
            using var resp = await Http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                return new(false, $"Sunucu hatası {(int)resp.StatusCode}: {body}", 0);

            int deleted = 0;
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
                if (doc.RootElement.TryGetProperty("deleted", out var del) && del.TryGetInt32(out var dn)) deleted = dn;
            }
            catch { /* gövde sayı döndürmeyebilir */ }
            return new(true, $"{deleted} yedek silindi.", deleted);
        }
        catch (Exception ex) { return new(false, "Silinemedi: " + ex.Message, 0); }
    }
}
