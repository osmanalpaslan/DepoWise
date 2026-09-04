using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Infrastructure.Sync;

namespace DepoWise.Desktop;

/// <summary>
/// SNK-03 — senkron turunun son başarısızlık TÜRÜ. Backoff yalnız <see cref="Transient"/> için uygulanır.
/// Bu ayrım olmadan (eskiden olduğu gibi) 401/403 ile 503 aynı kovaya düşer ve yetki hatası da geciktirilirdi.
/// </summary>
public enum SyncFailureKind
{
    /// <summary>Başarısızlık yok ya da hiç istek denenmedi (URL/token yok) → backoff YOK.</summary>
    None = 0,
    /// <summary>Geçici: ağ/DNS/bağlantı/zaman aşımı, HTTP 5xx, HTTP 429 → backoff VAR.</summary>
    Transient,
    /// <summary>Kalıcı: 401/403/diğer 4xx, JSON/format/veri hatası → backoff YOK (normal hata akışı).</summary>
    Permanent,
}

/// <summary>SNK-03 — HTTP yanıtını / istisnayı backoff açısından sınıflandırır (tek yer, iki servis kullanır).</summary>
internal static class SyncFailureClassifier
{
    /// <summary>5xx ve 429 geçici; 401/403 dahil diğer tüm başarısız kodlar kalıcı.</summary>
    public static SyncFailureKind FromStatus(System.Net.HttpStatusCode status) =>
        (int)status >= 500 || (int)status == 429 ? SyncFailureKind.Transient : SyncFailureKind.Permanent;

    /// <summary>Taşıma katmanı istisnaları geçici; JSON/veri/diğer istisnalar kalıcı (tekrar denemek düzeltmez).
    /// <c>TaskCanceledException</c> burada zaman aşımıdır (istek iptali kullanılmıyor).</summary>
    public static SyncFailureKind FromException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException or System.IO.IOException
            ? SyncFailureKind.Transient
            : SyncFailureKind.Permanent;
}

