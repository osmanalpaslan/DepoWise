using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// Şube ve KULLANICI SUNUCU-OTORİTELİ işlemleri (2026-07-25, veri kaybı düzeltmesi). Bu iki tablo masaüstü
/// iş senkronuna DAHİL DEĞİLDİR (kod/şifre/hash taşır) ve her girişte sunucudan aynalanır → masaüstünde yalnız
/// YERELE yazılan şube/kullanıcı sonraki girişte kaybolur. Çözüm: masaüstü ÇEVRİMİÇİYKEN bu işlemleri
/// doğrudan SUNUCU API'sine yapar (web ile aynı uç) → sunucu-otoriteli olur, aynalama korur. Çevrimdışıysa
/// çağıran uyarır (bu işlem çevrimiçi gerektirir); yerele-yaz yapılmaz (aksi halde yine kaybolurdu).
///
/// Result: Offline=true → sunucuya ulaşılamadı (token yok / ağ yok) → "çevrimiçi gerektirir" uyarısı.
///         Error!=null → sunucu reddetti (yetki/validasyon) → mesajı göster. Ok=true → başarılı (Id set).
/// </summary>
public static class OrgServerClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public sealed record Result(bool Ok, bool Offline, string? Error, string? Id);
    private static Result OfflineResult => new(false, true, null, null);

    // ── Şubeler ──
    public static Task<Result> CreateBranchAsync(string name, string kind, string? parentId, string? code, string? password, string? companyId)
        => PostIdAsync("/api/branches", new { name, kind, parentId, code, password, companyId });

    public static Task<Result> UpdateBranchAsync(string id, string name, string kind, string? parentId, string? code, string? password, string? companyId)
        => SendOkAsync(HttpMethod.Put, $"/api/branches/{id}", new { name, kind, parentId, code, password, companyId });

    public static Task<Result> DeleteBranchAsync(string id)
        => SendOkAsync(HttpMethod.Delete, $"/api/branches/{id}", null);

    // ── Kullanıcılar ──
    public static Task<Result> CreateUserAsync(string username, string password, string? fullName, List<string> roleKeys,
        string? companyId, string? branchId, bool canViewAllBranches, string? personnelId)
        => PostIdAsync("/api/users", new { username, password, fullName, roleKeys, companyId, branchId, canViewAllBranches, personnelId });

    private static async Task<Result> PostIdAsync(string path, object body)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return OfflineResult;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url + path)
            { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return new(false, false, ExtractError(text, (int)resp.StatusCode), null);
            string? id = null;
            try { using var doc = JsonDocument.Parse(text); if (doc.RootElement.TryGetProperty("id", out var v)) id = v.GetString(); } catch { }
            return new(true, false, null, id);
        }
        catch { return OfflineResult; }   // ağ hatası → çevrimdışı gibi
    }

    private static async Task<Result> SendOkAsync(HttpMethod method, string path, object? body)
    {
        var (url, token) = await ResolveAsync();
        if (url is null || token is null) return OfflineResult;
        try
        {
            using var req = new HttpRequestMessage(method, url + path);
            if (body is not null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return new(true, false, null, null);
            return new(false, false, ExtractError(await resp.Content.ReadAsStringAsync(), (int)resp.StatusCode), null);
        }
        catch { return OfflineResult; }
    }

    private static async Task<(string? Url, string? Token)> ResolveAsync()
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) return (null, null);
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        return string.IsNullOrWhiteSpace(token) ? (null, null) : (url!.TrimEnd('/'), token);
    }

    private static string ExtractError(string body, int status)
    {
        try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? $"Sunucu hatası ({status})."; }
        catch { }
        return $"Sunucu hatası ({status}).";
    }

    private static string? ResolveServerUrl()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "serverurl.txt");
            if (File.Exists(path)) { var v = File.ReadAllText(path).Trim(); if (!string.IsNullOrWhiteSpace(v)) return v; }
        }
        catch { }
        return "https://depowise-erp.fly.dev";
    }
}
