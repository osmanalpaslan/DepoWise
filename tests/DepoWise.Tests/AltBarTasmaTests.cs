using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ İ1 — ALT BAR: TAŞAN SEKMELER + "DİĞER SAYFALAR" + SABİT SOHBET ═══
/// (kullanıcı tasarımı ve eklediği görsel, 2026-09-06)
///
/// <para>Kullanıcının koyduğu kurallar: <i>"sığdığı kadar sekme → «Diğer Sayfalar ∨» → en sağda
/// sabit «Sohbet»; menü YUKARI açılır, ikonlarla listelenir, aynı isimlilerde adet gösterilir
/// (Bakım Takibi (x2)); pencere boyutu değişince taşanlar otomatik panele aktarılır; koyu VE açık
/// temaya, yuvarlatılmış hatlara tam uyum."</i></para>
///
/// <para>Masaüstü projesi test projesinden referanslanmadığı için sözleşme kaynak üzerinden korunur
/// (depoda yerleşik desen). Gerçek yerleşim ayrıca çalışan uygulamada ölçülerek doğrulandı:
/// 1366 px'te 4-5 sekme sığdı, kalanlar menüye düştü, Sohbet en sağda kaldı, aktif sekme barda kaldı.</para>
/// </summary>
public class AltBarTasmaTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { Kok() }.Concat(p).ToArray()));
    private static string Pencere() => Oku("src", "DepoWise.Desktop", "Views", "MainWindow.axaml");
    private static string Panel() => Oku("src", "DepoWise.Desktop", "Controls", "TasanSekmePaneli.cs");

    /// <summary>Sekme şeridi artık yatay kaydırma değil, TAŞMA paneli kullanır.</summary>
    [Fact]
    public void SekmeSeridi_TasmaPaneliKullanir_KaydirmaDegil()
    {
        var x = Pencere();
        Assert.Contains("ctrl:TasanSekmePaneli", x);
        Assert.Contains("TasmaKomutu=", x);
        Assert.Contains("AktifSira=", x);   // aktif sekme daima barda kalmalı
    }

    /// <summary>"Diğer Sayfalar" düğmesi var, YUKARI açılıyor ve yalnız taşma varken görünüyor.</summary>
    [Fact]
    public void DigerSayfalar_YukariAcilir_VeYalnizTasmaVarkenGorunur()
    {
        var x = Pencere();
        Assert.Contains("{Binding TasanBaslik}", x);
        Assert.Contains("IsVisible=\"{Binding TasanVar}\"", x);
        Assert.Contains("<Flyout Placement=\"Top\"", x);          // şerit altta → menü YUKARI açılır
        Assert.Contains("TasaniAcCommand", x);
        Assert.Contains("{Binding Gosterim}", x);                  // "Bakım Takibi (x2)" biçimi
        // Menü satırlarında İKON bulunur (kullanıcı isteği).
        var menuBlok = x[x.IndexOf("<Flyout Placement=\"Top\"", StringComparison.Ordinal)..];
        Assert.Contains("<PathIcon", menuBlok[..2000]);
    }

    /// <summary>Sohbet EN SAĞDA sabittir: sağa yaslanır ve "Diğer Sayfalar"dan SONRA yerleşir.</summary>
    [Fact]
    public void Sohbet_EnSagda_Sabit_Kalir()
    {
        var x = Pencere();
        var sohbet = x.IndexOf("ANA SOHBET DÜĞMESİ", StringComparison.Ordinal);
        var diger = x.IndexOf("\"Diğer Sayfalar ∨\" — sekmelerin HEMEN ARDINDA", StringComparison.Ordinal);
        Assert.True(sohbet > 0, "Ana sohbet düğmesi bulunamadı.");
        Assert.True(diger > 0, "\"Diğer Sayfalar\" düğmesi bulunamadı.");
        // DockPanel'de SAĞA yaslananlar YAZILDIKLARI sırayla dıştan içe dizilir:
        // Sohbet ÖNCE yazılır → en sağda kalır; "Diğer Sayfalar" SONRA yazılır → onun soluna girer.
        Assert.True(sohbet < diger,
            "Sohbet, DockPanel'de \"Diğer Sayfalar\"dan ÖNCE sağa yaslanmalı; aksi hâlde en sağda kalmaz.");
    }

    /// <summary>Panel, çocukların IsVisible'ına DOKUNMAZ (ölçüm döngüsü/titreme riski).</summary>
    [Fact]
    public void Panel_IsVisible_Degistirmez_VeAktifeYerAcar()
    {
        var p = Panel();
        Assert.DoesNotContain("IsVisible =", p);
        Assert.Contains("new Rect(0, 0, 0, 0)", p);     // sığmayan sekme sıfır alana yerleşir
        Assert.Contains("AktifSira", p);
        Assert.Contains("AffectsMeasure", p);           // aralık/aktiflik değişince yeniden ölçülür
    }

    /// <summary>
    /// KOYU VE AÇIK TEMA: yeni stillerin kullandığı renk anahtarlarının İKİ varyantta da tanımlı
    /// olması gerekir. Yalnız koyu varyantta tanımlı bir anahtar, açık temada görünmez yazıya yol açar.
    /// </summary>
    [Theory]
    [InlineData("SurfaceHoverBrush")]
    [InlineData("TextPrimaryBrush")]
    [InlineData("TextSecondaryBrush")]
    [InlineData("SurfaceElevatedBrush")]
    [InlineData("BorderSubtleBrush")]
    public void YeniStillerinRenkleri_HemKoyuHemAcikTemada_Tanimli(string anahtar)
    {
        var palet = Oku("src", "DepoWise.Desktop", "Themes", "Palette.axaml");
        var adet = Regex.Matches(palet, $"x:Key=\"{Regex.Escape(anahtar)}\"").Count;
        Assert.True(adet >= 2,
            $"{anahtar} paletin İKİ varyantında da (koyu + açık) tanımlı olmalı; bulunan tanım: {adet}.");
    }

    /// <summary>Yuvarlatılmış hatlar (kullanıcı: "yuvarlatılmış düğme/panel hatlarına tam uyum").</summary>
    [Fact]
    public void MenuSatirlari_Yuvarlatilmis()
    {
        var x = Pencere();
        var stil = x[x.IndexOf("Button.tasanSatir", StringComparison.Ordinal)..];
        Assert.Contains("CornerRadius", stil[..600]);
    }
}
