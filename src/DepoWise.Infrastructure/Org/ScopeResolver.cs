using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Organization;   // SB-01: şube ağacı (BranchTree) — BranchAccess ile AYNI kaynak

namespace DepoWise.Infrastructure.Org;

/// <summary>
/// Kullanıcının erişebileceği şube/şantiye kapsamını çözer. Kural (analiz §4/§6.2):
/// - Süper Admin / Firma Admini: oturum firmasının TÜM (silinmemiş) şubeleri.
/// - Kapsamlı kullanıcı (user_scopes satırı var): yalnız atanan şubeler.
/// - Hiç kapsam atanmamış admin-olmayan: boş (deny-by-default).
/// Şube seçimi bu kapsamın DIŞINA taşamaz; fail-closed.
/// </summary>
public sealed class ScopeResolver
{
    private readonly IDbConnectionFactory _factory;

    public ScopeResolver(IDbConnectionFactory factory) => _factory = factory;

    public IReadOnlyCollection<string> AllowedBranchIds(SessionContext session)
    {
        using var conn = _factory.Create();

        var explicitScopes = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT branch_id FROM user_scopes WHERE user_id = @u AND company_id = @c;";
            cmd.AddWithValue("@u", session.UserId);
            cmd.AddWithValue("@c", session.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) explicitScopes.Add(r.GetString(0));
        }

        // Açık kapsam varsa onu uygula (admin olsa bile sınırlanmış olabilir).
        //
        // ⭐ SB-01 (denetim 2026-08-26) — ŞUBE AĞACI BURADA DA GENİŞLETİLİR.
        //
        // Ürün kuralı ŞB-04 (2026-08-18): "Üst şubeye yetkili kullanıcı alt şubeleri de görsün."
        // BranchAccess bunu Expand ile uyguluyordu (araçlar, raporlar, stok hareketleri o yoldan geçer),
        // ama projedeki İKİNCİ kapsam otoritesi olan bu sınıf user_scopes satırlarını OLDUĞU GİBİ
        // döndürüyordu. Canlı kullanıcısı PersonnelService'tir (hem liste hem yazma kapısı) → üst şubeye
        // yetkili kullanıcı alt şantiyenin ARAÇLARINI görüyor ama PERSONELİNİ göremiyor, o şantiyeye
        // personel de EKLEYEMİYORDU ("şube kapsam dışı").
        //
        // Üretimde bu turda 9 şube bulundu ve 5'i bir üst şubenin altındadır; önceki turlarda 0 şube
        // olduğu için fark edilemiyordu. Yeni kural getirilmez — ŞB-04'ün kararı ikinci yerde de
        // uygulanır ve iki otorite AYNI cevabı verir. Genişleme yalnız AŞAĞI doğrudur (alt şubeler);
        // kardeş ve üst şubeler kapsama GİRMEZ.
        if (explicitScopes.Count > 0)
        {
            var agac = BranchTree.LoadDescendants(conn, session.CompanyId);
            if (agac is null) return explicitScopes;                 // düz yapı → genişletilecek bir şey yok
            foreach (var kok in explicitScopes.ToList())
                if (agac.TryGetValue(kok, out var altlar))
                    foreach (var alt in altlar) explicitScopes.Add(alt);
            return explicitScopes;
        }

        // Açık kapsam yoksa: admin → tüm firma şubeleri; admin değil → boş.
        if (!AccessControl.IsAdmin(session)) return Array.Empty<string>();

        var all = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM branches WHERE company_id = @c AND is_deleted = 0;";
            cmd.AddWithValue("@c", session.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) all.Add(r.GetString(0));
        }
        return all;
    }

    public bool IsBranchAllowed(SessionContext session, string? branchId)
    {
        if (branchId is null) return true; // şubesiz kayıt (firma geneli)
        return AllowedBranchIds(session).Contains(branchId);
    }

    public void EnsureBranchAllowed(SessionContext session, string? branchId)
    {
        if (!IsBranchAllowed(session, branchId))
            throw new ForbiddenException("Şube kapsam dışı: bu şubeye erişiminiz yok.");
    }
}
