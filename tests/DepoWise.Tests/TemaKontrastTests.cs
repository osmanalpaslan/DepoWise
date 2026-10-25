using System.Globalization;
using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-59 (AR-GE 2026-10-25, F-061 · F-125) — TEMA KONTRASTI İKİ TEMADA, İKİ ORTAMDA ═══
/// Kontrast göz kararı değildir: WCAG 2.1 bağıl ışıklılık oranı HESAPLANIR (MenuRenkTests ile aynı formül).
///  TK1 — masaüstü: yazı olarak kullanılan token'lar (metin, durum, aksan yazısı) her zeminde ≥4,5:1 — koyu + açık
///  TK2 — masaüstü: güçlü kenarlık ve odak halkası ≥3:1 (WCAG 1.4.11)
///  TK3 — birincil düğme yazısı (OnAccentBrush) kehribar düğmenin tüm tonlarında ≥4,5:1; sabit renk yerine token
///  TK4 — web (MudBlazor) durum renkleri iki temada ≥4,5:1
/// </summary>
public class TemaKontrastTests
{
    private static double Kanal(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Isiklilik(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];   // #AARRGGBB → yalnız renk (bu testte opak renkler beklenir)
        double R(int i) => int.Parse(h.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
        return 0.2126 * Kanal(R(0)) + 0.7152 * Kanal(R(2)) + 0.0722 * Kanal(R(4));
    }

    private static double Kontrast(string a, string b)
    {
        double x = Isiklilik(a), y = Isiklilik(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    /// <summary>Palette.axaml → (tema adı → anahtar → renk).</summary>
    private static Dictionary<string, Dictionary<string, string>> Palet()
    {
        var s = Oku("src/DepoWise.Desktop/Themes/Palette.axaml");
        int koyu = s.IndexOf("x:Key=\"Dark\"", StringComparison.Ordinal), acik = s.IndexOf("x:Key=\"Light\"", StringComparison.Ordinal);
        Assert.True(koyu > 0 && acik > koyu);
        Dictionary<string, string> Oz(string blok) => Regex.Matches(blok, @"<SolidColorBrush x:Key=""(\w+)""\s+Color=""(#[0-9A-Fa-f]{6,8})""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        return new() { ["KOYU"] = Oz(s[koyu..acik]), ["AÇIK"] = Oz(s[acik..]) };
    }

    private static readonly string[] Zeminler = { "SurfaceBrush", "AppBackgroundBrush", "SurfaceElevatedBrush" };

    [Fact]
    public void TK1_Masaustu_Yazi_Tokenlari_4_5()
    {
        var yazilar = new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush", "SuccessBrush", "WarningBrush",
                              "DangerBrush", "InfoBrush", "AccentTextBrush" };
        var zayif = new List<string>();
        foreach (var (tema, p) in Palet())
            foreach (var y in yazilar)
                foreach (var z in Zeminler)
                {
                    Assert.True(p.ContainsKey(y), $"{tema}: {y} tanımlı değil");
                    var oran = Kontrast(p[y], p[z]);
                    if (oran < 4.5) zayif.Add($"{tema} · {y} {p[y]} / {z} {p[z]} → {oran:F2}:1");
                }
        Assert.True(zayif.Count == 0, "WCAG 4,5:1 altında yazı rengi:\n  " + string.Join("\n  ", zayif));
    }

    [Fact]
    public void TK2_Masaustu_Kenarlik_Ve_Odak_3()
    {
        var zayif = new List<string>();
        foreach (var (tema, p) in Palet())
            foreach (var t in new[] { "BorderStrongBrush", "FocusRingBrush" })
                foreach (var z in Zeminler)
                {
                    var oran = Kontrast(p[t], p[z]);
                    if (oran < 3.0) zayif.Add($"{tema} · {t} {p[t]} / {z} → {oran:F2}:1");
                }
        Assert.True(zayif.Count == 0, "WCAG 3:1 altında kenarlık/odak:\n  " + string.Join("\n  ", zayif));
    }

    [Fact]
    public void TK3_Birincil_Dugme_Yazisi_Token_Ve_Kontrast()
    {
        var bilesen = Oku("src/DepoWise.Desktop/Themes/Components.axaml");
        var i = bilesen.IndexOf("<Style Selector=\"Button.Primary\">", StringComparison.Ordinal);
        Assert.True(i >= 0);
        var stil = bilesen[i..bilesen.IndexOf("</Style>", i, StringComparison.Ordinal)];
        Assert.Contains("{DynamicResource OnAccentBrush}", stil);
        // Birincil düğmenin gradyan tonları (normal + üzerine gelince) — hepsinde yazı okunur olmalı.
        var tonlar = Regex.Matches(bilesen[i..(i + 2500)], @"GradientStop Color=""(#[0-9A-Fa-f]{6})""").Select(m => m.Groups[1].Value).ToList();
        Assert.True(tonlar.Count >= 4, "birincil düğme gradyanı bulunamadı");
        foreach (var (tema, p) in Palet())
            foreach (var ton in tonlar)
                Assert.True(Kontrast(p["OnAccentBrush"], ton) >= 4.5, $"{tema}: OnAccent / {ton} 4,5:1 altında");
        // sabit #241703 artık yalnız palette (token tanımı) ve açıklama satırında
        foreach (var dosya in new[] { "src/DepoWise.Desktop/Views/MainWindow.axaml", "src/DepoWise.Desktop/Views/LoginWindow.axaml" })
            Assert.DoesNotContain("#241703", Oku(dosya));
    }

    /// <summary>TK5 — M-59 (2/2): TEK yazı tipi (Inter) iki ortamda; masaüstü gövde ≥14, yardım ≥12; web MudBlazor yazı tipi
    /// değişkenlerinin HEPSİ Inter'e bağlı (yoksa MudBlazor bileşenleri Roboto'da kalır).</summary>
    [Fact]
    public void TK5_Tek_Yazi_Tipi_Ve_Okunakli_Boyut()
    {
        var olcek = Oku("src/DepoWise.Desktop/Themes/Scales.axaml");
        Assert.Contains("<FontFamily x:Key=\"AppFontFamily\">fonts:Inter#Inter,", olcek);
        double Boyut(string k) => double.Parse(Regex.Match(olcek, @"x:Key=""" + k + @""">([\d.]+)<").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(Boyut("FontBody") >= 14, "gövde yazısı 14'ün altında");
        Assert.True(Boyut("FontHelper") >= 12, "yardım yazısı 12'nin altında");

        Assert.Contains("family=Inter:", Oku("src/DepoWise.Web/Components/App.razor"));
        Assert.DoesNotContain("Plus+Jakarta", Oku("src/DepoWise.Web/Components/App.razor"));
        var css = Oku("src/DepoWise.Web/wwwroot/app.css");
        foreach (var v in new[] { "default", "h1", "h2", "h3", "h4", "h5", "h6", "subtitle1", "subtitle2", "body1", "body2", "button", "caption", "overline" })
            Assert.Matches(new Regex(@"--mud-typography-" + v + @"-family: 'Inter'.*!important;"), css);
    }

    [Fact]
    public void TK4_Web_Durum_Renkleri_4_5()
    {
        var s = Oku("src/DepoWise.Web/Components/Layout/MainLayout.razor");
        int koyu = s.IndexOf("PaletteDark = new PaletteDark", StringComparison.Ordinal), acik = s.IndexOf("PaletteLight = new PaletteLight", StringComparison.Ordinal);
        Assert.True(koyu > 0 && acik > koyu);
        var zayif = new List<string>();
        foreach (var (tema, blok) in new[] { ("KOYU", s[koyu..acik]), ("AÇIK", s[acik..(acik + 2500)]) })
        {
            string Renk(string ad) => Regex.Match(blok, @"\b" + ad + @" = ""(#[0-9A-Fa-f]{6})""").Groups[1].Value;
            foreach (var zemin in new[] { "Surface", "Background" })
                foreach (var ad in new[] { "Success", "Warning", "Error", "Info", "TextPrimary", "TextSecondary" })
                {
                    var (r, z) = (Renk(ad), Renk(zemin));
                    Assert.False(string.IsNullOrEmpty(r) || string.IsNullOrEmpty(z), $"{tema}: {ad}/{zemin} bulunamadı");
                    var oran = Kontrast(r, z);
                    if (oran < 4.5) zayif.Add($"{tema} · {ad} {r} / {zemin} {z} → {oran:F2}:1");
                }
        }
        Assert.True(zayif.Count == 0, "Web: WCAG 4,5:1 altında renk:\n  " + string.Join("\n  ", zayif));
    }
}
