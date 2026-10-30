using System.Diagnostics;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ 2026-10-30 — YAYIN BETİĞİ EN DÜŞÜK SÜRÜM KORUMASI ═══
/// 1.0.204 yayınında --min unutuldu, en düşük desteklenen sürüm 0.0.0'a düştü (eski masaüstleri zorunlu güncellemeyi atlardı).
///  YB1 — --min verilmezse sunucudaki mevcut değer korunur; sunucuda yayın yoksa 0.0.0
///  YB2 — mevcuttan DÜŞÜK --min reddedilir; yalnız --min-dusur ile inilir; yükseltmek serbest; bozuk biçim reddedilir
///  YB3 — publish_release.mjs seçimi bu işlevden alır, sabit "0.0.0" varsayılanına dönmemiş
/// Gerçek Node ile koşar (betik Node'da çalışır; kaynak metne bakmak davranışı kanıtlamaz).
/// </summary>
public class YayinBetigiTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı");
    }

    /// <summary>enDusukSurumSec(verilen, mevcut, dusur) → sonuç ya da "HATA:..."</summary>
    private static string Sec(string? verilen, string? mevcut, bool dusur = false)
    {
        var modul = new Uri(Path.Combine(Kok(), "scripts", "lib", "yayin_ortak.mjs")).AbsoluteUri;
        string Js(string? s) => s is null ? "null" : "'" + s + "'";
        var kod = $"import {{ enDusukSurumSec }} from '{modul}'; " +
                  $"try {{ console.log(enDusukSurumSec({Js(verilen)}, {Js(mevcut)}, {(dusur ? "true" : "false")})); }} " +
                  "catch (e) { console.log('HATA:' + e.message); }";
        var psi = new ProcessStartInfo("node") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("--input-type=module");
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(kod);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("node başlatılamadı (yayın için Node gerekli)");
        var cikti = p.StandardOutput.ReadToEnd().Trim();
        var hata = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(30_000), "node zaman aşımı");
        Assert.True(p.ExitCode == 0, "node hata: " + hata);
        return cikti;
    }

    [Fact]
    public void YB1_Min_Verilmezse_Mevcut_Korunur()
    {
        Assert.Equal("1.0.203", Sec(null, "1.0.203"));
        Assert.Equal("0.0.0", Sec(null, null));
        Assert.Equal("0.0.0", Sec(null, "bozuk"));
    }

    [Fact]
    public void YB2_Dusurmek_Yalniz_Izinle_Yukseltmek_Serbest()
    {
        Assert.StartsWith("HATA:", Sec("0.0.0", "1.0.203"));
        Assert.StartsWith("HATA:", Sec("1.0.99", "1.0.203"));    // sayısal karşılaştırma (metin değil)
        Assert.Equal("1.0.99", Sec("1.0.99", "1.0.203", dusur: true));
        Assert.Equal("1.0.203", Sec("1.0.203", "1.0.203"));
        Assert.Equal("1.0.204", Sec("1.0.204", "1.0.203"));
        Assert.Equal("1.1.0", Sec("1.1.0", "1.0.203"));
        Assert.StartsWith("HATA:", Sec("", "1.0.203"));           // "--min" yazılıp değer unutulmuş
        Assert.StartsWith("HATA:", Sec("1.0", "1.0.203"));
    }

    [Fact]
    public void YB3_Yayin_Betigi_Korumayi_Kullanir()
    {
        var js = File.ReadAllText(Path.Combine(Kok(), "scripts", "publish_release.mjs"));
        Assert.Contains("enDusukSurumSec(minVerilen, onceki?.minSupportedVersion, dusurIzni)", js);
        Assert.DoesNotContain(": \"0.0.0\";", js);
        Assert.Contains("form.append(\"minSupportedVersion\", minSupported);", js);
    }
}
