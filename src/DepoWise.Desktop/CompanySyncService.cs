using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// FİRMALAR SUNUCU (WEB) OTORİTELİDİR — masaüstündeki Firma Tanım ekranı artık YALNIZ YEREL DB'ye yazmaz.
///
/// Eski davranış (hata): masaüstünde eklenen/silinen firma yalnız yerel SQLite'a yazılıyordu; firmalar iş
/// senkronu tablolarında da olmadığı için sunucuya HİÇ ulaşmıyordu → web ile asla eşitlenmiyordu.
///
/// Yeni davranış: ekle/güncelle/sil/aktifleştir doğrudan SUNUCU API'sine gider (çevrimiçi zorunlu), ardından
/// sunucunun firma listesi yerel DB'ye AYNALANIR (sunucuda olmayan yerel firma pasife alınır) — şubelerdeki
/// (ADR-066) modelin aynısı.
/// </summary>
public static class CompanySyncService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Sunucuya bağlanılamıyorsa fırlatılır — çağıran kullanıcıya "çevrimiçi olun" der.</summary>
    public sealed class OfflineException : Exception
    {
        public OfflineException() : base(
            "Firma işlemleri sunucu üzerinden yapılır (firmalar web-otoriteli). İnternet bağlantısı gerekiyor.") { }
    }

    private static async Task<string> RequireAsync()
    {
        await ServerAuthClient.EnsureFreshTokenAsync();
        var url = ServerAuthClient.BaseUrl;
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) throw new OfflineException();
        return url!.TrimEnd('/');
    }

    private static HttpRequestMessage Req(HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServerAuthClient.Token);
        if (body is not null)
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return req;
    }

    private static async Task SendAsync(HttpRequestMessage req)
    {
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req); }
        catch { throw new OfflineException(); }
        using (resp)
        {
            if (resp.IsSuccessStatusCode) return;
            var body = await resp.Content.ReadAsStringAsync();
            string msg = $"Sunucu hatası ({(int)resp.StatusCode}).";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    msg = e.GetString() ?? msg;
            }
            catch { }
            throw new InvalidOperationException(msg);
        }
    }

    public static async Task CreateAsync(object dto)
        => await SendAsync(Req(HttpMethod.Post, await RequireAsync() + "/api/companies", dto));

    public static async Task UpdateAsync(string id, object dto)
        => await SendAsync(Req(HttpMethod.Put, await RequireAsync() + $"/api/companies/{id}", dto));

    public static async Task DeleteAsync(string id)
        => await SendAsync(Req(HttpMethod.Delete, await RequireAsync() + $"/api/companies/{id}"));

    public static async Task ReactivateAsync(string id)
        => await SendAsync(Req(HttpMethod.Post, await RequireAsync() + $"/api/companies/{id}/reactivate", new { }));

    /// <summary>
    /// Sunucudaki firma listesini yerel DB'ye aynalar: gelenler upsert edilir, sunucuda ARTIK OLMAYANLAR
    /// (silinmiş) yerelde pasife alınır. Çevrimdışıysa hiçbir şey yapılmaz (yereldekiyle devam).
    /// </summary>
    public static async Task MirrorLocalAsync()
    {
        string baseUrl;
        try { baseUrl = await RequireAsync(); }
        catch { return; }   // çevrimdışı → dokunma

        List<(string Id, string Name)> rows = new();
        try
        {
            using var resp = await _http.SendAsync(Req(HttpMethod.Get, baseUrl + "/api/companies"));
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = Str(el, "id"); var name = Str(el, "name");
                if (!string.IsNullOrEmpty(id)) rows.Add((id, string.IsNullOrEmpty(name) ? id : name));
            }
        }
        catch { return; }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            using var conn = DesktopServices.Factory.Create();
            foreach (var (id, name) in rows)
            {
                using var c = conn.CreateCommand();
                c.CommandText =
                    "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES($id,$n,$now,$now,1,0) " +
                    "ON CONFLICT(id) DO UPDATE SET name=$n, is_deleted=0, updated_at=$now;";
                c.Parameters.AddWithValue("$id", id);
                c.Parameters.AddWithValue("$n", name);
                c.Parameters.AddWithValue("$now", now);
                c.ExecuteNonQuery();
            }

            // Sunucunun listesinde OLMAYAN yerel firmalar silinmiştir → yerelde de pasife al.
            using (var del = conn.CreateCommand())
            {
                var names = new List<string>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var p = "$k" + i;
                    names.Add(p);
                    del.Parameters.AddWithValue(p, rows[i].Id);
                }
                del.CommandText =
                    "UPDATE companies SET is_deleted=1, updated_at=$now WHERE is_deleted=0" +
                    (names.Count > 0 ? " AND id NOT IN (" + string.Join(",", names) + ")" : "") + ";";
                del.Parameters.AddWithValue("$now", now);
                del.ExecuteNonQuery();
            }
        }
        catch { /* yerel yazma hatası senkronu bozmasın */ }
    }

    private static string Str(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}
