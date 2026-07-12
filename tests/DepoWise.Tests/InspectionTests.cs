using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Muayene/Sigorta/Kasko/Kalibrasyon (InspectionService): kayıt + liste + tarih uyarısı
/// (Normal / Yaklaşan / Süresi geçti) + yalnız en güncel belge. QA raporu B3 — özel test dosyası eklendi.</summary>
public class InspectionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly VehicleService _vehicles;
    private readonly InspectionService _inspection;
    private readonly SessionContext _admin;

    public InspectionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_insp_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _vehicles = new VehicleService(_factory, _clock);
        _inspection = new InspectionService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    /// <summary>Şimdiden <paramref name="d"/> gün sonrası/öncesi (ms).</summary>
    private long Days(int d) => _clock.UtcNow.AddDays(d).ToUnixTimeMilliseconds();

    [Fact]
    public void Kaydet_ve_Listele_DogruSatir_NormalSeviye()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-001", Plate: "34 ABC 01"));
        _inspection.Save(_admin, new NewInspection(v, "inspection",
            LastDate: Days(-330), NextDate: Days(60), Place: "TÜVTÜRK", Result: "Geçti"));

        var rows = _inspection.List(_admin);
        var row = Assert.Single(rows);
        Assert.Equal("KM-001 - 34 ABC 01", row.VehicleText);
        Assert.Equal("Muayene", row.DocTypeText);
        Assert.Equal("TÜVTÜRK", row.Place);
        Assert.Equal(DateAlertLevel.Normal, row.Level);   // 60 gün > 30 → Normal
    }

    [Fact]
    public void Gecersiz_BelgeTipi_Reddedilir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-002"));
        Assert.Throws<ArgumentException>(() =>
            _inspection.Save(_admin, new NewInspection(v, "sacma-tip", LastDate: null, NextDate: Days(10))));
    }

    [Fact]
    public void TarihUyarisi_Yaklasan_ve_Gecmis_DogruSeviye()
    {
        var v1 = _vehicles.Create(_admin, new NewVehicle("KM-A"));
        var v2 = _vehicles.Create(_admin, new NewVehicle("KM-B"));
        _inspection.Save(_admin, new NewInspection(v1, "insurance", LastDate: null, NextDate: Days(10)));   // <=30 → Yaklaşan
        _inspection.Save(_admin, new NewInspection(v2, "inspection", LastDate: null, NextDate: Days(-5)));  // geçmiş → Süresi geçti

        var alerts = _inspection.GetAlerts(_admin);
        Assert.Equal(DateAlertLevel.Approaching, alerts.Single(a => a.VehicleId == v1).Level);
        Assert.Equal(DateAlertLevel.Expired, alerts.Single(a => a.VehicleId == v2).Level);
    }

    [Fact]
    public void GetAlerts_YalnizEnGuncelBelgeyiKullanir_Yenileme()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-C"));
        _inspection.Save(_admin, new NewInspection(v, "inspection", LastDate: null, NextDate: Days(-10)));   // eski: süresi geçmiş
        _clock.UtcNow = _clock.UtcNow.AddSeconds(1);                                          // created_at farkı
        _inspection.Save(_admin, new NewInspection(v, "inspection", LastDate: null, NextDate: Days(365)));    // yeni: yenilendi → Normal

        var alerts = _inspection.GetAlerts(_admin);
        var a = Assert.Single(alerts.Where(x => x.VehicleId == v && x.DocType == "inspection"));
        Assert.Equal(DateAlertLevel.Normal, a.Level);   // yalnız en güncel (365 gün) sayılır, eski geçmiş belge değil
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
