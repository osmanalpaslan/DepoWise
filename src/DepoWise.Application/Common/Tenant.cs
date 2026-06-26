namespace DepoWise.Application.Common;

/// <summary>
/// Güvenilir tenant bağlamı. company_id YALNIZ server/session context'ten gelir; kullanıcı
/// payload'ından kabul edilmez (analiz §9). Servis sınırında fail-closed doğrulanır.
/// </summary>
public interface ITenantContext
{
    string CompanyId { get; }
}

public sealed class TenantContext : ITenantContext
{
    public string CompanyId { get; }

    public TenantContext(string companyId)
    {
        CompanyId = TenantGuard.Require(companyId);
    }
}

public static class TenantGuard
{
    public static string Require(string? companyId)
    {
        if (string.IsNullOrWhiteSpace(companyId))
            throw new InvalidOperationException("Tenant ihlali: company_id zorunlu (fail-closed).");
        return companyId;
    }
}