/// <summary>
/// İş verisi GERİ-ÇEKME (server → masaüstü): firmanın sunucudaki iş verisini çeker ve YEREL DB'ye uygular (LWW).
/// Böylece bu makine, AYNI firmadaki DİĞER makinelerin girdiği veriyi görür (çok makineli görünürlük).
/// Push'un simetriğidir; birlikte çalışır. Çevrimdışı → sessiz.
///
/// ⚠️ BAKİYE (RPR-V1, 2026-08-27): <c>stock_balances</c> TÜRETİLMİŞ veridir ve senkron paketinde
/// TAŞINMAZ (bkz. <see cref="BusinessSyncService.Tables"/> — SNK-11 ile listeden çıkarıldı);
/// otoriter kaynak <c>stock_movements</c> defteridir. Bakiye, çekilen veri yerele uygulandıktan
/// SONRA defterden yeniden hesaplanır — bu artık <see cref="BusinessSyncService.ApplyPull"/>
/// içindedir, yani çağıranın hatırlamasına bağlı DEĞİLDİR.
/// </summary>
public static class BusinessSyncPullService
{
    // ⚠️ 300sn — bkz. BusinessSyncPushService (delta ile rutin çekme küçük; ilk/tam çekme büyük olabilir).
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(300) };

    /// <summary>SNK-03 — son <see cref="PullAsync"/> / <see cref="GetServerVersionAsync"/> çağrısının
    /// başarısızlık türü. Metot imzaları DEĞİŞMEDİ (5 çağıran etkilenmesin); bilgi bu özellikle taşınır.
    /// Başarıda ve hiç istek denenmediğinde <see cref="SyncFailureKind.None"/>.</summary>
    public static SyncFailureKind LastFailure { get; private set; } = SyncFailureKind.None;
    // Uygulama sırasında ATLANACAK tablo yok. (Dikkat: bu, stock_balances'ın çekmeyle GELDİĞİ anlamına
    // GELMEZ — o tablo sunucunun ürettiği pakette zaten yoktur; bakiye ApplyPull sonunda defterden
    // hesaplanır. Eski yorum bunun tersini söylüyordu ve hatayı gizliyordu — RPR-V1.)
    private static readonly System.Collections.Generic.HashSet<string>? Exclude = null;

    /// <summary>Sunucudan firmanın iş snapshot'ını çekip yerele uygular. Hata → sessiz (best-effort).
    ///
    /// ⚠️ PERFORMANS (bkz. BusinessSyncPushService.PushAsync üstteki not — aynı kök sebep): JSON ayrıştırma +
    /// yerel upsert döngüsü (<c>ApplyPull</c>) SENKRON ve binlerce satırda yavaş olabilir; <see cref="Task.Run"/>
    /// ile arka plana alındı ki periyodik zamanlayıcı/Eşitle butonu arayüzü dondurmasın.</summary>
    /// <param name="sinceVersion">DELTA: >0 ise sunucudan yalnız updated_at&gt;sinceVersion satırlar çekilir
    /// (rutin çekme küçük/hızlı). 0 ise TAM snapshot (ilk giriş / manuel tam eşitleme).</param>
    /// <returns>true = başarıyla çekilip uygulandı (kabuk pull imlecini o zaman ilerletir); false = ulaşılamadı/hata.</returns>
    /// <summary>
    /// ⭐ SNK-09 — SON ÇEKİMDE GERÇEKTEN ALINAN EN BÜYÜK DAMGA (pull watermark).
    /// <c>null</c> = paket boştu ya da damga okunamadı → çağıran imleci İLERLETMEZ.
    /// </summary>
    public static long? AlinanWatermark { get; private set; }

    /// <summary>
    /// 🔴 SNK-09 — KAPATILAN SESSİZ VERİ KAYBI.
    ///
    /// <b>Eski davranış:</b> çekimden sonra imleç, sunucunun bildirdiği GLOBAL SÜRÜM
    /// (<c>MAX(updated_at)</c>) olarak saklanıyordu. Sunucu sürümü okunduktan sonra <b>aynı
    /// milisaniyede</b> yazılan bir satır bir daha ASLA gelmiyordu: bir sonraki çekim
    /// <c>&gt; imleç</c> sorduğu için damgası eşit olan satır daima eleniyordu. Kayıt sunucuda
    /// vardı, bu makinede hiç görünmüyordu ve hiçbir hata da üretmiyordu.
    ///
    /// <b>Bu, Z4'ün PUSH tarafında çözdüğü hatanın PULL karşılığıdır</b> ve aynı çözümle giderildi:
    /// imleç artık "sunucunun global max'ı" değil, <b>gerçekten alınan satırların en büyük
    /// damgası</b>dır. Böylece <c>&gt;</c> koşulu tam olarak alınanı dışlar — ne kayıp ne tekrar.
    /// (<c>BuildSnapshot</c> içindeki <c>&gt;</c> bilinçli olarak DEĞİŞTİRİLMEDİ: aynı metot push'ta
    /// da kullanılıyor ve orada watermark semantiği zaten doğruydu.)
    /// </summary>
    private static long? AlinanEnBuyukDamga(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("tables", out var tablolar)
            || tablolar.ValueKind != JsonValueKind.Object) return null;

        long enBuyuk = 0;
        foreach (var tablo in tablolar.EnumerateObject())
        {
            if (tablo.Value.ValueKind != JsonValueKind.Array) continue;
            foreach (var satir in tablo.Value.EnumerateArray())
            {
                if (satir.ValueKind != JsonValueKind.Object) continue;
                // Damga sunucudaki kuralla AYNI: updated_at, yoksa created_at (StampColumn).
                foreach (var alan in new[] { "updated_at", "created_at" })
                {
                    if (!satir.TryGetProperty(alan, out var v)) continue;
                    long? d = v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n
                            : v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var sn) ? sn
                            : null;
                    if (d is { } dd && dd > enBuyuk) enBuyuk = dd;
                    break;   // updated_at varsa created_at'a bakma (sunucu da böyle yapar)
                }
            }
        }
        return enBuyuk > 0 ? enBuyuk : null;
    }

    public static async Task<bool> PullAsync(long sinceVersion = 0)
    {
        LastFailure = SyncFailureKind.None;   // SNK-03: bayat değer kalmasın (istek denenmeden dönülebilir)
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
            // 401: token yenilenir; KALICI sayılır → backoff tetiklemez (yetki akışı normal seyrinde kalır).
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { LastFailure = SyncFailureKind.Permanent; await ServerAuthClient.EnsureFreshTokenAsync(); return false; }
            if (!resp.IsSuccessStatusCode) { LastFailure = SyncFailureClassifier.FromStatus(resp.StatusCode); return false; }
            var json = await resp.Content.ReadAsStringAsync();
            // Trusted sunucu verisi → yerele uygula (yazma-yetkisi filtresi yok).
            // Ağır JSON parse + upsert döngüsü ARKA PLANDA (arayüzü bloklamasın).
            await Task.Run(() =>
            {
                using var doc = JsonDocument.Parse(json);
                new BusinessSyncService(DesktopServices.Factory).ApplyPull(companyId!, doc.RootElement, Exclude);
            });
            // Z5: son BAŞARILI çekme zamanı (senkron durum paneli gösterir).
            try { DesktopServices.Settings.Set(companyId!, "sync_last_pull_ok", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(), DesktopServices.Session?.UserId ?? ""); } catch { }
            LastFailure = SyncFailureKind.None;
            return true;
        }
        // Taşıma hatası → Transient (backoff); JSON/veri hatası → Permanent (tekrar denemek düzeltmez).
        catch (Exception ex) { LastFailure = SyncFailureClassifier.FromException(ex); return false; /* sessiz — ağ dönünce sonraki tur tekrar dener */ }
    }

    /// <summary>Sunucudaki firmanın iş verisi SÜRÜMÜ (en büyük updated_at) — ucuz tek sayı. Tam snapshot
    /// çekmeden "değişti mi?" için (kullanıcı isteği 2026-07-19: anlık ama bant israfsız). null = ulaşılamadı.</summary>
    public static async Task<long?> GetServerVersionAsync()
    {
        LastFailure = SyncFailureKind.None;   // SNK-03: bayat değer kalmasın
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
            if (!resp.IsSuccessStatusCode) { LastFailure = SyncFailureClassifier.FromStatus(resp.StatusCode); return null; }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            LastFailure = SyncFailureKind.None;
            return doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : (long?)null;
        }
        catch (Exception ex) { LastFailure = SyncFailureClassifier.FromException(ex); return null; }
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
