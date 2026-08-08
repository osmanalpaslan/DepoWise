using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Bakımda kullanılan malzeme "BAKIM EKİBİ STOĞUNDAN" kullanılmış olabilir (kullanıcı isteği 2026-08-08):
/// malzeme bakım kaydına yazılır ama MERKEZ DEPO stoğundan düşülmez (daha önce ekibe teslim edilmiştir).
///
/// ADDITIVE + GERİYE UYUMLU: tek yeni kolon, varsayılan 0. Mevcut satırlar 0 kalır → bugünkü davranış
/// (stok düşümü + defter hareketi + iptalde ters hareket) hiç değişmez. Veri silinmez/taşınmaz.
/// SQLite + PostgreSQL ortak sözdizimi (ALTER TABLE ... ADD COLUMN ... DEFAULT 0). Diğer bayrak kolonları
/// gibi BIGINT 0/1 tutulur (ör. vehicle_maintenances.is_cancelled, daily_activities.stock_processed).
/// İş senkronunda kolon-kesişimi ile otomatik taşınır (BusinessSyncService; maintenance_materials zaten listede).
/// </summary>
public sealed class Migration059_MaintenanceTeamStock : IMigration
{
    public int Version => 59;
    public string Name => "maintenance_team_stock";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE maintenance_materials ADD COLUMN from_team_stock BIGINT NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }
}
