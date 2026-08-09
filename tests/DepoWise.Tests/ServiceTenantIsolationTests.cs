using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// SERVİS KATMANI çok-firmalı izolasyon (Paket 1, 2026-08-09).
///
/// NEDEN AYRICA GEREKLİ: masaüstü uygulaması bu servisleri **doğrudan** çağırır (HTTP hattı yoktur).
/// Bu yüzden koruma servis katmanında olmalıdır; API testleri tek başına yeterli değildir.
/// Özellikle:
///  • <c>MaterialService.RemoveEquivalent</c> (Y-1) — API ucu YOK, yalnız masaüstünden çağrılıyor.
///  • <c>MaintenanceDefinitionService.SetVehicles/Create</c> (T-2, Y-2) — masaüstü doğrudan çağırıyor.
/// </summary>
public class ServiceTenantIsolationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly StockService _stock;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceDefinitionService _defs;
    private readonly RequestService _requests;
    private readonly UserService _users;

    private readonly SessionContext _a, _b;
    private readonly string _matA, _matB, _matB2, _vehA, _vehB, _defA, _defB;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public ServiceTenantIsolationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_tenant_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _requests = new RequestService(_factory, _stock, _clock);
        _users = new UserService(_factory, _clock);

        Company("A"); Company("B");
        _a = Session("A", "kul_a");
        _b = Session("B", "kul_b");

        _matA = _materials.Create(_a, new NewMaterial("MAT-A", "A malzemesi"));
        _matB = _materials.Create(_b, new NewMaterial("MAT-B", "B malzemesi"));
        _matB2 = _materials.Create(_b, new NewMaterial("MAT-B2", "B malzemesi 2"));
        _opening.RecordOpening(_b, _matB, 250m, "op-" + Guid.NewGuid().ToString("N"));

        _vehA = _vehicles.Create(_a, new NewVehicle("ARC-A", CurrentMeter: 10m));
        _vehB = _vehicles.Create(_b, new NewVehicle("ARC-B", CurrentMeter: 20m));
        _defA = _defs.Create(_a, new NewMaintenanceDefinition("A bakımı", 100m, "km"));
        _defB = _defs.Create(_b, new NewMaintenanceDefinition("B bakımı", 100m, "km"));
        _defs.SetVehicles(_b, _defB, new[] { _vehB });

        // B firmasının kendi muadil ilişkisi (Y-1 hedefi)
        _materials.AddEquivalent(_b, _matB, _matB2);
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

    private SessionContext Session(string company, string user)
    {
        var uid = _users.EnsureInitialAdmin(company, user, "Test!2026", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private int EquivalentCount(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM material_equivalents WHERE material_id=@m OR equivalent_material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private HashSet<string> LinkedVehicles(string defId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT vehicle_id FROM maintenance_definition_vehicles WHERE definition_id=@d;";
        cmd.AddWithValue("@d", defId);
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetString(0));
        return set;
    }

    // ── Y-1 · muadil silme (API ucu YOK — yalnız masaüstü) ─────────────────────────────────

    [Fact]
    public void Y1_Baska_firmanin_muadil_iliskisi_SILINEMEZ()
    {
        var before = EquivalentCount(_matB);
        Assert.Equal(2, before);   // simetrik: iki yön de yazılır

        try { _materials.RemoveEquivalent(_a, _matB, _matB2); } catch (ForbiddenException) { /* beklenen */ }

        Assert.Equal(before, EquivalentCount(_matB));   // B'nin ilişkisi DURUYOR
    }

    [Fact]
    public void Y1_Kendi_muadil_iliskisini_SILEBILIR()
    {
        _materials.RemoveEquivalent(_b, _matB, _matB2);
        Assert.Equal(0, EquivalentCount(_matB));
    }

    // ── T-2 / Y-2 · bakım tanımı ↔ araç (masaüstü doğrudan çağırıyor) ──────────────────────

    [Fact]
    public void T2b_Kendi_tanimina_BASKA_firmanin_araci_baglanamaz()
    {
        try { _defs.SetVehicles(_a, _defA, new[] { _vehB }); } catch (ForbiddenException) { /* beklenen */ }
        Assert.DoesNotContain(_vehB, LinkedVehicles(_defA));
    }

    [Fact]
    public void T2a_BASKA_firmanin_tanimina_arac_baglanamaz()
    {
        var before = LinkedVehicles(_defB);
        try { _defs.SetVehicles(_a, _defB, new[] { _vehA }); } catch (ForbiddenException) { /* beklenen */ }
        Assert.Equal(before, LinkedVehicles(_defB));   // B'nin tanımı DEĞİŞMEDİ
    }

    [Fact]
    public void Y2_Create_ile_BASKA_firmanin_araci_baglanamaz()
    {
        string? newId = null;
        try { newId = _defs.Create(_a, new NewMaintenanceDefinition("Yeni", 50m, "km"), new[] { _vehB }); }
        catch (ForbiddenException) { /* beklenen */ }
        if (newId is not null) Assert.DoesNotContain(_vehB, LinkedVehicles(newId));
    }

    [Fact]
    public void Kendi_aracini_baglayabilir()
    {
        _defs.SetVehicles(_a, _defA, new[] { _vehA });
        Assert.Contains(_vehA, LinkedVehicles(_defA));
    }

    // ── T-3 · tanımın araç listesi ─────────────────────────────────────────────────────────

    [Fact]
    public void T3_Baska_firmanin_tanim_araclari_okunamaz()
    {
        IReadOnlyList<string> ids = Array.Empty<string>();
        try { ids = _defs.GetVehicleIds(_a, _defB); } catch (ForbiddenException) { /* beklenen */ }
        Assert.DoesNotContain(_vehB, ids);
    }

    [Fact]
    public void T3_Kendi_tanim_araclarini_okuyabilir()
    {
        Assert.Contains(_vehB, _defs.GetVehicleIds(_b, _defB));
    }

    // ── T-1 · stok bakiyesi ────────────────────────────────────────────────────────────────

    [Fact]
    public void T1_Baska_firmanin_stok_bakiyesi_okunamaz()
    {
        decimal bal = 0m;
        try { bal = _stock.GetBalance(_a, _matB); } catch (ForbiddenException) { /* beklenen */ }
        Assert.NotEqual(250m, bal);
    }

    [Fact]
    public void T1_Kendi_stok_bakiyesini_okuyabilir()
    {
        Assert.Equal(250m, _stock.GetBalance(_b, _matB));
    }

    // ── T-6 · kullanıcı rolleri ────────────────────────────────────────────────────────────

    [Fact]
    public void T6_Baska_firmanin_kullanici_rolleri_okunamaz()
    {
        var userB = _b.UserId;
        IReadOnlyList<string> roles = Array.Empty<string>();
        try { roles = _users.GetRoleKeys(_a, userB); } catch (ForbiddenException) { /* beklenen */ }
        Assert.Empty(roles);
    }

    [Fact]
    public void T6_Kendi_kullanicisinin_rollerini_okuyabilir()
    {
        Assert.NotEmpty(_users.GetRoleKeys(_b, _b.UserId));
    }
}
