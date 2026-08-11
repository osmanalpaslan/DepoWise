using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// BKM-04 / KARAR-9 — POSTGRESQL LEHÇE DOĞRULAMASI.
///
/// Bakım tüketiminin lokasyon yolu iki yeni SQL parçası getirdi: hareket INSERT'ine <c>branch_id</c> +
/// <c>reverses_movement_id</c> parametreleri, iptalde ise defterden okuma
/// (<c>WHERE note=… AND movement_type='usage' ORDER BY created_at, id</c>).
/// Sunucu PostgreSQL, masaüstü SQLite olduğu için <b>ikisi de</b> doğrulanır (CLAUDE.md §4).
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresMaintenanceLocationTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    /// <summary>Seçilen depodan düşme + iptalin ORİJİNAL depoya dönmesi PostgreSQL'de de çalışır.</summary>
    [SkippableFact]
    public void PostgreSQLde_bakim_secilen_depodan_duser_ve_iptal_ayni_depoya_doner()
    {
        PostgresTestGuard.SkipUnlessSafe();

        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        using (var conn = factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                "VALUES('A', 'A', 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.ExecuteNonQuery();
        }

        var clock = new TestClock();
        var users = new UserService(factory, clock);
        var uid = users.EnsureInitialAdmin("A", "admin_bkm", "admin123", RoleKeys.CompanyAdmin);
        var branches = new BranchService(factory, clock);
        var admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var depoA = branches.Create(admin, new NewBranch("PG Depo A"));
        var depoB = branches.Create(admin, new NewBranch("PG Depo B"));
        var oturumA = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = depoA };

        var materials = new MaterialService(factory, clock);
        var opening = new OpeningStockService(factory, clock);
        var stock = new StockService(factory, clock);
        var defs = new MaintenanceDefinitionService(factory, clock);
        var vehicles = new VehicleService(factory, clock);
        var maintenance = new MaintenanceService(factory, clock);

        var mat = materials.Create(oturumA, new NewMaterial("PG-BKM-1", "Yağ filtresi"));
        var vehicle = vehicles.Create(oturumA, new NewVehicle("PG-IS-1", "34PG001", 2020, 1000m, "km", depoA));
        var def = defs.Create(oturumA, new NewMaintenanceDefinition("PG Periyodik", 10000m, "km"));

        opening.RecordOpening(oturumA, mat, 10m, "pg-op-a", branchId: depoA);
        opening.RecordOpening(oturumA, mat, 10m, "pg-op-b", branchId: depoB);

        // Oturum Depo A'da; kullanıcı bilerek DEPO B'yi seçiyor.
        var id = maintenance.Save(oturumA, new NewMaintenance(
            VehicleId: vehicle, DefinitionId: def, PerformedKm: 5000m,
            PerformedDate: clock.UtcNow.ToUnixTimeMilliseconds(),
            Materials: new[] { new MaintenanceMaterialLine(mat, 4m) },
            StockLocationId: depoB), "pg-mnt-1");

        Assert.Equal(10m, stock.GetBalanceAt(oturumA, mat, depoA));   // oturum şubesi ezilmedi
        Assert.Equal(6m, stock.GetBalanceAt(oturumA, mat, depoB));    // seçilen depo düştü
        Assert.Equal(0m, stock.GetBalanceAt(oturumA, mat, StockBalanceWriter.Unassigned));
        Assert.Equal(depoB, TekHareketinDeposu(factory, mat, "usage"));

        // İptal — kullanıcı bu kez DEPO B oturumundan farklı bir şubeyle çalışıyor olsun.
        var oturumB = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = depoA };
        maintenance.Cancel(oturumB, id, "pg iptal");

        Assert.Equal(10m, stock.GetBalanceAt(oturumA, mat, depoB));   // ORİJİNAL depoya döndü
        Assert.Equal(10m, stock.GetBalanceAt(oturumA, mat, depoA));   // diğer depo şişmedi
        Assert.Equal(depoB, TekHareketinDeposu(factory, mat, "usage_reverse"));
    }

    private static string? TekHareketinDeposu(IDbConnectionFactory f, string materialId, string type)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT branch_id FROM stock_movements WHERE company_id='A' AND material_id=@m AND movement_type=@t;";
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@t", type);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? null : (string)v;
    }
}
