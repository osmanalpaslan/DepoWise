using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

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
        if (explicitScopes.Count > 0) return explicitScopes;

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
