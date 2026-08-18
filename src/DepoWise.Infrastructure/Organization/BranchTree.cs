using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Organization;

/// <summary>
/// ŞB-04 (2026-08-18) — ŞUBE AĞACININ KAPANIŞI (üst şube → tüm alt şubeleri, geçişli).
///
/// <b>NEDEN VAR:</b> <c>branches.parent_id</c> ilk günden beri vardı ama kod tabanında YALNIZ saklanıp
/// gösteriliyordu. <see cref="Application.Security.BranchAccess"/>, raporlar ve hiçbir filtre onu
/// okumuyordu → "Üst Şube" alanı sadece bir etiketti: üst şubeye yetkili kullanıcı alt şubeleri
/// GÖREMİYOR, üst şube alt şubelerin toplamını ALMIYORDU (kullanıcı beklentisinin tersi).
///
/// Burası ağacın TEK çözücüsüdür: firma başına <c>branchId → tüm alt şubeleri</c> haritası üretir.
/// Harita oturum kurulurken bir kez yüklenir (şube sayısı onlarca mertebesindedir, maliyeti yok);
/// <c>BranchAccess</c> onu salt-okunur kullanır ve veritabanına HİÇ dokunmaz (katman ayrımı korunur).
///
/// <b>DÖNGÜYE DAYANIKLI:</b> ŞB-02 ile döngü kurulması artık engelleniyor, ama ESKİ veride döngü
/// olabilir. Gezinme ziyaret edilen düğümleri işaretler → sonsuz döngü olmaz.
/// </summary>
public static class BranchTree
{
    /// <summary>
    /// Firmanın (silinmemiş) şubeleri için <c>şube → tüm alt şubeleri</c> haritası.
    /// Alt şubesi olmayan şube haritada YER ALMAZ (boş liste taşımaya gerek yok).
    /// Hiç üst/alt ilişkisi yoksa <c>null</c> döner → çağıran taraf hiçbir ek iş yapmaz.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>>? LoadDescendants(DbConnection conn, string companyId)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, parent_id FROM branches WHERE company_id=@c AND is_deleted=0 AND parent_id IS NOT NULL;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                var parent = r.GetString(1);
                if (string.Equals(id, parent, StringComparison.Ordinal)) continue;   // kendine referans — yok say
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<string>();
                list.Add(id);
            }
        }
        if (children.Count == 0) return null;   // düz yapı → genişletilecek bir şey yok

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var parent in children.Keys)
        {
            var all = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { parent };
            var stack = new Stack<string>(children[parent]);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!seen.Add(node)) continue;              // döngü ya da tekrar → atla
                all.Add(node);
                if (children.TryGetValue(node, out var kids))
                    foreach (var k in kids) stack.Push(k);
            }
            if (all.Count > 0) result[parent] = all;
        }
        return result.Count > 0 ? result : null;
    }
}
