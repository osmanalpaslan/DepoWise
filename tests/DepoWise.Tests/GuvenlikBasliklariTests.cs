using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ J (2026-09-05) — TARAYICI GÜVENLİK BAŞLIKLARI ═══
///
/// <b>Bulgu:</b> web ve API tarayıcıya HİÇBİR güvenlik başlığı göndermiyordu. HSTS zaten vardı, ama
/// tıklama-hırsızlığı (clickjacking), MIME tipi tahmini ve referrer sızıntısı açık kalıyordu.
///
/// Bu testler başlıkların <b>kurulu kaldığını</b> kilitler: bir gün biri ara katmanı kaldırırsa ya da
/// sırasını bozarsa burada kırılır. Başlıklar kimlik doğrulamadan ÖNCE eklenir — yetkisiz yanıtlar da
/// (401/403) korunsun.
///
/// <b>⚠️ CSP bilinçli olarak YOK</b> ve bu test onu da kayda geçirir: Blazor Server + MudBlazor satır
/// içi betik/stil kullanır; yanlış bir politika arayüzü SESSİZCE bozar (ekran açılır, düğmeler
/// çalışmaz). Ölçülmeden eklenmemelidir — bu yüzden "eksik" değil, "bilinçli karar"dır.
///
///  GVN1 — Web: dört başlık da var
///  GVN2 — API: üç başlık da var
///  GVN3 — Başlıklar kimlik doğrulamadan ÖNCE ekleniyor (401/403 de kapsanır)
///  GVN4 — CSP bilinçli olarak yok ve gerekçesi kodda YAZILI (sessiz eksik değil)
/// </summary>
public class GuvenlikBasliklariTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static string Oku(params string[] p)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(p).ToArray()));

    private static string Web() => Oku("src", "DepoWise.Web", "Program.cs");
    private static string Api() => Oku("src", "DepoWise.Api", "Program.cs");

    [Fact]
    public void GVN1_Web_Guvenlik_Basliklari_Var()
    {
        var kod = Web();
        Assert.Contains("\"X-Content-Type-Options\"", kod);
        Assert.Contains("nosniff", kod);
        Assert.Contains("\"X-Frame-Options\"", kod);
        Assert.Contains("DENY", kod);
        Assert.Contains("\"Referrer-Policy\"", kod);
        Assert.Contains("\"X-Permitted-Cross-Domain-Policies\"", kod);
        // HSTS zaten vardı — kaldırılmadığı da kilitlenir.
        Assert.Contains("UseHsts()", kod);
    }

    [Fact]
    public void GVN2_Api_Guvenlik_Basliklari_Var()
    {
        var kod = Api();
        Assert.Contains("\"X-Content-Type-Options\"", kod);
        Assert.Contains("\"X-Frame-Options\"", kod);
        Assert.Contains("\"Referrer-Policy\"", kod);
    }

    /// <summary>
    /// ⭐ SIRA ÖNEMLİ: başlıklar kimlik doğrulamadan ÖNCE eklenmeli. Sonra eklenirse 401/403 yanıtları
    /// başlıksız çıkar — yetkisiz istek de tarayıcıda korunmamış olur.
    /// (Aynı gerekçeyle sıkıştırma da kimlik doğrulamadan önce yerleştirilmişti — SNK-08.)
    /// </summary>
    [Fact]
    public void GVN3_Basliklar_Kimlik_Dogrulamadan_Once()
    {
        var api = Api();
        var basliklar = api.IndexOf("\"X-Content-Type-Options\"", StringComparison.Ordinal);
        var kimlik = api.IndexOf("app.UseAuthentication()", StringComparison.Ordinal);
        Assert.True(basliklar > 0 && kimlik > 0);
        Assert.True(basliklar < kimlik,
            "Güvenlik başlıkları kimlik doğrulamadan SONRA ekleniyor → 401/403 yanıtları başlıksız çıkar.");
    }

    /// <summary>CSP'nin YOKLUĞU bir karardır, unutulmuş bir madde değil. Gerekçe kodda yazılı olmalı ki
    /// sonraki okuyan "eksik kalmış" sanıp ölçmeden eklemesin.</summary>
    [Fact]
    public void GVN4_CSP_Bilincli_Olarak_Yok_Ve_Gerekcesi_Yazili()
    {
        var web = Web();
        Assert.DoesNotContain("\"Content-Security-Policy\"", web);
        Assert.Contains("CSP", web);            // gerekçe metni kodda duruyor
        Assert.Contains("MudBlazor", web);      // hangi teknik kısıt yüzünden olduğu da yazılı
    }
}
