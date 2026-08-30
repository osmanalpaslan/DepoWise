using DepoWise.Application.Security;
using DepoWise.Application.Teams;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Teams;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — Migration084 POSTGRESQL KANITI ═══
///
/// SQLite kanıtı <see cref="EkipTanimiTests"/> ve <see cref="EkipMigrationVeAynaTests"/>'tedir; burada
/// AYNI migration'ın GERÇEK PostgreSQL'de çalıştığı kanıtlanır:
///  • İki tablo kurulur; <c>branch_id</c> YOKTUR (İK-8) ve <c>users</c> DEĞİŞMEMİŞTİR (PK-EK-02).
///  • Aktif üyelik benzersizliği KISMİ indeksle (<c>WHERE is_deleted = 0</c>) PG'de de zorlanır.
///  • Servis sözleşmesi (tenant izolasyonu, çoklu üyelik, lider-üyelik kuralı) PG lehçesinde de geçerli.
///  • ROLLBACK: bozuk bir 084 uygulanırsa şema <b>83'te kalır</b>.
///
/// ⚠️ Yalnız izole test PG'sinde (PostgresTestGuard çift kilidi: onay değişkeni + DB adında "test" +
/// boş şema + boyut tavanı). <b>Migration084 PRODUCTION'DA ÇALIŞTIRILMADI</b> — canlı şema 83.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresMigration084Tests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    [SkippableFact]
    public void PG_EK01_Migration084_PostgreSQLde_Tablolari_Ve_Kismi_Indeksi_Kurar()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();   // 084 dahil tüm katalog

        using (var conn = factory.Create())
        {
            // 1) İki tablo kuruldu.
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM information_schema.tables " +
                    "WHERE table_schema='public' AND table_name IN ('teams','team_members');";
                Assert.Equal(2L, Convert.ToInt64(cmd.ExecuteScalar()));
            }

            // 2) İK-8: şube kolonu YOK. PK-EK-02: users'a hiyerarşi kolonu EKLENMEDİ.
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema='public' AND " +
                    "((table_name IN ('teams','team_members') AND column_name='branch_id') OR " +
                    " (table_name='users' AND column_name IN ('manager_id','parent_user_id','is_manager','manager_user_id')));";
                Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
            }

            // 3) Aktif üyelik benzersizliği KISMİ indekstir (koşulu gerçekten taşır).
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT indexdef FROM pg_indexes WHERE indexname='ux_team_members_active';";
                var def = cmd.ExecuteScalar() as string;
                Assert.NotNull(def);
                Assert.Contains("UNIQUE", def!, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("is_deleted", def!, StringComparison.OrdinalIgnoreCase);
            }
        }

        // 4) Servis sözleşmesi PG lehçesinde: tenant izolasyonu + çoklu üyelik + lider kuralı.
        var users = new UserService(factory);
        Firma(factory, "PGA");
        Firma(factory, "PGB");
        var aId = users.EnsureInitialAdmin("PGA", "pg_admin_a", "admin123", RoleKeys.CompanyAdmin);
        var bId = users.EnsureInitialAdmin("PGB", "pg_admin_b", "admin123", RoleKeys.CompanyAdmin);
        var a = new SessionContext(aId, "PGA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var b = new SessionContext(bId, "PGB", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var svc = new TeamService(factory);
        var e1 = svc.Create(a, "PG Ekip 1");
        var e2 = svc.Create(a, "PG Ekip 2");

        svc.AddMember(a, e1, aId);
        svc.AddMember(a, e2, aId);                                    // İK-1: çoklu üyelik serbest
        Assert.Equal(2, svc.TeamsOfUser(a, aId).Count);
        Assert.Throws<ArgumentException>(() => svc.AddMember(a, e1, aId));   // aynı ekibe iki kez YOK

        Assert.Throws<ForbiddenException>(() => svc.AddMember(a, e1, bId));  // başka firmanın kullanıcısı
        Assert.Null(svc.ById(b, e1));                                        // tenant izolasyonu
        Assert.Throws<ForbiddenException>(() => svc.Delete(b, e1));          // IDOR kapalı

        Assert.Throws<ArgumentException>(() => svc.Update(a, e1, "PG Ekip 1", bId, true));  // lider üye değil
        svc.Update(a, e1, "PG Ekip 1", aId, true);
        Assert.Equal(aId, svc.ById(a, e1)!.LeadUserId);

        // 5) Yumuşak silme sonrası aynı kullanıcı YENİDEN eklenebilir (kısmi indeks doğru çalışıyor).
        svc.RemoveMember(a, e2, aId);
        svc.AddMember(a, e2, aId);
        Assert.Single(svc.Members(a, e2));
    }

    /// <summary>ROLLBACK — bozuk 084 uygulanırsa PostgreSQL'de de şema 83'te kalır ve tablo oluşmaz.</summary>
    [SkippableFact]
    public void PG_EK02_Migration_Basarisiz_Olursa_Sema_83te_Kalir()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory, MigrationCatalog.All().Where(m => m.Version <= 83)).Run();
        Assert.Equal(83L, Sema(factory));

        Assert.ThrowsAny<Exception>(() =>
            new MigrationRunner(factory, new IMigration[] { new BozukPgMigration84() }).Run());

        Assert.Equal(83L, Sema(factory));
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM information_schema.tables " +
            "WHERE table_schema='public' AND table_name IN ('teams','team_members');";
        Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    private sealed class BozukPgMigration84 : IMigration
    {
        public int Version => 84;
        public string Name => "bozuk_test";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE teams(id TEXT); CREATE INDEX x ON olmayan_tablo(id);";
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

    private static long Sema(PostgresMigrationTests.NpgsqlTestFactory f)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
