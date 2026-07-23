using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Sync;

/// <summary>Sunucudaki "makine tanımı sıfırlama" isteği — masaüstü bunu kendi uyguladığı zamanla kıyaslar.</summary>
public sealed record MachineResetStatus(string MachineName, long RequestedAt, string RequestedBy);

/// <summary>
/// MAKİNE TANIMI SIFIRLAMA (ADR-085) — SUNUCU tarafı.
///
/// Sorun: bir makine bir firmayla giriş yapınca hem sunucuda <c>sync_devices</c> satırı oluşur hem de
/// masaüstünde YEREL önbellek dosyaları yazılır (machine_status.txt / machine_branch.txt). Makine Yönetimi'ndeki
/// "Sil" YALNIZ sunucu satırını siler — masaüstü hâlâ eski firmayı/şubeyi "makinenin tanımı" sanar ve girişte
/// onu varsayılan olarak sunar. Bu yüzden makine başka bir firmaya devredilemiyordu.
///
/// Çözüm: süper admin "Tanımı Sıfırla" der →
///   1) o makine adına ait TÜM sync_devices satırları silinir (firmalar arası; makine hiçbir firmaya bağlı değil),
///   2) machine_resets'e künye yazılır.
/// Masaüstü, girişten sonra eşitleme adımında künyeyi görür → YEREL makine önbelleğini temizler → login'e döner.
/// Sonraki girişte makine "ilk kurulum" durumundadır → giriş yapan İLK kullanıcı firmayı/şubeyi yeniden tanımlar.
///
/// ⚠️ YIKICI DEĞİL: iş verisi (malzeme/araç/stok…) HİÇ etkilenmez — yalnız makinenin hangi firmaya ait
/// olduğu bilgisi silinir. ADR-083'teki (kalıcı firma silme) ile karıştırılmamalıdır.
///
/// Künye SİLİNMEZ: çevrimdışı bir makine haftalar sonra açılsa bile isteği görüp uygular.
/// </summary>
public sealed class MachineResetService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MachineResetService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>
    /// Makinenin tanımını sıfırlar: TÜM firmalardaki sync_devices satırlarını siler + künye bırakır.
    /// Yalnız süper admin (makine adı firmalar arası bir anahtardır — firma admini başka firmanın
    /// makine satırını silememelidir).
    /// </summary>
    public MachineResetStatus RequestReset(SessionContext actor, string machineName)
    {
        if (!actor.IsSuperAdmin) throw new ForbiddenException("Makine tanımı sıfırlama yalnız süper admin.");
        var name = (machineName ?? "").Trim();
        if (name.Length == 0) throw new ArgumentException("Makine adı zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // 1) Makinenin TÜM firma kayıtları silinir → makine hiçbir firmaya bağlı değil.
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM sync_devices WHERE device_name=@n;";
            del.AddWithValue("@n", name);
            del.ExecuteNonQuery();
        }

        // 2) Künye — masaüstü yerel önbelleğini bununla temizler (çevrimdışıysa açıldığında görür).
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO machine_resets(machine_name, requested_at, requested_by) VALUES(@n,@at,@by) " +
                "ON CONFLICT(machine_name) DO UPDATE SET requested_at=@at, requested_by=@by;";
            cmd.AddWithValue("@n", name);
            cmd.AddWithValue("@at", now);
            cmd.AddWithValue("@by", actor.UserId);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(actor.CompanyId, "machine_reset", name, AuditActions.Delete, actor.UserId), _clock);
        tx.Commit();
        return new MachineResetStatus(name, now, actor.UserId);
    }

    /// <summary>Bu makine için en son istenen sıfırlama zamanı (yoksa null). Masaüstü eşitlemede sorar.</summary>
    public MachineResetStatus? GetStatus(string machineName)
    {
        var name = (machineName ?? "").Trim();
        if (name.Length == 0) return null;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT machine_name, requested_at, requested_by FROM machine_resets WHERE machine_name=@n;";
        cmd.AddWithValue("@n", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new MachineResetStatus(r.GetString(0), r.GetInt64(1), r.GetString(2)) : null;
    }
}
