using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Çakışma tespiti (güvenli senkron): admin (web) ile personel (masaüstü) SON senkron sonrası AYNI kaydı
/// değiştirirse push sırasında çakışma tespit edilir. LWW (son düzenleyen kazanır) uygulanır; çakışma
/// data_conflicts'e yazılır → personel "admin ile çakıştınız" uyarısı alır, admin web ana ekranda listeyi görür.
/// sync_devices.last_business_push_at = cihazın son iş-verisi push zamanı (çakışma baseline'ı).
/// </summary>
public sealed class Migration028_DataConflicts : IMigration
{
    public int Version => 28;
    public string Name => "data_conflicts";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "ALTER TABLE sync_devices ADD COLUMN last_business_push_at BIGINT;";
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
CREATE TABLE data_conflicts (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    branch_id TEXT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    winner TEXT NOT NULL,                 -- admin | device
    admin_user_id TEXT NULL,
    admin_name TEXT NULL,
    server_updated_at BIGINT NOT NULL,
    device_updated_at BIGINT NOT NULL,
    personnel_seen BIGINT NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'open',   -- open | resolved
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL
);
CREATE INDEX ix_data_conflicts ON data_conflicts(company_id, status);
CREATE UNIQUE INDEX ux_data_conflicts_entity ON data_conflicts(company_id, entity_id) WHERE status='open';";
            cmd.ExecuteNonQuery();
        }
    }
}
