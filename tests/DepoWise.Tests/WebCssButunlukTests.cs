using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-11 — web <c>app.css</c> BÜTÜNLÜK kilidi. Bulgu: 89ca594'te bir stil bloğu taşınırken "18) MOB-W"
/// bölüm başlığının <c>/*</c> açılışı silinmişti. Kapanış <c>*/</c> yerinde kaldığı için başlık metni CSS
/// olarak okunuyor, ardından gelen ilk kural (<c>.dw-mobil-arama { display: none; }</c>) tarayıcı tarafından
/// SESSİZCE atılıyordu. Derleme/test hata vermediği için fark edilmedi. Bu testler aynı sınıf hatayı yakalar.
/// </summary>
public class WebCssButunlukTests
{
    private static string Css()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var p = Path.Combine(dir, "src", "DepoWise.Web", "wwwroot", "app.css");
            if (File.Exists(p)) return File.ReadAllText(p);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("app.css bulunamadı.");
    }

    [Fact]
    public void Yorumlar_dengeli_her_acilis_kapaniyor()
    {
        var s = Css();
        int i = 0, satir = 1;
        while (i < s.Length)
        {
            var ac = s.IndexOf("/*", i, StringComparison.Ordinal);
            var kapa = s.IndexOf("*/", i, StringComparison.Ordinal);
            if (kapa >= 0 && (ac < 0 || kapa < ac))
            {
                satir += s.AsSpan(i, kapa - i).Count('\n');
                Assert.Fail($"app.css satır ~{satir}: açılışı olmayan '*/' (yorum başlangıcı silinmiş olabilir).");
            }
            if (ac < 0) break;
            var son = s.IndexOf("*/", ac + 2, StringComparison.Ordinal);
            Assert.True(son >= 0, "app.css: kapanmamış '/*' yorumu.");
            satir += s.AsSpan(i, son + 2 - i).Count('\n');
            i = son + 2;
        }
    }

    [Fact]
    public void Mobil_katman_basligi_yorum_icinde_ve_mobil_arama_kurali_var()
    {
        var s = Css();
        var bas = s.IndexOf("18) MOB-W", StringComparison.Ordinal);
        Assert.True(bas > 0);
        var oncekiAc = s.LastIndexOf("/*", bas, StringComparison.Ordinal);
        var oncekiKapa = s.LastIndexOf("*/", bas, StringComparison.Ordinal);
        Assert.True(oncekiAc > oncekiKapa, "MOB-W başlığı bir yorumun içinde olmalı.");
        Assert.Matches(new Regex(@"\*/\s*\.dw-mobil-arama\s*\{\s*display:\s*none;\s*\}"), s);
    }
}
