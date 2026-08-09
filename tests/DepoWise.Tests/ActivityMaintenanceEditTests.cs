using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// GÜNLÜK FAALİYET + BAKIM KAYDI DÜZENLEME — İş #5 (2026-08-09), seçenek A (sınırlı düzenleme).
///
/// KARAR: yalnız YAN ETKİSİZ (metadata) alanlar düzenlenir. Malzeme/miktar ve sayaç alanları
/// düzenlenmez çünkü:
///   • stok defteri ana kaynaktır ve bakiye doğrudan değiştirilmez (CLAUDE.md §4),
///   • sayaç geriye gitmez (<c>MeterRule</c>),
///   • bu alanlar için mevcut yol iptal + yeniden oluşturmadır (İş 1/İş 2 deseni).
///
/// Bu testler ASIL GARANTİYİ kanıtlar: düzenleme HİÇBİR stok hareketi/bakiye/sayaç değişikliği ÜRETMEZ.
/// </summary>
public class ActivityMaintenanceEditTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly StockService _stock;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceDefinitionService _defs;
    private readonly MaintenanceService _maint;
    private readonly DailyActivityService _daily;
    private readonly PersonnelService _personnel;
    private readonly SessionContext _a, _b, _noEditA;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    public ActivityMaintenanceEditTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_actedit_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _maint = new MaintenanceService(_factory, _clock);
        _daily = new DailyActivityService(_factory, _maint, _clock, _defs);
        _personnel = new PersonnelService(_factory, new ScopeResolver(_factory), _clock);
        var users = new UserService(_factory, _clock);

        Company("A"); Company("B");
        _a = Sess(users, "A", "kul_a", RoleKeys.CompanyAdmin, PermissionSet.Empty);
        _b = Sess(users, "B", "kul_b", RoleKeys.CompanyAdmin, PermissionSet.Empty);
        _noEditA = Sess(users, "A", "okur_a", RoleKeys.Staff, new PermissionSet(new[]
        {
            new ModulePermission("maintenance", true, false, false, false),
            new ModulePermission("daily_activity", true, false, false, false),
        }));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    private void Company(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static SessionContext Sess(UserService users, string company, string user, string role, PermissionSet perms)
    {
        var uid = users.EnsureInitialAdmin(company, user, "Test!2026", role);
        return new SessionContext(uid, company, new[] { role }, perms);
    }

    /// <summary>Malzemeli bakım + ona bağlı günlük faaliyet üretir (stok 100 → 90).</summary>
    private (string ActivityId, string MaintenanceId, string MaterialId, string VehicleId) Seed(SessionContext s)
    {
        var v = _vehicles.Create(s, new NewVehicle("ARC-" + s.CompanyId, CurrentMeter: 1000m));
        var m = _materials.Create(s, new NewMaterial("MAT-" + s.CompanyId, "Filtre"));
        _opening.RecordOpening(s, m, 100m, "op-" + Guid.NewGuid().ToString("N"));
        var d = _defs.Create(s, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        var act = _daily.SaveMaintenanceActivity(s, new NewMaintenance(v, d, PerformedKm: 1100m,
            Materials: new[] { new MaintenanceMaterialLine(m, 10m) }), "op-" + Guid.NewGuid().ToString("N"));
        return (act, MaintenanceIdOf(act), m, v);
    }

    private string MaintenanceIdOf(string activityId) => Scalar($"SELECT maintenance_id FROM daily_activities WHERE id='{activityId}';")!;
    private string? Scalar(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }
    private long Count(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
    private long VersionOf(string table, string id) => Count($"SELECT version FROM {table} WHERE id='{id}';");

    // ── BAKIM metadata düzenleme ───────────────────────────────────────────────────────────

    [Fact]
    public void Bakim_metadata_duzenlenebilir_STOK_ve_SAYAC_DEGISMEZ()
    {
        var (_, mnt, mat, veh) = Seed(_a);
        var stokOnce = _stock.GetBalance(_a, mat);
        var hareketOnce = Count("SELECT COUNT(*) FROM stock_movements;");
        var sayacOnce = _vehicles.GetMeter(_a, veh);
        var tekn = _personnel.Create(_a, new NewPersonnel("Usta Ali", "Teknisyen", null, null));

        _maint.UpdateMetadata(_a, mnt, "Yeni açıklama", "Alt not", tekn);

        Assert.Equal("Yeni açıklama", Scalar($"SELECT description FROM vehicle_maintenances WHERE id='{mnt}';"));
        Assert.Equal("Alt not", Scalar($"SELECT sub_definition_note FROM vehicle_maintenances WHERE id='{mnt}';"));
        Assert.Equal(tekn, Scalar($"SELECT technician_id FROM vehicle_maintenances WHERE id='{mnt}';"));
        // ASIL GARANTİ: hiçbir stok/sayaç yan etkisi YOK
        Assert.Equal(stokOnce, _stock.GetBalance(_a, mat));
        Assert.Equal(hareketOnce, Count("SELECT COUNT(*) FROM stock_movements;"));
        Assert.Equal(sayacOnce, _vehicles.GetMeter(_a, veh));
    }

    [Fact]
    public void Bakim_malzeme_satirlari_duzenlemeden_ETKILENMEZ()
    {
        var (_, mnt, _, _) = Seed(_a);
        var satirOnce = Count($"SELECT COUNT(*) FROM maintenance_materials WHERE maintenance_id='{mnt}';");
        _maint.UpdateMetadata(_a, mnt, "x", null, null);
        Assert.Equal(satirOnce, Count($"SELECT COUNT(*) FROM maintenance_materials WHERE maintenance_id='{mnt}';"));
    }

    [Fact]
    public void Bakim_BASKA_firmanin_kaydi_duzenlenemez()
    {
        var (_, mntB, _, _) = Seed(_b);
        Assert.ThrowsAny<Exception>(() => _maint.UpdateMetadata(_a, mntB, "ELE GECIRILDI", null, null));
        Assert.NotEqual("ELE GECIRILDI", Scalar($"SELECT COALESCE(description,'') FROM vehicle_maintenances WHERE id='{mntB}';"));
    }

    [Fact]
    public void Bakim_YETKISIZ_kullanici_duzenleyemez()
    {
        var (_, mnt, _, _) = Seed(_a);
        Assert.ThrowsAny<Exception>(() => _maint.UpdateMetadata(_noEditA, mnt, "yetkisiz", null, null));
    }

    [Fact]
    public void Bakim_YABANCI_teknisyen_atanamaz()
    {
        var (_, mnt, _, _) = Seed(_a);
        var teknB = _personnel.Create(_b, new NewPersonnel("B Teknisyeni", null, null, null));
        Assert.ThrowsAny<Exception>(() => _maint.UpdateMetadata(_a, mnt, null, null, teknB));
        Assert.Null(Scalar($"SELECT technician_id FROM vehicle_maintenances WHERE id='{mnt}';"));
    }

    [Fact]
    public void Bakim_DUZENLEME_KILIDI_eski_surumu_reddeder()
    {
        var (_, mnt, _, _) = Seed(_a);
        var acilistaki = VersionOf("vehicle_maintenances", mnt);

        _clock.Advance(1000);
        _maint.UpdateMetadata(_a, mnt, "ilk kaydeden", null, null);          // başka kullanıcı kaydetti

        Assert.ThrowsAny<Exception>(() =>
            _maint.UpdateMetadata(_a, mnt, "eski veri", null, null, acilistaki));
        Assert.Equal("ilk kaydeden", Scalar($"SELECT description FROM vehicle_maintenances WHERE id='{mnt}';"));
    }

    [Fact]
    public void Bakim_IPTAL_EDILMIS_kayit_duzenlenemez()
    {
        var (_, mnt, _, _) = Seed(_a);
        _maint.Cancel(_a, mnt, "test iptali");
        Assert.ThrowsAny<Exception>(() => _maint.UpdateMetadata(_a, mnt, "iptalden sonra", null, null));
    }

    [Fact]
    public void Bakim_duzenlemesi_AUDIT_kaydi_yazar()
    {
        var (_, mnt, _, _) = Seed(_a);
        var once = Count($"SELECT COUNT(*) FROM audit_logs WHERE entity_id='{mnt}' AND action='update';");
        _maint.UpdateMetadata(_a, mnt, "denetim", null, null);
        Assert.Equal(once + 1, Count($"SELECT COUNT(*) FROM audit_logs WHERE entity_id='{mnt}' AND action='update';"));
    }

    // ── GÜNLÜK FAALİYET metadata düzenleme ────────────────────────────────────────────────

    [Fact]
    public void Faaliyet_metadata_duzenlenebilir_STOK_DEGISMEZ()
    {
        var (act, _, mat, _) = Seed(_a);
        var stokOnce = _stock.GetBalance(_a, mat);
        var hareketOnce = Count("SELECT COUNT(*) FROM stock_movements;");
        var oper = _personnel.Create(_a, new NewPersonnel("Operatör Veli", null, null, null));

        _daily.UpdateMetadata(_a, act, "Faaliyet açıklaması", oper, 3);

        Assert.Equal("Faaliyet açıklaması", Scalar($"SELECT description FROM daily_activities WHERE id='{act}';"));
        Assert.Equal(oper, Scalar($"SELECT operator_id FROM daily_activities WHERE id='{act}';"));
        Assert.Equal(3, Count($"SELECT duration_days FROM daily_activities WHERE id='{act}';"));
        Assert.Equal(stokOnce, _stock.GetBalance(_a, mat));
        Assert.Equal(hareketOnce, Count("SELECT COUNT(*) FROM stock_movements;"));
    }

    [Fact]
    public void Faaliyet_BASKA_firmanin_kaydi_duzenlenemez()
    {
        var (actB, _, _, _) = Seed(_b);
        Assert.ThrowsAny<Exception>(() => _daily.UpdateMetadata(_a, actB, "ELE GECIRILDI", null, null));
        Assert.NotEqual("ELE GECIRILDI", Scalar($"SELECT COALESCE(description,'') FROM daily_activities WHERE id='{actB}';"));
    }

    [Fact]
    public void Faaliyet_YETKISIZ_kullanici_duzenleyemez()
    {
        var (act, _, _, _) = Seed(_a);
        Assert.ThrowsAny<Exception>(() => _daily.UpdateMetadata(_noEditA, act, "yetkisiz", null, null));
    }

    [Fact]
    public void Faaliyet_YABANCI_operator_atanamaz()
    {
        var (act, _, _, _) = Seed(_a);
        var operB = _personnel.Create(_b, new NewPersonnel("B Operatörü", null, null, null));
        Assert.ThrowsAny<Exception>(() => _daily.UpdateMetadata(_a, act, null, operB, null));
        Assert.Null(Scalar($"SELECT operator_id FROM daily_activities WHERE id='{act}';"));
    }

    [Fact]
    public void Faaliyet_DUZENLEME_KILIDI_eski_surumu_reddeder()
    {
        var (act, _, _, _) = Seed(_a);
        var acilistaki = VersionOf("daily_activities", act);

        _clock.Advance(1000);
        _daily.UpdateMetadata(_a, act, "ilk kaydeden", null, null);

        Assert.ThrowsAny<Exception>(() => _daily.UpdateMetadata(_a, act, "eski veri", null, null, acilistaki));
        Assert.Equal("ilk kaydeden", Scalar($"SELECT description FROM daily_activities WHERE id='{act}';"));
    }

    [Fact]
    public void Faaliyet_IPTAL_EDILMIS_kayit_duzenlenemez()
    {
        var (act, _, _, _) = Seed(_a);
        _daily.Delete(_a, act);   // iptal (soft)
        Assert.ThrowsAny<Exception>(() => _daily.UpdateMetadata(_a, act, "iptalden sonra", null, null));
    }

    [Fact]
    public void Faaliyet_negatif_sure_reddedilir()
    {
        var (act, _, _, _) = Seed(_a);
        Assert.ThrowsAny<Exception>(() => _daily.UpdateMetadata(_a, act, null, null, -1));
    }

    // ── ATOMİKLİK: başarısız düzenleme yarım veri bırakmaz ────────────────────────────────

    [Fact]
    public void Basarisiz_duzenleme_YARIM_VERI_birakmaz()
    {
        var (act, _, _, _) = Seed(_a);
        var operB = _personnel.Create(_b, new NewPersonnel("B Operatörü", null, null, null));
        var aciklamaOnce = Scalar($"SELECT COALESCE(description,'') FROM daily_activities WHERE id='{act}';");
        var surumOnce = VersionOf("daily_activities", act);
        var auditOnce = Count($"SELECT COUNT(*) FROM audit_logs WHERE entity_id='{act}';");

        // Yabancı operatör → personel kontrolü UPDATE'ten ÖNCE patlar; hiçbir şey yazılmamalı
        Assert.ThrowsAny<Exception>(() => _daily.UpdateMetadata(_a, act, "yeni açıklama", operB, 5));

        Assert.Equal(aciklamaOnce, Scalar($"SELECT COALESCE(description,'') FROM daily_activities WHERE id='{act}';"));
        Assert.Equal(surumOnce, VersionOf("daily_activities", act));
        Assert.Equal(auditOnce, Count($"SELECT COUNT(*) FROM audit_logs WHERE entity_id='{act}';"));
    }
}
