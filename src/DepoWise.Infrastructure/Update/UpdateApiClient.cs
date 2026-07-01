using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DepoWise.Application.Update;

namespace DepoWise.Infrastructure.Update;

/// <summary>
/// Masaüstü → sunucu (DepoWise.Api) güncelleme istemcisi. `/api/releases/latest`'ten en güncel sürümü çeker.
/// İndirme adresi göreli (/api/releases/.../download) ise sunucu tabanı ile birleştirilir. Yapılandırma yoksa null.
/// </summary>
public sealed class UpdateApiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Sunucudaki en güncel sürüm (indirme adresi mutlak URL'e çevrilir). Hata/boşta null.</summary>
    public async Task<UpdatePackage?> GetLatestAsync(string serverBaseUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serverBaseUrl)) return null;
        try
        {
            var baseUri = new Uri(serverBaseUrl.TrimEnd('/') + "/");
            using var resp = await Http.GetAsync(new Uri(baseUri, "api/releases/latest"), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body) || body == "null") return null;
            var pkg = JsonSerializer.Deserialize<UpdatePackage>(body, Json);
            if (pkg is null) return null;
            // Göreli indirme adresini mutlaklaştır
            if (!string.IsNullOrWhiteSpace(pkg.DownloadUrl) && pkg.DownloadUrl.StartsWith("/", StringComparison.Ordinal))
                pkg = pkg with { DownloadUrl = new Uri(baseUri, pkg.DownloadUrl.TrimStart('/')).ToString() };
            return pkg;
        }
        catch { return null; }
    }
}
