using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop;

/// <summary>
/// ⭐ M-30 (AR-GE 2026-10-18) — DÖNEM KİLİDİ masaüstü istemcisi. Kilit SUNUCU otoritelidir: yönetici masaüstünden
/// ayarlarken çevrimiçi olmalıdır (çevrimdışı yerel ayar diğer bilgisayarlara ulaşmaz, sunucu da bilmezdi). Başarılı
/// yanıttan sonra yerel firma satırı ve oturum ANINDA güncellenir; diğer bilgisayarlar bir sonraki girişte alır.
/// </summary>
public static class DonemKilidiIstemci
{
    private static readonly HttpClient _http = DepoWise.Infrastructure.Net.HttpIstemci.Yeni(TimeSpan.FromSeconds(12));

    /// <summary>Kilidi ayarlar (null = kaldırır). Hata metni döner; başarıda null.</summary>
    public static async Task<string?> AyarlaAsync(SessionContext s, long? tarih)
    {
        var url = LookupSyncService.ResolveServerUrl()?.TrimEnd('/');
        var token = string.IsNullOrWhiteSpace(url) ? null : ServerAuthClient.JetonFor(url!, s.UserId);
        if (string.IsNullOrWhiteSpace(token))
            return "Dönem kilidi için sunucuya bağlı olmak gerekir. İnternet bağlantısını kontrol edip yeniden giriş yapın.";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Put, url + "/api/company/period-lock")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { tarih }), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var govde = await resp.Content.ReadAsStringAsync();
            using var doc = string.IsNullOrWhiteSpace(govde) ? null : JsonDocument.Parse(govde);
            if (!resp.IsSuccessStatusCode)
                return doc?.RootElement.TryGetProperty("error", out var e) == true ? e.GetString() : $"Sunucu hatası ({(int)resp.StatusCode}).";
            long? yeni = doc?.RootElement.TryGetProperty("tarih", out var t) == true && t.ValueKind == JsonValueKind.Number
                ? t.GetInt64() : null;
            YereleYaz(s, yeni);
            return null;
        }
        catch (Exception ex) { return "Sunucuya ulaşılamadı: " + ex.Message; }
    }

    /// <summary>Sunucunun onayladığı değeri yerel firma satırına ve açık oturuma yazar.</summary>
    private static void YereleYaz(SessionContext s, long? kilit)
    {
        using (var conn = DesktopServices.Factory.Create())
        {
            if (DbIntrospect.ColumnExists(conn, null, "companies", "period_lock_date"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE companies SET period_lock_date=@k WHERE id=@c;";
                cmd.AddWithValue("@k", (object?)kilit ?? DBNull.Value);
                cmd.AddWithValue("@c", s.CompanyId);
                cmd.ExecuteNonQuery();
            }
        }
        DonemKilidiService.Unut(s.CompanyId);
        s.DonemKilidi = kilit;
    }
}
