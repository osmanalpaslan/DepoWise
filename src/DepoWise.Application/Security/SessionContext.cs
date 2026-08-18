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

    /// <summary>Admin ile Süper Admin arası rol. Admin bypass'ı YOKTUR (yalnız açıkça verilen yetkiler);
    /// ek olarak süper adminin devrettiği süper-admin-only ekranlara erişebilir.</summary>
    public bool IsRestrictedSuperAdmin => RoleKeys.Contains(Security.RoleKeys.RestrictedSuperAdmin);

    /// <summary>Rol Yetki Kontrol ile bu kullanıcının ROLÜNE kapatılmış modüller (role_grant_limits).
    /// Admin bypass'ından ÖNCE uygulanır → kapalı ekran adminde de açılmaz. Süper adminde daima boştur.
    /// Oturum kurulurken AuthService doldurur.</summary>
    public IReadOnlySet<string> BlockedModules { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// G4-3b — Kullanıcının ERİŞMEYE YETKİLİ olduğu şubeler (user_scopes). null/boş = açık kapsam YOK.
    /// <b>OperatingBranchId ile karıştırılmaz:</b> o oturumun ÇALIŞMA şubesidir (görünüm tercihi),
    /// bu ise GÜVENLİK kapısıdır. Tek yorumlayıcısı BranchAccess'tir.
    /// Oturum kurulurken AuthService doldurur.
    /// </summary>
    public IReadOnlyList<string>? ScopeBranchIds { get; set; }

    /// <summary>Kullanıcının kendi (ana) şubesi — users.branch_id. Açık kapsam yoksa
    /// BranchAccess bunu tek izinli şube olarak kullanır.</summary>
    public string? HomeBranchId { get; set; }

    /// <summary>
    /// ŞB-04 — ŞUBE AĞACI: <c>üst şube → tüm alt şubeleri</c> (geçişli kapanış). Oturum kurulurken
    /// bir kez yüklenir (<c>BranchTree.LoadDescendants</c>); tek yorumlayıcısı BranchAccess'tir.
    ///
    /// <b>null = ağaç yok / yüklenmedi</b> → davranış ŞB-04 ÖNCESİYLE birebir aynıdır (fail-safe:
    /// haritayı doldurmayan bir kod yolu kapsamı kazara GENİŞLETMEZ).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? BranchDescendants { get; set; }

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
