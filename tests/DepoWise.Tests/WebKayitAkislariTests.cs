using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-69 (AR-GE 2026-10-26, F-022) — WEB KAYIT AKIŞLARI (yerel tarayıcı testinin bulgularının kilidi) ═══
/// Asıl kanıt gerçek tarayıcı testidir: <c>scripts/run_web_e2e_yerel.ps1</c> (boş geçici veritabanı + yerel API/web; yeni malzeme
/// girişi çift tıklama/çift onayda TEK kayıt, depo çıkışı, iptal). O koşu varsayılan takımda olmadığı için bulunan iki kusurun
/// düzeltmesi burada kaynak düzeyinde kilitlenir:
///  WK1 — sayfalı uçların { items, hasMore } yanıtı ortak liste okuyucuda okunur (eskiden sessizce BOŞ liste: web Stok İşlemleri'nde
///        zorunlu "Personel" listesi boştu, kayıt yapılamıyordu — canlıda 2026-09-04'ten beri)
///  WK2 — açık bir onay/gerekçe penceresi varken ikinci onay açılmaz (çift tıklamada iki pencere → çift kayıt riski)
///  WK3 — yerel kayıt testleri canlı adrese koşamaz (yalnız localhost; .env.test.local okunmaz; PG bağlantısı boşaltılır)
/// </summary>
public class WebKayitAkislariTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d!.FullName;
    }
    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel)).Replace("\r\n", "\n");

    private static string Govde(string kaynak, string imza)
    {
        var i = kaynak.IndexOf(imza, StringComparison.Ordinal);
        Assert.True(i >= 0, "bulunamadı: " + imza);
        var ac = kaynak.IndexOf('{', i);
        int derinlik = 0, son = ac;
        for (; son < kaynak.Length; son++)
        {
            if (kaynak[son] == '{') derinlik++;
            else if (kaynak[son] == '}' && --derinlik == 0) break;
        }
        return kaynak.Substring(ac, son - ac + 1);
    }

    [Fact]
    public void WK1_Liste_Okuyucu_Sayfali_Yaniti_Okur()
    {
        var g = Govde(Oku("src/DepoWise.Web/Services/ApiClient.cs"), "public async Task<System.Text.Json.JsonElement[]> GetArrayAsync(string path)");
        Assert.Contains("kok.TryGetProperty(\"items\", out var ogeler)", g);
        Assert.Contains("return ogeler.EnumerateArray().ToArray();", g);
        Assert.Contains("throw new HttpRequestException", g);                              // başka biçim → açık hata, sessiz boşluk yok
        Assert.DoesNotContain("ReadFromJsonAsync<System.Text.Json.JsonElement[]>", g);     // eski, yalnız dizi okuyan biçim
        // Uç gerçekten sayfalı biçimde dönüyor (okuyucu bu yüzden gerekli):
        Assert.Contains("items = rows", Govde(Oku("src/DepoWise.Api/Program.cs"), "app.MapGet(\"/api/personnel\", (HttpContext c, string? search) =>"));
    }

    [Fact]
    public void WK2_Acik_Onay_Varken_Ikinci_Onay_Acilmaz()
    {
        var s = Oku("src/DepoWise.Web/Services/DialogExtensions.cs");
        var onay = Govde(s, "public static async Task<bool> Confirm(this IDialogService dialog, string message,");
        Assert.Contains("if (!Kilitle(dialog)) return false;", onay);
        Assert.Contains("finally { Birak(dialog); }", onay);
        var gerekce = Govde(s, "public static async Task<string?> AskReason(this IDialogService dialog, string message,");
        Assert.Contains("if (!Kilitle(dialog)) return null;", gerekce);
        Assert.Contains("finally { Birak(dialog); }", gerekce);
        Assert.Contains("lock (d) { if (d.Var) return false; d.Var = true; return true; }", s);
    }

    [Fact]
    public void WK3_Yerel_Kayit_Testleri_Canliya_Kosamaz()
    {
        var ayar = Oku("tests/web-e2e/playwright.yerel.config.js");
        Assert.Contains("/^http:\\/\\/localhost:\\d+$/", ayar);
        Assert.DoesNotContain(".env.test.local", ayar.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).Aggregate("", (a, b) => a + b));
        var betik = Oku("scripts/run_web_e2e_yerel.ps1");
        Assert.Contains("[Environment]::SetEnvironmentVariable(\"DEPOWISE_PG_URL\", $null, \"Process\")", betik);
        Assert.Contains("\"--no-launch-profile\"", betik);
        Assert.True(File.Exists(Path.Combine(Kok(), "tests/web-e2e/yerel/stok-kayit.spec.js")));
    }
}
