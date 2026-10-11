using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-11 — web "Kiralık Araçlar ↔ Araç Listesi geçişinde ekran takılı kalıyor" hatasının kilidi.
/// Aynı bileşene giden iki adres arasında Blazor sayfayı yeniden kurmaz; ana yerleşim gövdeyi
/// <see cref="WebSayfaAnahtari"/> ile @key'ler. Bu testler (1) anahtar kuralını, (2) yerleşimin anahtarı
/// gerçekten kullandığını, (3) birden fazla adresi olan HER web sayfasının bilinçli sınıflandırıldığını kilitler.
/// </summary>
public class WebSayfaAnahtariTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Depo kökü bulunamadı.");
    }

    /// <summary>Alt ekranı yalnız GÖRÜNÜMÜ değiştiren (tüm veriyi bir kez yükleyen) sayfalar — elle doğrulandı
    /// 2026-10-11: Yakıt (dağıtım/depo/özet) ve Bakım (tanım/kayıt/uyarı) sekme değişince yarım form korunur.</summary>
    private static readonly string[] YalnizGorunumDegisen = { "fuel", "maintenance" };

    [Theory]
    [InlineData("vehicles", "vehicles")]
    [InlineData("vehicles/rental", "vehicles/rental")]
    [InlineData("/Vehicles/Rental/", "vehicles/rental")]
    [InlineData("vehicles/rental?edit=abc", "vehicles/rental")]
    [InlineData("reports/manager", "reports/manager")]
    [InlineData("requests/approve", "requests/approve")]
    [InlineData("materials/new", "materials/new")]
    [InlineData("fuel/summary", "fuel")]
    [InlineData("maintenance/defs?view=1", "maintenance")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Anahtar_kurali(string? yol, string beklenen)
        => Assert.Equal(beklenen, WebSayfaAnahtari.Hesapla(yol));

    [Fact]
    public void Arac_listesi_ile_kiralik_araclar_farkli_anahtar_alir()
        => Assert.NotEqual(WebSayfaAnahtari.Hesapla("vehicles"), WebSayfaAnahtari.Hesapla("vehicles/rental"));

    [Fact]
    public void Yakit_sekmeleri_ayni_anahtari_paylasir_davranis_korunur()
        => Assert.Equal(WebSayfaAnahtari.Hesapla("fuel/dist"), WebSayfaAnahtari.Hesapla("fuel/summary"));

    [Fact]
    public void Ana_yerlesim_govdeyi_anahtarla_sarar()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "src", "DepoWise.Web", "Components", "Layout", "MainLayout.razor"));
        Assert.Matches(new Regex(@"<SayfaKabi\s+@key=""DepoWise\.Application\.Security\.WebSayfaAnahtari\.Hesapla\(CurrentRoute\(\)\)"">@Body</SayfaKabi>"), src);
        Assert.DoesNotMatch(new Regex(@"^\s*@Body\s*$", RegexOptions.Multiline), src);
    }

    /// <summary>Yeni bir çok-adresli sayfa eklenirse sınıflandırılmadan geçemez: ya alt ekranda sıfırlanır
    /// listesine ya da (verisini bir kez yüklediği doğrulanarak) yalnız-görünüm listesine eklenmelidir.</summary>
    [Fact]
    public void Cok_adresli_her_web_sayfasi_siniflandirilmis()
    {
        var dir = Path.Combine(Root(), "src", "DepoWise.Web", "Components", "Pages");
        var eksik = new List<string>();
        foreach (var f in Directory.GetFiles(dir, "*.razor", SearchOption.AllDirectories))
        {
            var rotalar = Regex.Matches(File.ReadAllText(f), @"^@page\s+""/([^""]*)""", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).Distinct().ToList();
            if (rotalar.Count < 2) continue;
            var ilkler = rotalar.Select(r => r.Split('/')[0].ToLowerInvariant()).Distinct();
            foreach (var ilk in ilkler)
                if (!WebSayfaAnahtari.AltEkrandaSifirlanan.Contains(ilk) && !YalnizGorunumDegisen.Contains(ilk))
                    eksik.Add($"{Path.GetFileName(f)} → {ilk}");
        }
        Assert.True(eksik.Count == 0, "Sınıflandırılmamış çok-adresli sayfa: " + string.Join(", ", eksik));
    }
}
