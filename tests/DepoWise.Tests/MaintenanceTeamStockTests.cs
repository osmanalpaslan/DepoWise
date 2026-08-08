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
/// "Bakım Ekibi Stoğundan Kullanıldı" (kullanıcı isteği 2026-08-08, Migration059). İşaretli malzeme:
/// bakım kaydına GİRER + maliyete DÂHİL olur; ancak merkez depo stoğundan DÜŞÜLMEZ (bakiye + hareket defteri)
/// ve iptalde TERS HAREKET üretilmez. İşaretsiz = eski davranış (regresyon testleriyle korunur).
///
/// Kapsam: Araç Bakımları (MaintenanceService) + Günlük Faaliyet (Bakım ve İlave Yağ/Filtre/Tamir) — üçü de
/// AYNI ortak servisi kullanır, bu yüzden davranış tek yerde doğrulanır ve iki ekranda da geçerlidir.
/// </summary>
public class MaintenanceTeamStockTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceDefinitionService _defs;
    private readonly MaintenanceService _maint;
    private readonly DailyActivityService _daily;
    private readonly SessionContext _admin;

    public MaintenanceTeamStockTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_teamstock_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
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

    private (string Vehicle, string Material, string Def) Seed(decimal opening = 10m)
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        var m = _materials.Create(_admin, new NewMaterial("MAT-1", "Filtre"));
        _opening.RecordOpening(_admin, m, opening, "op-open-" + Guid.NewGuid().ToString("N"));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 5000m, "km"));
        return (v, m, def);
    }

    /// <summary>Hareket defterindeki tüketim (usage/usage_reverse) satır sayısı — defter ile bakiye tutarlılığı.</summary>
    private int MovementCount(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE material_id=@m AND movement_type IN ('usage','usage_reverse');";
        cmd.AddWithValue("@m", materialId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private decimal MaterialCost(string maintenanceId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(CAST(quantity AS REAL)*CAST(COALESCE(unit_price,'0') AS REAL)),0) FROM maintenance_materials WHERE maintenance_id=@mt;";
        cmd.AddWithValue("@mt", maintenanceId);
        return Convert.ToDecimal(cmd.ExecuteScalar());
    }

    // ── SENARYO 1: işaretlenmedi → ESKİ DAVRANIŞ (regresyon) ──
    [Fact]
    public void Isaretsiz_MerkezDepodan_NormalDusum()
    {
        var (v, m, def) = Seed();
        var id = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m) }), "op-1");

        Assert.Equal(8m, _opening.GetBalance(_admin, m));   // 10 - 2
        Assert.Equal(1, MovementCount(m));                   // defterde tüketim hareketi VAR
        Assert.Single(_maint.GetMaintenanceMaterials(_admin, id));
        Assert.False(_maint.GetMaintenanceMaterials(_admin, id)[0].FromTeamStock);
    }

    // ── SENARYO 2: işaretlendi → STOK DEĞİŞMEZ ──
    [Fact]
    public void Isaretli_MerkezDepoStogu_Degismez()
    {
        var (v, m, def) = Seed();
        var id = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m, FromTeamStock: true) }), "op-2");

        Assert.Equal(10m, _opening.GetBalance(_admin, m));   // DEĞİŞMEDİ
        Assert.Equal(0, MovementCount(m));                    // defterde tüketim hareketi YOK
        var rows = _maint.GetMaintenanceMaterials(_admin, id);
        Assert.Single(rows);                                  // kayıt YİNE var
        Assert.True(rows[0].FromTeamStock);
        Assert.Equal("Bakım ekibi stoğu", rows[0].SourceText);
    }

    [Fact]
    public void Isaretli_Malzeme_BakimMaliyetine_DAHIL()
    {
        var (v, m, def) = Seed();
        _materials.Update(_admin, m, new UpdateMaterial("MAT-1", "Filtre", UnitPrice: 50m));
        var id = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m, FromTeamStock: true) }), "op-cost");

        Assert.Equal(100m, MaterialCost(id));                 // 2 × 50 → maliyete dâhil (kullanıcı kararı)
        Assert.Equal(10m, _opening.GetBalance(_admin, m));     // ama stok düşmedi
    }

    // ── Karışık satır: biri depodan, biri ekip stoğundan ──
    [Fact]
    public void KarisikSatirlar_YalnizIsaretsizOlan_Duser()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        var m1 = _materials.Create(_admin, new NewMaterial("M-1", "Depo"));
        var m2 = _materials.Create(_admin, new NewMaterial("M-2", "Ekip"));
        _opening.RecordOpening(_admin, m1, 10m, "op-o1");
        _opening.RecordOpening(_admin, m2, 10m, "op-o2");
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));

        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m, Materials: new[]
        {
            new MaintenanceMaterialLine(m1, 3m),
            new MaintenanceMaterialLine(m2, 4m, FromTeamStock: true),
        }), "op-mix");

        Assert.Equal(7m, _opening.GetBalance(_admin, m1));    // düştü
        Assert.Equal(10m, _opening.GetBalance(_admin, m2));   // düşmedi
        Assert.Equal(1, MovementCount(m1));
        Assert.Equal(0, MovementCount(m2));
    }

    // ── İPTAL davranışı ──
    [Fact]
    public void Iptal_IsaretliSatir_TersHareketUretmez_StokSismez()
    {
        var (v, m, def) = Seed();
        var id = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m, FromTeamStock: true) }), "op-c1");

        _maint.Cancel(_admin, id, "yanlış kayıt");

        Assert.Equal(10m, _opening.GetBalance(_admin, m));    // ŞİŞMEDİ (hiç düşmemişti)
        Assert.Equal(0, MovementCount(m));                     // ters hareket de YOK
    }

    [Fact]
    public void Iptal_IsaretsizSatir_EskiDavranis_GeriEkler()
    {
        var (v, m, def) = Seed();
        var id = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m) }), "op-c2");
        Assert.Equal(8m, _opening.GetBalance(_admin, m));

        _maint.Cancel(_admin, id, "yanlış kayıt");

        Assert.Equal(10m, _opening.GetBalance(_admin, m));    // geri eklendi (regresyon)
        Assert.Equal(2, MovementCount(m));                     // tüketim + ters hareket
    }

    [Fact]
    public void Iptal_KarisikSatirlar_YalnizIsaretsizGeriDoner()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        var m1 = _materials.Create(_admin, new NewMaterial("M-1", "Depo"));
        var m2 = _materials.Create(_admin, new NewMaterial("M-2", "Ekip"));
        _opening.RecordOpening(_admin, m1, 10m, "op-o1");
        _opening.RecordOpening(_admin, m2, 10m, "op-o2");
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));
        var id = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m, Materials: new[]
        {
            new MaintenanceMaterialLine(m1, 3m),
            new MaintenanceMaterialLine(m2, 4m, FromTeamStock: true),
        }), "op-mix2");

        _maint.Cancel(_admin, id, "iptal");

        Assert.Equal(10m, _opening.GetBalance(_admin, m1));   // geri döndü
        Assert.Equal(10m, _opening.GetBalance(_admin, m2));   // hiç değişmedi
    }

    // ── GÜNLÜK FAALİYET: aynı ortak servis → aynı davranış ──
    [Fact]
    public void GunlukFaaliyet_Bakim_Isaretli_StokDusmez()
    {
        var (v, m, def) = Seed();
        _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m, FromTeamStock: true) }), "op-da1");

        Assert.Equal(10m, _opening.GetBalance(_admin, m));
        Assert.Equal(0, MovementCount(m));
    }

    [Fact]
    public void GunlukFaaliyet_Bakim_Isaretsiz_EskiDavranis()
    {
        var (v, m, def) = Seed();
        _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m) }), "op-da2");

        Assert.Equal(8m, _opening.GetBalance(_admin, m));
        Assert.Equal(1, MovementCount(m));
    }

    [Theory]
    [InlineData(ExtraActivityTypes.ExtraOil)]
    [InlineData(ExtraActivityTypes.ExtraFilter)]
    [InlineData(ExtraActivityTypes.Repair)]
    public void GunlukFaaliyet_IlaveYagFiltreTamir_Isaretli_StokDusmez(string type)
    {
        var (v, m, _) = Seed();
        _daily.SaveExtraActivity(_admin, type, new NewMaintenance(v, "", PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m, FromTeamStock: true) }), "op-x-" + type);

        Assert.Equal(10m, _opening.GetBalance(_admin, m));
        Assert.Equal(0, MovementCount(m));
    }

    [Theory]
    [InlineData(ExtraActivityTypes.ExtraOil)]
    [InlineData(ExtraActivityTypes.ExtraFilter)]
    [InlineData(ExtraActivityTypes.Repair)]
    public void GunlukFaaliyet_IlaveYagFiltreTamir_Isaretsiz_Duser(string type)
    {
        var (v, m, _) = Seed();
        _daily.SaveExtraActivity(_admin, type, new NewMaintenance(v, "", PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m) }), "op-y-" + type);

        Assert.Equal(8m, _opening.GetBalance(_admin, m));
        Assert.Equal(1, MovementCount(m));
    }

    // ── Geriye uyumluluk: bayrak verilmezse varsayılan false ──
    [Fact]
    public void Varsayilan_Bayraksiz_Cagri_EskiDavranis()
    {
        var (v, m, def) = Seed();
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 1m) }), "op-def");
        Assert.Equal(9m, _opening.GetBalance(_admin, m));
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
