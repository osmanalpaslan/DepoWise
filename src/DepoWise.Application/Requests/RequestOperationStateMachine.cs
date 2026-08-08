using DepoWise.Application.Security;

namespace DepoWise.Application.Requests;

/// <summary>
/// TALEP OPERASYON DURUM MAKİNESİ (kullanıcı onayı 2026-08-08, Faz 2).
/// Onay durum makinesinden (<see cref="RequestStatusMachine"/>) TAMAMEN AYRIDIR; onu değiştirmez.
///
/// Matris kullanıcı tarafından ONAYLANMIŞTIR — kendiliğinden değiştirilmez. Kullanıcı düzeltmesi:
/// "Teslim Edildi → Kısmen Karşılandı" geçişi KALDIRILDI (eksik teslim mantığı Faz 3'te miktar bazlı
/// karşılama ile ele alınacak).
///
/// ORTAK KURAL: Tamamlandı ve İptal Edildi TERMİNAL'dir (geçiş yok); diğer tüm durumlardan İptal Edildi'ye
/// geçilebilir. Aynı duruma tekrar geçiş yoktur (gereksiz geçmiş kaydı üretmez).
/// </summary>
public static class RequestOperationStateMachine
{
    private static readonly Dictionary<RequestOperationStatus, RequestOperationStatus[]> Allowed = new()
    {
        [RequestOperationStatus.PendingOps] = new[]
        {
            RequestOperationStatus.UnderReview, RequestOperationStatus.FromWarehouse,
            RequestOperationStatus.BranchTransfer, RequestOperationStatus.Purchasing,
        },
        [RequestOperationStatus.UnderReview] = new[]
        {
            RequestOperationStatus.FromWarehouse, RequestOperationStatus.BranchTransfer,
            RequestOperationStatus.Purchasing,
        },
        // Elden teslim (kullanıcı onayı): sevkiyat olmadan doğrudan Teslim Edildi olabilir.
        // Kaynak değişikliği (kullanıcı onayı): depoda çıkmazsa şubeye/satın almaya kaydırılabilir.
        [RequestOperationStatus.FromWarehouse] = new[]
        {
            RequestOperationStatus.Shipped, RequestOperationStatus.Delivered,
            RequestOperationStatus.PartiallyFulfilled, RequestOperationStatus.BranchTransfer,
            RequestOperationStatus.Purchasing,
        },
        [RequestOperationStatus.BranchTransfer] = new[]
        {
            RequestOperationStatus.Shipped, RequestOperationStatus.PartiallyFulfilled,
            RequestOperationStatus.FromWarehouse, RequestOperationStatus.Purchasing,
        },
        [RequestOperationStatus.Purchasing] = new[]
        {
            RequestOperationStatus.OrderPlaced, RequestOperationStatus.FromWarehouse,
        },
        // Tedarikçi doğrudan sevk edebilir → "Sipariş Hazırlanıyor" atlanabilir (kullanıcı onayı).
        [RequestOperationStatus.OrderPlaced] = new[]
        {
            RequestOperationStatus.OrderPreparing, RequestOperationStatus.Shipped,
        },
        [RequestOperationStatus.OrderPreparing] = new[] { RequestOperationStatus.Shipped },
        [RequestOperationStatus.Shipped] = new[]
        {
            RequestOperationStatus.ArrivedAtBranch, RequestOperationStatus.Delivered,
            RequestOperationStatus.PartiallyFulfilled,
        },
        [RequestOperationStatus.ArrivedAtBranch] = new[]
        {
            RequestOperationStatus.Delivered, RequestOperationStatus.PartiallyFulfilled,
        },
        // KULLANICI DÜZELTMESİ: "Teslim Edildi → Kısmen Karşılandı" YOK (Faz 3'te ele alınacak).
        [RequestOperationStatus.Delivered] = new[] { RequestOperationStatus.Completed },
        // Kalan miktar işlem görmeye devam eder (§7) — kaynaklara geri dönebilir.
        [RequestOperationStatus.PartiallyFulfilled] = new[]
        {
            RequestOperationStatus.FromWarehouse, RequestOperationStatus.BranchTransfer,
            RequestOperationStatus.Purchasing, RequestOperationStatus.Delivered,
            RequestOperationStatus.Completed,
        },
        [RequestOperationStatus.Completed] = Array.Empty<RequestOperationStatus>(),   // terminal
        [RequestOperationStatus.CancelledOps] = Array.Empty<RequestOperationStatus>(),// terminal
    };

    public static bool IsTerminal(RequestOperationStatus s)
        => s is RequestOperationStatus.Completed or RequestOperationStatus.CancelledOps;

    /// <summary>Geçiş serbest mi? İptal, terminal olmayan HER durumdan yapılabilir (ortak kural).</summary>
    public static bool CanTransition(RequestOperationStatus from, RequestOperationStatus to)
    {
        if (from == to) return false;                    // aynı duruma geçiş yok
        if (IsTerminal(from)) return false;              // Tamamlandı/İptal geri alınamaz
        if (to == RequestOperationStatus.CancelledOps) return true;
        return Allowed.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    /// <summary>Bu durumdan gidilebilecek durumlar (UI'da yalnız bunlar gösterilir) — şartname sırasında.</summary>
    public static IReadOnlyList<RequestOperationStatus> NextStates(RequestOperationStatus from)
        => RequestOperationStatusInfo.All.Where(t => CanTransition(from, t)).ToList();

    // ── YETKİ (kullanıcı onayı 2026-08-08) ──
    public const string ModuleOps = "request_ops";
    public const string ModuleWarehouse = "request_ops_warehouse";
    public const string ModulePurchase = "request_ops_purchase";

    /// <summary>
    /// Hedef duruma göre GEREKEN ek yetki modülü:
    ///  • Satın alma adımları → request_ops_purchase
    ///  • Depo / sevkiyat / teslim adımları → request_ops_warehouse
    ///  • Diğer (genel operasyon: inceleme, kaynak seçimi, kısmen, tamamlandı, iptal) → yalnız request_ops
    /// </summary>
    public static string? RequiredModuleFor(RequestOperationStatus to) => to switch
    {
        RequestOperationStatus.Purchasing or RequestOperationStatus.OrderPlaced
            or RequestOperationStatus.OrderPreparing => ModulePurchase,
        RequestOperationStatus.FromWarehouse or RequestOperationStatus.Shipped
            or RequestOperationStatus.ArrivedAtBranch or RequestOperationStatus.Delivered => ModuleWarehouse,
        _ => null,
    };

    /// <summary>Kullanıcı bu geçişi yapabilir mi? Admin/süper admin bypass (kullanıcı kararı).
    /// Genel operasyon yetkisi (request_ops Edit) HER geçişte gereklidir; ek modül varsa o da aranır.</summary>
    public static bool CanUserPerform(SessionContext s, RequestOperationStatus to)
    {
        if (AccessControl.IsAdmin(s)) return true;
        if (!AccessControl.Can(s, ModuleOps, PermissionAction.Edit)) return false;
        var extra = RequiredModuleFor(to);
        return extra is null || AccessControl.Can(s, extra, PermissionAction.Edit);
    }
}
