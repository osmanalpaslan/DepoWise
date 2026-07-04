using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Migration029 (2-rol modeli): legacy roldeki kullanıcılar Personel'e taşınır, izinler korunur.</summary>
public class Migration029Tests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public Migration029Tests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_m029_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
    }

    [Fact]
    public void LegacyRolluKullanici_PersoneleTasinir_EskiRolSoftDelete()
    {
        using var conn = _factory.Create();

        // Migration029 taze DB'de zaten çalıştı; legacy rolü GERİ ekleyip senaryoyu kur, sonra tekrar uygula.
        long now = 1_700_000_000_000;
        var legacyRoleId = Guid.NewGuid().ToString("N");
        var userId = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid().ToString("N");
        using (var tx = conn.BeginTransaction())
        {
            Exec(conn, tx, @"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES($c,'Firma',$n,$n,1,0);", ("$c", companyId), ("$n", now));
            Exec(conn, tx, @"INSERT INTO roles(id,company_id,role_key,name,is_system,created_at,updated_at,version,is_deleted)
VALUES($id,NULL,$k,'Depo Kullanıcısı',1,$n,$n,1,0);", ("$id", legacyRoleId), ("$k", RoleKeys.Warehouse), ("$n", now));
            Exec(conn, tx, @"INSERT INTO users(id,company_id,username,password_hash,full_name,is_active,created_at,updated_at,version,is_deleted)
VALUES($u,$c,'depocu','x','Depo',1,$n,$n,1,0);", ("$u", userId), ("$c", companyId), ("$n", now));
            Exec(conn, tx, "INSERT INTO user_roles(user_id,role_id) VALUES($u,$r);", ("$u", userId), ("$r", legacyRoleId));
            tx.Commit();
        }

        using (var tx = conn.BeginTransaction())
        {
            new Migration029_TwoRoleModel().Up((SqliteConnection)conn, (SqliteTransaction)tx);
            tx.Commit();
        }

        // Kullanıcı artık Personel (role-staff) rolünde, legacy rol kaydı yok.
        var roleKeys = Query(conn, @"SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$u;", ("$u", userId));
        Assert.Single(roleKeys);
        Assert.Equal(RoleKeys.Staff, roleKeys[0]);

        // Legacy rol soft-delete edildi.
        var del = Query(conn, "SELECT is_deleted FROM roles WHERE id=$id;", ("$id", legacyRoleId));
        Assert.Equal("1", del[0]);
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private static List<string> Query(SqliteConnection conn, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Convert.ToString(r.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? "");
        return list;
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
