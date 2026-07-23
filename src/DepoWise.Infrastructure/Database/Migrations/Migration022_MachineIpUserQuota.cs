using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Makine kaydına IP adresi (izleme) + firmaya max kullanıcı sayısı (kullanıcı/admin kotası temeli).
/// max_users = 0 → sınırsız (varsayılan). Admin sınırı: firma max kullanıcısının 2/10'u (ileride uygulanır).
/// </summary>
public sealed class Migration022_MachineIpUserQuota : IMigration
{
    public int Version => 22;
    public string Name => "machine_ip_user_quota";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE sync_devices ADD COLUMN ip_address TEXT;
ALTER TABLE companies ADD COLUMN max_users BIGINT NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }
}
