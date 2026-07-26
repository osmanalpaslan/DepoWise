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
/// ŞUBE KAPSAMI (2026-07-25): belirli bir şubeyle giriş yapıldığında veri o şubeye göre filtrelenir
/// (OperatingBranchId dolu); "Tüm Şubeler" (null) → tüm firma verisi. Şubesi OLMAYAN (NULL) eski kayıtlar
/// her şubede görünür (gizlenmez). Admin dahil herkes seçili şubeye göre görür. Yönetici raporları filtresiz.
/// </summary>
public class BranchScopeTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();

    public BranchScopeTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_branchscope_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000); }

    private SessionContext Admin(string co, string? branch = null)
    {
        var u = new UserService(_f, _clock);
        var id = u.EnsureInitialAdmin(co, "adm_" + co, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = branch };
    }

    [Fact]
    public void AracListesi_SecilenSubeye_Filtrelenir_SubesizGorunur()
    {
        var all = Admin("A");                                  // OperatingBranchId null → Tüm Şubeler
        var branches = new BranchService(_f, _clock);
        var b1 = branches.Create(all, new NewBranch("Şube 1"), companyId: "A");
        var b2 = branches.Create(all, new NewBranch("Şube 2"), companyId: "A");
        var veh = new VehicleService(_f, _clock);
        veh.Create(all, new NewVehicle("V-B1", BranchId: b1));
        veh.Create(all, new NewVehicle("V-B2", BranchId: b2));
        veh.Create(all, new NewVehicle("V-YOK"));              // şubesiz (NULL) — her şubede görünmeli

        // Tüm Şubeler (null) → 3 araç
        Assert.Equal(3, veh.SearchGrid(all, new VehicleGridFilter(), 1, 100).Items.Count);

        // Şube 1 seçili → V-B1 + şubesiz V-YOK = 2
        var s1 = new SessionContext(all.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = b1 };
        var r1 = veh.SearchGrid(s1, new VehicleGridFilter(), 1, 100).Items;
        Assert.Equal(2, r1.Count);
        Assert.Contains(r1, x => x.InternalCode == "V-B1");
        Assert.Contains(r1, x => x.InternalCode == "V-YOK");   // şubesiz gizlenmez
        Assert.DoesNotContain(r1, x => x.InternalCode == "V-B2");

        // Şube 2 seçili → V-B2 + V-YOK = 2
        var s2 = new SessionContext(all.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = b2 };
        var r2 = veh.SearchGrid(s2, new VehicleGridFilter(), 1, 100).Items;
        Assert.Equal(2, r2.Count);
        Assert.Contains(r2, x => x.InternalCode == "V-B2");
        Assert.DoesNotContain(r2, x => x.InternalCode == "V-B1");
    }

    [Fact]
    public void YoneticiRaporu_SubeFiltresiz_TumSubeler()
    {
        var all = Admin("A");
        var branches = new BranchService(_f, _clock);
        var b1 = branches.Create(all, new NewBranch("Şube 1"), companyId: "A");
        var veh = new VehicleService(_f, _clock);
        var vehTpl = new VehicleTemplateService(_f, _clock);
        var vt = vehTpl.Create(all, new NewVehicleTemplate("Ekskavatör"));
        veh.Create(all, new NewVehicle("V-B1", BranchId: b1, TemplateId: vt));

        // Şube 2 ile giriş yapılsa bile YÖNETİCİ raporu (VehiclesByTemplate) tüm şubeleri gösterir.
        var s2 = new SessionContext(all.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = "baska-sube" };
        var mgr = new ReportService(_f).VehiclesByTemplate(s2, new ReportRequest(Executed: true));
        Assert.Single(mgr.Rows);   // V-B1 yönetici raporunda görünür (şube filtresi yok)
    }

    [Fact]
    public void MalzemeListesi_SecilenSubeye_Filtrelenir_SubesizGorunur()
    {
        var all = Admin("A");   // OperatingBranchId null → Tüm Şubeler
        var branches = new BranchService(_f, _clock);
        var b1 = branches.Create(all, new NewBranch("Şube 1"), companyId: "A");
        var b2 = branches.Create(all, new NewBranch("Şube 2"), companyId: "A");
        var mats = new MaterialService(_f, _clock);

        // Şubesiz (Tüm Şubeler oturumu) → babanın eski verisi gibi, her şubede görünmeli
        mats.Create(all, new NewMaterial("M-YOK", "Şubesiz Malzeme"));

        // Şube 1 oturumuyla oluştur → yalnız Şube 1'de görünmeli
        var s1 = new SessionContext(all.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = b1 };
        mats.Create(s1, new NewMaterial("M-B1", "Şube1 Malzeme"));

        // Şube 2 oturumu: kendi malzemesi + şubesiz olan (2); Şube1'in malzemesini GÖRMEZ
        var s2 = new SessionContext(all.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = b2 };
        mats.Create(s2, new NewMaterial("M-B2", "Şube2 Malzeme"));
        var r2 = mats.SearchGrid(s2, new MaterialGridFilter(), 1, 100).Items;
        Assert.Equal(2, r2.Count);
        Assert.Contains(r2, x => x.Code == "M-B2");
        Assert.Contains(r2, x => x.Code == "M-YOK");   // şubesiz gizlenmez
        Assert.DoesNotContain(r2, x => x.Code == "M-B1");

        // Tüm Şubeler → hepsi (3)
        Assert.Equal(3, mats.SearchGrid(all, new MaterialGridFilter(), 1, 100).Items.Count);
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_db); } catch { } }
}
