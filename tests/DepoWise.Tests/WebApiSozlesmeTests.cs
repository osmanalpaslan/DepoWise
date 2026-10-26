using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-75 (AR-GE 2026-10-26, F-134) — WEB ↔ API SÖZLEŞMESİ: LİSTE OKUNAN UÇLARIN YANIT BİÇİMİ ═══
/// Web, API'yi tipsiz (JsonElement) okur; bir ucun yanıt biçimi değişince derleme bunu yakalamaz. 2026-09-04'te /api/personnel
/// { items, hasMore } biçimine geçti, web'in liste okuyucusu onu okuyamadı ve ~2 ay boyunca personel listeleri SESSİZCE boş kaldı
/// (M-69'da gerçek tarayıcıda bulundu). Bu test web kodunda liste olarak okunan HER ucu (GetArrayAsync / OptionsAsync / LoadList)
/// kaynaktan çıkarır, gerçek API'ye çağırır ve yanıtın web okuyucusunun anladığı biçimde (dizi ya da items dizisi taşıyan nesne)
/// olduğunu doğrular. Yeni bir uç eklenince ya da biçim değişince kendiliğinden kapsar.
///  WS1 — tüm liste uçları: 200 + dizi ya da { items: [...] }
///  WS2 — kaynaktan çıkarılan uç sayısı makul (çıkarım bozulup testi boşa düşürmesin)
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class WebApiSozlesmeTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        _client = await _host.LoginSeedAsync();   // süper yönetici: her ucu görebilir (yetki burada sınanmıyor — UcYetkiTaramasi'nda)
    }

    public async Task DisposeAsync() => await ((IAsyncLifetime)_host).DisposeAsync();

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d!.FullName;
    }

    /// <summary>Web kodunda liste okuyucularına SABİT metinle verilen uç yolları (sorgu dizesi sonundaki boş "?" atılır).</summary>
    public static IReadOnlyList<string> ListeUclari()
    {
        var desen = new Regex(@"(?:GetArrayAsync|OptionsAsync|LoadList)\(""(/api/[^""]+)""", RegexOptions.Compiled);
        var web = Path.Combine(Kok(), "src", "DepoWise.Web");
        return Directory.EnumerateFiles(web, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".razor") || f.EndsWith(".cs")) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .SelectMany(f => desen.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Select(y => y.TrimEnd('?', '&').EndsWith("=") ? y + "x" : y.TrimEnd('?', '&'))   // "?q=" gibi açık uçlu sorguya örnek değer
            .Select(y => ZorunluSorgu.TryGetValue(y, out var q) ? y + "?" + q : y)
            .Distinct(StringComparer.Ordinal).OrderBy(y => y, StringComparer.Ordinal).ToList();
    }

    /// <summary>Web'in sorgu dizesini dinamik eklediği (kaynakta görünmeyen) ZORUNLU parametreler — gerçekçi örnek değer.</summary>
    private static readonly Dictionary<string, string> ZorunluSorgu = new(StringComparer.Ordinal)
    {
        ["/api/calendar"] = "from=1760000000000&to=1762600000000",   // Calendar.razor Query(): görünen ay aralığı (ms)
    };

    [Fact]
    public void WS2_Kaynaktan_Cikarilan_Uc_Sayisi_Makul()
    {
        var uclar = ListeUclari();
        Assert.True(uclar.Count >= 60, $"yalnız {uclar.Count} uç çıkarıldı — çıkarım deseni bozulmuş olabilir");
        Assert.Contains("/api/personnel", uclar);
        Assert.Contains("/api/branches", uclar);
    }

    [Fact]
    public async Task WS1_Liste_Okunan_Her_Uc_Dizi_Ya_Da_Items_Doner()
    {
        var hatalar = new List<string>();
        foreach (var yol in ListeUclari())
        {
            var r = await _client.GetAsync(yol);
            var govde = await r.Content.ReadAsStringAsync();
            if (r.StatusCode != HttpStatusCode.OK) { hatalar.Add($"{yol} → {(int)r.StatusCode} {Kisalt(govde)}"); continue; }
            JsonElement kok;
            try { kok = JsonDocument.Parse(govde).RootElement; }
            catch (JsonException) { hatalar.Add($"{yol} → JSON değil: {Kisalt(govde)}"); continue; }
            var uygun = kok.ValueKind == JsonValueKind.Array
                || (kok.ValueKind == JsonValueKind.Object && kok.TryGetProperty("items", out var i) && i.ValueKind == JsonValueKind.Array);
            if (!uygun) hatalar.Add($"{yol} → web liste okuyucusunun anlamadığı biçim ({kok.ValueKind}): {Kisalt(govde)}");
        }
        Assert.True(hatalar.Count == 0, "Web'in liste olarak okuduğu uçlarda biçim uyumsuzluğu:\n" + string.Join("\n", hatalar));
    }

    private static string Kisalt(string s) => s.Length > 160 ? s[..160] + "…" : s;
}
