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
    /// <summary>SNK-03 — son push denemesinin başarısızlık TÜRÜ (backoff kararı için).
    /// <see cref="LastPushFailed"/> ve <see cref="LastPushResult"/>.HasProblem (Z3) ayrımı DEĞİŞMEDİ:
    /// Z3 "sunucu yanıt verdi ama satır atladı" durumudur ve kendi retry'ını yürütür → backoff'a girmez.</summary>
    public static SyncFailureKind LastFailure { get; private set; } = SyncFailureKind.None;

    public static async Task PushAsync()
    {
        LastFailure = SyncFailureKind.None;   // SNK-03: bayat değer kalmasın (gönderilecek yoksa çıkılabilir)
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
            var machineName = Environment.MachineName;
            var svc = new BusinessSyncService(DesktopServices.Factory);
            // ⚠️ Z4 (2026-07-19) KÖK NEDEN DÜZELTMESİ: eskiden "since = SUNUCU global max(updated_at)" kullanılıyordu.
            // Başka bir tablonun/makinenin daha YÜKSEK zaman damgası, bu makinenin KENDİ kayıtlarını "gönderilmiş
            // gibi" ATLATIYORDU (kanıtlı 94-araç ve personel bug'ı: yerelde vardı, sunucuya hiç ulaşmadı).
            // ARTIK: her makine KENDİ "son gönderilen watermark"ını (yerelde kalıcı) tutar; yalnız
            // updated_at > watermark olan — yani GERÇEKTEN GÖNDERİLMEMİŞ — satırlar gönderilir. Sunucu global
            // max'ına HİÇ bakılmaz → başka bir kaydın zaman damgası yüzünden atlama İMKÂNSIZDIR.
            // (since=0 yalnız ilk kurulumda; sürekli full push YOK, her şeyi tekrar gönderme YOK.)
            long pushWm = LoadPushWatermark(companyId!);
            long localV = await Task.Run(() => svc.CompanyVersion(companyId!));
            if (localV <= pushWm) return; // gönderilecek YENİ yerel değişiklik yok → boş push atma (bant israfı yok)
            SyncLog.Write("PUSH başladı", $"since(watermark)={pushWm} localV={localV}");
            // Yerel snapshot üret (paylaşılan Infrastructure servisi) + gönder — ARKA PLANDA (arayüzü bloklamasın).
            var snapshot = await Task.Run(() => svc.BuildSnapshot(companyId!, machineName, pushWm));
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

                // ── Z3 (2026-07-22) RETRY: sunucu HTTP-başarılı dönse bile bazı satırları UYGULAMAMIŞ olabilir
                // (skipped/errors). Eskiden watermark yine de ilerliyordu → o kayıtlar bir daha GÖNDERİLMİYORDU
                // ("11 kayıt gönderilemedi" şikâyeti). Artık: sorun varsa watermark İLERLEMEZ → aynı satırlar
                // sonraki turda otomatik yeniden denenir. Sonsuz döngü olmasın diye N denemeden sonra "poison":
                // watermark ilerletilir (kuyruk kilitlenmez) ve KALICI uyarı bırakılır (kullanıcı görür).
                if (!r.HasProblem)
                {
                    SavePushWatermark(companyId!, localV);
                    SetInt(companyId!, StuckKey, 0);
                    SetText(companyId!, PoisonKey, "");            // sorun çözüldü → kalıcı uyarıyı temizle
                    SetText(companyId!, "sync_last_push_ok", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()); // Z5
                    // ⭐ S1: kalıcı olarak atlanan satırlar varsa kuyruk KİLİTLENMEZ ama iz bırakılır.
                    // Bunlar öksüz/yinelenen kayıtlardır; tekrar denemek aynı sonucu verir.
                    if (r.HasPermanentSkips)
                        SyncLog.Write("PUSH bitti (kalıcı atlanan var)",
                            $"upserted={r.Upserted} kalıcı_atlanan={r.PermanentSkipped}; {string.Join(" | ", r.Errors)}");
                    else
                        SyncLog.Write("PUSH bitti", $"upserted={r.Upserted} skipped={r.Skipped}");
                }
                else
                {
                    var tries = GetInt(companyId!, StuckKey) + 1;
                    var errText = r.Errors.Count > 0 ? string.Join(" | ", r.Errors) : "(liste boş)";
                    SyncLog.Write("PUSH atlanan/hatalı",
                        $"yeniden_denenecek={r.Retryable} kalıcı_atlanan={r.PermanentSkipped} deneme={tries}/{MaxRetries}; errors: {errText}");
                    if (tries >= MaxRetries)
                    {
                        // Poison: aynı kayıtlar ısrarla reddediliyor (kalıcı sebep). Kuyruğu kilitleme —
                        // watermark'ı ilerlet ki DİĞER kayıtlar gönderilebilsin; sorunu kalıcı uyarıya taşı.
                        SavePushWatermark(companyId!, localV);
                        SetInt(companyId!, StuckKey, 0);
                        SetText(companyId!, PoisonKey, $"{r.Retryable}|{errText}");
                        SyncLog.Write("PUSH poison", $"{r.Skipped} kayıt {tries} denemede gönderilemedi → otomatik deneme durduruldu. {errText}");
                    }
                    else
                    {
                        // watermark İLERLETİLMEZ → sonraki turda aynı satırlar tekrar denenir
                        SetInt(companyId!, StuckKey, tries);
                    }
                }
            }
            else
            {
                LastPushFailed = true;
                LastFailure = SyncFailureClassifier.FromStatus(resp.StatusCode);   // SNK-03: 5xx/429 → backoff, 4xx → hayır
                SyncLog.Write("PUSH reddedildi", $"HTTP {(int)resp.StatusCode} {resp.StatusCode}; gövde: {Truncate(bodyText, 500)}");
            }
        }
        catch (Exception ex) { LastPushFailed = true; LastFailure = SyncFailureClassifier.FromException(ex); SyncLog.Write("PUSH hata", ex.Message); /* sync best-effort; ağ dönünce sonraki tur tekrar dener */ }
    }

    /// <summary>Sunucunun push yanıtı: kaç satır uygulandı / atlandı + hata mesajları (max 20). Z2 (2026-07-19):
    /// istemci eskiden bu yanıtı okumuyordu → atlanan kayıtlar sessizce kayboluyordu. Artık üst bar + log gösterir.</summary>
    public sealed record PushResult(int Upserted, int Skipped, System.Collections.Generic.IReadOnlyList<string> Errors,
        int PermanentSkipped = 0)
    {
        /// <summary>
        /// ⭐ S1 (2026-08-19) — GERÇEKTEN yeniden denenecek satır sayısı.
        ///
        /// Öksüz çocuk satırı (ebeveyni silinmiş), yinelenen doğal anahtar, kapsam/yetki kararı…
        /// bunlar tekrar denendiğinde de AYNI sonucu verir. Eskiden hepsi "atlandı" sayılıp gönderim
        /// damgası ilerletilmiyor, 5 turdan sonra kalıcı uyarı bırakılıyordu → kuyruk sonsuza kadar
        /// kirli kalıyordu (sahada 6 kayıtla yaşandı).
        /// </summary>
        public int Retryable => System.Math.Max(0, Skipped - PermanentSkipped);

        /// <summary>Yeniden deneme gerektiren bir sorun var mı — KALICI atlananlar buna dahil DEĞİLDİR.</summary>
        public bool HasProblem => Retryable > 0;

        /// <summary>Kalıcı olarak atlanan satır var mı (bilgilendirme; uyarı değil).</summary>
        public bool HasPermanentSkips => PermanentSkipped > 0;
    }

    /// <summary>Son BAŞARILI push'un sunucu sonucu (upserted/skipped/errors). Ağ hatasında değişmez (bkz. LastPushFailed).</summary>
    public static PushResult? LastPushResult { get; private set; }

    // ── Z4: bu makinenin "son gönderilen watermark"ı (firma bazında, KALICI — SettingsService). Kök neden
    // düzeltmesinin çekirdeği: push kararı sunucu global max'ına DEĞİL, bu makinenin kendi ilerleyişine bağlıdır. ──
    private const string PushWatermarkKey = "sync_push_watermark";

    // Damga sütunu (updated_at yoksa created_at) düzeltmesi — bkz. BusinessSyncService.StampColumn.
    // Düzeltmeden ÖNCE yazılmış watermark tehlikelidir: stock_movements o zamana kadar HİÇ filtrelenmiyordu
    // (her turda tamamı gönderiliyordu), şimdi filtreleniyor → watermark'ın altında kalmış, gönderilmemiş bir
    // defter satırı kalıcı olarak atlanabilir. Bu yüzden sürüm başına TEK KEZ watermark 0'a çekilir (bir tam
    // gönderim; zaten eski davranışın maliyeti buydu), sonra normal delta akışına dönülür.
    private const string WatermarkEpochKey = "sync_push_watermark_epoch";
    private const string WatermarkEpoch = "2"; // 2026-07-22

    private static long LoadPushWatermark(string companyId)
    {
        try
        {
            if (DesktopServices.Settings.Get(companyId, WatermarkEpochKey) != WatermarkEpoch)
            {
                var uid = DesktopServices.Session?.UserId ?? "";
                DesktopServices.Settings.Set(companyId, WatermarkEpochKey, WatermarkEpoch, uid);
                DesktopServices.Settings.Set(companyId, PushWatermarkKey, "0", uid);
                SyncLog.Write("PUSH watermark sifirlandi (damga sutunu duzeltmesi, tek seferlik tam gonderim)");
                return 0L;
            }
            return long.TryParse(DesktopServices.Settings.Get(companyId, PushWatermarkKey), out var v) ? v : 0L;
        }
        catch { return 0L; }
    }

    private static void SavePushWatermark(string companyId, long value)
    {
        try { DesktopServices.Settings.Set(companyId, PushWatermarkKey, value.ToString(), DesktopServices.Session?.UserId ?? ""); }
        catch { /* kalıcılık best-effort; başarısızsa bir sonraki turda tekrar yazılır */ }
    }

    // ── Z3: retry sayacı + "poison" (ısrarla gönderilemeyen) durumu — KALICI (uygulama kapansa da kaybolmaz) ──
    private const string StuckKey = "sync_push_stuck";
    private const string PoisonKey = "sync_push_poison";
    /// <summary>Bu kadar denemeden sonra otomatik tekrar durur, kalıcı uyarıya dönüşür (kuyruk kilitlenmesin).</summary>
    private const int MaxRetries = 5;

    private static int GetInt(string companyId, string key)
    {
        try { return int.TryParse(DesktopServices.Settings.Get(companyId, key), out var v) ? v : 0; }
        catch { return 0; }
    }

    private static void SetInt(string companyId, string key, int value) => SetText(companyId, key, value.ToString());

    private static void SetText(string companyId, string key, string value)
    {
        try { DesktopServices.Settings.Set(companyId, key, value, DesktopServices.Session?.UserId ?? ""); }
        catch { }
    }

    /// <summary>Kalıcı "gönderilemiyor" uyarısı: (adet, sebep). Yoksa null. Üst bar/panel bunu gösterir —
    /// Z2 rozeti yalnız SON push'u yansıttığı için sorun sürerken bile kaybolabiliyordu (kullanıcı bulgusu).</summary>
    public static (int Count, string Reason)? Poison(string? companyId = null)
    {
        var cid = companyId ?? DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(cid)) return null;
        string raw;
        try { raw = DesktopServices.Settings.Get(cid!, PoisonKey) ?? ""; } catch { return null; }
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var i = raw.IndexOf('|');
        var cnt = int.TryParse(i > 0 ? raw.Substring(0, i) : raw, out var n) ? n : 0;
        return (cnt, i > 0 ? raw.Substring(i + 1) : "");
    }

    /// <summary>
    /// ⭐ B4 (2026-08-19, kullanıcı bulgusu) — KALICI UYARIYI TEMİZLE.
    ///
    /// "Poison" durumu, bir kayıt grubu 5 denemede de sunucuya uygulanamayınca kurulur ve kuyruk
    /// kilitlenmesin diye watermark ilerletilir — yani o satırlar BİR DAHA GÖNDERİLMEZ, geriye yalnız
    /// uyarı metni kalır. Uyarıyı temizlemenin HİÇBİR yolu yoktu: firma iş verisi ve yerel veri
    /// sıfırlansa bile ekranda "6 kayıt gönderilemiyor" yazmaya devam ediyordu (sahada görüldü).
    ///
    /// Bu metot YALNIZ UYARIYI siler; hiçbir veriyi silmez, hiçbir şeyi sunucuya göndermez.
    /// </summary>
    public static void ClearPoison(string? companyId = null)
    {
        var cid = companyId ?? DesktopServices.Session?.CompanyId;
        if (string.IsNullOrWhiteSpace(cid)) return;
        SetText(cid!, PoisonKey, "");
        SetInt(cid!, StuckKey, 0);
        SyncLog.Write("PUSH uyarı temizlendi", "Kalıcı 'gönderilemiyor' uyarısı kullanıcı tarafından temizlendi.");
    }

    /// <summary>Bekleyen (yeniden denenecek) kayıt var mı — kaçıncı denemede olduğumuz.</summary>
    public static int RetryAttempts(string? companyId = null)
    {
        var cid = companyId ?? DesktopServices.Session?.CompanyId;
        return string.IsNullOrWhiteSpace(cid) ? 0 : GetInt(cid!, StuckKey);
    }

    /// <summary>Sunucu push yanıt gövdesini (JSON) PushResult'a çevirir. Saf/yan-etkisiz → birim testi kolay.</summary>
    public static PushResult ParseResult(string json)
    {
        int upserted = 0, skipped = 0, permanent = 0;
        var errors = new System.Collections.Generic.List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("upserted", out var u) && u.ValueKind == JsonValueKind.Number) upserted = u.GetInt32();
                if (root.TryGetProperty("skipped", out var s) && s.ValueKind == JsonValueKind.Number) skipped = s.GetInt32();
                // ⭐ S1: eski sunucuda bu alan YOKTUR → 0 kalır → davranış eskisiyle birebir aynı.
                if (root.TryGetProperty("permanentSkipped", out var ps) && ps.ValueKind == JsonValueKind.Number) permanent = ps.GetInt32();
                if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Array)
                    foreach (var it in e.EnumerateArray())
                        if (it.ValueKind == JsonValueKind.String) errors.Add(it.GetString() ?? "");
            }
        }
        catch { /* bozuk/boş gövde → sıfır sonuç */ }
        return new PushResult(upserted, skipped, errors, permanent);
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
