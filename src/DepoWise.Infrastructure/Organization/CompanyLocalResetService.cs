using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Sunucudaki "yerel sıfırlama isteği" durumu — masaüstü bunu kendi uyguladığı zamanla kıyaslar.</summary>
public sealed record LocalResetStatus(string CompanyId, long RequestedAt, string RequestedBy);

/// <summary>
/// Firma "yerel sıfırlama" isteği (ADR-084) — SUNUCU tarafı. Süper admin bir firma için istek bırakır;
/// o firmanın makineleri bir sonraki (çevrimiçi) girişte bunu görüp YEREL kopyalarını bir kez temizler.
///
/// company_purges'ten (ADR-083, kalıcı silme) FARKI: firma sunucuda durmaya devam eder, erişim engellenmez —
/// yalnız makinelerin yerel SQLite kopyaları sıfırdan yeniden doldurulsun istenir (örn. eski test verisi,
/// tutarsız/duplike kayıt şüphesi). Geri dönüşü yoktur ama YIKICI değildir: sunucudaki veri hiç etkilenmez.
/// </summary>
public sealed class CompanyLocalResetService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CompanyLocalResetService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Yeni bir sıfırlama isteği bırakır (var olanın üzerine yazar — her istek yeni bir "nesil"dir).</summary>
    public LocalResetStatus RequestReset(SessionContext actor, string companyId)
    {
        if (!actor.IsSuperAdmin) throw new ForbiddenException("Yerel sıfırlama isteği yalnız süper admin.");
        if (string.IsNullOrWhiteSpace(companyId)) throw new ArgumentException("Firma seçilmedi.");

        using var conn = _factory.Create();
        using (var chk = conn.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(*) FROM companies WHERE id=$c AND is_deleted=0;";
            chk.Parameters.AddWithValue("$c", companyId);
            if (Convert.ToInt64(chk.ExecuteScalar()) == 0) throw new InvalidOperationException("Firma bulunamadı.");
        }

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO company_local_resets(company_id, requested_at, requested_by) VALUES($c,$at,$by) " +
                "ON CONFLICT(company_id) DO UPDATE SET requested_at=$at, requested_by=$by;";
            cmd.Parameters.AddWithValue("$c", companyId);
            cmd.Parameters.AddWithValue("$at", now);
            cmd.Parameters.AddWithValue("$by", actor.UserId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "company_local_reset", companyId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
        return new LocalResetStatus(companyId, now, actor.UserId);
    }

    /// <summary>Bu firma için en son istenen sıfırlama zamanı (yoksa null). Tenant-güvenli: yalnız çağıranın
    /// KENDİ firması sorgulanır — companyId dışarıdan/istekten değil, aktörün oturumundan gelir.</summary>
    public LocalResetStatus? GetStatus(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT company_id, requested_at, requested_by FROM company_local_resets WHERE company_id=$c;";
        cmd.Parameters.AddWithValue("$c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new LocalResetStatus(r.GetString(0), r.GetInt64(1), r.GetString(2)) : null;
    }
}
