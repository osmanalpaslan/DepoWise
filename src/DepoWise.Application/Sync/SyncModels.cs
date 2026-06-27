namespace DepoWise.Application.Sync;

public enum SyncOpResult { Accepted, AlreadyApplied, Rejected, Conflict }

/// <summary>Bir senkron işlemi. operation_id idempotency; base_version düşük-riskli merge için.</summary>
public sealed record SyncOperation(string OperationId, string EntityType, string EntityId, string PayloadJson, long? BaseVersion = null);

public sealed record SyncOpOutcome(string OperationId, SyncOpResult Result, string? Reason = null);

public sealed record ServerChange(long Seq, string EntityType, string EntityId, string PayloadJson);

public sealed record PullPage(IReadOnlyList<ServerChange> Items, long NextCursor);

/// <summary>
/// Kritik entity'ler — basit LWW YASAK; operation_id + sunucu doğrulaması zorunlu (analiz §8).
/// Düşük-riskli entity'lerde version/updated_at ile kontrollü merge.
/// </summary>
public static class SyncPolicy
{
    private static readonly HashSet<string> Critical = new(StringComparer.Ordinal)
    {
        "stock_movement", "vehicle_maintenance", "fuel_distribution", "vehicle_meter", "material_request_approval",
    };

    public static bool IsCritical(string entityType) => Critical.Contains(entityType);
}

/// <summary>Cihaz token / hassas değer koruma (Windows DPAPI; testte passthrough).</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
