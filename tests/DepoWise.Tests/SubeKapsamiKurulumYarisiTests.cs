using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ H5 REGRESYONU (kullanıcı bildirimi, 2026-09-06) ═══
///
/// <para><b>Hata:</b> Cari Hesaplar açılırken ekranda
/// <i>"Liste yüklenemedi: Object reference not set to an instance of an object."</i> çıkıyordu.</para>
///
/// <para><b>Kök neden (yarış durumu / race condition):</b> <c>BranchScopeSelector</c> kurucusundaki
/// <c>Single = varsayilan;</c> ataması, <c>OnSingleChanged</c> üzerinden yenileme geri çağrısını
/// KURUCU DAHA BİTMEDEN çalıştırıyordu. Geri çağrı <c>() =&gt; _ = Load()</c> idi ve <c>Load()</c>
/// çağıran ViewModel'in <c>BranchScope</c> özelliğini okuyor; o özellik ise
/// <c>BranchScope = new BranchScopeSelector(...)</c> satırı HENÜZ TAMAMLANMADIĞI için <c>null</c>.
/// <c>Load()</c> okumayı <c>Task.Run</c> ile yaptığından hata bazen oluşup bazen oluşmuyordu.</para>
///
/// <para><b>Neden kaynak metni denetleniyor:</b> <c>BranchScopeSelector</c> masaüstü (Avalonia)
/// projesindedir; test projesi masaüstünü referans ALMAZ (bkz. <see cref="BranchScopeParityTests"/>
/// başlığı). Bu yüzden sözleşme, sınıf kurulmadan kaynak üzerinden korunur — depoda yerleşik desen.</para>
/// </summary>
public class SubeKapsamiKurulumYarisiTests
{
    private static string RepoKok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static string Oku(params string[] parcalar)
        => File.ReadAllText(Path.Combine(new[] { RepoKok() }.Concat(parcalar).ToArray()));

    private static string Secici() => Oku("src", "DepoWise.Desktop", "ViewModels", "BranchScopeSelector.cs");

    /// <summary>Kurulum bayrağı VAR ve kurucunun SONUNDA kuruluyor (öncesinde değil).</summary>
    [Fact]
    public void H5_KurulumBayragi_KurucununSonundaKurulur()
    {
        var s = Secici();
        Assert.Contains("_kurulumBitti", s);

        var buildPicks = s.IndexOf("BuildPicks();", StringComparison.Ordinal);
        var bayrak = s.IndexOf("_kurulumBitti = true;", StringComparison.Ordinal);
        Assert.True(buildPicks > 0, "BuildPicks() çağrısı bulunamadı — kurucu değişmiş olabilir.");
        Assert.True(bayrak > buildPicks,
            "_kurulumBitti bayrağı kurucunun SONUNDA (BuildPicks() sonrası) kurulmalı; " +
            "daha erken kurulursa H5 yarış durumu geri döner.");
    }

    /// <summary>
    /// Seçicinin KENDİ metotları geri çağrıyı doğrudan çalıştırmaz; hepsi korumalı
    /// <c>Tetikle()</c> üzerinden geçer. (İç <c>Pick</c> sınıfının kendi <c>_changed</c> alanı
    /// <c>OnPickChanged</c>'i işaret eder ve o da korumalıdır — bu yüzden hariç tutulur.)
    /// </summary>
    [Fact]
    public void H5_GeriCagri_YalnizKorumaliTetikleUzerindenCalisir()
    {
        var s = Secici();

        // Pick sınıfının gövdesini çıkar: onun "=> _changed();" satırı meşrudur.
        var pickBasi = s.IndexOf("public sealed partial class Pick", StringComparison.Ordinal);
        Assert.True(pickBasi > 0, "İç Pick sınıfı bulunamadı — dosya yapısı değişmiş.");
        var pickSonu = s.IndexOf("public ObservableCollection<Pick> Picks", StringComparison.Ordinal);
        var pickHaric = s.Remove(pickBasi, pickSonu - pickBasi);

        // Korumanın kendi tanımı meşru tek istisnadır — sayımdan çıkarılır.
        const string tanim = "private void Tetikle() { if (_kurulumBitti) _changed(); }";
        Assert.Contains(tanim, s);
        // XML belge yorumlarında (///) geçen <c>_changed()</c> metni koddan değildir — elenir.
        var kodSatirlari = pickHaric.Replace(tanim, "")
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal));
        var kalan = string.Join("\n", kodSatirlari);

        var dogrudan = Regex.Matches(kalan, @"_changed\(\)");
        Assert.True(dogrudan.Count == 0,
            $"Seçicide korumasız {dogrudan.Count} adet _changed() çağrısı var. " +
            "Yenileme geri çağrısı DAİMA Tetikle() üzerinden yapılmalı (H5 yarış durumu).");
    }

    /// <summary>
    /// Seçiciyi kuran HER ViewModel aynı desendedir: geri çağrı <c>Load()</c>'u çağırır ve
    /// <c>Load()</c> <c>BranchScope</c>'u okur. Bu liste, korumanın hangi ekranları koruduğunu
    /// belgeler; yeni bir ekran eklendiğinde test onu da kapsar.
    /// </summary>
    [Theory]
    [InlineData("PartiesViewModel.cs")]     // Cari Hesaplar (kullanıcının hatayı gördüğü ekran)
    [InlineData("InvoicesViewModel.cs")]    // Faturalar
    [InlineData("FinanceViewModel.cs")]     // Kasa-Banka
    [InlineData("PaymentsViewModel.cs")]    // Tahsilat-Ödeme
    public void H5_SeciciyiKuranEkranlar_YuklemedeBranchScopeOkur(string dosya)
    {
        var s = Oku("src", "DepoWise.Desktop", "ViewModels", dosya);
        Assert.Contains("new BranchScopeSelector(session, () => _ = Load())", s);
        Assert.Contains("BranchScope.Filter", s);   // korumasız kalırsa NRE tam burada olurdu
    }

    /// <summary>
    /// Kapsam seçicisini kuran BAŞKA bir ViewModel eklendiyse bu test onu yakalar:
    /// listeye eklenmesi (ve dolayısıyla yukarıdaki sözleşmeye dahil olması) gerekir.
    /// </summary>
    [Fact]
    public void H5_YeniBirEkranSeciciyiKurarsa_TestListesiGuncellenmeli()
    {
        var vmKok = Path.Combine(RepoKok(), "src", "DepoWise.Desktop", "ViewModels");
        var kuranlar = Directory.GetFiles(vmKok, "*.cs")
            .Where(f => File.ReadAllText(f).Contains("new BranchScopeSelector("))
            .Select(Path.GetFileName)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var beklenen = new[] { "FinanceViewModel.cs", "InvoicesViewModel.cs", "PartiesViewModel.cs", "PaymentsViewModel.cs" };
        Assert.Equal(beklenen, kuranlar);
    }
}
