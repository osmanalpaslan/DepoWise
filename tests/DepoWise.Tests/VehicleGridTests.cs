using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Araç Listesi — kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17) — bkz.
/// <see cref="MaterialGridTests"/> (aynı desen, GridQuery paylaşılır).</summary>
public class VehicleGridTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly VehicleService _vehicles;
    private readonly LookupService _lookups;

    public VehicleGridTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_vgrid_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _vehicles = new VehicleService(_factory, _clock);
        _lookups = new LookupService(_factory, _clock);
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

    [Fact]
    public void Filtre_IcKodIcerir()
    {
        var a = Admin("A");
        _vehicles.Create(a, new NewVehicle("KM-001", "34 AAA 11"));
        _vehicles.Create(a, new NewVehicle("KM-002", "34 BBB 22"));
        _vehicles.Create(a, new NewVehicle("EX-010", "06 CCC 33"));

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(InternalCode: "km"), 1, 50);

        Assert.Equal(2, res.TotalCount);
    }

    [Fact]
    public void Filtre_BaslangicaGoreOncelik()
    {
        var a = Admin("A");
        _vehicles.Create(a, new NewVehicle("AR-2-KM", "34 AAA 11"));   // "km" ortada
        _vehicles.Create(a, new NewVehicle("KM-001", "34 BBB 22"));    // "km" başta

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(InternalCode: "km"), 1, 50);

        Assert.Equal(2, res.TotalCount);
        Assert.Equal("KM-001", res.Items[0].InternalCode);
    }

    [Fact]
    public void Filtre_DurumTurkceEtiketeGoreArar()
    {
        var a = Admin("A");
        _vehicles.Create(a, new NewVehicle("KM-001", Status: DepoWise.Application.Ui.VehicleStatus.Maintenance));
        _vehicles.Create(a, new NewVehicle("KM-002", Status: DepoWise.Application.Ui.VehicleStatus.Active));

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(Status: "Bakımda"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("KM-001", res.Items[0].InternalCode);
        Assert.Equal("Bakımda", res.Items[0].StatusLabel);
    }

    [Fact]
    public void Filtre_SayacMetniIleArar()
    {
        var a = Admin("A");
        _vehicles.Create(a, new NewVehicle("KM-001", CurrentMeter: 15420m, MeterUnit: "km"));
        _vehicles.Create(a, new NewVehicle("KM-002", CurrentMeter: 300m, MeterUnit: "hour"));

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(Meter: "hour"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("KM-002", res.Items[0].InternalCode);
    }

    [Fact]
    public void Filtre_MarkaAdinaGore_JoinliKolon()
    {
        var a = Admin("A");
        var brandId = _lookups.AddVehicleBrand(a, "Komatsu");
        _vehicles.Create(a, new NewVehicle("KM-001", BrandId: brandId));
        _vehicles.Create(a, new NewVehicle("KM-002"));   // markasız

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(Brand: "komatsu"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("KM-001", res.Items[0].InternalCode);
    }

    // ── Sayısal filtre (kullanıcı isteği 2026-07-18): Üretim Yılı/Sayaç artık SAYISAL — "içerir" değil ──

    [Fact]
    public void SayisalFiltre_UretimYili_TamEslesirIcermez()
    {
        var a = Admin("A");
        _vehicles.Create(a, new NewVehicle("V-1", ProductionYear: 2015));
        _vehicles.Create(a, new NewVehicle("V-2", ProductionYear: 2016));   // "15" içerir mi? hayır, ama eski davranışta risk vardı

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(ProductionYear: "2015"), 1, 50);

        Assert.Equal(1, res.TotalCount);
        Assert.Equal("V-1", res.Items[0].InternalCode);
    }

    [Fact]
    public void SayisalFiltre_Sayac_KarsilastirmaCalisir()
    {
        var a = Admin("A");
        _vehicles.Create(a, new NewVehicle("V-1", CurrentMeter: 5m));
        _vehicles.Create(a, new NewVehicle("V-2", CurrentMeter: 15m));
        _vehicles.Create(a, new NewVehicle("V-3", CurrentMeter: 50m));

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(Meter: ">10"), 1, 50);

        Assert.Equal(new[] { "V-2", "V-3" }, res.Items.Select(i => i.InternalCode).OrderBy(x => x));
    }

    [Fact]
    public void SayisalFiltre_Sayac_Aralik()
    {
        var a = Admin("A");
        _vehicles.Create(a, new NewVehicle("V-1", CurrentMeter: 5m));
        _vehicles.Create(a, new NewVehicle("V-2", CurrentMeter: 15m));
        _vehicles.Create(a, new NewVehicle("V-3", CurrentMeter: 50m));

        var res = _vehicles.SearchGrid(a, new VehicleGridFilter(Meter: "5-15"), 1, 50);

        Assert.Equal(new[] { "V-1", "V-2" }, res.Items.Select(i => i.InternalCode).OrderBy(x => x));
    }

    [Fact]
    public void Sayfalama_ToplamVeSayfaSayisiDogru()
    {
        var a = Admin("A");
        for (int i = 0; i < 42; i++) _vehicles.Create(a, new NewVehicle($"V-{i:D3}"));

        var page1 = _vehicles.SearchGrid(a, new VehicleGridFilter(), 1, 20);
        Assert.Equal(42, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(20, page1.Items.Count);

        var page3 = _vehicles.SearchGrid(a, new VehicleGridFilter(), 3, 20);
        Assert.Equal(2, page3.Items.Count);
    }

    [Fact]
    public void TenantIzolasyonu_BaskaFirmayaSizmaz()
    {
        var a = Admin("A");
        var b = Admin("B");
        _vehicles.Create(a, new NewVehicle("A-1"));
        _vehicles.Create(b, new NewVehicle("B-1"));

        Assert.Equal(1, _vehicles.SearchGrid(a, new VehicleGridFilter(), 1, 50).TotalCount);
        Assert.Equal(1, _vehicles.SearchGrid(b, new VehicleGridFilter(), 1, 50).TotalCount);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
