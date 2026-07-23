using System.Data.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Migration035: eski "btn-approve" özel butonu → "request_approval" modülü (view+edit) taşınır,
/// eski buton izni temizlenir. Idempotent (iki kez çalışsa tek satır).</summary>
public class Migration035Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public Migration035Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_m035_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    [Fact]
    public void BtnApprove_RequestApprovalModulune_Tasinir_Idempotent()
    {
        using var conn = _factory.Create();
        long now = 1_700_000_000_000;
        var companyId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid().ToString("N");
        using (var tx = conn.BeginTransaction())
        {
            Exec(conn, tx, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,'F',@n,@n,1,0);", ("@c", companyId), ("@n", now));
            Exec(conn, tx, "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) VALUES(@u,@c,'p','x',1,@n,@n,1,0);", ("@u", userId), ("@c", companyId), ("@n", now));
            // Eski onay yetkisi: btn-approve özel butonu
            Exec(conn, tx, "INSERT INTO user_button_permissions(id,company_id,user_id,button_key,created_at) VALUES(@i,@c,@u,'btn-approve',@n);", ("@i", Guid.NewGuid().ToString("N")), ("@c", companyId), ("@u", userId), ("@n", now));
            tx.Commit();
        }

        // İki kez uygula (idempotent olmalı)
        for (int i = 0; i < 2; i++)
            using (var tx = conn.BeginTransaction())
            {
                new Migration035_SplitRequestApproval().Up((DbConnection)conn, (DbTransaction)tx);
                tx.Commit();
            }

        // request_approval modülü view+edit olarak verildi
        Assert.Equal("1", Scalar(conn, "SELECT can_view FROM user_permissions WHERE user_id=@u AND module_key='request_approval';", ("@u", userId)));
        Assert.Equal("1", Scalar(conn, "SELECT can_edit FROM user_permissions WHERE user_id=@u AND module_key='request_approval';", ("@u", userId)));
        Assert.Equal("0", Scalar(conn, "SELECT can_create FROM user_permissions WHERE user_id=@u AND module_key='request_approval';", ("@u", userId)));
        // Tek satır (idempotent)
        Assert.Equal("1", Scalar(conn, "SELECT COUNT(*) FROM user_permissions WHERE user_id=@u AND module_key='request_approval';", ("@u", userId)));
        // Eski buton izni temizlendi
        Assert.Equal("0", Scalar(conn, "SELECT COUNT(*) FROM user_button_permissions WHERE button_key='btn-approve';"));
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
