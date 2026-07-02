namespace DepoWise.Web.Services;

/// <summary>Oturum durumu (JWT + kullanıcı). Tarayıcıda saklanır (ProtectedLocalStorage) → sayfa geçişi/yenilemede korunur.</summary>
public sealed class AuthState
{
    public string? Token { get; private set; }
    public string? UserId { get; private set; }
    public string? CompanyId { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

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

    public void SignIn(string token, string userId, string companyId, bool isSuperAdmin)
    {
        Token = token; UserId = userId; CompanyId = companyId; IsSuperAdmin = isSuperAdmin;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Token = UserId = CompanyId = null; IsSuperAdmin = false;
        _modules = Array.Empty<MenuModule>();
        Changed?.Invoke();
    }
}
