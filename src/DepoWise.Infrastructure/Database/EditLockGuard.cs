using DepoWise.Application.Security;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// DÜZENLEME KİLİDİ (2026-07-22) — iyimser sürüm kontrolü için ortak yardımcı.
///
/// Sorun: tablolarda <c>version</c> yazılıyordu ama HİÇ kontrol edilmiyordu → iki kullanıcı (ya da
/// eşitlemeyle gelen iki makine) aynı kaydı düzenlediğinde ikincisi birincisini SESSİZCE eziyordu.
///
/// Neden gerçek "kilit" değil: DepoWise çevrimdışı çalışabilmeli. Sunucu tabanlı kilit çevrimdışı
/// makinede işlemez ve program çökerse kayıt kilitli kalır. Sürüm karşılaştırması çevrimdışı dahil
/// her zaman çalışır ve asıl zararı (sessiz üzerine yazma) önler.
///
/// Kullanım (üç adım, her serviste aynı):
/// <code>
/// cmd.CommandText = @"UPDATE tablo SET ... WHERE id=@id AND company_id=@c AND is_deleted=0"
///                   + EditLockGuard.Clause(expectedVersion) + ";";
/// EditLockGuard.Bind(cmd, expectedVersion);
/// if (cmd.ExecuteNonQuery() == 0)
/// {
///     EditLockGuard.ThrowIfStale(conn, tx, "tablo", id, companyId, expectedVersion);
///     throw new ForbiddenException("... bulunamadı veya başka firmaya ait.");
/// }
/// </code>
/// <c>expectedVersion == null</c> → kontrol yok (geriye uyumlu: sürüm taşımayan eski çağrılar bozulmaz).
/// </summary>
internal static class EditLockGuard
{
    /// <summary>UPDATE'in WHERE'ine eklenecek sürüm koşulu (sürüm verilmediyse boş).</summary>
    public static string Clause(long? expectedVersion) => expectedVersion.HasValue ? " AND version=@ev" : "";

    /// <summary>Sürüm parametresini bağlar (sürüm verilmediyse hiçbir şey yapmaz).</summary>
    public static void Bind(DbCommand cmd, long? expectedVersion)
    {
        if (expectedVersion.HasValue) cmd.AddWithValue("@ev", expectedVersion.Value);
    }

    /// <summary>
    /// UPDATE 0 satır etkilediyse çağrılır. Kayıt HÂLÂ duruyorsa sebep sürüm uyuşmazlığıdır →
    /// <see cref="ConcurrencyException"/>. Kayıt yoksa hiçbir şey yapmaz; çağıran kendi
    /// "bulunamadı/başka firmaya ait" hatasını atar (iki durum karışmasın diye ayrı tutulur).
    /// </summary>
    public static void ThrowIfStale(DbConnection conn, DbTransaction? tx, string table,
        string id, string companyId, long? expectedVersion)
    {
        if (!expectedVersion.HasValue) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // table sabit kod içinden gelir (kullanıcı girdisi DEĞİL); id/company parametreli.
        cmd.CommandText = $"SELECT version FROM {table} WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        var cur = cmd.ExecuteScalar();
        if (cur is not null and not System.DBNull)
            throw new ConcurrencyException(expectedVersion.Value, System.Convert.ToInt64(cur));
    }
}
