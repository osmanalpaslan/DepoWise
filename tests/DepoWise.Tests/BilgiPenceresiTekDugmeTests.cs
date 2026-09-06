using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ BİLGİ PENCERESİ TEK DÜĞME OLMALI (10.000 kayıtlık ekran QA'sinde görüldü, 2026-09-06) ═══
///
/// <para><b>Bulunan hata:</b> masaüstünde "Eşitleme tamamlandı" penceresinde yan yana <b>iki adet
/// "Tamam"</b> düğmesi çıkıyordu. Sebep: bilgi amaçlı pencereler onay penceresiyle açılıyor ve
/// hem onay hem vazgeç metnine <c>"Tamam"</c> veriliyordu.</para>
///
/// <para><b>Not:</b> Bu hata için daha önce <c>ConfirmService.InfoAsync</c> yardımcısı yazılmış ama
/// <b>çağrı yerleri dönüştürülmemişti</b> — yardımcı vardı, hata duruyordu. Bu test tam da onu
/// yakalar: yardımcının varlığı yetmez, <c>AskAsync(..., "Tamam", "Tamam")</c> deseni hiçbir yerde
/// kalmamalıdır.</para>
///
/// <para>Web ayrıca denetlendi ve temizdi: bilgi diyalogları <c>cancelText: ""</c> kullanıyor.</para>
/// </summary>
public class BilgiPenceresiTekDugmeTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<string> Kaynaklar(string altYol, string desen)
        => Directory.EnumerateFiles(Path.Combine(Kok(), altYol), desen, SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                             && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Masaüstünde hiçbir pencere iki kez "Tamam" göstermemeli.</summary>
    [Fact]
    public void Masaustu_HicbirPencerede_IkiKez_Tamam_Olmaz()
    {
        var suclu = new List<string>();
        foreach (var f in Kaynaklar(Path.Combine("src", "DepoWise.Desktop"), "*.cs"))
        {
            var s = File.ReadAllText(f);
            // ConfirmService'in KENDİ açıklaması bu deseni anlatıyor; oradaki geçiş meşrudur.
            if (f.EndsWith("ConfirmService.cs", StringComparison.Ordinal)) continue;
            if (s.Contains("\"Tamam\", \"Tamam\"", StringComparison.Ordinal))
                suclu.Add(Path.GetFileName(f));
        }
        Assert.True(suclu.Count == 0,
            "Bilgi penceresi onay penceresiyle açılmış (iki 'Tamam' düğmesi görünür). " +
            "ConfirmService.InfoAsync kullanılmalı. Dosyalar: " + string.Join(", ", suclu));
    }

    /// <summary>Tek düğmeli bilgi penceresi yardımcısı vardır ve vazgeç düğmesini hiç çizdirmez.</summary>
    [Fact]
    public void InfoAsync_VazgecDugmesini_Cizdirmez()
    {
        var svc = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ConfirmService.cs"));
        Assert.Contains("public static Task InfoAsync(", svc);
        Assert.Contains("AskAsync(message, title, okText, \"\", danger)", svc);

        var win = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views", "ConfirmWindow.axaml.cs"));
        Assert.Contains("cancel.IsVisible = !string.IsNullOrEmpty(cancelText)", win);
    }

    /// <summary>Web'de de aynı hata olmamalı (bilgi diyaloglarında iptal metni boş bırakılır).</summary>
    [Fact]
    public void Web_BilgiDiyaloglari_IptalMetnini_BosBirakir()
    {
        var suclu = new List<string>();
        foreach (var f in Kaynaklar(Path.Combine("src", "DepoWise.Web"), "*.razor")
                          .Concat(Kaynaklar(Path.Combine("src", "DepoWise.Web"), "*.cs")))
        {
            var s = File.ReadAllText(f);
            if (s.Contains("cancelText: \"Tamam\"", StringComparison.Ordinal)) suclu.Add(Path.GetFileName(f));
        }
        Assert.True(suclu.Count == 0,
            "Web'de bilgi diyaloğu iptal düğmesine de 'Tamam' vermiş: " + string.Join(", ", suclu));
    }
}
