using System.Linq;
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

/// <summary>
/// Günlük Faaliyet — "İlave Yağ / İlave Filtre / Tamir" (kullanıcı isteği 2026-07-19): Bakım ile AYNI
/// mekanizma (ortak MaintenanceService — sayaç/malzeme stok düşümü dahil), yalnız Bakım Tanımı/Alt Bakım
/// kullanıcıya hiç sorulmaz — her tür firma başına otomatik oluşan sabit bir tanıma bağlanır.
/// </summary>
public class DailyActivityExtraTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly VehicleService _vehicles;
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly MaintenanceDefinitionService _defs;
    private readonly MaintenanceService _maint;
    private readonly DailyActivityService _daily;
    private readonly SessionContext _admin;

    public DailyActivityExtraTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_dax_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _vehicles = new VehicleService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _maint = new MaintenanceService(_factory, _clock);
        _daily = new DailyActivityService(_factory, _maint, _clock, _defs);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Theory]
    [InlineData(ExtraActivityTypes.ExtraOil, "İlave Yağ")]
    [InlineData(ExtraActivityTypes.ExtraFilter, "İlave Filtre")]
    [InlineData(ExtraActivityTypes.Repair, "Tamir")]
    public void SaveExtraActivity_UcTur_BakimTanimiSormadanKaydeder(string type, string expectedTypeText)
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1", CurrentMeter: 100m));
        var id = _daily.SaveExtraActivity(_admin, type,
            new NewMaintenance(VehicleId: v, DefinitionId: "", PerformedKm: 105m), "op-1");

        Assert.NotNull(id);
        var row = _daily.List(_admin).Single(x => x.Id == id);
        Assert.Equal(type, row.ActivityType);
        Assert.Equal(expectedTypeText, row.TypeText);
        Assert.NotNull(row.MaintenanceId);   // ortak MaintenanceService'te gerçek bir bakım kaydı da açıldı
    }

    [Fact]
    public void SaveExtraActivity_MalzemeStoktanDuser_BakimIleAyniMekanizma()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1", CurrentMeter: 100m));
        var m = _materials.Create(_admin, new NewMaterial("YAG-1", "Motor Yağı"));
        _opening.RecordOpening(_admin, m, 20m, "op-open");

        _daily.SaveExtraActivity(_admin, ExtraActivityTypes.ExtraOil,
            new NewMaintenance(VehicleId: v, DefinitionId: "", PerformedKm: 105m,
                Materials: new[] { new MaintenanceMaterialLine(m, 5m) }), "op-2");

        Assert.Equal(15m, _opening.GetBalance(_admin, m));   // 20 - 5
    }

    /// <summary>Aynı türden İKİNCİ bir kayıt, İLK seferde oluşturulan SABİT tanımı YENİDEN KULLANIR
    /// (ikinci bir "İlave Yağ" tanımı OLUŞMAZ — idempotent eşleme).</summary>
    [Fact]
    public void SaveExtraActivity_AyniTurIkinciKayit_AyniSabitTanimiKullanir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1", CurrentMeter: 100m));
        _daily.SaveExtraActivity(_admin, ExtraActivityTypes.ExtraOil,
            new NewMaintenance(VehicleId: v, DefinitionId: "", PerformedKm: 105m), "op-a");
        _daily.SaveExtraActivity(_admin, ExtraActivityTypes.ExtraOil,
            new NewMaintenance(VehicleId: v, DefinitionId: "", PerformedKm: 110m), "op-b");

        var defs = _defs.List(_admin).Where(d => d.Name == "İlave Yağ").ToList();
        Assert.Single(defs);
    }

    /// <summary>operation_id tekrarında (retry) ikinci kayıt OLUŞMAZ — idempotent (§4 kuralı).</summary>
    [Fact]
    public void SaveExtraActivity_AyniOperationId_TekrarEtmez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1", CurrentMeter: 100m));
        var id1 = _daily.SaveExtraActivity(_admin, ExtraActivityTypes.Repair,
            new NewMaintenance(VehicleId: v, DefinitionId: "", PerformedKm: 105m), "op-retry");
        var id2 = _daily.SaveExtraActivity(_admin, ExtraActivityTypes.Repair,
            new NewMaintenance(VehicleId: v, DefinitionId: "", PerformedKm: 999m), "op-retry");

        Assert.Equal(id1, id2);
        Assert.Single(_daily.List(_admin));
    }

    [Fact]
    public void SaveExtraActivity_GecersizTur_Reddedilir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        Assert.Throws<ArgumentException>(() =>
            _daily.SaveExtraActivity(_admin, "not_a_real_type", new NewMaintenance(VehicleId: v, DefinitionId: ""), "op-x"));
    }

    /// <summary>3 farklı tür 3 AYRI sabit tanım kullanır — birbirine karışmaz.</summary>
    [Fact]
    public void SaveExtraActivity_UcTurAyriTanimlar()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1"));
        _daily.SaveExtraActivity(_admin, ExtraActivityTypes.ExtraOil, new NewMaintenance(VehicleId: v, DefinitionId: ""), "op-1");
        _daily.SaveExtraActivity(_admin, ExtraActivityTypes.ExtraFilter, new NewMaintenance(VehicleId: v, DefinitionId: ""), "op-2");
        _daily.SaveExtraActivity(_admin, ExtraActivityTypes.Repair, new NewMaintenance(VehicleId: v, DefinitionId: ""), "op-3");

        var names = _defs.List(_admin).Select(d => d.Name).ToList();
        Assert.Equal(3, names.Count);
        Assert.Contains("İlave Yağ", names);
        Assert.Contains("İlave Filtre", names);
        Assert.Contains("Tamir", names);
    }

    /// <summary>Sabit tanımın periyot değeri 0 — bu türler için asla "bakım vadesi geldi" uyarı SEVİYESİ
    /// ÜRETİLMEZ (AlertRules.Progress: interval &lt;= 0 → 0 → Level=Normal, bkz. Application/Maintenance/
    /// AlertRules.cs) — ne kadar tüketilmiş olursa olsun bu tür asla Kritik/Gecikti göstermez.</summary>
    [Fact]
    public void SaveExtraActivity_SabitTanimPeriyotSifir_UyariUretmez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("KM-1", CurrentMeter: 100m));
        _daily.SaveExtraActivity(_admin, ExtraActivityTypes.ExtraOil,
            new NewMaintenance(VehicleId: v, DefinitionId: "", PerformedKm: 105m), "op-1");

        var def = _defs.List(_admin).Single(d => d.Name == "İlave Yağ");
        Assert.Equal(0m, def.IntervalValue);
        var alert = _maint.GetAlerts(_admin).SingleOrDefault(a => a.DefinitionId == def.Id);
        if (alert is not null) Assert.Equal(DepoWise.Application.Maintenance.AlertLevel.Normal, alert.Level);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
