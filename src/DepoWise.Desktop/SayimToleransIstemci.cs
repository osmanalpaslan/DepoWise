using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop;

/// <summary>
/// ⭐ M-28 (AR-GE 2026-10-18) — SAYIM FARK TOLERANSI masaüstü istemcisi. Dönem kilidiyle aynı yol (<see cref="DonemKilidiIstemci"/>):
/// ayar sunucu otoritelidir, yönetici çevrimiçi ayarlar; başarılı yanıttan sonra yerel firma satırı güncellenir, diğer
/// bilgisayarlar bir sonraki girişte alır.
/// </summary>
public static class SayimToleransIstemci
{
    private static readonly HttpClient _http = DepoWise.Infrastructure.Net.HttpIstemci.Yeni(TimeSpan.FromSeconds(12));

    /// <summary>Toleransı ayarlar (null = kontrol yok). Hata metni döner; başarıda null.</summary>
    public static async Task<string?> AyarlaAsync(SessionContext s, decimal? yuzde)
    {
        var url = LookupSyncService.ResolveServerUrl()?.TrimEnd('/');
        var token = string.IsNullOrWhiteSpace(url) ? null : ServerAuthClient.JetonFor(url!, s.UserId);
        if (string.IsNullOrWhiteSpace(token))
            return "Sayım toleransı için sunucuya bağlı olmak gerekir. İnternet bağlantısını kontrol edip yeniden giriş yapın.";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, url + "/api/stock/count-tolerance")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { tolerance = yuzde }), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var govde = await resp.Content.ReadAsStringAsync();
            using var doc = string.IsNullOrWhiteSpace(govde) ? null : JsonDocument.Parse(govde);
            if (!resp.IsSuccessStatusCode)
                return doc?.RootElement.TryGetProperty("error", out var e) == true ? e.GetString() : $"Sunucu hatası ({(int)resp.StatusCode}).";
            decimal? yeni = doc?.RootElement.TryGetProperty("tolerance", out var t) == true && t.ValueKind == JsonValueKind.Number ? t.GetDecimal() : null;
            using var conn = DesktopServices.Factory.Create();
            if (DbIntrospect.ColumnExists(conn, null, "companies", "count_tolerance_pct"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE companies SET count_tolerance_pct=@t WHERE id=@c;";
                cmd.AddWithValue("@t", yeni is { } v ? Money.Serialize(v) : DBNull.Value);
                cmd.AddWithValue("@c", s.CompanyId);
                cmd.ExecuteNonQuery();
            }
            return null;
        }
        catch (Exception ex) { return "Sunucuya ulaşılamadı: " + ex.Message; }
    }
}
