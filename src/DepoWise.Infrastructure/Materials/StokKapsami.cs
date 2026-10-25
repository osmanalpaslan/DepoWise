using System.Data.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Materials;

/// <summary>
/// ═══ ŞUBE STOĞU KAPSAMI (kullanıcı bildirimi 2026-10-25) ═══
///
/// Malzeme KATALOĞU firma genelidir (ortak liste — kullanıcı kararı 2026-07-26); STOK ise şubeye (depoya) özgüdür
/// (<c>stock_balances.location_id</c> = <c>branches.id</c>). Malzeme listesi ve ana ekran "düşük stok" uyarısı eskiden stok
/// sütununda firmanın TÜM depolarının toplamını kullanıyordu: stoğu olmayan şube başka şubenin stoğunu kendininmiş gibi görüyordu.
///
/// Kapsam: oturum bir ÇALIŞMA şubesiyle (şube/şantiye) açıldıysa YALNIZ o şubenin deposu; "Tüm Şubeler"de kullanıcının
/// yetkili olduğu şubeler (<see cref="BranchAccess.Allowed"/> — null = sınırsız → tüm depolar, eski firma toplamı).
/// Değerler SQL'e parametre olarak bağlanır (<see cref="Bagla"/>), metne gömülmez.
/// </summary>
public static class StokKapsami
{
    /// <summary>Stoğu sayılacak depolar (null = süzgeç yok).</summary>
    public static IReadOnlyList<string>? Konumlar(SessionContext s)
        => !string.IsNullOrEmpty(s.OperatingBranchId) ? new[] { s.OperatingBranchId! } : BranchAccess.Allowed(s);

    /// <summary><see cref="SqlDialect.StockTotalSubquery"/>'ye eklenecek konum süzgeci ("" = süzgeç yok).</summary>
    public static string Sql(SessionContext s, string prefix = "@sk")
    {
        var l = Konumlar(s);
        if (l is null) return "";
        if (l.Count == 0) return " AND 1=0";   // hiçbir şubeye yetki yok → stok 0 görünür (kapalı tarafa düşer)
        return " AND location_id IN (" + string.Join(",", Enumerable.Range(0, l.Count).Select(i => prefix + i)) + ")";
    }

    /// <summary><see cref="Sql"/> parametrelerini bağlar — AYNI kaynaktan (tutarlı).</summary>
    public static void Bagla(DbCommand cmd, SessionContext s, string prefix = "@sk")
    {
        var l = Konumlar(s);
        if (l is null) return;
        for (int i = 0; i < l.Count; i++) cmd.AddWithValue(prefix + i, l[i]);
    }
}
