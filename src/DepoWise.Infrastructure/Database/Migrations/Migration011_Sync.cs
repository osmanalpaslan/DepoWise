using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Senkronizasyon sertleştirme: tek-kullanımlık enrollment anahtarı (10 dk), cihaz token/revoke,
/// sunucu değişiklik feed'i (pull cursor) ve çakışma kuyruğu. Kritik işlemlerde LWW YOK.
/// </summary>
public sealed class Migration011_Sync : IMigration
{
    public int Version => 11;
    public string Name => "sync_hardening";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE sync_devices ADD COLUMN token_hash TEXT NULL;
ALTER TABLE sync_devices ADD COLUMN revoked_at INTEGER NULL;
ALTER TABLE sync_devices ADD COLUMN last_seen_at INTEGER NULL;

CREATE TABLE enrollment_keys (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    key_hash TEXT NOT NULL,
    expires_at INTEGER NOT NULL,
    used_at INTEGER NULL,                       -- tek kullanımlık: kullanılınca dolu
    created_at INTEGER NOT NULL
);
CREATE INDEX ix_enrollment_keys ON enrollment_keys(company_id, expires_at);

-- Sunucu otoriteli değişiklik feed'i (pull). Monoton seq cursor.
CREATE TABLE server_changes (
    seq INTEGER PRIMARY KEY AUTOINCREMENT,
    company_id TEXT NOT NULL,
    operation_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    valid INTEGER NOT NULL DEFAULT 1,           -- 0 = bozuk (pull sayfa rollback testi)
    created_at INTEGER NOT NULL
);
CREATE INDEX ix_server_changes ON server_changes(company_id, seq);

CREATE TABLE sync_conflicts (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    operation_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    incoming_payload TEXT NULL,
    reason TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'open',         -- open | resolved
    created_at INTEGER NOT NULL
);
CREATE INDEX ix_sync_conflicts ON sync_conflicts(company_id, status);";
        cmd.ExecuteNonQuery();
    }
}
