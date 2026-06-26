namespace DepoWise.Domain.Common;

/// <summary>
/// Tüm tenant-kapsamlı kayıtların temel sözleşmesi. company_id yalnız güvenilir
/// server/session context'ten atanır (analiz §9); ana kayıt kimliği çakışmasızdır (UUID/ULID).
/// İskelet aşamasında yalnız sözleşme; alanlar ilgili modül fazlarında genişletilecek.
/// </summary>
public abstract class TenantEntity
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string CompanyId { get; init; } = string.Empty;
    public bool IsDeleted { get; init; }
}
