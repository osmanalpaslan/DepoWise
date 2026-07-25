using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Durum Rapor (2026-07-25) — YÖNETİCİ: şube bazlı SAYISAL özet. Malzeme firma-genelidir (şube yok) → tek
/// "Firma Geneli" satırı, şablonlu/şablon-dışı ayrımıyla. Araç şube bazlı + şablon ayrımlı. Ayrıca Excel
/// dışa aktarma özel butonları deny-by-default (admin bypass; personel açıkça verilmedikçe aktaramaz).
/// </summary>
public class StatusReportTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();

    public StatusReportTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_statrep_" + Guid.NewGuid().ToString("N") + ".db");
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
    public void DurumRapor_MalzemeFirmaGeneli_AracSubeBazli_Ayrisir()
    {
        var a = Admin("A");
        var branches = new BranchService(_f, _clock);
        var b1 = branches.Create(a, new NewBranch("Merkez Şube"), companyId: "A");
        var b2 = branches.Create(a, new NewBranch("Şantiye 1"), companyId: "A");

        var mats = new MaterialService(_f, _clock);
        var matTpl = new MaterialTemplateService(_f, _clock);
        var veh = new VehicleService(_f, _clock);
        var vehTpl = new VehicleTemplateService(_f, _clock);
        var reports = new ReportService(_f);
        var req = new ReportRequest(Executed: true);

        // Malzeme — firma geneli: 1 şablonlu + 1 şablon-dışı
        var mt = matTpl.Create(a, new NewMaterialTemplate("Yağ Filtresi", Code: "YF"));
        mats.Create(a, new NewMaterial("M-T", "Yağ Filtresi", TemplateId: mt));
        mats.Create(a, new NewMaterial("M-N", "Serbest Malzeme"));

        // Araç — Merkez: 1 şablonlu + 1 şablon-dışı; Şantiye 1: yalnız 1 şablon-dışı
        var vt = vehTpl.Create(a, new NewVehicleTemplate("Ekskavatör"));
        veh.Create(a, new NewVehicle("V-T1", BranchId: b1, TemplateId: vt));
        veh.Create(a, new NewVehicle("V-N1", BranchId: b1));
        veh.Create(a, new NewVehicle("V-N2", BranchId: b2));

        var t = reports.StatusReport(a, req);

        // Malzeme firma geneli tek satır
        var m = t.Rows.Single(r => (string)r[1]! == "Malzeme");
        Assert.Equal("Firma Geneli", m[0]);
        Assert.Equal(1, Convert.ToInt32(m[2]));   // şablonlu
        Assert.Equal(1, Convert.ToInt32(m[3]));   // şablon-dışı
        Assert.Equal(2, Convert.ToInt32(m[4]));   // toplam

        // Merkez Şube araç: 1 şablonlu + 1 şablon-dışı
        var v1 = t.Rows.Single(r => (string)r[0]! == "Merkez Şube" && (string)r[1]! == "Araç");
        Assert.Equal(1, Convert.ToInt32(v1[2]));
        Assert.Equal(1, Convert.ToInt32(v1[3]));
        Assert.Equal(2, Convert.ToInt32(v1[4]));

        // Şantiye 1 araç: 0 şablonlu + 1 şablon-dışı
        var v2 = t.Rows.Single(r => (string)r[0]! == "Şantiye 1" && (string)r[1]! == "Araç");
        Assert.Equal(0, Convert.ToInt32(v2[2]));
        Assert.Equal(1, Convert.ToInt32(v2[3]));

        // Şablonsuz modüller "—" ile gelir (Personel örneği)
        var p = t.Rows.Single(r => (string)r[0]! == "Merkez Şube" && (string)r[1]! == "Personel");
        Assert.Equal("—", p[2]);
        Assert.Equal("—", p[3]);
        Assert.Equal(0, Convert.ToInt32(p[4]));
    }

    [Fact]
    public void DurumRapor_TenantIzole()
    {
        var a = Admin("A");
        var b = Admin("B");
        new BranchService(_f, _clock).Create(a, new NewBranch("A-Merkez"), companyId: "A");
        new MaterialService(_f, _clock).Create(a, new NewMaterial("A-M", "A Malzeme"));

        // B firması yöneticisi A'nın verisini GÖRMEZ (malzeme firma geneli satırı 0/0/0)
        var tB = new ReportService(_f).StatusReport(b, new ReportRequest(Executed: true));
        var mB = tB.Rows.Single(r => (string)r[1]! == "Malzeme");
        Assert.Equal(0, Convert.ToInt32(mB[4]));
        Assert.DoesNotContain(tB.Rows, r => (string)r[0]! == "A-Merkez");
    }

    [Fact]
    public void ExcelExport_Yetkisi_DenyByDefault()
    {
        // Yeni iki özel buton katalogda (yetki ağacına otomatik gelir)
        Assert.Contains(SpecialButtons.All, x => x.Key == SpecialButtons.ExportReports);
        Assert.Contains(SpecialButtons.All, x => x.Key == SpecialButtons.ExportManagerReports);

        // Personel (admin değil) + boş yetki → dışa aktaramaz
        var staff = new SessionContext("u1", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.False(AccessControl.CanUseButton(staff, SpecialButtons.ExportReports));
        Assert.False(AccessControl.CanUseButton(staff, SpecialButtons.ExportManagerReports));

        // Admin → bypass ile aktarabilir
        var admin = Admin("A");
        Assert.True(AccessControl.CanUseButton(admin, SpecialButtons.ExportReports));
        Assert.True(AccessControl.CanUseButton(admin, SpecialButtons.ExportManagerReports));
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_db); } catch { } }
}
