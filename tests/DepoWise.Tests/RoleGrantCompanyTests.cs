using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ A1 (ADR-116) — ROL YETKİ TAVANI FİRMA BAZLI ═══
///
/// Eskiden <c>role_grant_limits</c> platform geneliydi: bir firmada yapılan değişiklik <b>bütün
/// firmaları</b> etkiliyordu (kaydetme tabloyu komple siliyordu). Bu testler yeni davranışı ve
/// migration'ın <b>veri kaybetmediğini</b> kilitler.
/// </summary>
public class RoleGrantCompanyTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public RoleGrantCompanyTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rgc_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Scalar(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 1 · ŞEMA
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>R1 — Tablo firma kolonu taşır ve benzersizlik firma ekseniyle birlikte kurulur:
    /// AYNI (rol, modül) çifti FARKLI firmalarda yan yana durabilmelidir.</summary>
    [Fact]
    public void R1_Tablo_Firma_Bazli()
    {
        using var conn = _factory.Create();
        Assert.True(DbIntrospect.ColumnExists(conn, null, "role_grant_limits", "company_id"));

        Sql("INSERT INTO role_grant_limits(id,company_id,role_key,module_key,created_at) VALUES('x1','A','role-staff','fuel',1);");
        Sql("INSERT INTO role_grant_limits(id,company_id,role_key,module_key,created_at) VALUES('x2','B','role-staff','fuel',1);");
        Assert.Equal(2, Scalar("SELECT COUNT(*) FROM role_grant_limits;"));

        // Aynı firmada tekrar YASAK (benzersizlik korunuyor).
        Assert.ThrowsAny<Exception>(() =>
            Sql("INSERT INTO role_grant_limits(id,company_id,role_key,module_key,created_at) VALUES('x3','A','role-staff','fuel',1);"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 2 · MIGRATION — VERİ KOPYALANARAK TAŞINIR
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ R2 — Migration 072, v71 biçimindeki (firma-üstü) satırları HER FİRMAYA kopyalar.
    /// Kullanıcının kararı buydu: "mevcut ayarlar her firmaya kopyalanarak taşınsın".
    /// Eski tablo elle kurulup migration doğrudan çalıştırılır (gerçek yükseltmenin aynısı).
    /// </summary>
    [Fact]
    public void R2_Migration_Eski_Kisitlari_Her_Firmaya_Kopyalar()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_rg71_" + Guid.NewGuid().ToString("N") + ".db");
        var f = new SqliteConnectionFactory(yol);
        try
        {
            using (var conn = f.Create())
            {
                using var tx = conn.BeginTransaction();
                void E(string sql)
                {
                    using var c = conn.CreateCommand();
                    c.Transaction = tx; c.CommandText = sql; c.ExecuteNonQuery();
                }
                // v71 durumu: firma kolonu YOK
                E(@"CREATE TABLE role_grant_limits (
                        id TEXT PRIMARY KEY, role_key TEXT NOT NULL, module_key TEXT NOT NULL,
                        created_at BIGINT NOT NULL, UNIQUE(role_key, module_key));");
                E("CREATE TABLE companies (id TEXT PRIMARY KEY, name TEXT NOT NULL);");
                E("INSERT INTO companies(id,name) VALUES('A','A'),('B','B'),('C','C');");
                E("INSERT INTO role_grant_limits(id,role_key,module_key,created_at) VALUES" +
                  "('g1','role-staff','fuel',7),('g2','role-staff','reports',7);");
                tx.Commit();
            }

            using (var conn = f.Create())
            {
                using var tx = conn.BeginTransaction();
                new Migration072_RoleGrantLimitsCompany().Up(conn, tx);
                tx.Commit();
            }

            using (var conn = f.Create())
            {
                Assert.True(DbIntrospect.ColumnExists(conn, null, "role_grant_limits", "company_id"));
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM role_grant_limits;";
                Assert.Equal(6L, Convert.ToInt64(cmd.ExecuteScalar()));   // 2 kısıt × 3 firma

                // Her firma AYNI iki kısıtla başlar → görünen davranış yükseltmeden ÖNCEKİYLE aynı.
                foreach (var firma in new[] { "A", "B", "C" })
                {
                    using var c2 = conn.CreateCommand();
                    c2.CommandText = $"SELECT COUNT(*) FROM role_grant_limits WHERE company_id='{firma}';";
                    Assert.Equal(2L, Convert.ToInt64(c2.ExecuteScalar()));
                }

                // created_at korunur (iz kaybolmaz).
                using var c3 = conn.CreateCommand();
                c3.CommandText = "SELECT COUNT(*) FROM role_grant_limits WHERE created_at=7;";
                Assert.Equal(6L, Convert.ToInt64(c3.ExecuteScalar()));
            }

            // Idempotent: ikinci kez çalışması hiçbir şeyi değiştirmez.
            using (var conn = f.Create())
            {
                using var tx = conn.BeginTransaction();
                new Migration072_RoleGrantLimitsCompany().Up(conn, tx);
                tx.Commit();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM role_grant_limits;";
                Assert.Equal(6L, Convert.ToInt64(cmd.ExecuteScalar()));
            }
        }
        finally
        {
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { File.Delete(yol); } catch { }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // 3 · DAVRANIŞ — FİRMALAR BİRBİRİNİ ETKİLEMEZ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ R3 — A firmasında bir ekranı role kapatmak B firmasını ETKİLEMEZ. Eski davranışta
    /// kaydetme tabloyu komple sildiği için B'nin kısıtları da uçuyordu.
    /// </summary>
    [Fact]
    public void R3_Bir_Firmanin_Tavani_Digerini_Etkilemez()
    {
        var users = new UserService(_factory, _clock);
        var auth = new AuthService(_factory, _clock);
        var roles = new RoleGrantService(_factory, _clock);

        users.EnsureInitialAdmin("A", "rootA", "root123", RoleKeys.SuperAdmin);
        users.EnsureInitialAdmin("B", "rootB", "root123", RoleKeys.SuperAdmin);
        var suA = auth.Login("A", "rootA", "root123").Session!;
        var suB = auth.Login("B", "rootB", "root123").Session!;

        // A: yakıt Personel'e kapalı. B: dokunulmadı.
        roles.SetMatrix(suA, new Dictionary<string, IReadOnlyList<string>> { [RoleKeys.Staff] = new[] { "fuel" } });

        var perA = users.CreateUser(suA, new NewUser("perA", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "A"));
        var perB = users.CreateUser(suB, new NewUser("perB", "p12345", null, new[] { RoleKeys.Staff }, CompanyId: "B"));

        using var conn = _factory.Create();
        Assert.Contains("fuel", RoleGrantService.BlockedForUser(conn, null, "A", perA));
        Assert.DoesNotContain("fuel", RoleGrantService.BlockedForUser(conn, null, "B", perB));   // ⭐ sızıntı yok

        // B kendi tavanını kurar → A'nınki BOZULMAZ.
        roles.SetMatrix(suB, new Dictionary<string, IReadOnlyList<string>> { [RoleKeys.Staff] = new[] { "reports" } });
        Assert.Contains("fuel", RoleGrantService.BlockedForUser(conn, null, "A", perA));         // ⭐ hâlâ duruyor
        Assert.Contains("reports", RoleGrantService.BlockedForUser(conn, null, "B", perB));
        Assert.DoesNotContain("fuel", RoleGrantService.BlockedForUser(conn, null, "B", perB));
    }

    /// <summary>R4 — Yönetim ekranı da firma bazlı okur: A'da kapalı olan B'de açık görünür.</summary>
    [Fact]
    public void R4_Yonetim_Ekrani_Firma_Bazli_Okur()
    {
        var users = new UserService(_factory, _clock);
        var auth = new AuthService(_factory, _clock);
        var roles = new RoleGrantService(_factory, _clock);

        users.EnsureInitialAdmin("A", "rootA", "root123", RoleKeys.SuperAdmin);
        Sql("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('B','B',1,1,1,0);");
        var suA = auth.Login("A", "rootA", "root123").Session!;

        roles.SetMatrix(suA, new Dictionary<string, IReadOnlyList<string>> { [RoleKeys.Staff] = new[] { "fuel" } });

        bool Kapali(string companyId)
            => roles.GetControl(suA, companyId)
                    .Single(r => r.ModuleKey == "fuel")
                    .Cells.Single(c => c.RoleKey == RoleKeys.Staff).Blocked;

        Assert.True(Kapali("A"));
        Assert.False(Kapali("B"));   // ⭐ başka firmanın tavanı görünmez

        // Süper admin B'yi de yönetebilir; A'ya dokunmaz.
        roles.SetMatrix(suA, new Dictionary<string, IReadOnlyList<string>> { [RoleKeys.Staff] = new[] { "fuel" } }, "B");
        Assert.True(Kapali("B"));
        Assert.True(Kapali("A"));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
