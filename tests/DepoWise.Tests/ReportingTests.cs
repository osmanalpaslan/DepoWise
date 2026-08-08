using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

public class ReportingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly ReportService _reports;
    private readonly ExcelExportService _excel = new();
    private readonly DashboardService _dashboard;
    private readonly MaterialImportService _import;
    private readonly SessionContext _admin;

    public ReportingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_rep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _reports = new ReportService(_factory);
        _import = new MaterialImportService(_materials, new LookupService(_factory, _clock));
        var maint = new MaintenanceService(_factory, _clock);
        var insp = new InspectionService(_factory, _clock);
        _dashboard = new DashboardService(_factory, maint, insp);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private SessionContext Admin(string company)
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(company, "admin_" + company, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    // ---- Rapor kapısı ----
    [Fact]
    public void Rapor_FiltreTiklanmadan_Calismaz()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _reports.StockStatus(_admin, new ReportRequest(Executed: false)));
        // Executed=true → çalışır
        var t = _reports.StockStatus(_admin, new ReportRequest(Executed: true));
        Assert.Equal("Stok Durumu", t.Title);
    }

    [Fact]
    public void Rapor_TenantSizintisiYok()
    {
        _materials.Create(_admin, new NewMaterial("A-1", "A malzeme"));
        var adminB = Admin("B");
        _materials.Create(adminB, new NewMaterial("B-1", "B malzeme"));

        var ra = _reports.StockStatus(_admin, new ReportRequest(Executed: true));
        Assert.Single(ra.Rows);
        Assert.Equal("A-1", ra.Rows[0][0]);
    }

    [Fact]
    public void Rapor_FirmaFiltresi_YalnizSuperAdmin()
    {
        var superA = new SessionContext("su", "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        Assert.True(ReportGate.ShowCompanyFilter(superA));
        Assert.False(ReportGate.ShowCompanyFilter(_admin));
        // Normal admin başka firma isteyemez
        Assert.Throws<ForbiddenException>(() => _reports.StockStatus(_admin, new ReportRequest(Executed: true, CompanyId: "B")));
    }

    [Fact]
    public void Rapor_DenyByDefault()
    {
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _reports.StockStatus(noPerm, new ReportRequest(Executed: true)));
    }

    // ---- Yeni raporlar (Bakım / Depo / Talep) ----
    [Fact]
    public void YeniRaporlar_Calisir_DogruBaslikVeKolonlar()
    {
        var bakim = _reports.Maintenance(_admin, new ReportRequest(Executed: true));
        Assert.Equal("Bakım Raporu", bakim.Title);
        // Bakım Raporu ortak standarda taşındı (2026-08-08): 12 kolon, işlenen şube, km/saat sayaç, kalem+maliyet.
        Assert.Equal(new[] { "Şube", "Tarih", "Araç İç Kod", "Plaka", "Araç Adı", "Araç Türü",
            "Bakım", "Alt Bakım", "Sayaç", "Teknisyen", "Malzeme Kalem Sayısı", "Malzeme Maliyeti" }, bakim.Headers);

        var depo = _reports.FuelDepot(_admin, new ReportRequest(Executed: true));
        Assert.Equal("Depo Girişi Raporu", depo.Title);
        Assert.Equal(5, depo.Headers.Count);

        var talep = _reports.Requests(_admin, new ReportRequest(Executed: true));
        Assert.Equal("Talep Raporu", talep.Title);

        // "Genel Rapor" → "Araç Raporu" (2026-08-07): araç başına yakıt+bakım+parça maliyeti, 14 kolon.
        var arac = _reports.VehicleReport(_admin, new ReportRequest(Executed: true));
        Assert.Equal("Araç Raporu", arac.Title);
        Assert.Equal(14, arac.Headers.Count);

        // Stok Sayım Raporu: fark 0 olan + olmayan satırlar
        var stock = new StockService(_factory, _clock);
        var m1 = _materials.Create(_admin, new NewMaterial("S-1", "A"));
        var m2 = _materials.Create(_admin, new NewMaterial("S-2", "B"));
        _opening.RecordOpening(_admin, m1, 10m, "op-s1");
        _opening.RecordOpening(_admin, m2, 5m, "op-s2");
        stock.Count(_admin, new[] { new CountLine(m1, 10m), new CountLine(m2, 8m) }, "Sayım", "op-count");
        var sayim = _reports.StockCount(_admin, new ReportRequest(Executed: true));
        Assert.Equal("Stok Sayım Raporu", sayim.Title);
        Assert.Equal(2, sayim.Rows.Count); // fark 0 (m1) + fark +3 (m2)

        // Sorgula yapılmadan çalışmaz (gate)
        Assert.Throws<InvalidOperationException>(() => _reports.Maintenance(_admin, new ReportRequest(Executed: false)));
    }

    // ---- Excel ----
    [Fact]
    public void Excel_GecerliXlsx_Uretir()
    {
        _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        var table = _reports.StockStatus(_admin, new ReportRequest(Executed: true));
        var bytes = _excel.Export(table);
        Assert.True(bytes.Length > 0);
        // xlsx = ZIP (PK imzası)
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    // ---- Dashboard ----
    [Fact]
    public void Dashboard_TenantKpi_VeDusukStokUyarisi()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre", MinStock: 5m));
        _opening.RecordOpening(_admin, m, 2m, "op"); // 2 <= 5 → düşük stok

        var sum = _dashboard.GetSummary(_admin);
        Assert.Equal(1, sum.MaterialCount);
        Assert.Equal(1, sum.LowStockCount);
        Assert.Contains(sum.Alerts, a => a.Kind == AlertKind.LowStock);
    }

    // ---- Import ----
    [Fact]
    public void Import_OrnekBaslik()
    {
        Assert.Contains("Kod", _import.SampleHeaders());
        Assert.Contains("Birim Fiyat", _import.SampleHeaders());
    }

    [Fact]
    public void Import_DryRun_SatirBazliHata_DBYazmaz()
    {
        var rows = new[]
        {
            Row(1, ("Kod", "M-1"), ("Ad", "İyi"), ("Birim Fiyat", "12.5")),
            Row(2, ("Kod", ""), ("Ad", "Kodsuz")),                  // hata: kod yok
            Row(3, ("Kod", "M-3"), ("Ad", "Hatalı"), ("Birim Fiyat", "abc")), // hata: fiyat
        };
        var res = _import.DryRun(_admin, rows);
        Assert.True(res.DryRun);
        Assert.Equal(3, res.Total);
        Assert.Equal(1, res.Valid);
        Assert.Equal(2, res.Failed);
        Assert.Equal(0, res.Added);
        // DB'ye yazılmadı
        Assert.Empty(_materials.List(_admin, new PageRequest { Limit = 50 }).Items);
    }

    [Fact]
    public void Import_Commit_GecerliSatirlariUygular_HatalilariRaporlar()
    {
        var rows = new[]
        {
            Row(1, ("Kod", "M-1"), ("Ad", "Bir"), ("Birim Fiyat", "10")),
            Row(2, ("Kod", "M-2"), ("Ad", "İki"), ("Para Birimi", "USD")),
            Row(3, ("Kod", ""), ("Ad", "Hatalı")),                  // hata
        };
        var res = _import.Commit(_admin, rows);
        Assert.False(res.DryRun);
        Assert.Equal(2, res.Added);
        Assert.Equal(1, res.Failed);
        Assert.Equal(2, _materials.List(_admin, new PageRequest { Limit = 50 }).Items.Count);
    }

    [Fact]
    public void Import_IsKurallariniAtlamaz_KodBenzersiz()
    {
        _materials.Create(_admin, new NewMaterial("M-1", "Var olan"));
        var rows = new[] { Row(1, ("Kod", "M-1"), ("Ad", "Tekrar")) };
        var res = _import.Commit(_admin, rows);
        // Mevcut kod → eklenmedi (updated/idempotent), çift kayıt yok
        Assert.Equal(0, res.Added);
        Assert.Single(_materials.List(_admin, new PageRequest { Limit = 50 }).Items);
    }

    private static ImportRow Row(int n, params (string, string?)[] cells)
    {
        var d = new Dictionary<string, string?>();
        foreach (var (k, v) in cells) d[k] = v;
        return new ImportRow(n, d);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
