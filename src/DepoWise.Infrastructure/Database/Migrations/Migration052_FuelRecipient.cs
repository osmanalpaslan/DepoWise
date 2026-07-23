using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Yakıt dağıtımına "yakıtı alan kişi" (kullanıcı isteği 2026-07-19). Mevcut <c>personnel_id</c> "Yakıtı Veren"
/// (dağıtan) idi; alıcı ayrı bir alandır → yeni <c>recipient_personnel_id</c> kolonu. Varsayılan NULL (opsiyonel);
/// iş senkronunda kolon-kesişimi ile otomatik taşınır (BusinessSyncService).
/// </summary>
public sealed class Migration052_FuelRecipient : IMigration
{
    public int Version => 52;
    public string Name => "fuel_recipient";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE fuel_distributions ADD COLUMN recipient_personnel_id TEXT NULL;";
        cmd.ExecuteNonQuery();
    }
}
