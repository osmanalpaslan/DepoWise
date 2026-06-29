using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

public class FuelDailyActivityTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly VehicleService _vehicles;
    private readonly FuelService _fuel;
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly MaintenanceDefinitionService _defs;
    private readonly MaintenanceService _maint;
    private readonly DailyActivityService _daily;
    private readonly SessionContext _admin;

    public FuelDailyActivityTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_fda_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _vehicles = new VehicleService(_factory, _clock);
        _fuel = new FuelService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _maint = new MaintenanceService(_factory, _clock);
        _daily = new DailyActivityService(_factory, _maint, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    // ---- Yakıt liste (Faz 7b read-query) ----
    [Fact]
    public void Yakit_Listeler_DepoVeDagitim_DonerveAracKodunuKatar()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-9", CurrentMeter: 100m));
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(Liters: 200m, UnitPrice: 40m), Guid.NewGuid().ToString("N"));
        _fuel.Distribute(_admin, new NewDistribution(v, 50m, 150m), Guid.NewGuid().ToString("N"));

        var depots = _fuel.ListDepotEntries(_admin);
        Assert.Single(depots);
        Assert.Equal(200m, depots[0].Liters);

        var dists = _fuel.ListDistributions(_admin);
        Assert.Single(dists);
        Assert.Equal("KM-9", dists[0].VehicleCode);
        Assert.Equal(50m, dists[0].Liters);
    }

    // ---- Yakıt ----
    [Fact]
    public void Yakit_Depo_VeDagitim_BakiyeVeSayacTutarli()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(100m, 40m), "op-depo");
        Assert.Equal(100m, _fuel.GetDepotBalance(_admin));
        Assert.Equal(40m, _fuel.GetCurrentFuelPrice(_admin));

        _fuel.Distribute(_admin, new NewDistribution(v, 30m, CurrentMeter: 1200m), "op-dist");
        Assert.Equal(70m, _fuel.GetDepotBalance(_admin)); // 100-30
        Assert.Equal(1200m, _vehicles.GetMeter(_admin, v)); // sayaç ilerledi
    }

    [Fact]
    public void Yakit_DepoYetersiz_Engellenir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(10m, 40m), "op-depo");
        Assert.Throws<InvalidOperationException>(() =>
            _fuel.Distribute(_admin, new NewDistribution(v, 20m, CurrentMeter: 1100m), "op-dist"));
        Assert.Equal(10m, _fuel.GetDepotBalance(_admin)); // değişmedi
    }

    [Fact]
    public void Yakit_FiyatSnapshot_GecmistedeDegismez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(100m, 40m), "op-d1");
        var dist = _fuel.Distribute(_admin, new NewDistribution(v, 10m, CurrentMeter: 1100m), "op-dist"); // fiyat 40 snapshot
        _clock.Advance(1000);
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(100m, 55m), "op-d2"); // güncel fiyat 55

        Assert.Equal(55m, _fuel.GetCurrentFuelPrice(_admin));
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM fuel_distributions WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", dist);
        Assert.Equal(40m, Money.Parse(cmd.ExecuteScalar() as string)); // eski dağıtım hâlâ 40
    }

    [Fact]
    public void Yakit_Dagitim_Idempotent()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        _fuel.AddDepotEntry(_admin, new NewDepotEntry(100m, 40m), "op-depo");
        var d1 = _fuel.Distribute(_admin, new NewDistribution(v, 30m, CurrentMeter: 1200m), "dup");
        var d2 = _fuel.Distribute(_admin, new NewDistribution(v, 30m, CurrentMeter: 1200m), "dup");
        Assert.Equal(d1, d2);
        Assert.Equal(70m, _fuel.GetDepotBalance(_admin)); // 40 değil
    }

    // ---- Günlük Faaliyet: bakım çoğaltmaz ----
    [Fact]
    public void GunlukFaaliyet_Bakim_TekKayit_CiftStokDusmez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre", UnitPrice: 20m));
        _opening.RecordOpening(_admin, m, 10m, "op-open");
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));

        var activityId = _daily.SaveMaintenanceActivity(_admin,
            new NewMaintenance(v, def, PerformedKm: 1000m, Materials: new[] { new MaintenanceMaterialLine(m, 2m) }),
            "op-act");

        // Tek bakım kaydı
        using var conn = _factory.Create();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM vehicle_maintenances;";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        // Tek stok düşümü (10→8)
        Assert.Equal(8m, _opening.GetBalance(_admin, m));
        // Günlük faaliyet bakım kaydına referans veriyor (aynı veri iki ekranda)
        var act = _daily.GetForVehicle(_admin, v, "maintenance").Single();
        Assert.Equal(activityId, act.Id);
        Assert.NotNull(act.MaintenanceId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT stock_processed FROM daily_activities WHERE id=$id;";
            cmd.Parameters.AddWithValue("$id", activityId);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
    }

    [Fact]
    public void GunlukFaaliyet_Bakim_Idempotent_TekrarCiftYazmaz()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));
        var a1 = _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, def, PerformedKm: 1000m), "op-act");
        var a2 = _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, def, PerformedKm: 1000m), "op-act");
        Assert.Equal(a1, a2);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vehicle_maintenances;";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    // ---- Hareket / transfer ----
    [Fact]
    public void GunlukFaaliyet_Transfer_AraciPasifeAlir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        _daily.SaveMovement(_admin, new NewMovementActivity("transfer", VehicleId: v, FromLocationId: "b1", ToLocationId: "b2"), "op-trf");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM vehicles WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", v);
        Assert.Equal("passive", cmd.ExecuteScalar());
    }

    [Fact]
    public void GunlukFaaliyet_List_HareketleriDoner()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-7", Plate: "34ABC07"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", VehicleId: v, OperatorId: null,
            DurationDays: 3, Description: "Şantiyeye sevk"), "op-list-1");

        var all = _daily.List(_admin);
        Assert.Single(all);
        Assert.Equal("Hareket", all[0].TypeText);
        Assert.Equal("V-7 - 34ABC07", all[0].VehicleText);
        Assert.Equal("3 gün", all[0].DurationText);

        // Tür filtresi: bakım yok → movement filtresinde 1, maintenance filtresinde 0
        Assert.Single(_daily.List(_admin, "movement"));
        Assert.Empty(_daily.List(_admin, "maintenance"));
    }

    [Fact]
    public void GunlukFaaliyet_Sil_ListedenKalkar()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-8"));
        var id = _daily.SaveMovement(_admin, new NewMovementActivity("movement", VehicleId: v), "op-del-1");
        Assert.Single(_daily.List(_admin));
        _daily.Delete(_admin, id);
        Assert.Empty(_daily.List(_admin));
    }

    [Fact]
    public void GunlukFaaliyet_Hareket_AracDurumDegismez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        _daily.SaveMovement(_admin, new NewMovementActivity("movement", VehicleId: v), "op-mov");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT status FROM vehicles WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", v);
        Assert.Equal("active", cmd.ExecuteScalar());
    }

    [Fact]
    public void Yakit_DenyByDefault()
    {
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _fuel.AddDepotEntry(noPerm, new NewDepotEntry(10m, 40m), "op"));
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
