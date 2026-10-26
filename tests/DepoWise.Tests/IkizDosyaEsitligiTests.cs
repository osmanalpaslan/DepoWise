using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-73 (AR-GE 2026-10-26, F-100/F-105) — KULLANILMAYAN KOD + EL İLE İKİZ DOSYALAR ═══
/// Web projesi Application'a başvurmadığı için bazı dosyalar iki kopya tutulur; biri güncellenip diğeri unutulursa iki ortam
/// sessizce ayrışır. Bu testler kopyaların AYNI kalmasını zorlar.
///  IK1 — VehicleStatus: iki kopyanın KODU birebir aynı (ad alanı ve yorumlar hariç)
///  IK2 — BranchGuard: iki ortamın pencere aracı farklı, ama "Tüm Şubeler" uyarı metni ve kural (çalışma şubesi boş) aynı
///  IK3 — kaldırılan ölü bağımlılık geri gelmedi (Semi.Avalonia paketi)
/// </summary>
public class IkizDosyaEsitligiTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    /// <summary>Yorum satırları, ad alanı satırı ve boşluk farkları atılmış kod.</summary>
    private static string Kod(string metin) => string.Join("\n", metin.Replace("\r\n", "\n").Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0 && !l.StartsWith("//") && !l.StartsWith("namespace ")));

    [Fact]
    public void IK1_VehicleStatus_Kopyalari_Ayni()
    {
        var uyg = Kod(Oku("src/DepoWise.Application/Ui/VehicleStatus.cs"));
        var web = Kod(Oku("src/DepoWise.Web/Services/VehicleStatus.cs"));
        Assert.True(uyg.Length > 200, "kopya boş/okunamadı");
        Assert.Equal(uyg, web);
    }

    [Fact]
    public void IK2_BranchGuard_Uyari_Metni_Ve_Kural_Ayni()
    {
        var mas = Oku("src/DepoWise.Desktop/BranchGuard.cs");
        var web = Oku("src/DepoWise.Web/Services/BranchGuard.cs");
        static string[] Metinler(string s) => Regex.Matches(s, "\\$\"((?:[^\"\\\\]|\\\\.)*)\"").Select(m => m.Groups[1].Value).ToArray();
        var m1 = Metinler(mas);
        Assert.Equal(4, m1.Length);
        Assert.Equal(m1, Metinler(web));
        Assert.Contains("string.IsNullOrEmpty(session.OperatingBranchId)", mas);   // masaüstü: çalışma şubesi yok = Tüm Şubeler
        Assert.Contains("string.IsNullOrEmpty(auth.BranchId)", web);               // web: aynı kural
        Assert.Contains("\"Tamam\"", mas);
        Assert.Contains("okText: \"Tamam\"", web);
    }

    [Fact]
    public void IK3_Kaldirilan_Paket_Geri_Gelmedi()
    {
        Assert.DoesNotContain("Semi.Avalonia", Oku("src/DepoWise.Desktop/DepoWise.Desktop.csproj"));
    }
}
