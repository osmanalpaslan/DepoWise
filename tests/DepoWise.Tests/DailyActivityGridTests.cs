using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Günlük Faaliyet listesine ADR-087/088/089 deseni (kolon bazlı filtre + sayfalama + sıralama + Excel'e
/// aktar) uygulandı (kullanıcı isteği 2026-07-19, madde 15). "Tarih" bilinçli olarak filtre kutusu YOK —
/// yalnız sıralanır (bkz. DailyActivityListColumns).
/// </summary>
public class DailyActivityGridTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly VehicleService _vehicles;
    private readonly BranchService _branches;
    private readonly LookupService _lookups;
    private readonly MaintenanceService _maint;
    private readonly DailyActivityService _daily;
    private readonly SessionContext _admin;

    public DailyActivityGridTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_dag_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _vehicles = new VehicleService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);
        _lookups = new LookupService(_factory, _clock);
        _maint = new MaintenanceService(_factory, _clock);
        _daily = new DailyActivityService(_factory, _maint, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Fact]
    public void SearchGrid_TipIcerirAramasi_UcuAyriTuruYakalar()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v), "op-1");
        _daily.SaveMovement(_admin, new NewMovementActivity("transfer", v), "op-2");

        var res = _daily.SearchGrid(_admin, new DailyActivityGridFilter(Type: "Hareket"), 1, 50);
        Assert.Single(res.Items);
        Assert.Equal("Hareket", res.Items[0].TypeText);

        var resTransfer = _daily.SearchGrid(_admin, new DailyActivityGridFilter(Type: "Transfer"), 1, 50);
        Assert.Single(resTransfer.Items);
    }

    [Fact]
    public void SearchGrid_AracFiltresi_KodVePlakayaGoreEsler()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("EX-100", Plate: "34 ABC 12"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v), "op-1");

        Assert.Single(_daily.SearchGrid(_admin, new DailyActivityGridFilter(Vehicle: "EX-100"), 1, 50).Items);
        Assert.Single(_daily.SearchGrid(_admin, new DailyActivityGridFilter(Vehicle: "34 ABC"), 1, 50).Items);
        Assert.Empty(_daily.SearchGrid(_admin, new DailyActivityGridFilter(Vehicle: "yok-boyle-bir-arac"), 1, 50).Items);
    }

    [Fact]
    public void SearchGrid_RotaVeOperatorFiltresi()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        var from = _branches.Create(_admin, new NewBranch("Merkez Depo", "branch"));
        var to = _branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        var op = _lookups.AddPersonnel(_admin, "Ahmet Yılmaz", "Şoför");
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, from, to, op), "op-1");

        Assert.Single(_daily.SearchGrid(_admin, new DailyActivityGridFilter(Route: "Merkez"), 1, 50).Items);
        Assert.Single(_daily.SearchGrid(_admin, new DailyActivityGridFilter(Route: "Şantiye A"), 1, 50).Items);
        Assert.Single(_daily.SearchGrid(_admin, new DailyActivityGridFilter(Operator: "Ahmet"), 1, 50).Items);
    }

    [Fact]
    public void SearchGrid_AciklamaFiltresi()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, Description: "Kum sevkiyatı"), "op-1");
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, Description: "Çakıl sevkiyatı"), "op-2");

        Assert.Single(_daily.SearchGrid(_admin, new DailyActivityGridFilter(Description: "Kum"), 1, 50).Items);
        Assert.Equal(2, _daily.SearchGrid(_admin, new DailyActivityGridFilter(Description: "sevkiyatı"), 1, 50).Items.Count);
    }

    [Fact]
    public void SearchGrid_VarsayilanSira_TarihYeniOnce()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, Description: "Eski"), "op-1");
        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(1_700_100_000_000);
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, Description: "Yeni"), "op-2");

        var res = _daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 50);
        Assert.Equal("Yeni", res.Items[0].Description);
        Assert.Equal("Eski", res.Items[1].Description);
    }

    [Fact]
    public void SearchGrid_BaslikaTiklaSirala_AracaGoreAZ()
    {
        var vb = _vehicles.Create(_admin, new NewVehicle("BB-1"));
        var va = _vehicles.Create(_admin, new NewVehicle("AA-1"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", vb), "op-1");
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", va), "op-2");

        var res = _daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 50, sortColumn: "vehicle", sortDesc: false);
        Assert.Equal("AA-1", res.Items[0].Vehicle);
        Assert.Equal("BB-1", res.Items[1].Vehicle);
    }

    [Fact]
    public void SearchGridAll_TumSayfalariDolasir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        for (var i = 0; i < 12; i++) _daily.SaveMovement(_admin, new NewMovementActivity("movement", v), $"op-{i}");

        var res1 = _daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 5);
        Assert.Equal(5, res1.Items.Count);
        Assert.Equal(3, res1.TotalPages);

        var all = _daily.SearchGridAll(_admin, new DailyActivityGridFilter());
        Assert.Equal(12, all.Count);
    }

    [Fact]
    public void ToTableModel_BasliklarVeSatirlarDogru()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1", Plate: "34 X 1"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, Description: "Test"), "op-1");
        var rows = _daily.SearchGridAll(_admin, new DailyActivityGridFilter());

        var table = DailyActivityService.ToTableModel(rows);
        Assert.Equal(DailyActivityListColumns.All.Select(c => c.Label), table.Headers);
        Assert.Single(table.Rows);
    }

    [Fact]
    public void SearchGrid_TenantIzolasyonu()
    {
        var usersB = new UserService(_factory, _clock);
        var uidB = usersB.EnsureInitialAdmin("B", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var b = new SessionContext(uidB, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var vA = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", vA), "op-1");
        var vB = _vehicles.Create(b, new NewVehicle("KM-1"));   // farklı firmada aynı iç kod serbest
        _daily.SaveMovement(b, new NewMovementActivity("movement", vB), "op-2");

        Assert.Equal(1, _daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 50).TotalCount);
        Assert.Equal(1, _daily.SearchGrid(b, new DailyActivityGridFilter(), 1, 50).TotalCount);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
