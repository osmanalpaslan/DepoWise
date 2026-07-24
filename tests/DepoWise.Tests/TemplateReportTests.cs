using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Yönetici raporları (2026-07-24): ŞABLONLU / ŞABLON-DIŞI ayrımı. Şablon SEÇİLEREK oluşturulan malzeme/araç
/// "şablonlu"; şablonsuz oluşturulan "şablon-dışı". Rapor ikisini doğru ayırmalı + tenant-izole olmalı.
/// </summary>
public class TemplateReportTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();

    public TemplateReportTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_tplrep_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000); }

    private SessionContext Admin(string co)
    {
        var u = new UserService(_f, _clock);
        var id = u.EnsureInitialAdmin(co, "adm_" + co, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    [Fact]
    public void Sablonlu_SablonDisi_Malzeme_Ve_Arac_Raporlari_Ayrisir()
    {
        var a = Admin("A");
        var mats = new MaterialService(_f, _clock);
        var matTpl = new MaterialTemplateService(_f, _clock);
        var veh = new VehicleService(_f, _clock);
        var vehTpl = new VehicleTemplateService(_f, _clock);
        var reports = new ReportService(_f);
        var req = new ReportRequest(Executed: true);

        // MALZEME — 1 şablon; 1 şablonlu + 1 şablonsuz
        var mt = matTpl.Create(a, new NewMaterialTemplate("Yağ Filtresi Şb", Code: "YF-T"));
        mats.Create(a, new NewMaterial("M-T", "Yağ Filtresi", TemplateId: mt));
        mats.Create(a, new NewMaterial("M-N", "Serbest Malzeme"));   // şablonsuz → template_id NULL

        var mByT = reports.MaterialsByTemplate(a, req);
        Assert.Equal(2, mByT.Rows.Count);                 // 1 şablon satırı + TOPLAM
        Assert.Equal("YF-T", mByT.Rows[0][0]);            // şablon kodu
        Assert.Equal(1, Convert.ToInt32(mByT.Rows[0][2])); // kayıt sayısı

        var mNon = reports.MaterialsNonTemplate(a, req);
        Assert.Single(mNon.Rows);
        Assert.Equal("M-N", mNon.Rows[0][0]);

        // ARAÇ — 1 şablon; 1 şablonlu + 1 şablonsuz
        var vt = vehTpl.Create(a, new NewVehicleTemplate("Ekskavatör Şb"));
        veh.Create(a, new NewVehicle("V-T", TemplateId: vt));
        veh.Create(a, new NewVehicle("V-N"));             // şablonsuz

        var vByT = reports.VehiclesByTemplate(a, req);
        Assert.Single(vByT.Rows);
        Assert.Equal("V-T", vByT.Rows[0][1]);            // (0=şablon, 1=iç kod)

        var vNon = reports.VehiclesNonTemplate(a, req);
        Assert.Single(vNon.Rows);
        Assert.Equal("V-N", vNon.Rows[0][0]);
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_db); } catch { } }
}
