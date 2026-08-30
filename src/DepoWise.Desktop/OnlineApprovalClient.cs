using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187, PK-EK-05 / İK-9) — ONAY YALNIZ ÇEVRİMİÇİ ═══
///
/// <b>Neden bu sınıf var.</b> İK-9 kesindir: <i>"çevrimdışıyken onay ekranından onay vermeye çalışırsa
/// hem engellenmeli hem uyarı mesajı verilmeli; sadece çevrimiçi onay verilebilir."</i> Masaüstü
/// eskiden onayı YEREL veritabanına yazıyor ve senkron kuyruğuyla (<c>sync_outbox</c>) sunucuya
/// taşıyordu — bu, tanımı gereği ÇEVRİMDIŞI ONAYDIR. Artık onay/ret <b>doğrudan sunucuya</b> gider.
///
/// <b>Sonuçlar (İK-9'un teknik karşılığı):</b>
///  • Bağlantı yoksa hiçbir şey YAZILMAZ — ne yerel tabloya, ne <c>sync_outbox</c>'a.
///  • Bekleyen/kuyruğa alınmış onay OLUŞMAZ; sonradan otomatik gönderim YOKTUR.
///  • Karar tek otoritede (sunucu) verilir → "ilk gelen kazanır" gibi bir çevrimdışı çözüm yoktur
///    ve SNK-05'in onayda LWW yasağı korunur.
///  • Onay zinciri (<c>approval_instance</c>/<c>approval_step</c>) masaüstüne hiç inmediği için
///    zincir kararı da yalnız sunucuda yürür.
///
/// Hata mesajları kullanıcıya doğrudan gösterilir; teknik ayrıntı sızdırılmaz.
/// </summary>
public static class OnlineApprovalClient
{
    private static readonly HttpClient _http = new() { Timeout = System.TimeSpan.FromSeconds(20) };

    /// <summary>Kullanıcıya gösterilen tek tip çevrimdışı uyarısı.</summary>
    public const string CevrimdisiUyari =
        "Onay işlemi yalnız çevrimiçiyken yapılabilir. Sunucuya bağlanılamadığı için işlem yapılmadı; " +
        "bağlantı kurulduğunda tekrar deneyin. (Çevrimdışı onay kaydedilmez.)";

    public static Task<(bool Ok, string Message)> ApproveAsync(string requestId)
        => GonderAsync($"/api/requests/{requestId}/approve", null);

    public static Task<(bool Ok, string Message)> RejectAsync(string requestId, string reason)
        => GonderAsync($"/api/requests/{requestId}/reject",
            "{\"reason\":" + JsonKacis(reason) + "}");

    // ── ALT FAZ 3: "Onaylamalarım" — ZİNCİR ADIMI kararları ────────────────────────────────
    // Aynı çevrimdışı sözleşmesi geçerlidir: bağlantı yoksa hiçbir şey yazılmaz, kuyruk oluşmaz.
    // Adım sahipliği/sıra/self-approval/eşzamanlılık kapıları SUNUCUDADIR; burada tekrarlanmaz.

    public static Task<(bool Ok, string Message)> ApproveStepAsync(string stepId)
        => GonderAsync($"/api/approvals/steps/{stepId}/approve", "{}");

    public static Task<(bool Ok, string Message)> RejectStepAsync(string stepId, string reason)
        => GonderAsync($"/api/approvals/steps/{stepId}/reject",
            "{\"reason\":" + JsonKacis(reason) + "}");

    /// <summary>"Onaylamalarım" listesini SUNUCUDAN çeker. Onay verisi yerelde YOKTUR (senkron dışı) —
    /// bu yüzden liste de yalnız çevrimiçi görüntülenebilir; çevrimdışıysa boş liste + uyarı döner.</summary>
    public static async Task<(bool Ok, string Message, System.Text.Json.JsonElement[] Rows)> MineAsync()
    {
        var token = ServerAuthClient.Token;
        var baseUrl = ServerAuthClient.BaseUrl;
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(baseUrl))
            return (false, CevrimdisiUyari, System.Array.Empty<System.Text.Json.JsonElement>());
        try
        {
            await ServerAuthClient.EnsureFreshTokenAsync();
            using var req = new HttpRequestMessage(HttpMethod.Get, baseUrl!.TrimEnd('/') + "/api/approvals/mine");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServerAuthClient.Token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (false, SunucuHatasi(await resp.Content.ReadAsStringAsync())
                               ?? $"Onay listesi alınamadı ({(int)resp.StatusCode}).",
                        System.Array.Empty<System.Text.Json.JsonElement>());

            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return (true, "", doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToArray());
        }
        catch (HttpRequestException) { return (false, CevrimdisiUyari, System.Array.Empty<System.Text.Json.JsonElement>()); }
        catch (TaskCanceledException) { return (false, CevrimdisiUyari, System.Array.Empty<System.Text.Json.JsonElement>()); }
    }

    private static async Task<(bool Ok, string Message)> GonderAsync(string path, string? body)
    {
        var token = ServerAuthClient.Token;
        var baseUrl = ServerAuthClient.BaseUrl;
        // ⭐ Çevrimdışı/oturumsuz: HİÇBİR yazma yapılmaz, kuyruk oluşturulmaz.
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(baseUrl))
            return (false, CevrimdisiUyari);

        try
        {
            await ServerAuthClient.EnsureFreshTokenAsync();
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl!.TrimEnd('/') + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServerAuthClient.Token);
            if (body is not null) req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return (true, "");

            var govde = await resp.Content.ReadAsStringAsync();
            return (false, SunucuHatasi(govde) ?? $"Sunucu işlemi reddetti ({(int)resp.StatusCode}).");
        }
        catch (HttpRequestException) { return (false, CevrimdisiUyari); }
        catch (TaskCanceledException) { return (false, CevrimdisiUyari); }
    }

    /// <summary>Ortak hata modeli: gövde <c>{"error":"..."}</c> ise mesajı çıkar.</summary>
    private static string? SunucuHatasi(string? govde)
    {
        if (string.IsNullOrWhiteSpace(govde)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(govde);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch { return null; }
    }

    private static string JsonKacis(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}
