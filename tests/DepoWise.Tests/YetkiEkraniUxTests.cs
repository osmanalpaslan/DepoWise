using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3d — YETKİ EKRANI UX (ADR-222 §12) ═══
///
/// Eklenen: <b>arama · süzgeç (yalnız verilenler / yalnız değişenler) · üç durumlu (kısmi) grup
/// kutusu · kaydedilmemiş değişiklik izi</b>. Hiçbiri yetki KARARI vermez; ağaç zaten yalnız
/// verilebilir kalemlerle kurulur (<c>BuildTree</c> · <c>AccessControl.CanGrantModule</c>).
///
/// Bu testler XAML/Razor <b>sözleşmesini</b> kilitler: Avalonia bağlaması yanlış yazılırsa
/// <b>sessizce</b> çalışmaz (hata vermez, kutu hep boş kalır). Gerçek GUI doğrulaması ayrıca
/// yapıldı ve rapora yazıldı; bu testler onun yerine geçmez, tekrar bozulmasını engeller.
///
///  UX1 — Masaüstü görünümündeki her yeni bağlama, ViewModel'de GERÇEKTEN var
///  UX2 — Grup kutusu ÜÇ DURUMLU (kısmi) olarak tanımlı
///  UX3 — Süzgeç yalnız GÖRÜNÜRLÜK: kaydetme yolu (Collect) süzgeci hiç sormaz
///  UX4 — Web ve masaüstü AYNI üç süzgeci sunar (parite)
/// </summary>
public class YetkiEkraniUxTests
{
    private static string Kok()
    {
        var dizin = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dizin is not null; i++)
        {
            if (File.Exists(Path.Combine(dizin, "src", "DepoWise.Desktop", "Views", "PermissionsView.axaml"))) return dizin;
            dizin = Path.GetDirectoryName(dizin);
        }
        throw new DirectoryNotFoundException("Proje kökü bulunamadı.");
    }

    private static string Oku(params string[] parcalar) => File.ReadAllText(Path.Combine(Kok(), Path.Combine(parcalar)));

    private static string Xaml() => Oku("src", "DepoWise.Desktop", "Views", "PermissionsView.axaml");
    private static string Vm() => Oku("src", "DepoWise.Desktop", "ViewModels", "PermissionsViewModel.cs");
    private static string Web() => Oku("src", "DepoWise.Web", "Components", "PermMatrix.razor");

    // ══════════════════ UX1 ══════════════════

    /// <summary>
    /// 🔴 Avalonia'da var olmayan bir özelliğe bağlanmak HATA VERMEZ — kutu sessizce hep kapalı
    /// kalır. Bu yüzden görünümdeki her yeni bağlamanın ViewModel'de karşılığı olduğu kanıtlanır.
    /// </summary>
    [Theory]
    [InlineData("AgacArama")]          // arama kutusu
    [InlineData("YalnizVerilenler")]   // süzgeç
    [InlineData("YalnizDegisenler")]   // süzgeç
    [InlineData("DegisiklikRozeti")]   // kaydedilmemiş değişiklik özeti
    [InlineData("TumSecili")]          // grup üç-durumu
    [InlineData("Gorunur")]            // satır/grup görünürlüğü
    [InlineData("Degisti")]            // satır değişiklik işareti
    public void UX1_Masaustu_Baglamalarinin_ViewModel_Karsiligi_Var(string ozellik)
    {
        Assert.Contains("{Binding " + ozellik + "}", Xaml());

        // Tanım ya [ObservableProperty] alanıdır (_camelCase) ya da doğrudan public özelliktir.
        var camel = char.ToLowerInvariant(ozellik[0]) + ozellik.Substring(1);
        var desen = $@"(private\s+[\w\?\[\]<>]+\s+_{camel}\b)|(public\s+[\w\?\[\]<>]+\s+{ozellik}\b)";
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(Vm(), desen),
            $"ViewModel'de '{ozellik}' tanımı bulunamadı — bağlama sessizce çalışmaz.");
    }

    // ══════════════════ UX2 ══════════════════

    /// <summary>Kısmi (indeterminate) durum GÖSTERİLEBİLİR olmalı: iki durumlu kutuda "grubun
    /// yarısı yetkili" bilgisi kaybolur ve yönetici satırları tek tek okumak zorunda kalır.</summary>
    [Fact]
    public void UX2_Grup_Kutusu_Uc_Durumlu()
    {
        Assert.Contains("IsThreeState=\"True\"", Xaml());
        Assert.Contains("TriState=\"true\"", Web());
    }

    // ══════════════════ UX3 ══════════════════

    /// <summary>
    /// 🔴 EN KRİTİK SÖZLEŞME: süzgeç yalnız GÖRÜNÜRLÜKTÜR. Kaydetme yolu süzgeci sorarsa,
    /// arama açıkken kaydeden yönetici <b>görünmeyen satırların yetkilerini sessizce siler</b>.
    /// Bu yüzden <c>Collect()</c>/<c>CollectButtons()</c> içinde süzgeç adları GEÇMEMELİDİR.
    /// </summary>
    [Fact]
    public void UX3_Kaydetme_Yolu_Suzgeci_Sormaz()
    {
        var web = Web();
        var basla = web.IndexOf("public List<object> Collect()", StringComparison.Ordinal);
        Assert.True(basla > 0, "Collect() bulunamadı.");
        var govde = web.Substring(basla);

        Assert.DoesNotContain("_arama", govde);
        Assert.DoesNotContain("_yalnizVerilenler", govde);
        Assert.DoesNotContain("_yalnizDegisenler", govde);
        Assert.DoesNotContain("Gecer(", govde);
    }

    // ══════════════════ UX4 ══════════════════

    /// <summary>Web ve masaüstü aynı üç süzgeci sunar — iki platform aynı ekranı göstermelidir
    /// (CLAUDE.md §4: işlevsel eşitlik; piksel eşitliği değil).</summary>
    [Fact]
    public void UX4_Web_Ve_Masaustu_Ayni_Suzgecleri_Sunar()
    {
        var web = Web();
        Assert.Contains("Yalnız verilenler", web);
        Assert.Contains("Yalnız değişenler", web);
        Assert.Contains("Ekran / alan ara", web);

        var xaml = Xaml();
        Assert.Contains("Yalnız verilenler", xaml);
        Assert.Contains("Yalnız değişenler", xaml);
        Assert.Contains("Ekran / alan ara", xaml);
    }
}
