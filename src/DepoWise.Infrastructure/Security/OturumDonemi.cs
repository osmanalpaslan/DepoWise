using System.Collections.Concurrent;
using System.Data.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Security;

/// <summary>
/// ⭐ M-10 (AR-GE 2026-10-18, 05 F-066) — OTURUM DÖNEMİ: kullanıcının geçerli jeton dönemi (<c>users.token_epoch</c>,
/// Migration104). Şifre değişince/sıfırlanınca dönem artar → o andan önce verilmiş jetonlar geçersizdir.
///
/// Her API isteğinde okunur; veritabanı yükü olmasın diye 30 sn önbellek tutulur. Dönem bu süreçte değiştirildiğinde
/// (<see cref="Arttir"/>) önbellek ANINDA silinir (API tek makine/süreç). Kolon yoksa (eski şema) dönem hep 0.
/// </summary>
public sealed class OturumDonemi
{
    // Önbellek NESNEYE özeldir (bir veritabanı = bir nesne; testlerde aynı kullanıcı kimliği farklı veritabanlarında olabilir).
    // Değişim duyurusu süreç genelidir: Arttir/Unut kullanıcının sayacını ilerletir → tüm nesnelerdeki kaydı bayatlar.
    private static readonly ConcurrentDictionary<string, long> Degisim = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long Donem, long Zaman, long Degisim)> _onbellek = new(StringComparer.Ordinal);
    private const long OnbellekMs = 30_000;
    private readonly IDbConnectionFactory _factory;

    public OturumDonemi(IDbConnectionFactory factory) => _factory = factory;

    public long Oku(string userId)
    {
        var simdi = Environment.TickCount64;
        var sayac = Degisim.GetValueOrDefault(userId);
        if (_onbellek.TryGetValue(userId, out var o) && simdi - o.Zaman < OnbellekMs && o.Degisim == sayac) return o.Donem;
        long donem = 0;
        using (var conn = _factory.Create())
        {
            if (DbIntrospect.ColumnExists(conn, null, "users", "token_epoch"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT token_epoch FROM users WHERE id=@u;";
                cmd.AddWithValue("@u", userId);
                var v = cmd.ExecuteScalar();
                donem = v is null or DBNull ? 0 : Convert.ToInt64(v);
            }
        }
        _onbellek[userId] = (donem, simdi, sayac);   // okumadan ÖNCE alınan sayaçla: arada değişim olduysa kayıt hemen bayat
        return donem;
    }

    /// <summary>Jetonun taşıdığı dönem kullanıcının güncel dönemiyle aynı mı?</summary>
    public bool Gecerli(string userId, long jetonDonemi) => Oku(userId) == jetonDonemi;

    /// <summary>Kullanıcının dönemini aynı işlem içinde 1 artırır (şifre değişimi/sıfırlama). Önbellek hemen silinir;
    /// işlem geri alınırsa önbellek bir sonraki okumada veritabanından düzelir.</summary>
    public static void Arttir(DbConnection conn, DbTransaction? tx, string userId)
    {
        if (!DbIntrospect.ColumnExists(conn, tx, "users", "token_epoch")) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE users SET token_epoch = COALESCE(token_epoch, 0) + 1 WHERE id=@u;";
        cmd.AddWithValue("@u", userId);
        cmd.ExecuteNonQuery();
        Unut(userId);
    }

    /// <summary>Önbellekteki kaydı bayatlatır (işlem tamamlandıktan sonra bir kez daha çağrılır: işlem sürerken eski
    /// dönemi okuyup önbelleğe yazan bir istek kalmasın).</summary>
    public static void Unut(string userId) => Degisim.AddOrUpdate(userId, 1, (_, v) => v + 1);
}
