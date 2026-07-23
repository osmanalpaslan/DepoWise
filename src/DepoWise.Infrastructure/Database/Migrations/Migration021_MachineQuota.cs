using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Firma başına makine kotası. Kota dahilindeki makineler otomatik aktifleşir ve giriş yapabilir;
/// kota aşılırsa yeni makineler 'pending' kalır ve süper admin onaylayana/başka makineyi pasife
/// alana kadar giriş yapamaz. Varsayılan 3.
/// </summary>
public sealed class Migration021_MachineQuota : IMigration
{
    public int Version => 21;
    public string Name => "machine_quota";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE companies ADD COLUMN machine_quota INTEGER NOT NULL DEFAULT 3;";
        cmd.ExecuteNonQuery();
    }
}
