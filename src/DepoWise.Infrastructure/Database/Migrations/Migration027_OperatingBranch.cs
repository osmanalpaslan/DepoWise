using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// İşlem (transaction) kayıtlarına "işlenen şube" (op_branch_id) — kaydı GİREN kullanıcının login'de seçtiği
/// çalışma şubesi. Nullable, additive; mevcut davranış DEĞİŞMEZ. Store-and-forward'ın Faz 1 temeli: veriler
/// artık hangi şubede işlendiği bilgisini taşır (sonraki fazlarda şubeye yönlendirme + filtre için kullanılır).
/// </summary>
public sealed class Migration027_OperatingBranch : IMigration
{
    public int Version => 27;
    public string Name => "operating_branch";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var table in new[] { "vehicle_maintenances", "fuel_depot_entries", "fuel_distributions", "daily_activities", "stock_movements" })
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN op_branch_id TEXT;";
            cmd.ExecuteNonQuery();
        }
    }
}
