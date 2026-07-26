namespace DepoWise.Web.Services;

/// <summary>Oturum durumu (JWT + kullanıcı). Tarayıcıda saklanır (ProtectedLocalStorage) → sayfa geçişi/yenilemede korunur.</summary>
public sealed class AuthState
{
    public string? Token { get; private set; }
    public string? UserId { get; private set; }
    public string? CompanyId { get; private set; }
    public string? BranchId { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    /// <summary>Firma adı (görünen). GUID yerine üst bar/ana ekranda gösterilir. Boşsa CompanyId'ye düşülebilir.</summary>
    public string? CompanyName { get; private set; }
    /// <summary>Giriş yapan kullanıcının görünen adı/kullanıcı adı (üst bar).</summary>
    public string? UserName { get; private set; }
    /// <summary>Üst bar/etiketlerde gösterilecek firma metni — ad varsa ad, yoksa id.</summary>
    public string CompanyDisplay => string.IsNullOrWhiteSpace(CompanyName) ? (CompanyId ?? "") : CompanyName!;

    /// <summary>Tarayıcı deposundan yükleme denendi mi (guard erken yönlendirmesin diye).</summary>
    public bool Loaded { get; set; }

    // Kullanıcının görebileceği modüller + yetkileri (masaüstüyle aynı; menü + buton görünürlüğü buna göre).
    private IReadOnlyList<MenuModule> _modules = Array.Empty<MenuModule>();
    public IReadOnlyList<MenuModule> Modules => _modules;
    /// <summary>Kullanıcı Admin veya Süper Admin mi (menü yanıtından). Admin-only alan görünürlüğü için (#5).</summary>
    public bool IsAdmin { get; private set; }
    /// <summary>Kısıtlı Süper Admin rolü (menü yanıtından). "Yedek Yönetimi" gibi süper+kısıtlı-süper-only ekranlar için.</summary>
    public bool IsRestrictedSuperAdmin { get; private set; }
    public void SetModules(IReadOnlyList<MenuModule> m, bool isAdmin = false, bool isRestrictedSuperAdmin = false)
    { _modules = m; IsAdmin = isAdmin || IsSuperAdmin; IsRestrictedSuperAdmin = isRestrictedSuperAdmin; Changed?.Invoke(); }

    public bool CanView(string key) => IsSuperAdmin || _modules.Any(x => x.Key == key);
    public bool CanCreate(string key) => IsSuperAdmin || (_modules.FirstOrDefault(x => x.Key == key)?.Create ?? false);
    public bool CanEdit(string key) => IsSuperAdmin || (_modules.FirstOrDefault(x => x.Key == key)?.Edit ?? false);
    public bool CanDelete(string key) => IsSuperAdmin || (_modules.FirstOrDefault(x => x.Key == key)?.Delete ?? false);

    public event Action? Changed;

    public void SignIn(string token, string userId, string companyId, bool isSuperAdmin, string? branchId = null,
        string? companyName = null, string? userName = null)
    {
        Token = token; UserId = userId; CompanyId = companyId; IsSuperAdmin = isSuperAdmin; BranchId = branchId;
        CompanyName = companyName; UserName = userName;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Token = UserId = CompanyId = BranchId = CompanyName = UserName = null; IsSuperAdmin = false; IsAdmin = false;
        IsRestrictedSuperAdmin = false;
        _modules = Array.Empty<MenuModule>();
        Changed?.Invoke();
    }
}
