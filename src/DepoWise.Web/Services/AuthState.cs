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
    public void SetModules(IReadOnlyList<MenuModule> m) { _modules = m; Changed?.Invoke(); }

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
        Token = UserId = CompanyId = BranchId = CompanyName = UserName = null; IsSuperAdmin = false;
        _modules = Array.Empty<MenuModule>();
        Changed?.Invoke();
    }
}
