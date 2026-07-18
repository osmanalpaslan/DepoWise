using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Desktop;

/// <summary>
/// İş verisi SNAPSHOT push (Faz 2 — güvenli web görünürlüğü): giriş sonrası + periyodik olarak firmanın iş
/// tablolarını sunucuya gönderir → web adminleri masaüstünde girilen tüm veriyi (salt-okunur) görür.
/// Çevrimdışı/sunucusuz sessizce atlar. Tek yön (masaüstü → sunucu); tam çift-yönlü birleşme sonraki fazda.
/// </summary>
public static class BusinessSyncPushService
{
    // ⚠️ 30sn'den 120sn'e çıkarıldı (kullanıcı bulgusu 2026-07-19): babanın ~2600 satırlık dosyası içeri
    // alındıktan sonra snapshot büyüdü; eski 30sn sınırı büyük firmalarda push'u SESSİZCE zaman aşımına
    // uğratıyordu (catch{} hatayı yutuyordu) → veri hiç sunucuya ulaşmıyordu (görünürde "eşitlenmiyor").
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

    /// <summary>Saklı JWT (ServerAuthClient.Token) ile firmanın iş snapshot'ını sunucuya gönderir. Hata → sessiz.
    /// Giriş, "Eşitle" butonu ve periyodik döngü buradan çağırır (çevrimdışıysa token yoktur → atlar).
    ///
    /// ⚠️ PERFORMANS (kullanıcı bulgusu 2026-07-19 — "menüler arasında geçişte donma"): <c>BuildSnapshot</c>
    /// SENKRON çalışır (binlerce satırı okuyan ADO.NET döngüsü). Periyodik zamanlayıcı (ShellViewModel
    /// _connTimer.Tick) ve "Eşitle" butonu bu metodu ARAYÜZ İŞ PARÇACIĞININ devamı olarak çağırıyordu →
    /// büyük firmada (binlerce kayıt) bu senkron iş arayüzü DONDURUYORDU. <see cref="Task.Run"/> ile arka
    /// plana alındı — kim çağırırsa çağırsın arayüz artık bloklanmaz.</summary>
    public static async Task PushAsync()
    {
        var url = ResolveServerUrl();
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(companyId)) return;
        // Push öncesi token süresi yaklaştıysa yenile (uzun oturumda sync sessizce durmasın).
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var baseUrl = url!.TrimEnd('/');
            // Yerel snapshot üret (paylaşılan Infrastructure servisi) + gönder — ARKA PLANDA (arayüzü bloklamasın).
            var machineName = Environment.MachineName;
            var snapshot = await Task.Run(() => new BusinessSyncService(DesktopServices.Factory).BuildSnapshot(companyId!, machineName));
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/sync/business-push")
            {
                Content = new StringContent(snapshot, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            // 401 → token beklenmedik şekilde geçersiz; bir kez yenilemeyi dene (yine olmazsa SessionExpired sinyali).
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                await ServerAuthClient.EnsureFreshTokenAsync();
            LastPushFailed = !resp.IsSuccessStatusCode;
        }
        catch { LastPushFailed = true; /* sync best-effort; ağ dönünce sonraki tur tekrar dener */ }
    }

    /// <summary>Son push denemesi başarısız mı oldu (zaman aşımı/ağ/sunucu hatası) — üst bar/tanı için.
    /// Kullanıcıya "eşitleniyor" sanılan sessiz başarısızlığı görünür kılmak amacıyla eklendi (2026-07-19).</summary>
    public static bool LastPushFailed { get; private set; }

    /// <summary>Personelin görmediği açık çakışmaları çeker (şube kapsamında). Gösterildikten sonra
    /// <see cref="MarkSeenAsync"/> çağrılmalı. Çevrimdışıysa boş liste.</summary>
    public static async Task<System.Collections.Generic.List<string>> GetUnseenConflictsAsync()
    {
        var result = new System.Collections.Generic.List<string>();
        var url = ResolveServerUrl();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) return result;
        try
        {
            var baseUrl = url!.TrimEnd('/');
            var branch = DesktopServices.CurrentBranchId;
            var uri = baseUrl + "/api/sync/conflicts/unseen" + (string.IsNullOrWhiteSpace(branch) ? "" : "?branchId=" + Uri.EscapeDataString(branch!));
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return result;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string label = e.TryGetProperty("entityLabel", out var l) ? l.GetString() ?? "" : "";
                string admin = e.TryGetProperty("adminName", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "";
                string winner = e.TryGetProperty("winnerText", out var w) ? w.GetString() ?? "" : "";
                var who = string.IsNullOrWhiteSpace(admin) ? "admin" : admin;
                result.Add($"• {label}: {who} ile çakışma — {winner}");
            }
        }
        catch { }
        return result;
    }

    /// <summary>Personel çakışma uyarıları gösterildi → sunucuda 'görüldü' işaretle.</summary>
    public static async Task MarkSeenAsync()
    {
        var url = ResolveServerUrl();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token)) return;
        try
        {
            var baseUrl = url!.TrimEnd('/');
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/sync/conflicts/seen")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { branchId = DesktopServices.CurrentBranchId }), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            _ = resp.IsSuccessStatusCode;
        }
        catch { }
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (File.Exists(path))
            {
                var v = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
        }
        catch { }
        return "https://depowise-erp.fly.dev";
    }
}
