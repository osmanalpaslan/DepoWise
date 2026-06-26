namespace DepoWise.Application.Requests;

public enum RequestStatus { Draft, Pending, Approved, Rejected, Cancelled }

/// <summary>
/// Talep durum makinesi (analiz §6.12). Geçişler kısıtlı; onay stok DEĞİŞTİRMEZ.
/// Web `requestStatus.ts` ile aynı kurallar.
/// </summary>
public static class RequestStatusMachine
{
    private static readonly Dictionary<RequestStatus, RequestStatus[]> Allowed = new()
    {
        [RequestStatus.Draft] = new[] { RequestStatus.Pending, RequestStatus.Cancelled },
        [RequestStatus.Pending] = new[] { RequestStatus.Approved, RequestStatus.Rejected, RequestStatus.Cancelled },
        [RequestStatus.Approved] = Array.Empty<RequestStatus>(),   // terminal (stok çıkışı ayrı işlem)
        [RequestStatus.Rejected] = Array.Empty<RequestStatus>(),
        [RequestStatus.Cancelled] = Array.Empty<RequestStatus>(),
    };

    public static bool CanTransition(RequestStatus from, RequestStatus to)
        => Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool IsTerminal(RequestStatus s) => Allowed[s].Length == 0;

    public static string ToDb(RequestStatus s) => s switch
    {
        RequestStatus.Draft => "draft",
        RequestStatus.Pending => "pending",
        RequestStatus.Approved => "approved",
        RequestStatus.Rejected => "rejected",
        RequestStatus.Cancelled => "cancelled",
        _ => "draft",
    };

    public static RequestStatus FromDb(string s) => s switch
    {
        "pending" => RequestStatus.Pending,
        "approved" => RequestStatus.Approved,
        "rejected" => RequestStatus.Rejected,
        "cancelled" => RequestStatus.Cancelled,
        _ => RequestStatus.Draft,
    };
}
