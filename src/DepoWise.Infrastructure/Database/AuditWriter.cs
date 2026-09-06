using DepoWise.Application.Common;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// audit_logs yazıcı. Kritik işlemlerin transaction'ı içinde çağrılır (aynı conn+tx) →
/// iş kaydı ile audit birlikte commit/rollback olur.
/// </summary>
public static class AuditWriter
{
    public static void Write(DbConnection conn, DbTransaction? tx, AuditEntry e, IClock? clock = null)
    {
        TenantGuard.Require(e.CompanyId);

        // ⭐ FAZ 4.3 (kullanıcı isteği 2026-09-06) — LOG ARTIK "NE DEĞİŞTİĞİNİ" DE YAZAR.
        // Çağıran kendi anlık görüntüsünü vermediyse kaydın o andaki hâli buradan alınır
        // (bkz. AuditSnapshot). Böylece 59 dosyadaki 162 çağrı yerinin hiçbirine dokunulmadan
        // tüm ekranlar alan bazlı geçmişe kavuşur. Görüntü alınamazsa davranış ESKİSİ GİBİDİR.
        var after = e.AfterJson ?? AuditSnapshot.Al(conn, tx, e.EntityType, e.EntityId);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO audit_logs(id, company_id, user_id, entity_type, entity_id, action,
                       before_json, after_json, correlation_id, created_at)
VALUES(@id, @company, @user, @etype, @eid, @action, @before, @after, @corr, @ts);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@company", e.CompanyId);
        cmd.AddWithValue("@user", (object?)e.UserId ?? DBNull.Value);
        cmd.AddWithValue("@etype", e.EntityType);
        cmd.AddWithValue("@eid", e.EntityId);
        cmd.AddWithValue("@action", e.Action);
        cmd.AddWithValue("@before", (object?)e.BeforeJson ?? DBNull.Value);
        cmd.AddWithValue("@after", (object?)after ?? DBNull.Value);
        cmd.AddWithValue("@corr", (object?)e.CorrelationId ?? DBNull.Value);
        cmd.AddWithValue("@ts", (clock?.UtcNow ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }
}
