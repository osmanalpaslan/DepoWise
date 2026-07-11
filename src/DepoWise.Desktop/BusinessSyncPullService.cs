using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Desktop;

/// <summary>
/// İş verisi GERİ-ÇEKME (server → masaüstü): firmanın sunucudaki iş verisini çeker ve YEREL DB'ye uygular (LWW).
/// Böylece bu makine, AYNI firmadaki DİĞER makinelerin girdiği veriyi görür (çok makineli görünürlük).
/// Push'un simetriğidir; birlikte çalışır. NOT: stock_balances (türetilmiş) hariç tutulur — sunucu-otoriteli
/// bakiye hesabı 2b'de gelecek (o zamana kadar bakiye yereldeki hareketlerden hesaplanır). Çevrimdışı → sessiz.
/// </summary>
public static class BusinessSyncPullService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    // Geri-çekmede uygulanmayan tablolar (türetilmiş/otoriteli olması gerekenler).
    private static readonly System.Collections.Generic.HashSet<string> Exclude = new(StringComparer.Ordinal) { "stock_balances" };

    /// <summary>Sunucudan firmanın iş snapshot'ını çekip yerele uygular. Hata → sessiz (best-effort).</summary>
    public static async Task PullAsync()
    {
        var url = ResolveServerUrl();
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(companyId)) return;
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url!.TrimEnd('/') + "/api/sync/business-pull");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) { await ServerAuthClient.EnsureFreshTokenAsync(); return; }
            if (!resp.IsSuccessStatusCode) return;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            // Trusted sunucu verisi → yerele uygula (yazma-yetkisi filtresi yok); stock_balances hariç.
            new BusinessSyncService(DesktopServices.Factory).ApplyPull(companyId!, doc.RootElement, Exclude);
        }
        catch { /* sessiz — ağ dönünce sonraki tur tekrar dener */ }
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (System.IO.File.Exists(path))
            {
                var v = System.IO.File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { }
        return "https://depowise-erp.fly.dev";
    }
}
