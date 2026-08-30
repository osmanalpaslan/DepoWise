using DepoWise.Application.Approvals;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Approvals;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 — Migration085 POSTGRESQL KANITI ═══
///
/// SQLite kanıtı <see cref="HiyerarsiTests"/>/<see cref="OnayZinciriTests"/>'tedir; burada AYNI
/// migration ve AYNI sözleşmenin GERÇEK PostgreSQL'de de geçerli olduğu kanıtlanır:
///  • Üç tablo + kısmi benzersiz indeksler PG'de kurulur.
///  • Hiyerarşi kuralları (derinlik/döngü/tekil aktif üst) PG lehçesinde de zorlanır.
///  • Snapshot ve adım sahipliği PG'de de aynı davranır.
///  • ROLLBACK: bozuk 085 uygulanırsa şema 84'te kalır.
///
/// ⚠️ Yalnız izole test PG'sinde (PostgresTestGuard çift kilidi). <b>Migration085 PRODUCTION'DA
/// ÇALIŞTIRILMADI</b> — canlı şema 83.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresMigration085Tests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    [SkippableFact]
    public void PG_ON01_Migration085_Semayi_Ve_Kismi_Indeksleri_Kurar()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        using var conn = factory.Create();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' " +
                "AND table_name IN ('user_hierarchy','approval_instance','approval_step');";
            Assert.Equal(3L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        // users DEĞİŞMEDİ (PK-EK-02) ve purchase_orders'a onay kolonu EKLENMEDİ (ADR-188 §2).
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND " +
                "((table_name='users' AND column_name IN ('manager_id','parent_user_id','is_manager','manager_user_id')) OR " +
                " (table_name='purchase_orders' AND column_name IN ('approval_status','approver_id','approval_instance_id')));";
            Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
        }

        // Kısmi benzersiz indeksler PG'de gerçekten KOŞULLU kuruldu.
        foreach (var (ix, kosul) in new[]
                 {
                     ("ux_user_hierarchy_active", "is_deleted"),
                     ("ux_approval_instance_open", "pending"),
                     ("ux_approval_step_no", "step_no"),
                 })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT indexdef FROM pg_indexes WHERE indexname='{ix}';";
            var def = cmd.ExecuteScalar() as string;
            Assert.NotNull(def);
            Assert.Contains("UNIQUE", def!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(kosul, def!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [SkippableFact]
    public void PG_ON02_Hiyerarsi_Ve_Onay_Sozlesmesi_PostgreSQLde_Gecerli()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        Firma(factory, "PGH");
        var users = new UserService(factory);
        var adminId = users.EnsureInitialAdmin("PGH", "pg_h_admin", "admin123", RoleKeys.CompanyAdmin);
        var admin = new SessionContext(adminId, "PGH", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var b = Kullanici(factory, "PGH", "pg_b");
        var c = Kullanici(factory, "PGH", "pg_c");
        var d = Kullanici(factory, "PGH", "pg_d");
        var e = Kullanici(factory, "PGH", "pg_e");

        var hier = new UserHierarchyService(factory);
        hier.SetManager(admin, adminId, b);
        hier.SetManager(admin, b, c);
        hier.SetManager(admin, c, d);
        Assert.Equal(new[] { b, c, d }, hier.ResolveChain(admin, adminId));          // 4 düğüm geçerli

        Assert.Throws<ArgumentException>(() => hier.SetManager(admin, d, e));        // 5. seviye
        Assert.Throws<ArgumentException>(() => hier.SetManager(admin, d, adminId));  // döngü
        Assert.Throws<ArgumentException>(() => hier.SetManager(admin, adminId, adminId)); // self

        // Tekil aktif üst: ikinci atama öncekini kapatır.
        hier.SetManager(admin, adminId, c);
        Assert.Equal(c, hier.ManagerOf(admin, adminId));

        // Onay motoru PG'de: snapshot + adım sahipliği + eşzamanlılık koruması.
        var appr = new ApprovalService(factory);
        appr.Register(ApprovalEntityTypes.PurchaseOrder, (_, _, _, _, _, _, _, _) => { });

        string instanceId;
        using (var conn = factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            instanceId = appr.Start(conn, tx, admin, ApprovalEntityTypes.PurchaseOrder, "PG-PO-1", adminId, 1000)!;
            tx.Commit();
        }
        Assert.NotNull(instanceId);
        var adimlar = appr.Steps(admin, instanceId);
        Assert.Equal(2, adimlar.Count);                                              // c → d
        Assert.Equal(c, adimlar[0].ApproverUserId);

        // Hiyerarşi DEĞİŞSE de açık sürecin snapshot'ı DEĞİŞMEZ.
        hier.SetManager(admin, adminId, b);
        Assert.Equal(c, appr.Steps(admin, instanceId)[0].ApproverUserId);

        var cOturum = new SessionContext(c, "PGH", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("purchasing", true, true, true, true) }));
        appr.Approve(cOturum, adimlar[0].Id);
        Assert.Throws<InvalidOperationException>(() => appr.Approve(cOturum, adimlar[0].Id));  // ikinci kez YOK
    }

    [SkippableFact]
    public void PG_ON03_Migration_Basarisiz_Olursa_Sema_84te_Kalir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory, MigrationCatalog.All().Where(m => m.Version <= 84)).Run();
        Assert.Equal(84L, Sema(factory));

        Assert.ThrowsAny<Exception>(() =>
            new MigrationRunner(factory, new IMigration[] { new BozukPgMigration85() }).Run());

        Assert.Equal(84L, Sema(factory));
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public' " +
            "AND table_name IN ('user_hierarchy','approval_instance','approval_step');";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    private sealed class BozukPgMigration85 : IMigration
    {
        public int Version => 85;
        public string Name => "bozuk_test";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE user_hierarchy(id TEXT); CREATE INDEX x ON olmayan_tablo(id);";
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

    private static string Kullanici(PostgresMigrationTests.NpgsqlTestFactory f, string co, string username)
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
            "VALUES(@i,@c,@u,'x',1,1,1,1,0);";
        foreach (var (n, v) in new[] { ("@i", id), ("@c", co), ("@u", username) })
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
