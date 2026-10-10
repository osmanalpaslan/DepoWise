using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ 2026-10-10 — KİRALIK ARAÇ + TALEP ÇOKLU ARAÇ, PostgreSQL karşılığı. Sunucu/web PostgreSQL'de çalışır;
/// tarih metni (SqlDialect.DayText) iki lehçede FARKLI SQL ile üretilir → burada aynı sonucu verdiği kanıtlanır.
/// Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı) ile koşar; yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresKiralikAracTests
{
    private static string? PgUrl => PostgresTestGuard.Url;
    private static long Gun(int y, int m, int d) => new DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [SkippableFact]
    public void PostgreSQLde_kiralik_arac_tablosu_degisim_ve_talep_coklu_arac()
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
        var clock = new SystemClock();
        var uid = new UserService(factory, clock).EnsureInitialAdmin("A", "admin_a", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var v = new VehicleService(factory, clock);

        var sirket = v.Create(s, new NewVehicle("SRK-01"));
        var eski = v.Create(s, new NewVehicle("KRL-01", Rental: new RentalInfo("Kiracı", Gun(2025, 12, 31))));
        var yeni = v.Create(s, new NewVehicle("KRL-02", Rental: new RentalInfo("Kiracı", Gun(2026, 10, 2), ReplacedVehicleId: eski)));

        Assert.Equal(new[] { sirket }, v.SearchGrid(s, new VehicleGridFilter(), 1, 50).Items.Select(x => x.Id));
        var rows = v.SearchGrid(s, new VehicleGridFilter(), 1, 50, VehicleListColumns.RentalStart, false, rental: true).Items;
        Assert.Equal(new[] { "KRL-01", "KRL-02" }, rows.Select(x => x.InternalCode));
        Assert.Equal("31.12.2025", rows[0].RentalStart);
        Assert.Equal("02.10.2026", rows[0].RentalEnd);          // değişimle otomatik yazılan bitiş
        Assert.Equal("KRL-01", rows[1].ReplacedVehicle);
        Assert.Equal(VehicleStatus.Passive, v.Get(s, eski).Status);
        Assert.Equal("KRL-02", v.Get(s, eski).ReplacedByCode);
        Assert.Single(v.SearchGrid(s, new VehicleGridFilter(RentalStart: "10.2026"), 1, 50, rental: true).Items);

        // Talep kaleminde iki araç (vehicle_ids) — PostgreSQL'de de tam okunur.
        var mat = new MaterialService(factory, clock).Create(s, new NewMaterial("FLT-01", "Filtre"));
        var req = new RequestService(factory, new StockService(factory, clock), clock);
        var id = req.Create(s, new NewRequest(new[] { new RequestItemInput(mat, 2m, VehicleIds: new[] { sirket, yeni }) })).Id;
        Assert.Equal(new[] { sirket, yeni }, req.GetForEdit(s, id).Items.Single().Vehicles!.Select(x => x.Id));
        Assert.Contains("(Kiralık)", v.List(s).Single(x => x.Id == yeni).Display);
    }
}
