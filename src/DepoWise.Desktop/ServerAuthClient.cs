using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop;

/// <summary>
/// Yerel DB'de bulunmayan (web'te oluşturulan) kullanıcı için SUNUCU doğrulaması. Yerel login
/// başarısızsa çağrılır: sunucudan tam kullanıcı paketi çekilir, yerele yazılır ve normal yerel
/// login akışı devam eder (sonraki açılışlarda offline da çalışır).
/// </summary>
public static class ServerAuthClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public enum AuthState { Ok, WrongPassword, Offline }
    public readonly record struct AuthResult(AuthState State, string? CompanyId);

    public sealed record LoginCompany(string Id, string Name) { public override string ToString() => Name; }
    /// <summary>ŞB-01: <paramref name="Kind"/> ve <paramref name="ParentId"/> sona EKLENDİ (geriye uyumlu —
    /// eski sunucu bu alanları göndermezse null/varsayılan kalır). Şube aynası bunları yerele taşır.</summary>
    public sealed record LoginBranch(string Id, string Name, string? Code, bool HasPassword,
        string? Kind = null, string? ParentId = null)
    { public override string ToString() => string.IsNullOrEmpty(Code) ? Name : $"{Name} ({Code})"; }

    /// <summary>Login öncesi firma listesi (sunucudan, anonim). Çevrimdışıysa null.</summary>
    public static async Task<List<LoginCompany>?> GetLoginCompaniesAsync()
    {
        var url = ResolveServerUrl(); if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var resp = await _http.GetAsync(url!.TrimEnd('/') + "/api/public/companies");
            if (!resp.IsSuccessStatusCode) return null;
            return JsonSerializer.Deserialize<List<LoginCompany>>(await resp.Content.ReadAsStringAsync(), _json);
        }
        catch { return null; }
    }

    /// <summary>Login öncesi şube listesi (sunucudan, anonim). Çevrimdışıysa null.</summary>
    public static async Task<List<LoginBranch>?> GetLoginBranchesAsync(string companyId)
    {
        var url = ResolveServerUrl(); if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            using var resp = await _http.GetAsync(url!.TrimEnd('/') + "/api/public/branches?companyId=" + Uri.EscapeDataString(companyId));
            if (!resp.IsSuccessStatusCode) return null;
            return JsonSerializer.Deserialize<List<LoginBranch>>(await resp.Content.ReadAsStringAsync(), _json);
        }
        catch { return null; }
    }

    /// <summary>Şube şifresi doğrulaması (sunucudan). true/false; çevrimdışıysa null.</summary>
    public static async Task<bool?> VerifyBranchAsync(string companyId, string branchId, string? password)
    {
        var url = ResolveServerUrl(); if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var body = JsonSerializer.Serialize(new { companyId, branchId, branchPassword = password });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url!.TrimEnd('/') + "/api/public/verify-branch", content);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("ok", out var v) && v.GetBoolean();
        }
        catch { return null; }
    }

    /// <summary>Giriş sonrası saklanan sunucu JWT'si + adresi (Eşitle vb. yetki gerektiren çağrılar için).</summary>
    public static string? Token { get; private set; }
    public static string? BaseUrl { get; private set; }
    /// <summary>Giriş anındaki kullanıcı yetki/şifre imzası (değişiklik tespiti için).</summary>
    public static string? AuthSig { get; private set; }

    /// <summary>Saklı token'ın son geçerlilik anı (UTC). Yenileme zamanlaması için.</summary>
    public static DateTime? TokenExpiresUtc { get; private set; }

    /// <summary>Oturum sunucuda geçersizleşti (token süresi doldu ve yenilenemedi). UI kullanıcıyı
    /// tekrar girişe yönlendirebilir — sync artık sessizce durmuyor, sinyal veriyor.</summary>
    public static bool SessionExpired { get; private set; }

    /// <summary>Oturum ilk kez geçersizleştiğinde bir kez tetiklenir (UI: dialog + tekrar giriş).</summary>
    public static event Action? SessionExpiredRaised;

    /// <summary>SessionExpired'ı true yapar ve (yalnız geçişte) UI olayını tetikler.</summary>
    private static void MarkSessionExpired()
    {
        if (SessionExpired) return;
        SessionExpired = true;
        try { SessionExpiredRaised?.Invoke(); } catch { }
    }

    /// <summary>Token süresinin dolmasına bu süreden az kaldıysa proaktif yenilenir.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromHours(2);

    /// <summary>JWT'nin exp alanından son geçerlilik anını okur (doğrulama yapmadan; zamanlama için).</summary>
    private static DateTime? ReadJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4) { case 2: payload += "=="; break; case 3: payload += "="; break; }
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            if (doc.RootElement.TryGetProperty("exp", out var e) && e.TryGetInt64(out var secs))
                return DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
        }
        catch { }
        return null;
    }

    /// <summary>Token süresi yaklaştıysa /api/auth/refresh ile yeniler. Süre çoksa dokunmaz.
    /// 401 → oturum süresi dolmuş, yenilenemez (SessionExpired=true). Çevrimdışıysa dokunmaz (tekrar denenir).
    /// Yetki gerektiren her çağrıdan önce çağrılmalı.</summary>
    public static async Task EnsureFreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(BaseUrl)) return;
        if (TokenExpiresUtc is DateTime exp && exp - DateTime.UtcNow > RefreshMargin) return; // hâlâ taze
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl!.TrimEnd('/') + "/api/auth/refresh");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Token gerçekten süresi dolmuş/geçersiz → yenilenemez. UI'ya sinyal ver.
                MarkSessionExpired();
                return;
            }
            if (!resp.IsSuccessStatusCode) return; // 5xx/ağ → çevrimdışı gibi, sonra tekrar dene
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 } nt)
            {
                Token = nt;
                TokenExpiresUtc = ReadJwtExpiry(nt);
                SessionExpired = false;
            }
        }
        catch { /* çevrimdışı — sonraki turda tekrar denenir */ }
    }

    /// <summary>İLK KURULUM: makinenin şubesi henüz yoksa giriş yapan kullanıcının şubesini makineye tanımlar
    /// (onay sonrası). Sunucu, zaten atanmışsa dokunmaz. Dönen: atandı mı (null = erişilemedi).</summary>
    public static async Task<bool?> SelfAssignMachineBranchAsync(string machineName, string branchId)
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(BaseUrl)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, BaseUrl!.TrimEnd('/') + "/api/machines/self-assign")
            { Content = new StringContent(JsonSerializer.Serialize(new { machineName, branchId }), System.Text.Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("assigned", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch { return null; }
    }

    /// <summary>
    /// ADR-083 — Bu makinenin firması sunucuda KALICI silindi mi? (eşitleme adımında sorulur)
    /// Dönen: true = silindi (yerel veri temizlenecek), false = duruyor, null = erişilemedi/çevrimdışı.
    ///
    /// null ile false AYRI tutulur: çevrimdışı bir makinenin yerel verisini "cevap alamadım" diye
    /// silmek felaket olur. Silme YALNIZ sunucu açıkça "silindi" dediğinde yapılır (fail-safe).
    /// </summary>
    public static async Task<bool?> IsCompanyPurgedAsync()
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(BaseUrl)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl!.TrimEnd('/') + "/api/sync/purge-status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("purged", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch { return null; }
    }

    /// <summary>
    /// ADR-084 — Bu firma için sunucuda BEKLEYEN bir "yerel sıfırlama" isteği var mı, varsa ne zaman istendi?
    /// Dönen: istek zamanı (Unix ms) ya da hiç istek yoksa/erişilemezse null.
    ///
    /// Kalıcı silmeden (ADR-083, IsCompanyPurgedAsync) FARKI: bu YIKICI/erişim-engelleyici değildir — yalnız
    /// "en son ne zaman istendi" bilgisini döner; çağıran bunu kendi yerel "en son uyguladım" zamanıyla
    /// kıyaslayıp bir kerelik mi uygulayacağına karar verir (LocalResetService).
    /// </summary>
    public static async Task<long?> GetLocalResetRequestedAtAsync()
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(BaseUrl)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl!.TrimEnd('/') + "/api/sync/local-reset-status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("requestedAt", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt64() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// ADR-085 — Bu MAKİNE için sunucuda BEKLEYEN bir "tanım sıfırlama" isteği var mı, varsa ne zaman istendi?
    /// Dönen: istek zamanı (Unix ms) ya da hiç istek yoksa/erişilemezse null. Firma bağımsız — makine adı
    /// firmalar arası bir anahtardır (bkz. MachineResetService).
    /// </summary>
    public static async Task<long?> GetMachineResetRequestedAtAsync(string machineName)
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(BaseUrl)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                BaseUrl!.TrimEnd('/') + "/api/sync/machine-reset-status?machineName=" + Uri.EscapeDataString(machineName));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("requestedAt", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetInt64() : null;
        }
        catch { return null; }
    }

    /// <summary>Sunucudan güncel kullanıcı imzasını çeker (Token ile). Erişilemezse null.</summary>
    public static async Task<string?> FetchAuthSigAsync()
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(BaseUrl)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, BaseUrl!.TrimEnd('/') + "/api/me/authsig");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("sig", out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>SUNUCU ile kimlik doğrula (web KAYNAK-OTORİTE). Sunucuya erişilirse: parola doğruysa kullanıcıyı
    /// yerele yazar (hash güncellenir) → Ok + firma id; parola/kullanıcı yanlışsa → WrongPassword. Sunucuya
    /// erişilemiyorsa → Offline (çağıran yerel DB ile devam eder). companyId BOŞ → tüm firmalar taranır.</summary>
    public static async Task<AuthResult> AuthenticateAsync(string username, string password)
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) return new(AuthState.Offline, null);
        var baseUrl = url!.TrimEnd('/');
        try
        {
            var json = JsonSerializer.Serialize(new { companyId = "", username, password });
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(baseUrl + "/api/auth/sync-login", content);

            if (resp.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var body = await resp.Content.ReadAsStringAsync();
                var bundle = JsonSerializer.Deserialize<RemoteUserBundle>(body, _json);
                if (bundle is null || string.IsNullOrWhiteSpace(bundle.UserId)) return new(AuthState.WrongPassword, null);
                // GUI-01 — SIRA ÖNEMLİ: kullanıcı paketi artık ŞUBE KAPSAMINI (user_scopes) da taşıyor.
                //   1) kullanıcı  → FİRMA satırını oluşturur (şube aynalaması buna FK ile bağlı)
                //   2) şubeler    → kapsamın FK hedefi
                //   3) kapsam     → artık yazılabilir
                // Tek adımda yapılırsa ilk kurulumda kapsam sessizce düşer ve kullanıcı yerelde
                // "kısıtsız" görünür (yetkisi olmayan şubeyi görüp o şubeye girebilirdi).
                DesktopServices.Auth.ImportRemoteUser(bundle); // yerel hash + yetkiler
                await BranchMirror.RefreshAsync(bundle.CompanyId);
                DesktopServices.Auth.ImportUserScopes(bundle); // şube kapsamı
                await StoreTokenAsync(baseUrl, username, password); // Eşitle için JWT sakla
                return new(AuthState.Ok, bundle.CompanyId);
            }
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new(AuthState.WrongPassword, null); // web'de parola değişmiş/yanlış → yerel de reddedilir
            return new(AuthState.Offline, null); // 5xx vb. → çevrimdışı gibi davran
        }
        catch { return new(AuthState.Offline, null); } // ağ yok → çevrimdışı
    }

    /// <summary>İLK GİRİŞ şifre belirleme: saklanan token (AuthenticateAsync sonrası) ile sunucuda kendi şifresini
    /// değiştirir (must_change_password sıfırlanır). Başarıda true. Token yoksa / hata → false.</summary>
    public static async Task<bool> ChangeInitialPasswordAsync(string newPassword)
    {
        var baseUrl = BaseUrl; var token = Token;
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl!.TrimEnd('/') + "/api/auth/change-initial-password");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new { newPassword }), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>/api/auth/login ile JWT alıp saklar (Eşitle vb. için). Hata olursa token null kalır.</summary>
    private static async Task StoreTokenAsync(string baseUrl, string username, string password)
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { username, password }), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(baseUrl + "/api/auth/login", content);
            if (!resp.IsSuccessStatusCode) return;
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString() : null;
            BaseUrl = baseUrl;
            TokenExpiresUtc = Token is null ? null : ReadJwtExpiry(Token);
            SessionExpired = false; // taze giriş
            AuthSig = await FetchAuthSigAsync(); // giriş anındaki yetki/şifre imzası
        }
        catch { }
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var companyId = DesktopServices.DefaultCompanyId;
            var s = DesktopServices.Settings.Get(companyId, SettingKeys.UpdateServerUrl);
            if (!string.IsNullOrWhiteSpace(s)) return s;
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
