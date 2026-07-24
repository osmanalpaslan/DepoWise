using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PostgreSQL GEÇİŞİ — CANLI GEÇİŞ aracı (2026-07-24): <see cref="SqliteToPgCopier"/> bir SQLite veritabanının
/// TÜM verisini Neon PostgreSQL'e doğru aktarıyor mu? Bu, babanın gerçek verisinin KOPYASINI yükleyeceğimiz
/// araç → sağlamlığı burada kanıtlanır. Kontroller: satır sayıları tablo tablo eşleşir; company_id'siz çocuklar
/// (maintenance_materials/material_request_items/vehicle_meter_logs) taşınır; IDENTITY (server_changes.seq)
/// açık değerle kopyalanıp sequence max+1'e ilerler (geçiş sonrası çakışma yok).
///
/// ⚠️ Yalnız DEPOWISE_PG_URL üzerinde; PG şemasını sıfırlar. Kaynak SQLite geçici bir dosyadır. Yoksa ATLANIR.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresDataCopyTests : IDisposable
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");
    private readonly string _sqlitePath = Path.Combine(Path.GetTempPath(), "dw_copy_" + Guid.NewGuid().ToString("N") + ".db");

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static long Count(IDbConnectionFactory f, string table)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [SkippableFact]
    public void Copier_SQLite_Verisini_PostgreSQLe_Dogru_Tasir()
    {
        Skip.If(string.IsNullOrWhiteSpace(PgUrl), "DEPOWISE_PG_URL yok → veri kopyalama testi atlandı.");

        // 1) KAYNAK: SQLite'ta zengin, gerçekçi veri kur.
        var sqlite = new SqliteConnectionFactory(_sqlitePath);
        new MigrationRunner(sqlite).Run();
        var clock = new TestClock();
        var users = new UserService(sqlite, clock);
        var companies = new CompanyService(sqlite, clock);
        var branches = new BranchService(sqlite, clock);
        var rootId = users.EnsureInitialAdmin("A", "root", "root123", RoleKeys.SuperAdmin);
        var su = new SessionContext(rootId, "A", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
        companies.Create(su, new NewCompany("B Firma"), explicitId: "B");
        var bBranch = branches.Create(su, new NewBranch("B-Merkez"), companyId: "B");
        var bId = users.CreateUser(su, new NewUser("badm", "p12345", "B Admin", new[] { RoleKeys.CompanyAdmin }, CompanyId: "B", BranchId: bBranch));
        var b = new SessionContext(bId, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var materials = new MaterialService(sqlite, clock);
        var opening = new OpeningStockService(sqlite, clock);
        var vehicles = new VehicleService(sqlite, clock);
        var defs = new MaintenanceDefinitionService(sqlite, clock);
        var maint = new MaintenanceService(sqlite, clock);
        var requests = new RequestService(sqlite, new StockService(sqlite, clock), clock);

        var m = materials.Create(b, new NewMaterial("M-1", "Filtre", UnitPrice: 50m, MinStock: 5m));
        opening.RecordOpening(b, m, 100m, "b-open");
        var v = vehicles.Create(b, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = defs.Create(b, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        maint.Save(b, new NewMaintenance(v, def, PerformedKm: 1000m, Materials: new[] { new MaintenanceMaterialLine(m, 10m) }), "b-mnt");
        requests.Create(b, new NewRequest(new[] { new RequestItemInput(m, 20m) }, SubmitImmediately: true));

        // Kendine-referanslı satır (material_categories.parent_id) + IDENTITY satırı (server_changes.seq).
        long srcSeq;
        using (var conn = sqlite.Create())
        {
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "INSERT INTO material_categories(id,company_id,name,created_at,updated_at,version,is_deleted) VALUES('cat_p','B','Ana',1,1,1,0);" +
                                "INSERT INTO material_categories(id,company_id,name,parent_id,created_at,updated_at,version,is_deleted) VALUES('cat_c','B','Alt','cat_p',1,1,1,0);";
                c.ExecuteNonQuery();
            }
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "INSERT INTO server_changes(company_id,operation_id,entity_type,entity_id,payload_json,created_at) VALUES('B','op1','material','m1','{}',1);";
                c.ExecuteNonQuery();
            }
            using (var c = conn.CreateCommand()) { c.CommandText = "SELECT MAX(seq) FROM server_changes;"; srcSeq = Convert.ToInt64(c.ExecuteScalar()); }
        }

        // 2) HEDEF: temiz Neon şeması (53 migration).
        var pg = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        using (var conn = pg.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DROP SCHEMA public CASCADE; CREATE SCHEMA public;";
            cmd.ExecuteNonQuery();
        }
        new MigrationRunner(pg).Run();

        // 3) KOPYALA.
        var report = SqliteToPgCopier.Copy(sqlite, pg);
        Assert.True(report.TotalRows > 0);

        // 4) Satır sayıları tablo tablo eşleşmeli (kritik tablolar + company_id'siz çocuklar).
        foreach (var t in new[] { "companies", "users", "roles", "user_roles", "materials", "vehicles",
                                  "material_categories", "vehicle_maintenances", "maintenance_materials",
                                  "material_requests", "material_request_items", "stock_movements", "server_changes" })
            Assert.True(Count(sqlite, t) == Count(pg, t), $"{t}: SQLite={Count(sqlite, t)} PG={Count(pg, t)}");

        // 5) Nokta kontrol: malzeme kimliği taşındı.
        Assert.Equal(1, (int)CountWhere(pg, "materials", "id", m));

        // 6) IDENTITY: kopyalanan seq korundu + sequence ilerledi → sonraki doğal ekleme çakışmaz (max+1).
        using (var conn = pg.Create())
        {
            using var c = conn.CreateCommand();
            c.CommandText = "INSERT INTO server_changes(company_id,operation_id,entity_type,entity_id,payload_json,created_at) " +
                            "VALUES('B','op2','material','m2','{}',2) RETURNING seq;";
            var newSeq = Convert.ToInt64(c.ExecuteScalar());
            Assert.True(newSeq > srcSeq, $"yeni seq ({newSeq}) kopyalanan seq'ten ({srcSeq}) büyük olmalı → sequence ilerlemedi");
        }
    }

    private static long CountWhere(IDbConnectionFactory f, string table, string col, string val)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\" WHERE \"{col}\"=@v;";
        cmd.AddWithValue("@v", val);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_sqlitePath); } catch { }
    }
}
