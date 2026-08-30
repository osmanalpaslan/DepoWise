using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — MIGRATION084 YÜKSELTME/ROLLBACK + LOOKUP AYNASI ═══
///
/// Kilitlenenler:
///  • 083 → 084 GERÇEK yükseltme provası; mevcut veri KORUNUR.
///  • Migration başarısız olursa şema <b>83'te kalır</b> (runner tek transaction).
///  • Ayna sözleşmesi: <c>teams</c> masaüstüne <c>/api/lookups/sync</c> ile iner,
///    <c>BusinessSyncService.Tables</c>'a EKLENMEZ (iki senkron yolu birbirine karışmaz).
///  • <b>ESKİ İSTEMCİ:</b> Migration084 uygulanmamış yerel veritabanında ayna işlemi tabloyu
///    bulamaz ve SESSİZCE ATLAR — eski istemci bozulmaz.
/// </summary>
public class EkipMigrationVeAynaTests
{
    /// <summary>EKM01 — 083 → 084 yükseltmesi: yeni tablolar kurulur, mevcut veri korunur.</summary>
    [Fact]
    public void EKM01_Yukseltme_83ten84e_Mevcut_Veri_Korunur()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_ek83_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 83)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('UP','UP',1,1,1,0);";
                cmd.ExecuteNonQuery();
            }
            Assert.Equal(83L, Sema(f));
            Assert.Equal(0L, TabloVar(f, "teams"));

            var uygulanan = new MigrationRunner(f).Run();

            Assert.Contains(84, uygulanan);
            Assert.Equal(1L, TabloVar(f, "teams"));
            Assert.Equal(1L, TabloVar(f, "team_members"));
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM companies WHERE id='UP';";
                Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));   // mevcut veri KORUNDU
            }
        }
        finally { Temizle(yol); }
    }

    /// <summary>EKM02 — ROLLBACK: 084 başarısız olursa şema 83'te kalır ve hiçbir tablo oluşmaz.</summary>
    [Fact]
    public void EKM02_Migration_Basarisiz_Olursa_Sema_83te_Kalir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_ekrb_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 83)).Run();
            Assert.Equal(83L, Sema(f));

            Assert.ThrowsAny<Exception>(() =>
                new MigrationRunner(f, new IMigration[] { new BozukMigration84() }).Run());

            Assert.Equal(83L, Sema(f));
            Assert.Equal(0L, TabloVar(f, "teams"));
            Assert.Equal(0L, TabloVar(f, "team_members"));
        }
        finally { Temizle(yol); }
    }

    /// <summary>EKM03 — Ekip tabloları İŞ SENKRONUNA (<c>BusinessSyncService.Tables</c>) EKLENMEZ.
    /// Ekip verisi sunucu otoriteli AYNADIR; iki yoldan birden akarsa çakışma/LWW sorusu doğardı.</summary>
    [Fact]
    public void EKM03_Ekip_Tablolari_Is_Senkronunda_Degil()
    {
        Assert.DoesNotContain("teams", DepoWise.Infrastructure.Sync.BusinessSyncService.Tables);
        Assert.DoesNotContain("team_members", DepoWise.Infrastructure.Sync.BusinessSyncService.Tables);
    }

    /// <summary>EKM04 — <b>ESKİ İSTEMCİ:</b> Migration084 uygulanmamış yerel veritabanında ekip
    /// tabloları YOKTUR; ayna tüketicisinin kullandığı <c>TableExists</c> kapısı bunu yakalar →
    /// işlem sessizce atlanır, eski istemci bozulmaz.</summary>
    [Fact]
    public void EKM04_Eski_Istemci_Ekip_Tablosuz_Calisir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_ekold_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 83)).Run();

            using var conn = f.Create();
            Assert.False(DbIntrospect.TableExists(conn, null, "teams"));
            Assert.False(DbIntrospect.TableExists(conn, null, "team_members"));

            // Aynı veritabanında eski istemcinin diğer tanımları çalışmaya devam eder.
            Assert.True(DbIntrospect.TableExists(conn, null, "branches"));
            Assert.True(DbIntrospect.TableExists(conn, null, "custom_report_defs"));
        }
        finally { Temizle(yol); }
    }

    /// <summary>EKM05 — Ayna DEĞİŞTİRME (replace) semantiğiyle çalışır: sunucuda silinen ekip/üye
    /// yerelde de düşer ve sunucu KİMLİKLERİ korunur. (Masaüstü tüketicisinin yaptığı işin
    /// veritabanı seviyesindeki karşılığı — <c>DesktopServices</c> olmadan doğrulanır.)</summary>
    [Fact]
    public void EKM05_Ayna_Replace_Semantigi_Sunucu_Kimligini_Korur()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_ekmir_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f).Run();
            Ekle(f, "T1", "U1");
            Ekle(f, "T2", "U2");
            Assert.Equal(2L, Say(f, "SELECT COUNT(*) FROM teams WHERE company_id='C';"));

            // Sunucu artık YALNIZ T1 gönderiyor → önce üyeler, sonra ekipler silinir; sonra yazılır.
            using (var conn = f.Create())
            using (var tx = conn.BeginTransaction())
            {
                Calistir(conn, tx, "DELETE FROM team_members WHERE company_id='C';");
                Calistir(conn, tx, "DELETE FROM teams WHERE company_id='C';");
                tx.Commit();
            }
            Ekle(f, "T1", "U1");

            Assert.Equal(1L, Say(f, "SELECT COUNT(*) FROM teams WHERE company_id='C';"));
            Assert.Equal(1L, Say(f, "SELECT COUNT(*) FROM teams WHERE id='T1';"));   // sunucu kimliği korundu
            Assert.Equal(0L, Say(f, "SELECT COUNT(*) FROM teams WHERE id='T2';"));
            Assert.Equal(1L, Say(f, "SELECT COUNT(*) FROM team_members WHERE team_id='T1';"));
        }
        finally { Temizle(yol); }
    }

    // ── yardımcılar ────────────────────────────────────────────────────────────────────────

    private sealed class BozukMigration84 : IMigration
    {
        public int Version => 84;
        public string Name => "bozuk_test";
        public void Up(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "CREATE TABLE teams(id TEXT); CREATE TABLE team_members(id TEXT); " +
                              "CREATE INDEX x ON olmayan_tablo(id);";
            cmd.ExecuteNonQuery();
        }
    }

    private static void Ekle(SqliteConnectionFactory f, string teamId, string userId)
    {
        using var conn = f.Create();
        using var tx = conn.BeginTransaction();
        Calistir(conn, tx,
            "INSERT OR IGNORE INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C','C',1,1,1,0);");
        Calistir(conn, tx,
            $"INSERT INTO teams(id,company_id,name,lead_user_id,is_active,created_at,updated_at,version,is_deleted) " +
            $"VALUES('{teamId}','C','{teamId}',NULL,1,1,1,1,0);");
        Calistir(conn, tx,
            $"INSERT INTO team_members(id,company_id,team_id,user_id,is_lead,created_at,updated_at,version,is_deleted) " +
            $"VALUES('{teamId}-m','C','{teamId}','{userId}',0,1,1,1,0);");
        tx.Commit();
    }

    private static void Calistir(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long Say(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long Sema(SqliteConnectionFactory f)
        => Say(f, "SELECT MAX(version) FROM schema_migrations;");

    private static long TabloVar(SqliteConnectionFactory f, string tablo)
        => Say(f, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tablo}';");

    private static void Temizle(string yol)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(yol); } catch { }
    }
}
