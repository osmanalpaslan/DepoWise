using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Sync;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Sync;

/// <summary>
/// Senkron sunucu push/pull. Push: cihaz doğrulama (revoked→403) + operation_id idempotency
/// (already_applied) + KRİTİK işlemlerde sunucu doğrulaması (LWW yok; geçersizse rejected/conflict).
/// Düşük-riskli: base_version eşleşmezse conflict (kör LWW yok). Pull: seq cursor; bozuk sayfada rollback,
/// cursor ilerlemez.
/// </summary>
public sealed class SyncServer
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public SyncServer(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public delegate (bool Ok, string? Reason) CriticalValidator(SyncOperation op);

    public IReadOnlyList<SyncOpOutcome> Push(string token, IReadOnlyList<SyncOperation> ops, CriticalValidator? validator = null)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        var (deviceId, companyId) = AuthDevice(conn, token); // revoked/pending → 403
        Touch(conn, deviceId, now);

        var outcomes = new List<SyncOpOutcome>();
        foreach (var op in ops)
        {
            using var tx = conn.BeginTransaction(deferred: false);
            if (InboxHas(conn, tx, op.OperationId))
            {
                outcomes.Add(new SyncOpOutcome(op.OperationId, SyncOpResult.AlreadyApplied));
                tx.Commit();
                continue;
            }

            SyncOpResult result; string? reason = null;
            if (SyncPolicy.IsCritical(op.EntityType))
            {
                // Sunucu otoriteli: doğrulama zorunlu, LWW yok
                var (ok, why) = validator?.Invoke(op) ?? (false, "Kritik işlem için sunucu doğrulayıcı gerekli.");
                if (ok) result = SyncOpResult.Accepted;
                else { result = SyncOpResult.Rejected; reason = why; }
            }
            else
            {
                // Düşük-riskli: base_version eşleşmezse conflict
                var current = CurrentVersion(conn, tx, op.EntityType, op.EntityId);
                if (op.BaseVersion is long bv && current is long cv && bv != cv)
                { result = SyncOpResult.Conflict; reason = $"Sürüm uyuşmazlığı: base {bv}, mevcut {cv}."; }
                else result = SyncOpResult.Accepted;
            }

            // Inbox kaydı (idempotency) — her sonuç için yazılır
            InsertInbox(conn, tx, companyId, op, MapResult(result), now);
            if (result == SyncOpResult.Accepted)
                AppendServerChange(conn, tx, companyId, op, now);
            else
                InsertConflict(conn, tx, companyId, op, reason ?? result.ToString(), now);

            outcomes.Add(new SyncOpOutcome(op.OperationId, result, reason));
            tx.Commit();
        }
        return outcomes;
    }

    public PullPage Pull(string token, long afterSeq, int limit = 100)
    {
        using var conn = _factory.Create();
        var (deviceId, companyId) = AuthDevice(conn, token); // revoked → 403
        Touch(conn, deviceId, _clock.UtcNow.ToUnixTimeMilliseconds());

        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT seq, entity_type, entity_id, payload_json, valid FROM server_changes " +
            "WHERE company_id=$c AND seq > $after ORDER BY seq LIMIT $lim;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$after", afterSeq);
        cmd.Parameters.AddWithValue("$lim", limit < 1 ? 1 : limit);

        var items = new List<ServerChange>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // Bozuk kayıt → tüm sayfa rollback, cursor SABİT kalır
            if (r.GetInt64(4) != 1)
                throw new InvalidOperationException("Pull sayfasında geçersiz kayıt: sayfa reddedildi, cursor ilerlemedi.");
            items.Add(new ServerChange(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }
        var next = items.Count > 0 ? items[^1].Seq : afterSeq;
        return new PullPage(items, next);
    }

    // ---- yardımcılar ----
    private static (string DeviceId, string CompanyId) AuthDevice(SqliteConnection conn, string token)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, company_id, status FROM sync_devices WHERE token_hash=$h;";
        cmd.Parameters.AddWithValue("$h", SyncCrypto.Sha256Hex(token));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Geçersiz cihaz token'ı.");
        var status = r.GetString(2);
        if (status != "active") throw new ForbiddenException($"Cihaz aktif değil (status={status}).");
        return (r.GetString(0), r.GetString(1));
    }

    private static void Touch(SqliteConnection conn, string deviceId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_devices SET last_seen_at=$now WHERE id=$id;";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", deviceId);
        cmd.ExecuteNonQuery();
    }

    private static bool InboxHas(SqliteConnection conn, SqliteTransaction tx, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sync_inbox WHERE operation_id=$op;";
        cmd.Parameters.AddWithValue("$op", operationId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static void InsertInbox(SqliteConnection conn, SqliteTransaction tx, string companyId, SyncOperation op, string result, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO sync_inbox(id, company_id, operation_id, entity_type, entity_id, payload_json, result, applied_at) " +
            "VALUES($id,$c,$op,$et,$eid,$pl,$res,$now);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$op", op.OperationId);
        cmd.Parameters.AddWithValue("$et", op.EntityType);
        cmd.Parameters.AddWithValue("$eid", op.EntityId);
        cmd.Parameters.AddWithValue("$pl", op.PayloadJson);
        cmd.Parameters.AddWithValue("$res", result);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void AppendServerChange(SqliteConnection conn, SqliteTransaction tx, string companyId, SyncOperation op, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO server_changes(company_id, operation_id, entity_type, entity_id, payload_json, valid, created_at) " +
            "VALUES($c,$op,$et,$eid,$pl,1,$now);";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$op", op.OperationId);
        cmd.Parameters.AddWithValue("$et", op.EntityType);
        cmd.Parameters.AddWithValue("$eid", op.EntityId);
        cmd.Parameters.AddWithValue("$pl", op.PayloadJson);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertConflict(SqliteConnection conn, SqliteTransaction tx, string companyId, SyncOperation op, string reason, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO sync_conflicts(id, company_id, operation_id, entity_type, entity_id, incoming_payload, reason, status, created_at) " +
            "VALUES($id,$c,$op,$et,$eid,$pl,$reason,'open',$now);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$op", op.OperationId);
        cmd.Parameters.AddWithValue("$et", op.EntityType);
        cmd.Parameters.AddWithValue("$eid", op.EntityId);
        cmd.Parameters.AddWithValue("$pl", op.PayloadJson);
        cmd.Parameters.AddWithValue("$reason", reason);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static long? CurrentVersion(SqliteConnection conn, SqliteTransaction tx, string entityType, string entityId)
    {
        // Düşük-riskli kart tabloları için version okunur (yoksa null → yeni kayıt)
        var table = entityType switch { "material" => "materials", "vehicle" => "vehicles", "branch" => "branches", _ => null };
        if (table is null) return null;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT version FROM {table} WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", entityId);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? null : Convert.ToInt64(v);
    }

    private static string MapResult(SyncOpResult r) => r switch
    {
        SyncOpResult.Accepted => "applied",
        SyncOpResult.AlreadyApplied => "already_applied",
        SyncOpResult.Rejected => "rejected",
        SyncOpResult.Conflict => "conflict",
        _ => "rejected",
    };
}
