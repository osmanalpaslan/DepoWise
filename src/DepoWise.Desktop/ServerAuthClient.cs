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
    public sealed record LoginBranch(string Id, string Name, string? Code, bool HasPassword)
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
                DesktopServices.Auth.ImportRemoteUser(bundle); // yerel hash güncellenir
                await StoreTokenAsync(baseUrl, username, password); // Eşitle için JWT sakla
                return new(AuthState.Ok, bundle.CompanyId);
            }
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new(AuthState.WrongPassword, null); // web'de parola değişmiş/yanlış → yerel de reddedilir
            return new(AuthState.Offline, null); // 5xx vb. → çevrimdışı gibi davran
        }
        catch { return new(AuthState.Offline, null); } // ağ yok → çevrimdışı
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
