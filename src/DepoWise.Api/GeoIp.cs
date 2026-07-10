using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace DepoWise.Api;

/// <summary>
/// IP'den YAKLAŞIK il (best-effort, makine tanımayı kolaylaştırmak için). Sonuç bellek-önbelleğinde tutulur;
/// ilk sorguda arka planda çözülür (isteği BLOKLAMAZ) ve sonraki listede görünür. Çözülemezse/özel IP'de boş döner.
/// Dış servis (ip-api.com, ücretsiz) erişilemezse sessizce boş kalır — kritik değil.
/// </summary>
public static class GeoIp
{
    private static readonly ConcurrentDictionary<string, string> _cache = new();
    private static readonly ConcurrentDictionary<string, byte> _inflight = new();
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };

    /// <summary>Verilen IPv4/IPv6'dan ile (public IP tercih edilir). Bilinmiyorsa "".</summary>
    public static string Province(string? ip4, string? ip6)
    {
        var ip = FirstPublic(ip4) ?? FirstPublic(ip6);
        if (ip is null) return "";
        if (_cache.TryGetValue(ip, out var v)) return v;
        if (_inflight.TryAdd(ip, 0)) _ = ResolveAsync(ip); // arka planda çöz, bu sefer boş dön
        return "";
    }

    private static string? FirstPublic(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || ip == "—") return null;
        if (!IPAddress.TryParse(ip.Trim(), out var addr)) return null;
        return IsPrivate(addr) ? null : addr.ToString();
    }

    private static bool IsPrivate(IPAddress a)
    {
        if (IPAddress.IsLoopback(a)) return true;
        var b = a.GetAddressBytes();
        if (a.AddressFamily == AddressFamily.InterNetwork)
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254);
        // IPv6: loopback (üstte), ULA (fc00::/7), link-local (fe80::/10)
        return b[0] == 0xfd || b[0] == 0xfc || (b[0] == 0xfe && (b[1] & 0xc0) == 0x80);
    }

    private static async Task ResolveAsync(string ip)
    {
        try
        {
            var url = $"http://ip-api.com/json/{ip}?fields=status,regionName&lang=tr";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("status", out var st) && st.GetString() == "success";
            var region = ok && root.TryGetProperty("regionName", out var rn) ? rn.GetString() : null;
            _cache[ip] = string.IsNullOrWhiteSpace(region) ? "" : region!;
        }
        catch { _cache[ip] = ""; } // başarısız → boş önbelleğe al (hammering'i önler)
        finally { _inflight.TryRemove(ip, out _); }
    }
}
