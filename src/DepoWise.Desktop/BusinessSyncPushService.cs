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
    // ⚠️ 300sn: rutin push artık DELTA (yalnız değişenler, aşağıya bak) → küçük ve hızlı; ama ilk kurulum/
    // manuel TAM eşitleme büyük firmada (2508+ kayıt) uzun sürebilir → geniş zaman aşımı (kullanıcı bulgusu
    // 2026-07-19: DESKTOP-SIKIB3U'da tam snapshot 120sn'yi aşıp zaman aşımına uğruyordu).
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };

    /// <summary>Saklı JWT (ServerAuthClient.Token) ile firmanın iş snapshot'ını sunucuya gönderir. Hata → sessiz.
    /// Giriş, "Eşitle" butonu ve periyodik döngü buradan çağırır (çevrimdışıysa token yoktur → atlar).
    ///
    /// ⚠️ PERFORMANS (kullanıcı bulgusu 2026-07-19 — "menüler arasında geçişte donma"): <c>BuildSnapshot</c>
    /// SENKRON çalışır (binlerce satırı okuyan ADO.NET döngüsü). Periyodik zamanlayıcı (ShellViewModel
    /// _connTimer.Tick) ve "Eşitle" butonu bu metodu ARAYÜZ İŞ PARÇACIĞININ devamı olarak çağırıyordu →
    /// büyük firmada (binlerce kayıt) bu senkron iş arayüzü DONDURUYORDU. <see cref="Task.Run"/> ile arka
    /// plana alındı — kim çağırırsa çağırsın arayüz artık bloklanmaz.</summary>
    /// <param name="sinceVersion">DELTA sınırı: >0 ise yalnız updated_at&gt;sinceVersion satırlar gönderilir
    /// (rutin eşitleme küçük/hızlı). 0 ise TAM snapshot (ilk kurulum / manuel "Eşitle" — büyük olabilir,
    /// geniş zaman aşımı var). Kullanıcı bulgusu 2026-07-19: server'da zaten olan 2508 kaydı her seferinde
    /// yeniden göndermek zaman aşımına yol açıyordu; artık server sürümünden yenisi gönderilir.</param>
    public static async Task PushAsync(long sinceVersion = 0)
    {
        var url = ResolveServerUrl();
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(companyId)) return;
        // Push öncesi token süresi yaklaştıysa yenile (uzun oturumda sync sessizce durmasın).
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return;
        SyncLog.Write("PUSH başladı", $"since={sinceVersion}");
        try
        {
            var baseUrl = url!.TrimEnd('/');
            // Yerel snapshot üret (paylaşılan Infrastructure servisi) + gönder — ARKA PLANDA (arayüzü bloklamasın).
            var machineName = Environment.MachineName;
            var snapshot = await Task.Run(() => new BusinessSyncService(DesktopServices.Factory).BuildSnapshot(companyId!, machineName, sinceVersion));
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/sync/business-push")
            {
                Content = new StringContent(snapshot, Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            // 401 → token beklenmedik şekilde geçersiz; bir kez yenilemeyi dene (yine olmazsa SessionExpired sinyali).
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                await ServerAuthClient.EnsureFreshTokenAsync();

            // Z2 (2026-07-19): sunucu yanıtını OKU — "eşitlendi" sanılan sessiz atlamayı bitir.
            // Sunucu {upserted, skipped, errors} döndürür; eskiden yalnız HTTP durumuna bakılıp gövde atılıyordu.
            var bodyText = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode)
            {
                var r = ParseResult(bodyText);
                LastPushResult = r;
                LastPushFailed = false;
                SyncLog.Write("PUSH bitti", $"upserted={r.Upserted} skipped={r.Skipped}");
                if (r.HasProblem) // sunucu bazı satırları uygulamadı (yetki/doğrulama/hata) → GÖRÜNÜR kıl
                    SyncLog.Write("PUSH atlanan/hatalı", $"skipped={r.Skipped}; " +
                        (r.Errors.Count > 0 ? "errors: " + string.Join(" | ", r.Errors) : "errors: (liste boş)"));
            }
            else
            {
                LastPushFailed = true;
                SyncLog.Write("PUSH reddedildi", $"HTTP {(int)resp.StatusCode} {resp.StatusCode}; gövde: {Truncate(bodyText, 500)}");
            }
        }
        catch (Exception ex) { LastPushFailed = true; SyncLog.Write("PUSH hata", ex.Message); /* sync best-effort; ağ dönünce sonraki tur tekrar dener */ }
    }

    /// <summary>Sunucunun push yanıtı: kaç satır uygulandı / atlandı + hata mesajları (max 20). Z2 (2026-07-19):
    /// istemci eskiden bu yanıtı okumuyordu → atlanan kayıtlar sessizce kayboluyordu. Artık üst bar + log gösterir.</summary>
    public sealed record PushResult(int Upserted, int Skipped, System.Collections.Generic.IReadOnlyList<string> Errors)
    {
        /// <summary>Sunucu en az bir satırı uygulamadı mı (atlandı ya da hata) — kullanıcıya uyarı çıkar.</summary>
        public bool HasProblem => Skipped > 0 || Errors.Count > 0;
    }

    /// <summary>Son BAŞARILI push'un sunucu sonucu (upserted/skipped/errors). Ağ hatasında değişmez (bkz. LastPushFailed).</summary>
    public static PushResult? LastPushResult { get; private set; }

    /// <summary>Sunucu push yanıt gövdesini (JSON) PushResult'a çevirir. Saf/yan-etkisiz → birim testi kolay.</summary>
    public static PushResult ParseResult(string json)
    {
        int upserted = 0, skipped = 0;
        var errors = new System.Collections.Generic.List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("upserted", out var u) && u.ValueKind == JsonValueKind.Number) upserted = u.GetInt32();
                if (root.TryGetProperty("skipped", out var s) && s.ValueKind == JsonValueKind.Number) skipped = s.GetInt32();
                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
                    foreach (var it in e.EnumerateArray())
                        if (it.ValueKind == JsonValueKind.String) errors.Add(it.GetString() ?? "");
            }
        }
        catch { /* bozuk/boş gövde → sıfır sonuç */ }
        return new PushResult(upserted, skipped, errors);
    }

    private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";

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
