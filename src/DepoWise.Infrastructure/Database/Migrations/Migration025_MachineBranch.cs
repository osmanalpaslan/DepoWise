using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Makine kaydına ŞUBE (branch_id). Login'de seçilen şube heartbeat ile makineye yazılır → makine hangi
/// firma+şubeye ait izlenebilir. Store-and-forward'ta veri ilgili şube makinesine yönlendirmek için temel.
/// </summary>
public sealed class Migration025_MachineBranch : IMigration
{
    public int Version => 25;
    public string Name => "machine_branch";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE sync_devices ADD COLUMN branch_id TEXT;";
        cmd.ExecuteNonQuery();
    }
}
