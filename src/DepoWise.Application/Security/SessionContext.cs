using DepoWise.Application.Common;

namespace DepoWise.Application.Security;

/// <summary>
/// Doğrulanmış oturum. company_id ve roller YALNIZ buradan (server/session) okunur;
/// istek payload'ından gelen company_id reddedilir (analiz §9).
/// </summary>
public sealed class SessionContext : ITenantContext
{
    public string UserId { get; }
    public string CompanyId { get; }
    public IReadOnlyList<string> RoleKeys { get; }
    public PermissionSet Permissions { get; }

    public bool IsSuperAdmin => RoleKeys.Contains(Security.RoleKeys.SuperAdmin);
    public bool IsCompanyAdmin => RoleKeys.Contains(Security.RoleKeys.CompanyAdmin);

    public SessionContext(string userId, string companyId, IEnumerable<string> roleKeys, PermissionSet permissions)
    {
        UserId = string.IsNullOrWhiteSpace(userId) ? throw new ArgumentException("userId zorunlu") : userId;
        CompanyId = TenantGuard.Require(companyId);
        RoleKeys = roleKeys.ToList();
        Permissions = permissions;
    }
}
