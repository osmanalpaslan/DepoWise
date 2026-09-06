using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ FAZ 4.14 (kullanıcı isteği 2026-09-06) — KOLON TERCİHİ SUNUCUDA DA SAKLANIR ═══
///
/// <b>Kullanıcı şikâyeti:</b> <i>"Kolonları ayarla → Kaydet dediğimde, ben değiştirene kadar her
/// login'de aynı seçim geçerli kalsın; her oturumda kolon ekleyip çıkarmak zorunda kalmayayım."</i>
///
/// <b>Kök neden.</b> Kalıcılık ZİNCİRİ çalışıyordu ama tercih yalnız <b>o makinenin YEREL</b>
/// veritabanında duruyordu (<c>user_list_preferences</c> senkron kataloğunda YOKTUR ve orada olması
/// da doğru değildir — kişisel arayüz ayarıdır, iş verisi değil). Kullanıcı iki bilgisayar
/// kullandığı için (ve web ayrı sakladığı için) diğer makinede/web'de hep VARSAYILAN kolonlar
/// geliyordu — yani "kaydettiğim seçim kayboluyor".
///
/// <b>Çözüm.</b> Web'in zaten kullandığı <c>/api/me/list-columns/{listKey}</c> ucu masaüstünden de
/// kullanılır: kaydederken sunucuya da yazılır, yüklerken çevrimiçiyse sunucudan okunup yerele
/// aynalanır. Böylece tercih KULLANICIYA bağlı olur, makineye değil — web ve masaüstü aynı seçimi
/// gösterir.
///
/// <b>Sınırlar (bilinçli).</b>
/// <list type="bullet">
///   <item>Çevrimdışıyken sunucuya gidilmez; YEREL değer kullanılır ve kaydedilir (çalışma durmaz).</item>
///   <item>Sunucu hatası SESSİZ değildir ama ekranı kilitlemez: yerel değer geçerli kalır.</item>
///   <item>Yeni tablo/migration YOKTUR; sunucu tarafı zaten mevcut (web bunu kullanıyor).</item>
/// </list>
/// </summary>
public static class ServerListPrefsClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>Sunucu adresi — diğer istemcilerle AYNI kural (serverurl.txt varsa o, yoksa üretim).</summary>
    private static string? SunucuAdresi()
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

    private static async Task<string?> TokenAsync()
    {
        var url = SunucuAdresi();
        if (string.IsNullOrWhiteSpace(url)) return null;
        await ServerAuthClient.EnsureFreshTokenAsync();
        return string.IsNullOrWhiteSpace(ServerAuthClient.Token) ? null : url;
    }

    /// <summary>Sunucudaki kolon tercihi. Çevrimdışı/erişilemezse <c>null</c> (çağıran yerele düşer).</summary>
    public static async Task<List<string>?> GetColumnsAsync(string listKey)
    {
        var url = await TokenAsync();
        if (url is null) return null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url.TrimEnd('/') + "/api/me/list-columns/" + Uri.EscapeDataString(listKey));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServerAuthClient.Token);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("columns", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var list = new List<string>();
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String) list.Add(e.GetString()!);
            return list.Count > 0 ? list : null;
        }
        catch { return null; }
    }

    /// <summary>Kolon tercihini sunucuya yazar. Çevrimdışıysa sessizce atlanır (yerel kayıt zaten yapıldı).</summary>
    public static async Task<bool> SaveColumnsAsync(string listKey, IReadOnlyList<string> columns)
    {
        var url = await TokenAsync();
        if (url is null) return false;
        try
        {
            var govde = JsonSerializer.Serialize(new { columns });
            using var req = new HttpRequestMessage(HttpMethod.Post, url.TrimEnd('/') + "/api/me/list-columns/" + Uri.EscapeDataString(listKey))
            { Content = new StringContent(govde, Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServerAuthClient.Token);
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
