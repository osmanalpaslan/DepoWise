namespace DepoWise.Infrastructure.Database;

/// <summary>
/// Tenant + soft-delete + keyset için tekrar kullanılan SQL parçaları. Tüm liste/okuma
/// sorguları bu yardımcıları kullanır; tenant filtresi atlanırsa testler kırılır.
/// </summary>
public static class TenantSql
{
    /// <summary>company_id eşitliği + (opsiyonel) is_deleted=0 koşulu.</summary>
    public static string ScopePredicate(bool includeSoftDeleteFilter = true)
        => includeSoftDeleteFilter
            ? "company_id = @companyId AND is_deleted = 0"
            : "company_id = @companyId";

    /// <summary>Kararlı keyset sıralaması: created_at DESC, id DESC (benzersiz tie-break).</summary>
    public const string KeysetOrderBy = "ORDER BY created_at DESC, id DESC";

    /// <summary>İmleçten sonraki kayıtlar için keyset koşulu (created_at,id) &lt; (cursor).</summary>
    public const string KeysetAfterPredicate =
        "(created_at < @cursorCreatedAt OR (created_at = @cursorCreatedAt AND id < @cursorId))";
}
