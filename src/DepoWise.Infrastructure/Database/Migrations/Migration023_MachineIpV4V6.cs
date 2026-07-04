using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Makine IP'sini IPv4 ve IPv6 olarak AYRI tut. Her kayıt/heartbeat'te bağlanılan adresin ailesine göre
/// ilgili slot güncellenir (diğeri korunur) → makine hem v4 hem v6 ile bağlandıysa ikisi de görünür.
/// </summary>
public sealed class Migration023_MachineIpV4V6 : IMigration
{
    public int Version => 23;
    public string Name => "machine_ip_v4_v6";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE sync_devices ADD COLUMN ip_v4 TEXT;
ALTER TABLE sync_devices ADD COLUMN ip_v6 TEXT;";
        cmd.ExecuteNonQuery();
    }
}
