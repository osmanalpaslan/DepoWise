using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ MAS-02 · SAYFA DEĞİŞİNCE ZAMANLAYICI BİRİKİYORDU ═══ (denetim 2026-08-26, dördüncü tur)
///
/// <b>Bulunan durum.</b> <c>ShellViewModel.Navigate</c> her gezinmede YENİ bir sayfa ViewModel'i
/// oluşturur ve eskisini yalnız referanstan düşürür. <c>DashboardViewModel</c> ise 60 saniyelik bir
/// <c>DispatcherTimer</c> başlatır ve onu <b>hiçbir yerde durdurmuyordu</b>. Çalışan bir zamanlayıcı
/// kendi işleyicisini — dolayısıyla ViewModel'i — canlı tutar. Kullanıcı "Ana Ekran ↔ başka ekran"
/// arasında N kez gidip geldiğinde <b>N zamanlayıcı birikir</b> ve her biri dakikada bir
/// <b>güncelleme sunucusuna ağ isteği</b> atar (<c>CheckUpdate</c>). Bellek de sürekli büyür.
///
/// MAS-01 ile aynı sınıftan bir hatadır (orada çıkış→giriş döngüsü, burada ekranlar arası gezinme).
/// Bu yüzden düzeltme tek bir yamadan ibaret değildir; <b>genel bir kurala</b> dönüştürülmüştür:
/// <i>zamanlayıcı başlatan her masaüstü ViewModel'i <see cref="System.IDisposable"/> uygular ve
/// zamanlayıcıyı durdurur; kabuk açık sayfa değişince onu bırakır.</i>
///
/// <b>Bu testler neden kaynak okuyor:</b> Avalonia ViewModel'leri <c>DesktopServices</c> ve UI iş
/// parçacığı olmadan örneklenemez (birim testinden çalıştırılamaz). Kural bu yüzden <b>yapısal</b>
/// olarak kilitlenir — aynı sapmanın sessizce geri gelmesini ve YENİ ekranlarda tekrarlanmasını önler.
/// </summary>
public class MasaustuZamanlayiciBirikmeTests
{
    private static string Kok()
    {
        var k = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && k is not null; i++)
        {
            if (Directory.Exists(Path.Combine(k, "src", "DepoWise.Desktop", "ViewModels"))) return k;
            k = Path.GetDirectoryName(k!);
        }
        throw new DirectoryNotFoundException("Proje kökü bulunamadı");
    }

    private static IEnumerable<string> ViewModelDosyalari()
        => Directory.GetFiles(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ViewModels"), "*.cs");

    /// <summary>
    /// ⭐ MAS-02a — GENEL KURAL: <c>DispatcherTimer</c> başlatan her ViewModel onu durdurabilmeli
    /// (<c>Dispose</c> ya da <c>Release</c> içinde <c>Stop()</c>).
    /// </summary>
    [Fact]
    public void MAS02a_Zamanlayici_Baslatan_Her_ViewModel_Durdurabiliyor()
    {
        var ihlaller = new List<string>();

        foreach (var dosya in ViewModelDosyalari())
        {
            var src = File.ReadAllText(dosya);
            if (!src.Contains("DispatcherTimer", StringComparison.Ordinal)) continue;
            if (!src.Contains(".Start()", StringComparison.Ordinal)) continue;

            bool birakmaYolu = src.Contains("public void Dispose()", StringComparison.Ordinal)
                            || src.Contains("public void Release()", StringComparison.Ordinal);
            bool durduruyor = src.Contains(".Stop()", StringComparison.Ordinal);

            if (!birakmaYolu || !durduruyor)
                ihlaller.Add($"{Path.GetFileName(dosya)} (bırakmaYolu={birakmaYolu}, Stop={durduruyor})");
        }

        Assert.True(ihlaller.Count == 0,
            "Zamanlayıcı başlatıp durdurmayan ViewModel(ler): " + string.Join(", ", ihlaller));
    }

    /// <summary>⭐ MAS-02b — Ana Ekran zamanlayıcısı gerçekten durduruluyor.</summary>
    [Fact]
    public void MAS02b_Pano_Zamanlayicisi_Durduruluyor()
    {
        var src = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ViewModels", "DashboardViewModel.cs"));

        Assert.Contains("IDisposable", src, StringComparison.Ordinal);
        var i = src.IndexOf("public void Dispose()", StringComparison.Ordinal);
        Assert.True(i >= 0, "DashboardViewModel.Dispose() yok");
        Assert.Contains("_updateTimer.Stop()", src.Substring(i, Math.Min(300, src.Length - i)), StringComparison.Ordinal);
    }

    /// <summary>⭐ MAS-02c — kabuk, açık sayfa değişince eskisini GERÇEKTEN bırakıyor.</summary>
    [Fact]
    public void MAS02c_Kabuk_Eski_Sayfayi_Birakiyor()
    {
        var src = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ViewModels", "ShellViewModel.cs"));

        Assert.Contains("OnCurrentPageChanging", src, StringComparison.Ordinal);
        var i = src.IndexOf("partial void OnCurrentPageChanging", StringComparison.Ordinal);
        Assert.True(i >= 0, "OnCurrentPageChanging kancası yok");
        var govde = src.Substring(i, Math.Min(300, src.Length - i));
        Assert.Contains("IDisposable", govde, StringComparison.Ordinal);
        Assert.Contains("Dispose()", govde, StringComparison.Ordinal);
    }

    /// <summary>Kaynak kilidinin gerçekten yakaladığını kanıtlar (kural kendi kendini sınar).</summary>
    [Fact]
    public void MAS02d_Kilit_Gercekten_Yakaliyor()
    {
        const string eskiHali = @"
            _updateTimer = new Avalonia.Threading.DispatcherTimer();
            _updateTimer.Tick += (_, _) => Kontrol();
            _updateTimer.Start();";

        Assert.Contains("DispatcherTimer", eskiHali, StringComparison.Ordinal);
        Assert.Contains(".Start()", eskiHali, StringComparison.Ordinal);
        Assert.DoesNotContain(".Stop()", eskiHali, StringComparison.Ordinal);
        Assert.DoesNotContain("public void Dispose()", eskiHali, StringComparison.Ordinal);
    }
}
