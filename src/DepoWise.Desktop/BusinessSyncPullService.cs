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
    // ⚠️ 300sn — bkz. BusinessSyncPushService (delta ile rutin çekme küçük; ilk/tam çekme büyük olabilir).
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };
    // Senkron 2b sonrası: stock_balances artık SUNUCU-OTORİTELİ (push sonrası sunucu hareketlerden hesaplar) →
    // geri-çekmede uygulanır (LWW; sunucunun birleşik/doğru bakiyesi gelir). Hariç tablo kalmadı.
    private static readonly System.Collections.Generic.HashSet<string>? Exclude = null;

    /// <summary>Sunucudan firmanın iş snapshot'ını çekip yerele uygular. Hata → sessiz (best-effort).
    ///
    /// ⚠️ PERFORMANS (bkz. BusinessSyncPushService.PushAsync üstteki not — aynı kök sebep): JSON ayrıştırma +
    /// yerel upsert döngüsü (<c>ApplyPull</c>) SENKRON ve binlerce satırda yavaş olabilir; <see cref="Task.Run"/>
    /// ile arka plana alındı ki periyodik zamanlayıcı/Eşitle butonu arayüzü dondurmasın.</summary>
    /// <param name="sinceVersion">DELTA: >0 ise sunucudan yalnız updated_at&gt;sinceVersion satırlar çekilir
    /// (rutin çekme küçük/hızlı). 0 ise TAM snapshot (ilk giriş / manuel tam eşitleme).</param>
    /// <returns>true = başarıyla çekilip uygulandı (kabuk pull imlecini o zaman ilerletir); false = ulaşılamadı/hata.</returns>
    public static async Task<bool> PullAsync(long sinceVersion = 0)
    {
        var url = ResolveServerUrl();
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(companyId)) return false;
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var pullUrl = url!.TrimEnd('/') + "/api/sync/business-pull" + (sinceVersion > 0 ? "?since=" + sinceVersion : "");
            using var req = new HttpRequestMessage(HttpMethod.Get, pullUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized) { await ServerAuthClient.EnsureFreshTokenAsync(); return false; }
            if (!resp.IsSuccessStatusCode) return false;
            var json = await resp.Content.ReadAsStringAsync();
            // Trusted sunucu verisi → yerele uygula (yazma-yetkisi filtresi yok); stock_balances hariç.
            // Ağır JSON parse + upsert döngüsü ARKA PLANDA (arayüzü bloklamasın).
            await Task.Run(() =>
            {
                using var doc = JsonDocument.Parse(json);
                new BusinessSyncService(DesktopServices.Factory).ApplyPull(companyId!, doc.RootElement, Exclude);
            });
            return true;
        }
        catch { return false; /* sessiz — ağ dönünce sonraki tur tekrar dener */ }
    }

    /// <summary>Sunucudaki firmanın iş verisi SÜRÜMÜ (en büyük updated_at) — ucuz tek sayı. Tam snapshot
    /// çekmeden "değişti mi?" için (kullanıcı isteği 2026-07-19: anlık ama bant israfsız). null = ulaşılamadı.</summary>
    public static async Task<long?> GetServerVersionAsync()
    {
        var url = ResolveServerUrl();
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(companyId)) return null;
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url!.TrimEnd('/') + "/api/sync/business-version");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : (long?)null;
        }
        catch { return null; }
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
