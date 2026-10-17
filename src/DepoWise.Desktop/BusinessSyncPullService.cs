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

    // Çekme hataları sessizdir (best-effort). ⚠️ PERFORMANS (bkz. BusinessSyncPushService.PushAsync üstteki not): JSON
    // ayrıştırma + yerel upsert (ApplyPull) SENKRON ve binlerce satırda yavaş olabilir → Task.Run ile arka planda;
    // periyodik zamanlayıcı/Eşitle butonu arayüzü dondurmasın.
    // ⭐ M-03 (2026-10-17): SNK-09'un "alınan en büyük damga" imleci (AlinanWatermark/AlinanEnBuyukDamga) KALDIRILDI —
    // değeri HİÇBİR YERDE atanmıyordu (e5d04f2'den beri) ve imleç hiç ilerlemiyordu. Yerine sunucu işlem-kimliği ufku
    // (PullDeltaAsync / "sync_pull_txid") geldi; o, damga eşitliği ve saat sorunlarını kökten kaldırır.

    // ═══ ⭐ M-03 (AR-GE 2026-10-17) — ÇEKME v2: SUNUCU İŞLEM KİMLİĞİ UFKU ═══
    // İmleç artık istemci saatli damga DEĞİL, sunucunun bir önceki yanıtta verdiği ufuktur (nextAfter). Geç gelen/geri
    // damgalı satır ve saati yanlış makine imleci bozamaz; değişmeyen veri tekrar inmez (eski hatada her tur tam paket).
    // Sunucu v2 ucunu bilmiyorsa (404) bu oturumda eski yönteme dönülür → sürüm uyumsuzluğunda eşitleme kesilmez.
    private const string TxidKey = "sync_pull_txid";

    /// <summary>null = henüz bilinmiyor · true = sunucu v2 destekliyor · false = eski sunucu (eski yöntem).</summary>
    public static bool? V2Destekli { get; private set; }

    private static long OkuImlec(string companyId)
    {
        try { return long.TryParse(DesktopServices.Settings.Get(companyId, TxidKey), out var v) ? v : 0; }
        catch { return 0; }
    }

    private static void YazImlec(string companyId, long v)
    {
        try { DesktopServices.Settings.Set(companyId, TxidKey, v.ToString(), DesktopServices.Session?.UserId ?? ""); }
        catch { /* yazılamazsa sonraki çekim eski imleçle tekrarlar — veri kaybı değil, yalnız tekrar */ }
    }

    /// <summary>v2 çekim: <paramref name="tam"/> = true ise ufuk 0'dan (tam paket), değilse saklı imleçten.
    /// Döner: true başarı · false hata · null sunucu v2 bilmiyor (çağıran eski yönteme düşer).</summary>
    private static async Task<bool?> PullV2Async(bool tam)
    {
        var url = ResolveServerUrl();
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(companyId)) return false;
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var after = tam ? 0 : OkuImlec(companyId!);
        using var req = new HttpRequestMessage(HttpMethod.Get, url!.TrimEnd('/') + "/api/sync/business-pull-v2?after=" + after);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) { V2Destekli = false; return null; }
        if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { LastFailure = SyncFailureKind.Permanent; await ServerAuthClient.EnsureFreshTokenAsync(); return false; }
        if (!resp.IsSuccessStatusCode) { LastFailure = SyncFailureClassifier.FromStatus(resp.StatusCode); return false; }
        V2Destekli = true;
        var json = await resp.Content.ReadAsStringAsync();
        long sonraki = 0;
        await Task.Run(() =>
        {
            using var doc = JsonDocument.Parse(json);
            var sonuc = new BusinessSyncService(DesktopServices.Factory).ApplyPull(companyId!, doc.RootElement, Exclude);
            SonAlinanSayisi = sonuc.Upserted;
            if (doc.RootElement.TryGetProperty("nextAfter", out var na) && na.TryGetInt64(out var n)) sonraki = n;
        });
        // İmleç YALNIZ uygulama BAŞARIYLA bittikten sonra ilerler (yarıda kalan uygulama → aynı aralık tekrar çekilir;
        // satır kimliği + operation_id ile tekrar zararsızdır).
        if (sonraki > 0) YazImlec(companyId!, sonraki);
        try { DesktopServices.Settings.Set(companyId!, "sync_last_pull_ok", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(), DesktopServices.Session?.UserId ?? ""); } catch { }
        LastFailure = SyncFailureKind.None;
        return true;
    }

    /// <summary>
    /// Rutin (delta) çekim — 15 sn turu ve 60 sn güvenlik turu bunu çağırır. Sunucu v2 destekliyorsa saklı ufuktan
    /// çeker; desteklemiyorsa (eski sunucu / geri dönüş) eski uçtan TAM paket çeker. Eski uca damga imleci VERİLMEZ:
    /// "sunucu sürümü" imleci aynı milisaniyede yazılan satırı kaybettirir (SNK-09) — tam paket yavaş ama kayıpsızdır
    /// ve M-03 öncesinin fiilî davranışıdır (eski imleç hiç ilerlemiyordu).
    /// </summary>
    public static async Task<bool> PullDeltaAsync()
    {
        LastFailure = SyncFailureKind.None;
        if (V2Destekli != false)
        {
            try
            {
                var r = await PullV2Async(tam: false);
                if (r is { } ok) return ok;
            }
            catch (Exception ex) { LastFailure = SyncFailureClassifier.FromException(ex); return false; }
        }
        return await PullLegacyAsync(0);
    }

    /// <summary>
    /// Sunucudan çekip yerele uygular. <paramref name="sinceVersion"/> = 0 → TAM çekim (giriş, elle eşitle, yereli sıfırla):
    /// v2 sunucuda ufuk 0'dan alınır ve imleç sıfırdan kurulur. &gt; 0 → eski delta (geriye uyum).
    /// </summary>
    public static async Task<bool> PullAsync(long sinceVersion = 0)
    {
        if (sinceVersion == 0 && V2Destekli != false)
        {
            LastFailure = SyncFailureKind.None;
            try
            {
                var r = await PullV2Async(tam: true);
                if (r is { } ok) return ok;
            }
            catch (Exception ex) { LastFailure = SyncFailureClassifier.FromException(ex); return false; }
        }
        return await PullLegacyAsync(sinceVersion);
    }

    /// <summary>ESKİ yöntem (istemci damgası, <c>/api/sync/business-pull</c>) — yalnız v2 bilmeyen sunucuda.</summary>
    private static async Task<bool> PullLegacyAsync(long sinceVersion)
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
                var sonuc = new BusinessSyncService(DesktopServices.Factory).ApplyPull(companyId!, doc.RootElement, Exclude);
                SonAlinanSayisi = sonuc.Upserted;   // 2026-10-04: üst bardaki "gelen" sayacı
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
    /// <summary>2026-10-04: son başarılı çekmede yerele uygulanan kayıt sayısı (üst bar "↓ gelen").</summary>
    public static int SonAlinanSayisi { get; private set; }

    /// <summary>
    /// ⭐ 2026-10-04 — ANLIK EŞİTLEME: sunucuda <paramref name="since"/>'ten yeni değişiklik olana kadar
    /// (en fazla ~25 sn) bekler. Değişiklik olursa sunucu HEMEN yanıt verir → masaüstü 15 sn beklemez.
    /// null = çevrimdışı / sunucu bu ucu bilmiyor (eski sürüm) / hata → çağıran periyodik tura güvenir.
    /// </summary>
    public static async Task<(long Version, bool Changed)?> WaitForChangeAsync(long since, System.Threading.CancellationToken ct = default)
    {
        var url = ResolveServerUrl();
        var companyId = DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(companyId)) return null;
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                url!.TrimEnd('/') + "/api/sync/wait?since=" + (since < 0 ? 0 : since));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var r = doc.RootElement;
            return (r.GetProperty("version").GetInt64(), r.TryGetProperty("changed", out var c) && c.GetBoolean());
        }
        catch { return null; }
    }

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
