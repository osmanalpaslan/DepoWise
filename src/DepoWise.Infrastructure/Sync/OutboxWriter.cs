using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Sync;

/// <summary>
/// Yerel write + outbox AYNI transaction. Çağıran iş servisi kendi conn+tx'i içinde Enqueue çağırır →
/// kayıt ve outbox birlikte commit/rollback olur. operation_id + payload_hash + base_version taşınır.
/// </summary>
public static class OutboxWriter
{
    public static void Enqueue(SqliteConnection conn, SqliteTransaction tx, string companyId, string operationId,
        string entityType, string entityId, string payloadJson, long? baseVersion, string? deviceId, long createdAt)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO sync_outbox(id, company_id, operation_id, entity_type, entity_id, payload_json, payload_hash,
    base_version, device_id, status, created_at)
VALUES($id,$c,$op,$et,$eid,$pl,$hash,$bv,$dev,'pending',$now);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$op", operationId);
        cmd.Parameters.AddWithValue("$et", entityType);
        cmd.Parameters.AddWithValue("$eid", entityId);
        cmd.Parameters.AddWithValue("$pl", payloadJson);
        cmd.Parameters.AddWithValue("$hash", SyncCrypto.Sha256Hex(payloadJson));
        cmd.Parameters.AddWithValue("$bv", (object?)baseVersion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dev", (object?)deviceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", createdAt);
        cmd.ExecuteNonQuery();
    }

    public static int PendingCount(SqliteConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE company_id=$c AND status='pending';";
        cmd.Parameters.AddWithValue("$c", companyId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
