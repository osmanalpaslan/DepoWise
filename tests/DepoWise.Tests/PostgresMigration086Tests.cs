using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ 7b — Migration086 POSTGRESQL KANITI (PK-F9, ADR-191) ═══
///
/// SQLite kanıtı <see cref="EkipmanBakimTests"/>/<see cref="EkipmanMigrationVeIsEmriTests"/>'tedir.
/// Burada AYNI migration ve sözleşmenin GERÇEK PostgreSQL'de de geçerli olduğu kanıtlanır:
///  • 4 tablo + firma kapsamlı benzersiz indeks kurulur,
///  • ARAÇ bakım şeması DEĞİŞMEZ (<c>vehicle_id</c> hâlâ NOT NULL, <c>equipment_id</c> eklenmemiş),
///  • servis sözleşmesi (idempotency, tenant, iptal) PG lehçesinde de aynı davranır,
///  • ROLLBACK: bozuk 086 uygulanırsa şema 85'te kalır.
///
/// ⚠️ Yalnız izole test PG'sinde (PostgresTestGuard). <b>Migration086 PRODUCTION'DA ÇALIŞTIRILMADI</b>.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresMigration086Tests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    [SkippableFact]
    public void PG_EQ01_Migration086_Semayi_Kurar_Arac_Semasi_Degismez()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        using var conn = factory.Create();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' AND table_name IN " +
                "('equipment_maintenances','equipment_maintenance_materials','equipment_inspections','maintenance_definition_equipment');";
            Assert.Equal(4L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        // ⭐ ARAÇ ŞEMASI DEĞİŞMEDİ: vehicle_id hâlâ NOT NULL ve equipment_id eklenmemiş.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' " +
                "AND table_name IN ('vehicle_maintenances','vehicle_inspections','maintenance_definition_vehicles') " +
                "AND column_name='equipment_id';";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT is_nullable FROM information_schema.columns WHERE table_schema='public' " +
                "AND table_name='vehicle_maintenances' AND column_name='vehicle_id';";
            Assert.Equal("NO", cmd.ExecuteScalar() as string);
        }

        // İdempotency indeksi FİRMA KAPSAMLI (FIN-B1 sözleşmesi).
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname='ux_equipment_maintenances_op';";
            var def = cmd.ExecuteScalar() as string;
            Assert.NotNull(def);
            Assert.Contains("UNIQUE", def!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("company_id", def!);
        }
    }

    [SkippableFact]
    public void PG_EQ02_Servis_Sozlesmesi_PostgreSQLde_Gecerli()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        Firma(factory, "PGE");
        Firma(factory, "PGF");
        var users = new UserService(factory);
        var aId = users.EnsureInitialAdmin("PGE", "pg_e_admin", "admin123", RoleKeys.CompanyAdmin);
        var bId = users.EnsureInitialAdmin("PGF", "pg_f_admin", "admin123", RoleKeys.CompanyAdmin);
        var a = new SessionContext(aId, "PGE", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var b = new SessionContext(bId, "PGF", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var ekipmanA = Ekipman(factory, "PGE", "EKP-A");
        var ekipmanB = Ekipman(factory, "PGF", "EKP-B");
        var defs = new MaintenanceDefinitionService(factory);
        var defA = defs.Create(a, new NewMaintenanceDefinition("PG Bakım", 30m, "day", null, null));

        var svc = new EquipmentMaintenanceService(factory);
        var id1 = svc.Save(a, new NewEquipmentMaintenance(ekipmanA, defA), "pg-op");
        var id2 = svc.Save(a, new NewEquipmentMaintenance(ekipmanA, defA), "pg-op");
        Assert.Equal(id1, id2);                                    // idempotent

        var defB = defs.Create(b, new NewMaintenanceDefinition("PG B", 10m, "day", null, null));
        Assert.NotEqual(id1, svc.Save(b, new NewEquipmentMaintenance(ekipmanB, defB), "pg-op"));  // firma kapsamlı

        Assert.Throws<ForbiddenException>(() => svc.Save(a, new NewEquipmentMaintenance(ekipmanB, defA), "pg-idor"));
        Assert.Empty(svc.List(b).Where(x => x.Id == id1));         // tenant

        svc.Cancel(a, id1, "pg iptal");
        Assert.True(svc.List(a).Single(x => x.Id == id1).IsCancelled);

        // Tanım ↔ ekipman eşlemesi PG'de de çalışır ve araç kapsamına dokunmaz.
        defs.SetEquipment(a, defA, new[] { ekipmanA });
        Assert.Equal(new[] { ekipmanA }, defs.GetEquipmentIds(a, defA));
        Assert.Empty(defs.GetVehicleIds(a, defA));
    }

    [SkippableFact]
    public void PG_EQ03_Migration_Basarisiz_Olursa_Sema_85te_Kalir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory, MigrationCatalog.All().Where(m => m.Version <= 85)).Run();
        Assert.Equal(85L, Sema(factory));

        Assert.ThrowsAny<Exception>(() =>
            new MigrationRunner(factory, new IMigration[] { new BozukPgMigration86() }).Run());

        Assert.Equal(85L, Sema(factory));
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' " +
            "AND table_name IN ('equipment_maintenances','equipment_inspections','maintenance_definition_equipment');";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    private sealed class BozukPgMigration86 : IMigration
    {
        public int Version => 86;
        public string Name => "bozuk_test";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE equipment_maintenances(id TEXT); CREATE INDEX x ON olmayan_tablo(id);";
            cmd.ExecuteNonQuery();
        }
    }

    private static void Firma(PostgresMigrationTests.NpgsqlTestFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0) " +
            "ON CONFLICT (id) DO NOTHING;";
        var p = cmd.CreateParameter(); p.ParameterName = "@i"; p.Value = id; cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    private static string Ekipman(PostgresMigrationTests.NpgsqlTestFactory f, string co, string kod)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO equipment(id,company_id,code,name,status,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@k,@k,'active',1,1,1,0);";
        foreach (var (n, v) in new[] { ("@i", id), ("@c", co), ("@k", kod) })
        {
            var p = cmd.CreateParameter(); p.ParameterName = n; p.Value = v; cmd.Parameters.Add(p);
        }
        cmd.ExecuteNonQuery();
        return id;
    }

    private static long Sema(PostgresMigrationTests.NpgsqlTestFactory f)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
