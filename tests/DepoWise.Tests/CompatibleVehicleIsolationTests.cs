using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// UYUMLU ARAÇ — FİRMA İZOLASYONU (İş C, 2026-08-09).
///
/// <b>Açık (İş B analizinde bulundu):</b> <c>MaterialService.SetCompatibleVehicles</c> YALNIZ malzemenin
/// sahipliğini doğruluyordu; gönderilen <c>vehicleId</c> değerlerinin firması HİÇ kontrol edilmiyordu.
/// API üç uçta (<c>PUT /api/materials/{id}</c>, <c>POST /api/materials</c>,
/// <c>POST /api/materials/{id}/compatible-vehicles</c>) bu listeyi doğrudan geçiriyordu.
///
/// Zincirin ikinci halkası okuma tarafındaydı: <c>SearchGrid</c>'in "Uyumlu Araçlar" alt sorgusu
/// <c>vehicles</c> tablosuna firma filtresi UYGULAMIYORDU → yabancı bir ilişki yazılabilirse
/// diğer firmanın araç iç kodu Malzeme Listesi grid'inde GÖRÜNÜYORDU.
///
/// Bu testler her iki halkayı ayrı ayrı kanıtlar. Düzeltmeden ÖNCE kırmızıdırlar.
/// FK ile ilgisi YOKTUR: FK cross-company yazımı engellemez (iki firmanın aracı da aynı tabloda).
/// </summary>
public class CompatibleVehicleIsolationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly VehicleService _vehicles;
    private readonly UserService _users;
    private readonly SessionContext _a, _b;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public CompatibleVehicleIsolationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_compatveh_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _users = new UserService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);

        Company("A"); Company("B");
        _a = Admin("A", "kul_a");
        _b = Admin("B", "kul_b");
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

    private SessionContext Admin(string company, string user)
    {
        var uid = _users.EnsureInitialAdmin(company, user, "Test!2026", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    /// <summary>Grid'in "Uyumlu Araçlar" kolonundaki metin (o malzeme satırı için).</summary>
    private string CompatibleColumn(SessionContext s, string materialId)
    {
        var res = _materials.SearchGrid(s, new MaterialGridFilter(), 1, 50);
        var row = res.Items.FirstOrDefault(r => r.Id == materialId);
        Assert.NotNull(row);   // satır grid'de olmalı; yoksa test yanlış şeyi ölçer
        return row!.CompatibleVehicles ?? "";
    }

    /// <summary>Servis guard'ını ATLAYARAK doğrudan satır yazar — okuma tarafı savunmasını test etmek için.
    /// (Yazma guard'ı eklendikten sonra bu durum servis üzerinden oluşturulamaz; savunma katmanı yine de sınanır.)</summary>
    private void ForceRow(string materialId, string vehicleId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO material_compatible_vehicles(material_id, vehicle_id) VALUES(@m,@v) ON CONFLICT DO NOTHING;";
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@v", vehicleId);
        cmd.ExecuteNonQuery();
    }

    // ── C-1: YAZMA TARAFI ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BASKA_firmanin_araci_uyumlu_arac_olarak_YAZILAMAZ()
    {
        var matA = _materials.Create(_a, new NewMaterial("M-A", "A malzemesi"));
        var vehB = _vehicles.Create(_b, new NewVehicle("ARC-B"));

        Assert.Throws<ForbiddenException>(() => _materials.SetCompatibleVehicles(_a, matA, new[] { vehB }));

        // Reddedilmeli VE hiçbir satır yazılmamalı (transaction geri alınır).
        Assert.Equal("", CompatibleColumn(_a, matA));
    }

    [Fact]
    public void Karisik_listede_BIR_yabanci_arac_TUM_islemi_reddeder()
    {
        // Yarım yazma olmamalı: geçerli araç da yazılmamalı (tek transaction).
        var matA = _materials.Create(_a, new NewMaterial("M-A2", "A malzemesi 2"));
        var vehA = _vehicles.Create(_a, new NewVehicle("ARC-A2"));
        var vehB = _vehicles.Create(_b, new NewVehicle("ARC-B2"));

        Assert.Throws<ForbiddenException>(() => _materials.SetCompatibleVehicles(_a, matA, new[] { vehA, vehB }));
        Assert.Equal("", CompatibleColumn(_a, matA));
    }

    [Fact]
    public void KENDI_firmasinin_araci_normal_sekilde_YAZILIR()
    {
        var matA = _materials.Create(_a, new NewMaterial("M-A3", "A malzemesi 3"));
        var vehA = _vehicles.Create(_a, new NewVehicle("ARC-A3"));

        _materials.SetCompatibleVehicles(_a, matA, new[] { vehA });

        Assert.Contains("ARC-A3", CompatibleColumn(_a, matA));
    }

    [Fact]
    public void SILINMIS_arac_uyumlu_arac_olarak_YAZILAMAZ()
    {
        // EnsureVehicleOwned deseni is_deleted=0 da arar (Paket 1 ile aynı davranış).
        var matA = _materials.Create(_a, new NewMaterial("M-A4", "A malzemesi 4"));
        var vehA = _vehicles.Create(_a, new NewVehicle("ARC-A4"));
        _vehicles.Delete(_a, vehA);

        Assert.Throws<ForbiddenException>(() => _materials.SetCompatibleVehicles(_a, matA, new[] { vehA }));
    }

    // ── C-2: OKUMA TARAFI (savunma katmanı) ───────────────────────────────────────────────

    [Fact]
    public void Grid_YABANCI_aracin_kodunu_GOSTERMEZ()
    {
        // Yazma guard'ını atlayarak bozuk ilişki üretilir (eski sürümde yazılmış olabilecek satır).
        var matA = _materials.Create(_a, new NewMaterial("M-A5", "A malzemesi 5"));
        var vehB = _vehicles.Create(_b, new NewVehicle("SIZAN-KOD"));
        ForceRow(matA, vehB);

        // A firmasının grid'i B'nin araç iç kodunu ASLA göstermemeli.
        Assert.DoesNotContain("SIZAN-KOD", CompatibleColumn(_a, matA));
    }

    [Fact]
    public void Grid_KENDI_araclarini_gostermeye_DEVAM_eder()
    {
        // Savunma filtresi doğru olanı da gizlememeli (aşırı-filtreleme regresyonu).
        var matA = _materials.Create(_a, new NewMaterial("M-A6", "A malzemesi 6"));
        var veh1 = _vehicles.Create(_a, new NewVehicle("ARC-X1"));
        var veh2 = _vehicles.Create(_a, new NewVehicle("ARC-X2"));
        _materials.SetCompatibleVehicles(_a, matA, new[] { veh1, veh2 });

        var text = CompatibleColumn(_a, matA);
        Assert.Contains("ARC-X1", text);
        Assert.Contains("ARC-X2", text);
    }

    [Fact]
    public void B_firmasi_KENDI_iliskisini_gormeye_devam_eder()
    {
        var matB = _materials.Create(_b, new NewMaterial("M-B", "B malzemesi"));
        var vehB = _vehicles.Create(_b, new NewVehicle("ARC-BB"));
        _materials.SetCompatibleVehicles(_b, matB, new[] { vehB });

        Assert.Contains("ARC-BB", CompatibleColumn(_b, matB));
    }
}
