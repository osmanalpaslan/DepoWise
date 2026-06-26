namespace DepoWise.Application.Common;

public static class AuditActions
{
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
    public const string Restore = "restore";
    public const string Reverse = "reverse";
}

public sealed record AuditEntry(
    string CompanyId,
    string EntityType,
    string EntityId,
    string Action,
    string? UserId = null,
    string? BeforeJson = null,
    string? AfterJson = null,
    string? CorrelationId = null);
