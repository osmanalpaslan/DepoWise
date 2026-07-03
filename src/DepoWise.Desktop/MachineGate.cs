using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// Makine erişim kapısı. Sunucu bu makineyi 'revoked' (pasif) işaretlerse giriş engellenir.
/// Çevrimiçi: /api/machines/register durum döner + önbelleğe (machine_status.txt) yazılır.
/// Çevrimdışı: son bilinen durum kullanılır (pasif kaldıysa yine engel). Süper admin aktifleştirip
/// makine tekrar çevrimiçi olduğunda durum 'active/pending' döner ve giriş açılır.
/// </summary>
public static class MachineGate
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };

    private static string CacheFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "machine_status.txt");

    public static async Task<(bool Allowed, string Reason)> CheckAsync(string companyId)
    {
        var url = ResolveServerUrl();
        string? status = null;

        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var json = JsonSerializer.Serialize(new { companyId, machineName = Environment.MachineName });
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync(url!.TrimEnd('/') + "/api/machines/register", content);
                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                    status = doc.RootElement.TryGetProperty("status", out var st) ? st.GetString() : null;
                    if (status is not null) TryCache(status);
                }
            }
            catch { /* çevrimdışı → önbelleğe düş */ }
        }

        status ??= TryReadCache(); // çevrimdışı: son bilinen durum

        if (string.Equals(status, "revoked", StringComparison.OrdinalIgnoreCase))
            return (false, "Bu makine pasife alınmış. Girişe kapalı. İnternete bağlanıp süper adminin makineyi aktifleştirmesi gerekir.");

        return (true, "");
    }

    private static void TryCache(string status)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!); File.WriteAllText(CacheFile, status); } catch { }
    }

    private static string? TryReadCache()
    {
        try { return File.Exists(CacheFile) ? File.ReadAllText(CacheFile).Trim() : null; } catch { return null; }
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (File.Exists(path))
            {
                var v = File.ReadAllText(path).Trim();
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
        }
        catch { }
        return null;
    }
}
