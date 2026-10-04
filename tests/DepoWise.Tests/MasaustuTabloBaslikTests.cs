using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-04 (kullanıcı isteği) — tablolar (rapor tabloları DAHİL, kural: bir tabloda olan diğerinde de olur):
/// TBB1 başlık yazıları ortada (genel stil), TBB2 sıralanabilir başlık ve rapor başlığı içerik ortası,
/// TBB3 hiçbir tablo başlığında yerel sola/sağa hizalama kalmadı, TBB4 rapor tablosu tutamağı çalışan yöntemle.
/// TBB5 bakım formları (masaüstü + web) tek, araca bağlı sayaç alanı kullanır.
/// </summary>
public class MasaustuTabloBaslikTests
{
    private static string Kok()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "DepoWise.sln"))) dir = Path.GetDirectoryName(dir);
        return Path.Combine(dir!, "src");
    }
    private static string Kaynak(params string[] p) => File.ReadAllText(Path.Combine(new[] { Kok() }.Concat(p).ToArray()));

    [Fact]
    public void TBB1_Genel_Baslik_Stili_Ortalar()
    {
        var x = Kaynak("DepoWise.Desktop", "Themes", "Components.axaml");
        var stil = Regex.Matches(x, @"<Style Selector=""Border\.TableHeader :is\(TextBlock\)"">[\s\S]*?</Style>")
            .Select(m => m.Value).Last();
        Assert.Contains(@"<Setter Property=""TextAlignment"" Value=""Center""/>", stil);
        Assert.Contains(@"<Setter Property=""VerticalAlignment"" Value=""Center""/>", stil);
    }

    [Fact]
    public void TBB2_SortHeader_Ve_Rapor_Basligi_Ortada()
    {
        var s = Kaynak("DepoWise.Desktop", "SortHeader.cs");
        Assert.Contains("HorizontalContentAlignment = HorizontalAlignment.Center", s);
        Assert.Contains("VerticalContentAlignment = VerticalAlignment.Center", s);
        var d = Kaynak("DepoWise.Desktop", "Controls", "DataGridView.axaml");
        Assert.Contains(@"HorizontalContentAlignment=""Center"" VerticalContentAlignment=""Center""", d);
    }

    [Fact]
    public void TBB3_Hicbir_Tablo_Basliginda_Sola_Saga_Hiza_Yok()
    {
        var hatalar = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(Kok(), "DepoWise.Desktop", "Views"), "*.axaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f), @"<Border[^>]*Classes=""TableHeader""[\s\S]*?</Border>"))
                if (Regex.IsMatch(m.Value, @"(TextAlignment|HorizontalContentAlignment|HorizontalAlignment)=""(Left|Right)"""))
                    hatalar.Add(Path.GetFileName(f));
        Assert.Empty(hatalar);
    }

    [Fact]
    public void TBB4_Rapor_Tablosu_Tutamagi_Isaretci_Yakalamali()
    {
        var x = Kaynak("DepoWise.Desktop", "Controls", "DataGridView.axaml");
        Assert.Contains(@"Classes=""ColGrip""", x);
        Assert.DoesNotContain("<Thumb ", x);
        var cs = Kaynak("DepoWise.Desktop", "Controls", "DataGridView.axaml.cs");
        Assert.Contains("e.Pointer.Capture(b);", cs);
        Assert.Contains("g.CommitWidth(_surukKolon);", cs);   // bırakınca kişisel tercihe kaydedilir
    }

    [Fact]
    public void TBB5_Bakim_Formlari_Tek_Arac_Sayaci()
    {
        foreach (var v in new[] { "MaintenanceView.axaml", "DailyActivityView.axaml" })
        {
            var x = Kaynak("DepoWise.Desktop", "Views", v);
            Assert.DoesNotContain(@"Label=""Yapılma KM""", x);
            Assert.DoesNotContain(@"Label=""Yapılma Saat""", x);
        }
        Assert.Contains("RequireMeter: true", Kaynak("DepoWise.Desktop", "ViewModels", "MaintenanceViewModel.cs"));
        Assert.Contains("RequireMeter: true", Kaynak("DepoWise.Desktop", "ViewModels", "DailyActivityViewModel.cs"));
        foreach (var r in new[] { "Maintenance.razor", "Daily.razor" })
        {
            var x = Kaynak("DepoWise.Web", "Components", "Pages", r);
            Assert.DoesNotContain(@"Label=""Yapılma KM""", x);
            Assert.Contains(@"Label=""@MeterLabel""", x);
        }
    }
}
