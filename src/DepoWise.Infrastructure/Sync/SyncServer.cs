using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Sync;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

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

    public delegate (bool Ok, string? Reason) CriticalValidator(string companyId, SyncOperation op);

    public IReadOnlyList<SyncOpOutcome> Push(string token, IReadOnlyList<SyncOperation> ops, CriticalValidator? validator = null)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        var (deviceId, companyId) = AuthDevice(conn, token); // revoked/pending → 403
        Touch(conn, deviceId, now);

        var outcomes = new List<SyncOpOutcome>();
        foreach (var op in ops)
        {
            using var tx = conn.BeginImmediate();
            if (InboxHas(conn, tx, companyId, op.OperationId))
            {
                outcomes.Add(new SyncOpOutcome(op.OperationId, SyncOpResult.AlreadyApplied));
                tx.Commit();
                continue;
            }

            SyncOpResult result; string? reason = null;
            if (SyncPolicy.IsCritical(op.EntityType))
            {
                // Sunucu otoriteli: doğrulama zorunlu, LWW yok
                var (ok, why) = validator?.Invoke(companyId, op) ?? (false, "Kritik işlem için sunucu doğrulayıcı gerekli.");
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
            "WHERE company_id=@c AND seq > @after ORDER BY seq LIMIT @lim;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@after", afterSeq);
        cmd.AddWithValue("@lim", limit < 1 ? 1 : limit);

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

    /// <summary>
    /// ⭐ YED-02 (denetim 2026-08-26) — cihaz jetonunu FİRMAYA çözer; jeton geçersiz ya da cihaz aktif
    /// değilse <c>null</c> döner (fırlatmaz).
    ///
    /// <para><b>Neden eklendi:</b> <c>/api/backups</c> ucu jetonu HİÇ doğrulamıyordu; yalnız
    /// <c>Authorization: Bearer …</c> başlığının VAR OLUP OLMADIĞINA bakıyordu. Bu metot,
    /// <c>/sync/push</c> ve <c>/sync/pull</c>'un zaten kullandığı doğrulamanın (<c>AuthDevice</c>)
    /// istisna fırlatmayan sürümüdür — böylece yükleme ucu da aynı tek kaynaktan doğrular.</para>
    /// </summary>
    public string? CompanyForDevice(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            using var conn = _factory.Create();
            return AuthDevice(conn, token!).CompanyId;
        }
        catch (ForbiddenException) { return null; }
    }

    // ---- yardımcılar ----
    private static (string DeviceId, string CompanyId) AuthDevice(DbConnection conn, string token)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, company_id, status FROM sync_devices WHERE token_hash=@h;";
        cmd.AddWithValue("@h", SyncCrypto.Sha256Hex(token));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Geçersiz cihaz token'ı.");
        var status = r.GetString(2);
        if (status != "active") throw new ForbiddenException($"Cihaz aktif değil (status={status}).");
        return (r.GetString(0), r.GetString(1));
    }

    private static void Touch(DbConnection conn, string deviceId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_devices SET last_seen_at=@now WHERE id=@id;";
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@id", deviceId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Senkron yinelenme kalkanı: bu firma bu operation_id'yi daha önce işledi mi?
    ///
    /// ⭐ FIN-B1 (ADR-185 / PK-FIN-02=B, Migration082 ile birlikte): FİRMA KAPSAMLI. Eskiden bu kontrol
    /// firma-kördü ve Push akışında servis katmanından ÖNCE çalıştığı için, başka bir firmada kullanılmış
    /// bir operation_id ile gelen MEŞRU işlem "AlreadyApplied" sayılıp alt katmana hiç inmeden düşüyordu.
    /// Senkronun kritik tipleri (stock_movement, vehicle_maintenance, fuel_distribution) tam da FIN-B1
    /// tabloları olduğundan, YALNIZ servisleri düzeltmek yeterli DEĞİLDİ — giriş kapısı da kapsandı.
    ///
    /// ⚠️ Senkron PROTOKOLÜ değişmedi: istek/yanıt biçimi, cursor, çakışma çözümü ve SNK-05(a) sözleşmesi
    /// aynen; yalnız yinelenme kontrolünün KAPSAMI firmaya daraldı. Aynı firmanın tekrar push'u aynen
    /// idempotenttir. company_id, cihaz token'ından (AuthDevice) gelir — istemci gönderemez.</summary>
    private static bool InboxHas(DbConnection conn, DbTransaction tx, string companyId, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sync_inbox WHERE company_id=@c AND operation_id=@op;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", operationId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static void InsertInbox(DbConnection conn, DbTransaction tx, string companyId, SyncOperation op, string result, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO sync_inbox(id, company_id, operation_id, entity_type, entity_id, payload_json, result, applied_at) " +
            "VALUES(@id,@c,@op,@et,@eid,@pl,@res,@now);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", op.OperationId);
        cmd.AddWithValue("@et", op.EntityType);
        cmd.AddWithValue("@eid", op.EntityId);
        cmd.AddWithValue("@pl", op.PayloadJson);
        cmd.AddWithValue("@res", result);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void AppendServerChange(DbConnection conn, DbTransaction tx, string companyId, SyncOperation op, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO server_changes(company_id, operation_id, entity_type, entity_id, payload_json, valid, created_at) " +
            "VALUES(@c,@op,@et,@eid,@pl,1,@now);";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", op.OperationId);
        cmd.AddWithValue("@et", op.EntityType);
        cmd.AddWithValue("@eid", op.EntityId);
        cmd.AddWithValue("@pl", op.PayloadJson);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertConflict(DbConnection conn, DbTransaction tx, string companyId, SyncOperation op, string reason, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO sync_conflicts(id, company_id, operation_id, entity_type, entity_id, incoming_payload, reason, status, created_at) " +
            "VALUES(@id,@c,@op,@et,@eid,@pl,@reason,'open',@now);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", op.OperationId);
        cmd.AddWithValue("@et", op.EntityType);
        cmd.AddWithValue("@eid", op.EntityId);
        cmd.AddWithValue("@pl", op.PayloadJson);
        cmd.AddWithValue("@reason", reason);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static long? CurrentVersion(DbConnection conn, DbTransaction tx, string entityType, string entityId)
    {
        // Düşük-riskli kart tabloları için version okunur (yoksa null → yeni kayıt)
        var table = entityType switch { "material" => "materials", "vehicle" => "vehicles", "branch" => "branches", _ => null };
        if (table is null) return null;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT version FROM {table} WHERE id=@id;";
        cmd.AddWithValue("@id", entityId);
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
