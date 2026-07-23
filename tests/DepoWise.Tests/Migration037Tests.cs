using System.Data.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Migration037: company_grant_limits'e 'level' kolonu + eski dinamik global kilitler her firmaya
/// 'admin' düzeyli satıra taşınır, global ayar silinir. Idempotent.</summary>
public class Migration037Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public Migration037Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_m037_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    [Fact]
    public void GlobalKilit_HerFirmaya_AdminDuzeyi_Tasinir_Idempotent()
    {
        using var conn = _factory.Create();
        long now = 1_700_000_000_000;
        var cA = Guid.NewGuid().ToString("N");
        var cB = Guid.NewGuid().ToString("N");
        using (var tx = conn.BeginTransaction())
        {
            Exec(conn, tx, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,'A',@n,@n,1,0);", ("@c", cA), ("@n", now));
            Exec(conn, tx, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,'B',@n,@n,1,0);", ("@c", cB), ("@n", now));
            // Eski dinamik global kilit: fuel + reports
            Exec(conn, tx, "INSERT INTO app_settings(id,company_id,setting_key,setting_value,updated_at) VALUES(@i,NULL,'global_grant_limits','fuel,reports',@n);",
                ("@i", Guid.NewGuid().ToString("N")), ("@n", now));
            tx.Commit();
        }

        // İki kez uygula (idempotent)
        for (int i = 0; i < 2; i++)
            using (var tx = conn.BeginTransaction())
            {
                new Migration037_GrantLevel().Up((DbConnection)conn, (DbTransaction)tx);
                tx.Commit();
            }

        // Her firma her modül için tek 'admin' satırı aldı
        foreach (var c in new[] { cA, cB })
            foreach (var m in new[] { "fuel", "reports" })
            {
                Assert.Equal("admin", Scalar(conn, "SELECT level FROM company_grant_limits WHERE company_id=@c AND module_key=@m;", ("@c", c), ("@m", m)));
                Assert.Equal("1", Scalar(conn, "SELECT COUNT(*) FROM company_grant_limits WHERE company_id=@c AND module_key=@m;", ("@c", c), ("@m", m)));
            }
        // Global ayar silindi
        Assert.Equal("0", Scalar(conn, "SELECT COUNT(*) FROM app_settings WHERE company_id IS NULL AND setting_key='global_grant_limits';"));
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static string Scalar(DbConnection conn, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.AddWithValue(n, v);
        return Convert.ToString(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
