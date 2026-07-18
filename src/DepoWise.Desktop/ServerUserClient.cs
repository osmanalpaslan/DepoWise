using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Desktop;

/// <summary>
/// Sunucudan KULLANICI listesi çekme (personel ekranı "mevcut kullanıcıyı bağla" için). KÖK NEDEN
/// (kullanıcı bulgusu 2026-07-19): kullanıcılar iş senkronunda YOK (yalnız giriş yapan kullanıcının kendi
/// kaydı yerele iner) → başka makinede/web'de oluşturulmuş bir kullanıcı (ör. Mustafa Alpaslan) bu makinenin
/// yerel <c>users</c> tablosunda olmadığından bağlama listesinde çıkmıyordu. Bu istemci, ÇEVRİMİÇİYKEN
/// sunucudaki bağlanabilir kullanıcıları çeker (web ile aynı uç: /api/personnel/linkable-users). Yerel
/// <c>users</c> tablosuna DOKUNMAZ (§4 kullanıcı/firma değişmezliği korunur) — yalnız listeleme amaçlı.
/// Çevrimdışı → null (çağıran yerel listeye düşer).
/// </summary>
public static class ServerUserClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<List<LinkableUser>?> GetLinkableUsersAsync()
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) return null;
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url!.TrimEnd('/') + "/api/personnel/linkable-users");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<LinkableUser>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                list.Add(new LinkableUser(Str(e, "id"), Str(e, "username"), NullS(e, "fullName"), ReadBool(e, "isActive"), NullS(e, "branchName")));
            }
            return list;
        }
        catch { return null; }
    }

    /// <summary>Mevcut kullanıcıyı personele SUNUCUDA bağlar (bağ users.personnel_id'de tutulur ve users
    /// tablosu masaüstünden push EDİLMEZ → bağ sunucuda yapılmalı ki otoriteli olsun ve diğer makinelere ulaşsın).
    /// Personel önce sunucuya push edilmiş olmalı. Dönüş: null=başarılı, aksi halde hata metni (çevrimdışı dâhil).</summary>
    public static async Task<string?> LinkUserAsync(string personnelId, string userId)
    {
        var url = ResolveServerUrl();
        if (string.IsNullOrWhiteSpace(url)) return "Sunucu adresi yok.";
        await ServerAuthClient.EnsureFreshTokenAsync();
        var token = ServerAuthClient.Token;
        if (string.IsNullOrWhiteSpace(token)) return "Çevrimdışı (sunucuya bağlanılamadı).";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url!.TrimEnd('/') + $"/api/personnel/{personnelId}/link-user")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { userId }), System.Text.Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync();
            try { using var doc = JsonDocument.Parse(body); if (doc.RootElement.TryGetProperty("error", out var er)) return er.GetString(); }
            catch { }
            return $"Sunucu hatası ({(int)resp.StatusCode}).";
        }
        catch (Exception ex) { return "Bağlanamadı: " + ex.Message; }
    }

    private static string Str(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string? NullS(JsonElement o, string k) { var s = Str(o, k); return string.IsNullOrEmpty(s) ? null : s; }
    private static bool ReadBool(JsonElement o, string k)
        => o.TryGetProperty(k, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n != 0));

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
