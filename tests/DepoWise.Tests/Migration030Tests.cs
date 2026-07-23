using System.Data.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Migration030: vehicles→vehicle_templates, users→quota_monitor izinleri mevcut kullanıcıya kopyalanır.</summary>
public class Migration030Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public Migration030Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_m030_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    [Fact]
    public void UstModulIzni_YeniEkranaKopyalanir_Idempotent()
    {
        using var conn = _factory.Create();
        long now = 1_700_000_000_000;
        var companyId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid().ToString("N");
        using (var tx = conn.BeginTransaction())
        {
            Exec(conn, tx, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES($c,'F',$n,$n,1,0);", ("$c", companyId), ("$n", now));
            Exec(conn, tx, "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) VALUES($u,$c,'p','x',1,$n,$n,1,0);", ("$u", userId), ("$c", companyId), ("$n", now));
            // 'vehicles' view+create, 'users' view
            Exec(conn, tx, "INSERT INTO user_permissions(id,company_id,user_id,module_key,can_view,can_create,can_edit,can_delete,created_at,updated_at,version) VALUES($i,$c,$u,'vehicles',1,1,0,0,$n,$n,1);", ("$i", Guid.NewGuid().ToString("N")), ("$c", companyId), ("$u", userId), ("$n", now));
            Exec(conn, tx, "INSERT INTO user_permissions(id,company_id,user_id,module_key,can_view,can_create,can_edit,can_delete,created_at,updated_at,version) VALUES($i,$c,$u,'users',1,0,0,0,$n,$n,1);", ("$i", Guid.NewGuid().ToString("N")), ("$c", companyId), ("$u", userId), ("$n", now));
            tx.Commit();
        }

        // Migration030'u iki kez uygula (idempotent olmalı)
        for (int i = 0; i < 2; i++)
            using (var tx = conn.BeginTransaction())
            {
                new Migration030_SplitPermScreens().Up((DbConnection)conn, (DbTransaction)tx);
                tx.Commit();
            }

        Assert.Equal("1", Scalar(conn, "SELECT can_view FROM user_permissions WHERE user_id=$u AND module_key='vehicle_templates';", ("$u", userId)));
        Assert.Equal("1", Scalar(conn, "SELECT can_create FROM user_permissions WHERE user_id=$u AND module_key='vehicle_templates';", ("$u", userId)));
        Assert.Equal("1", Scalar(conn, "SELECT can_view FROM user_permissions WHERE user_id=$u AND module_key='quota_monitor';", ("$u", userId)));
        // İki kez çalıştı ama tek satır (idempotent)
        Assert.Equal("1", Scalar(conn, "SELECT COUNT(*) FROM user_permissions WHERE user_id=$u AND module_key='vehicle_templates';", ("$u", userId)));
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
