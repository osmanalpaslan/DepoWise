namespace DepoWise.Web.Services;

/// <summary>Oturum durumu (JWT + kullanıcı bilgisi). Scoped — Blazor circuit başına.</summary>
public sealed class AuthState
{
    public string? Token { get; private set; }
    public string? UserId { get; private set; }
    public string? CompanyId { get; private set; }
    public bool IsSuperAdmin { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);

    public event Action? Changed;

    public void SignIn(string token, string userId, string companyId, bool isSuperAdmin)
    {
        Token = token; UserId = userId; CompanyId = companyId; IsSuperAdmin = isSuperAdmin;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        Token = UserId = CompanyId = null; IsSuperAdmin = false;
        Changed?.Invoke();
    }
}
