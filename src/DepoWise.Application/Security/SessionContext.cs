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

    /// <summary>Süper Admin'in bu kullanıcıya verdiği "tüm şube verisini görme/işleme" yetkisi.</summary>
    public bool CanViewAllBranches { get; }

    /// <summary>Login'de seçilen ÇALIŞMA şubesi — bu oturumda girilen işlem kayıtları bununla etiketlenir
    /// (op_branch_id). Null = şubesiz / firma geneli / "Tüm Şubeler". Oturum kurulduktan sonra atanır.</summary>
    public string? OperatingBranchId { get; set; }

    public bool IsSuperAdmin => RoleKeys.Contains(Security.RoleKeys.SuperAdmin);
    public bool IsCompanyAdmin => RoleKeys.Contains(Security.RoleKeys.CompanyAdmin);

    public SessionContext(string userId, string companyId, IEnumerable<string> roleKeys, PermissionSet permissions,
        bool canViewAllBranches = false)
    {
        UserId = string.IsNullOrWhiteSpace(userId) ? throw new ArgumentException("userId zorunlu") : userId;
        CompanyId = TenantGuard.Require(companyId);
        RoleKeys = roleKeys.ToList();
        Permissions = permissions;
        CanViewAllBranches = canViewAllBranches;
    }
}
