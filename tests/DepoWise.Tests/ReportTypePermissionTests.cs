using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ RPT-YETKI (2026-08-29, PK-R2=A) — RAPOR TÜRÜ (KATEGORİ) YETKİLERİ sözleşme kilitleri.
///
/// Yeni sözleşme (kullanıcı onayı): "reports" ÜST KAPI olarak kalır; her rapor türü ayrıca
/// KATEGORİSİNİN yetki modülünü ister (8 anahtar: report_vehicle/stock/fuel/maintenance/requests/
/// management/material/accounting). Kilitlenen kurallar:
///  • reports YOK → hiçbir rapor çalışmaz (eski kural aynen).
///  • reports VAR + kategori YOK → o kategorinin raporu ÇALIŞMAZ (yeni ikinci kapı).
///  • reports VAR + kategori VAR → çalışır (kapatma/RequiredModule yoksa başka şart aranmaz).
///  • Bir kategori yetkisi BAŞKA kategorinin raporunu AÇMAZ (çapraz sızma yok).
///  • Eşleme TEK merkezden (ReportCatalog.CategoryModule) ve kataloğdaki HER rapor geçerli bir
///    AppModules anahtarına çözülür — rapor tür adı değiştirilerek kapı atlatılamaz.
///  • Admin/firma admini mevcut bypass kuralıyla geçer; ceiling/rol-blok/tenant/BranchAccess DEĞİŞMEDİ.
///  • Katalog SÜZMELERİ (API + masaüstü) aynı eşlemeyi kullanır (kaynak-düzeyi parite kilidi).
/// </summary>
public class ReportTypePermissionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    public ReportTypePermissionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_rptperm_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var uid = new UserService(_factory).EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private static ReportRequest Istek() => new(true, 1, 2_000_000_000_000);

    private static SessionContext Personel(params string[] moduller)
        => new("u1", "A", new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, false, false, false)).ToArray()));

    // ── Eşleme bütünlüğü ──

    /// <summary>Kataloğdaki HER rapor türü geçerli bir kategori yetki modülüne çözülür ve o modül
    /// yetki ağacında (AppModules.All) VARDIR — yeni rapor/kategori eklenirse bu kilit yakalar.</summary>
    [Fact]
    public void HerRapor_GecerliBirKategoriModulune_Eslenir()
    {
        var agac = AppModules.All.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var d in ReportCatalog.All)
        {
            var modul = ReportCatalog.CategoryModule(d.Category);
            Assert.StartsWith("report_", modul, StringComparison.Ordinal);
            Assert.Contains(modul, agac);
            Assert.False(string.IsNullOrWhiteSpace(AppModules.Label(modul)));
        }
    }

    [Fact]
    public void SekizKategoriAnahtari_YetkiAgacinda_Var()
    {
        var agac = AppModules.All.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var k in new[]
        {
            "report_vehicle", "report_stock", "report_fuel", "report_maintenance",
            "report_requests", "report_management", "report_material", "report_accounting",
        })
            Assert.Contains(k, agac);
    }

    // ── Çift kapı matrisi (temsili raporlar; acc-* AccountingReportTests'te ayrıca kapsanır) ──

    [Theory]
    [InlineData("vehicle", "report_vehicle")]
    [InlineData("vehicle-daily", "report_vehicle")]
    [InlineData("stock", "report_stock")]
    [InlineData("stock-movements", "report_stock")]
    [InlineData("fuel", "report_fuel")]
    [InlineData("maintenance", "report_maintenance")]
    [InlineData("requests", "report_requests")]
    public void ReportsVar_KategoriYok_403(string rapor, string kategori)
    {
        Assert.Equal(kategori, ReportCatalog.CategoryModule(ReportCatalog.ByKey(rapor)!.Category));
        var yalnizReports = Personel("reports");
        Assert.Throws<ForbiddenException>(() => _reports.Run(yalnizReports, rapor, Istek()));
    }

    [Theory]
    [InlineData("vehicle", "report_vehicle")]
    [InlineData("vehicle-daily", "report_vehicle")]
    [InlineData("stock", "report_stock")]
    [InlineData("fuel", "report_fuel")]
    public void ReportsVeKategori_Var_Calisir(string rapor, string kategori)
    {
        var t = _reports.Run(Personel("reports", kategori), rapor, Istek());
        Assert.NotNull(t);   // veri boş olabilir; kapıdan GEÇMESİ sözleşmedir
    }

    /// <summary>Çapraz sızma yok: report_stock sahibi ARAÇ raporunu, report_vehicle sahibi STOK
    /// raporunu çalıştıramaz.</summary>
    [Fact]
    public void KategoriYetkisi_BaskaKategoriyi_Acmaz()
    {
        var stokcu = Personel("reports", "report_stock");
        Assert.Throws<ForbiddenException>(() => _reports.Run(stokcu, "vehicle", Istek()));
        Assert.Throws<ForbiddenException>(() => _reports.Run(stokcu, "fuel", Istek()));

        var aracci = Personel("reports", "report_vehicle");
        Assert.Throws<ForbiddenException>(() => _reports.Run(aracci, "stock", Istek()));
        Assert.NotNull(_reports.Run(aracci, "vehicle", Istek()));
    }

    /// <summary>Üst kapı korunur: kategori VAR ama "reports" YOK → yine reddedilir.</summary>
    [Fact]
    public void KategoriVar_ReportsYok_403()
        => Assert.Throws<ForbiddenException>(() => _reports.Run(Personel("report_stock"), "stock", Istek()));

    /// <summary>Firma admini mevcut bypass kuralıyla kategori ataması olmadan raporları görür
    /// (PK-R3=A geçiş penceresinde canlı erişimin sürmesinin dayanağı).</summary>
    [Fact]
    public void FirmaAdmini_KategorisizDe_Gorur()
    {
        Assert.NotNull(_reports.Run(_admin, "vehicle", Istek()));
        Assert.NotNull(_reports.Run(_admin, "vehicle-daily", Istek()));
        Assert.NotNull(_reports.Run(_admin, "stock", Istek()));
    }

    // ── Katalog süzmesi paritesi (kaynak-düzeyi kilit — RPR15h deseni) ──

    /// <summary>Web (API) ve masaüstü katalog süzmeleri AYNI merkezi eşlemeyi (CategoryModule)
    /// kullanmak zorundadır — iki platformda farklı yetki mantığı gelişemez.</summary>
    [Fact]
    public void KatalogSuzmeleri_IkiPlatformda_AyniEslemeyi_Kullanir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);

        var api = File.ReadAllText(Path.Combine(kok!.FullName, "src", "DepoWise.Api", "Program.cs"));
        var masaustu = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Desktop", "ViewModels", "ReportsViewModel.cs"));
        var servis = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Infrastructure", "Reporting", "ReportService.cs"));

        Assert.Contains("CategoryModule", api, StringComparison.Ordinal);       // web katalog süzmesi
        Assert.Contains("CategoryModule", masaustu, StringComparison.Ordinal);  // masaüstü katalog süzmesi
        Assert.Contains("CategoryModule", servis, StringComparison.Ordinal);    // ortak servis kapısı (Run)
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
