namespace DepoWise.Application.Security;

/// <summary>
/// Rol atama / kullanıcı oluşturma yetki yükseltme koruması (analiz §4).
/// - Süper Admin'i YALNIZ Süper Admin atayabilir/oluşturabilir.
/// - Firma Admini rolünü yalnız Süper Admin veya Firma Admini atayabilir.
/// - Firma Admini firma DEĞİŞTİREMEZ (kendi company_id'sinde kullanıcı açar).
/// - Admin olmayan kullanıcı admin/süper-admin rolü ATAYAMAZ.
/// </summary>
public static class RoleAssignmentGuard
{
    public static void EnsureCanAssign(SessionContext actor, IEnumerable<string> targetRoleKeys)
    {
        foreach (var role in targetRoleKeys)
        {
            switch (role)
            {
                case RoleKeys.SuperAdmin when !actor.IsSuperAdmin:
                    throw new ForbiddenException("Yetki yükseltme reddedildi: Süper Admin yalnız Süper Admin tarafından atanır.");
                case RoleKeys.RestrictedSuperAdmin when !actor.IsSuperAdmin:
                    throw new ForbiddenException("Yetki yükseltme reddedildi: Kısıtlı Süper Admin rolünü yalnız Süper Admin atar.");
                case RoleKeys.CompanyAdmin when !AccessControl.IsAdmin(actor):
                    throw new ForbiddenException("Yetki yükseltme reddedildi: Firma Admini rolünü yalnız admin atar.");
            }
        }
    }

    /// <summary>Yeni kullanıcı için hedef firmayı güvenle çözer (Firma Admini kendi firmasına kilitli).</summary>
    public static string ResolveTargetCompany(SessionContext actor, string? requestedCompanyId)
        => TenantAccessGuard.ResolveCompanyId(actor, requestedCompanyId);
}
