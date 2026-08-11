using System.Text;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// BKM-04 / KARAR-9 — ARAYÜZ KURALLARININ PARİTESİ (Web ↔ masaüstü).
///
/// Bazı KARAR-9 maddeleri servis katmanında değil, YALNIZ arayüzde yaşar:
///  • md. 1-3 "varsayılan = oturum şubesi"
///  • md. 7 "Atanmamış yeni yazma hedefi olarak SUNULMAZ"
///  • kırmızı çizgi: kullanıcının seçimi <c>Auth.BranchId</c> ile YENİDEN EZİLMEZ
///
/// Test projesi Web/Desktop projelerine referans VERMEZ (Razor/Avalonia derlenmez) — RPR-01'de kurulan
/// desenle iki arayüzün KAYNAK METNİ okunur. Üretim kodu değiştirilmez.
///
/// ⚠️ Bu testler görsel (piksel) doğrulama DEĞİLDİR; alanın var olduğunu, doğru kaynağa bağlandığını
/// ve yasak seçeneğin sunulmadığını kilitler.
/// </summary>
public class MaintenanceLocationUiParityTests
{
    private static readonly string Root = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DepoWise.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "DepoWise.sln bulunamadı — arayüz kaynakları okunamıyor (sessizce atlanmaz).");
    }

    private static string Src(params string[] parts)
    {
        var p = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        if (!File.Exists(p)) throw new FileNotFoundException($"BKM-04 parite testi için gereken kaynak yok: {p}", p);
        return File.ReadAllText(p, Encoding.UTF8);
    }

    private static readonly string WebMaintenance = Src("src", "DepoWise.Web", "Components", "Pages", "Maintenance.razor");
    private static readonly string WebDaily = Src("src", "DepoWise.Web", "Components", "Pages", "Daily.razor");
    private static readonly string DeskMaintVm = Src("src", "DepoWise.Desktop", "ViewModels", "MaintenanceViewModel.cs");
    private static readonly string DeskDailyVm = Src("src", "DepoWise.Desktop", "ViewModels", "DailyActivityViewModel.cs");
    private static readonly string DeskMaintView = Src("src", "DepoWise.Desktop", "Views", "MaintenanceView.axaml");
    private static readonly string DeskDailyView = Src("src", "DepoWise.Desktop", "Views", "DailyActivityView.axaml");
    private static readonly string Picker = Src("src", "DepoWise.Desktop", "StockLocationPicker.cs");
    private static readonly string LocationOptions = Src("src", "DepoWise.Web", "Services", "LocationOptions.cs");

    // ══════════════ 1 — Varsayılan = oturum şubesi (iki arayüzde de) ══════════════

    /// <summary>1 — Varsayılan depo, kullanıcının aktif/oturum şubesidir. Web'de <c>Auth.BranchId</c>,
    /// masaüstünde <c>session.OperatingBranchId</c> — ikisi de "oturum şubesi" demektir.</summary>
    [Fact]
    public void Varsayilan_Depo_Oturum_Subesi_IKI_Arayuzde_de()
    {
        // WEB: varsayılan Auth.BranchId'den gelir, ama YALNIZ listede varsa (rastgele depo tahmin edilmez).
        Assert.Contains("_mLocations.Any(x => x.Id == Auth.BranchId) ? Auth.BranchId : null", WebMaintenance, StringComparison.Ordinal);
        Assert.Contains("_mLocations.Any(x => x.Id == Auth.BranchId) ? Auth.BranchId : null", WebDaily, StringComparison.Ordinal);

        // MASAÜSTÜ: aynı kural tek yerde (StockLocationPicker) — iki ekran da oradan besleniyor.
        Assert.Contains("session.OperatingBranchId", Picker, StringComparison.Ordinal);
        Assert.Contains("StockLocationPicker.Load", DeskMaintVm, StringComparison.Ordinal);
        Assert.Contains("StockLocationPicker.DefaultFor", DeskDailyVm, StringComparison.Ordinal);
    }

    // ══════════════ 11 — "Atanmamış" YENİ YAZMA HEDEFİ OLARAK SUNULMAZ ══════════════

    /// <summary>11 — 🔴 Hiçbir arayüz "Atanmamış"ı bakım deposu SEÇENEĞİ olarak listelememeli.
    /// Web listesi <c>WriteTargets()</c>'ten gelir (bu metot Atanmamış'ı bilinçli olarak DIŞARIDA bırakır);
    /// masaüstü listesi yalnız gerçek şubelerdir.</summary>
    [Fact]
    public void Atanmamis_Yeni_Yazma_Hedefi_Olarak_SUNULMAZ()
    {
        // Web: seçenek kaynağı WriteTargets — FilterOptionsAsync DEĞİL (o Atanmamış'ı içerir).
        Assert.Contains("Locations.WriteTargets()", WebMaintenance, StringComparison.Ordinal);
        Assert.Contains("Locations.WriteTargets()", WebDaily, StringComparison.Ordinal);
        Assert.DoesNotContain("Locations.FilterOptionsAsync()", WebMaintenance, StringComparison.Ordinal);
        Assert.DoesNotContain("Locations.FilterOptionsAsync()", WebDaily, StringComparison.Ordinal);

        // WriteTargets sözleşmesi hâlâ "yalnız gerçek lokasyonlar" diyor (STK-04'te kurulan kural).
        Assert.Contains("YALNIZ gerçek lokasyonlar", LocationOptions, StringComparison.Ordinal);

        // Masaüstü: bakım ekranlarındaki depo seçicisi "📦 Atanmamış" satırı EKLEMİYOR
        // (bu etiket yalnız GÖRÜNTÜLEME/filtre listelerinde kullanılır — ör. Raporlar ekranı).
        Assert.DoesNotContain("📦 Atanmamış", DeskMaintVm, StringComparison.Ordinal);
        Assert.DoesNotContain("📦 Atanmamış", DeskDailyVm, StringComparison.Ordinal);
        Assert.DoesNotContain("📦 Atanmamış", Picker, StringComparison.Ordinal);
    }

    // ══════════════ KIRMIZI ÇİZGİ — kullanıcının seçimi sessizce ezilmez ══════════════

    /// <summary>🔴 Web, POST gövdesine KULLANICININ SEÇTİĞİ alanı gönderir (<c>_mLocationId</c>),
    /// <c>Auth.BranchId</c>'yi DEĞİL. Aksi hâlde kullanıcının değişikliği sessizce yok sayılırdı.</summary>
    [Fact]
    public void Web_Gonderirken_Kullanicinin_Secimini_Kullanir_Auth_BranchId_ile_EZMEZ()
    {
        Assert.Contains("branchId = _mLocationId", WebMaintenance, StringComparison.Ordinal);
        Assert.Contains("branchId = _mLocationId", WebDaily, StringComparison.Ordinal);
        // Bakım/faaliyet gövdesinde "branchId = Auth.BranchId" GEÇMEMELİ.
        Assert.DoesNotContain("branchId = Auth.BranchId,\n                    materials", WebMaintenance, StringComparison.Ordinal);
    }

    /// <summary>🔴 Masaüstü de kullanıcının seçtiği nesneyi gönderir — oturum şubesini DEĞİL.</summary>
    [Fact]
    public void Masaustu_Gonderirken_Kullanicinin_Secimini_Kullanir()
    {
        Assert.Contains("StockLocationId: MntLocation?.Id", DeskMaintVm, StringComparison.Ordinal);
        Assert.Contains("StockLocationId: MntLocation?.Id", DeskDailyVm, StringComparison.Ordinal);
        // Doğrudan oturum şubesi gönderilmiyor (sessiz yönlendirme yasağı).
        Assert.DoesNotContain("StockLocationId: _session.OperatingBranchId", DeskMaintVm, StringComparison.Ordinal);
        Assert.DoesNotContain("StockLocationId: _session.OperatingBranchId", DeskDailyVm, StringComparison.Ordinal);
    }

    // ══════════════ 23 — İki arayüz aynı alanı, aynı anlamda kullanıyor ══════════════

    /// <summary>23 — Web ve masaüstü AYNI sözleşmeyi kullanır: aynı etiket, aynı alan, aynı varsayılan.
    /// (Aynı seçilen lokasyonla aynı stok sonucunun oluştuğu servis testlerinde kanıtlanıyor —
    /// rapor motoru ve stok servisi zaten ORTAK.)</summary>
    [Fact]
    public void Web_ve_Masaustu_Ayni_Alani_Ayni_Etiketle_Kullaniyor()
    {
        const string etiket = "Malzemenin çekildiği depo";
        Assert.Contains(etiket, WebMaintenance, StringComparison.Ordinal);
        Assert.Contains(etiket, WebDaily, StringComparison.Ordinal);
        Assert.Contains(etiket, DeskMaintView, StringComparison.Ordinal);
        Assert.Contains(etiket, DeskDailyView, StringComparison.Ordinal);

        // Seçici gerçekten bağlı (yalnız etiket yazılıp bırakılmamış).
        Assert.Contains("Binding MntLocation", DeskMaintView, StringComparison.Ordinal);
        Assert.Contains("Binding MntLocation", DeskDailyView, StringComparison.Ordinal);
        Assert.Contains("@bind-Value=\"_mLocationId\"", WebMaintenance, StringComparison.Ordinal);
        Assert.Contains("@bind-Value=\"_mLocationId\"", WebDaily, StringComparison.Ordinal);
    }

    /// <summary>12 — Firmada hiç depo yoksa dört arayüz de kullanıcıyı AÇIKÇA uyarır
    /// (kayıt engellenmez ama stoğun "Atanmamış"a düşeceği gizlenmez).</summary>
    [Fact]
    public void Depo_Yoksa_Kullanici_Aciklamayla_Uyarilir()
    {
        foreach (var (kaynak, ad) in new[]
                 {
                     (WebMaintenance, "Web/Maintenance.razor"), (WebDaily, "Web/Daily.razor"),
                     (DeskMaintView, "Desktop/MaintenanceView.axaml"), (DeskDailyView, "Desktop/DailyActivityView.axaml"),
                 })
        {
            Assert.True(kaynak.Contains("tanımlı depo/şantiye yok", StringComparison.Ordinal),
                $"{ad}: firmada depo yokken gösterilecek uyarı metni bulunamadı.");
            Assert.True(kaynak.Contains("Atanmamış", StringComparison.Ordinal),
                $"{ad}: uyarı, stoğun \"Atanmamış\" olarak düşeceğini söylemiyor.");
        }
    }

    /// <summary>Bakım ekranlarındaki "Tüm Şubeler" kapısı KORUNDU (KARAR-9 bunu kaldırmadı).</summary>
    [Fact]
    public void Tum_Subeler_Kapisi_Korundu()
    {
        Assert.Contains("RequireBranchAsync(Auth, \"Bakım Takibi\")", WebMaintenance, StringComparison.Ordinal);
        Assert.Contains("RequireBranchAsync(_session, \"Bakım Takibi\")", DeskMaintVm, StringComparison.Ordinal);
    }
}
