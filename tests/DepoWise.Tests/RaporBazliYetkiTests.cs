using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RAPOR BAZLI YETKİ (kullanıcı isteği 2026-09-03) ═══
///
/// Kullanıcı: "raporlar ekranında listelenen BÜTÜN raporların ayrı yetkilere bağlanmasını istiyorum."
///
/// Kural (ReportCatalog.CanSee, TEK MERKEZ): rapor görünür/çalışır ⇔ KATEGORİ anahtarı VEYA o rapora
/// özel kalem (rpt_&lt;anahtar&gt;). "VEYA" bilinçli: mevcut kategori atamaları AYNEN çalışır (yayında
/// kimsenin gördüğü rapor değişmez); ince kontrol isteyen yönetici kategori anahtarını kaldırıp rapor
/// kalemlerini tek tek verir.
///
///  RY1 — Her sabit raporun yetki kalemi kataloğdan OTOMATİK üretilir (kalıcı kural: yeni rapor →
///        yetki kalemi kendiliğinden; ayrıca hiçbir kalem ekran modülleriyle ÇAKIŞMAZ).
///  RY2 — Yalnız RAPOR KALEMİ olan kullanıcı O raporu çalıştırır; kategorinin diğer raporu 403.
///  RY3 — Yalnız KATEGORİ anahtarı olan kullanıcı (mevcut atamalar) kategorinin raporlarını çalıştırır.
///  RY4 — İkisi de yoksa rapor çalışmaz (reports üst kapısı verilmiş olsa bile).
///  RY5 — Kategorize ağaç: "Diğer" grubu BOŞ (eşlenmemiş anahtar yok) ve rapor kalemleri "Raporlar"
///        grubunda; menü kaynağına (All) SIZMADI.
/// </summary>
public class RaporBazliYetkiTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly ReportService _reports;
    private const string Co = "RYETKI";

    public RaporBazliYetkiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ryetki_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _reports = new ReportService(_f);
    }

    /// <summary>Personel oturumu: "reports" üst kapısı + verilen ek modül anahtarları (yalnız View).</summary>
    private static SessionContext Personel(params string[] ekModuller)
        => new("u-r", Co, new[] { RoleKeys.Staff },
            new PermissionSet(new[] { "reports" }.Concat(ekModuller)
                .Select(m => new ModulePermission(m, true, false, false, false)).ToArray()));

    private static ReportRequest Istek() => new(true,
        DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds(),
        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    [Fact]
    public void RY1_Her_Raporun_Kalemi_Otomatik_Uretilir_ve_Cakismaz()
    {
        Assert.Equal(ReportCatalog.All.Count, AppModules.ReportItems.Count);
        foreach (var d in ReportCatalog.All)
        {
            var kalem = AppModules.ReportItemKey(d.Key);
            Assert.Contains(AppModules.ReportItems, x => x.Key == kalem);
            Assert.True(AppModules.IsReportItem(kalem));
            Assert.Contains(d.Name, AppModules.Label(kalem));          // etiket rapor adını taşır
            Assert.DoesNotContain(AppModules.All, m => m.Key == kalem); // menü kaynağına SIZMADI
        }
    }

    [Fact]
    public void RY2_Yalniz_Rapor_Kalemi_O_Raporu_Acar_Digerini_Acmaz()
    {
        var s = Personel(AppModules.ReportItemKey("stock"));   // yalnız "Stok Durumu" kalemi

        var stok = ReportCatalog.ByKey("stock")!;
        Assert.True(ReportCatalog.CanSee(s, stok));
        _reports.Run(s, "stock", Istek());                     // çalışır (patlamaz)

        // Aynı kategorideki BAŞKA rapor kapalı: kalem raporu açar, kategoriyi AÇMAZ.
        var sayim = ReportCatalog.ByKey("stock-count")!;
        Assert.False(ReportCatalog.CanSee(s, sayim));
        Assert.Throws<ForbiddenException>(() => _reports.Run(s, "stock-count", Istek()));
    }

    [Fact]
    public void RY3_Kategori_Anahtari_Eskisi_Gibi_Calisir()
    {
        var s = Personel("report_stock");                      // mevcut atama biçimi (RPT-YETKI)
        Assert.True(ReportCatalog.CanSee(s, ReportCatalog.ByKey("stock")!));
        Assert.True(ReportCatalog.CanSee(s, ReportCatalog.ByKey("stock-count")!));
        _reports.Run(s, "stock", Istek());                     // davranış korunur
    }

    [Fact]
    public void RY4_Ikisi_de_Yoksa_Rapor_Calismaz()
    {
        var s = Personel();                                    // yalnız "reports" üst kapısı
        Assert.False(ReportCatalog.CanSee(s, ReportCatalog.ByKey("stock")!));
        Assert.Throws<ForbiddenException>(() => _reports.Run(s, "stock", Istek()));
    }

    [Fact]
    public void RY5_Kategorize_Agac_Tam_ve_Rapor_Kalemleri_Raporlar_Grubunda()
    {
        var gruplar = AppModules.Grouped();

        // "Diğer" boş kalmalı: yeni anahtar eklenip GRUBA eşlenmezse bu test düşer (sessiz kaybolma yok).
        Assert.DoesNotContain(gruplar, g => g.Title == "Diğer");

        // Gruplar, All + rapor kalemlerinin TAMAMINI kapsar (hiçbir anahtar kaybolmaz/ikizlenmez).
        var duz = gruplar.SelectMany(g => g.Items.Select(i => i.Key)).ToList();
        Assert.Equal(AppModules.All.Count + AppModules.ReportItems.Count, duz.Count);
        Assert.Equal(duz.Count, duz.Distinct(StringComparer.Ordinal).Count());

        var raporlar = gruplar.Single(g => g.Title == "Raporlar");
        Assert.Contains(raporlar.Items, i => i.Key == "report_stock");                      // kategori anahtarı
        Assert.Contains(raporlar.Items, i => i.Key == AppModules.ReportItemKey("stock"));   // rapor kalemi
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
