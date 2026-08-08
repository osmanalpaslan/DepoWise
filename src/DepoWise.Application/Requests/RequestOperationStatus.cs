namespace DepoWise.Application.Requests;

/// <summary>
/// TALEP OPERASYON DURUMU (kullanıcı şartnamesi 2026-08-08, madde 5) — ONAY durumundan (<see cref="RequestStatus"/>)
/// TAMAMEN AYRIDIR ve onunla karıştırılmaz. Onay akışı: Taslak→Beklemede→Onaylı/Reddedildi/İptal.
/// Operasyon akışı ise talep ONAYLANDIKTAN SONRA başlar (<see cref="PendingOps"/> = "Beklemede").
///
/// İsimler ve SIRA kullanıcının şartnamesinden BİREBİR alınmıştır (projede önceden tanımı yoktu).
/// FAZ 1 KAPSAMI: yalnız veri modeli + gösterim + onay sonrası başlangıç değeri. Durumlar arası GEÇİŞ
/// KURALLARI (matris) bu fazda UYGULANMAZ — Faz 2 başında kullanıcıya onaylatılıp eklenecektir.
/// </summary>
public enum RequestOperationStatus
{
    PendingOps,          // Beklemede
    UnderReview,         // İnceleniyor
    FromWarehouse,       // Depodan Karşılanacak
    BranchTransfer,      // Şubeden Transfer Edilecek
    Purchasing,          // Satın Alma Sürecinde
    OrderPlaced,         // Tedarikçiye Sipariş Verildi
    OrderPreparing,      // Sipariş Hazırlanıyor
    Shipped,             // Gönderim İçin Yola Çıktı
    ArrivedAtBranch,     // Şubeye Ulaştı
    Delivered,           // Teslim Edildi
    PartiallyFulfilled,  // Kısmen Karşılandı
    Completed,           // Tamamlandı
    CancelledOps,        // İptal Edildi
}

/// <summary>Talep önceliği (şartname madde 18). Varsayılan <see cref="Normal"/> (kullanıcı kararı).</summary>
public enum RequestPriority { Normal, High, Urgent, Critical }

/// <summary>
/// Operasyon durumu / öncelik için TEK doğru kaynak: DB değeri ↔ Türkçe etiket ↔ RENK anahtarı.
/// Masaüstü (Avalonia) ve web (MudBlazor) aynı etiketleri ve aynı renk anahtarlarını buradan alır
/// (şartname madde 16: renkli durum gösterimi). Bilinmeyen değer olduğu gibi/varsayılan döner.
/// </summary>
public static class RequestOperationStatusInfo
{
    /// <summary>Sıra ŞARTNAMEDEKİ sıradır (UI listeleri bu sırayı kullanır).</summary>
    public static readonly IReadOnlyList<RequestOperationStatus> All = new[]
    {
        RequestOperationStatus.PendingOps,
        RequestOperationStatus.UnderReview,
        RequestOperationStatus.FromWarehouse,
        RequestOperationStatus.BranchTransfer,
        RequestOperationStatus.Purchasing,
        RequestOperationStatus.OrderPlaced,
        RequestOperationStatus.OrderPreparing,
        RequestOperationStatus.Shipped,
        RequestOperationStatus.ArrivedAtBranch,
        RequestOperationStatus.Delivered,
        RequestOperationStatus.PartiallyFulfilled,
        RequestOperationStatus.Completed,
        RequestOperationStatus.CancelledOps,
    };

    public static string ToDb(RequestOperationStatus s) => s switch
    {
        RequestOperationStatus.PendingOps => "pending_ops",
        RequestOperationStatus.UnderReview => "under_review",
        RequestOperationStatus.FromWarehouse => "from_warehouse",
        RequestOperationStatus.BranchTransfer => "branch_transfer",
        RequestOperationStatus.Purchasing => "purchasing",
        RequestOperationStatus.OrderPlaced => "order_placed",
        RequestOperationStatus.OrderPreparing => "order_preparing",
        RequestOperationStatus.Shipped => "shipped",
        RequestOperationStatus.ArrivedAtBranch => "arrived_at_branch",
        RequestOperationStatus.Delivered => "delivered",
        RequestOperationStatus.PartiallyFulfilled => "partially_fulfilled",
        RequestOperationStatus.Completed => "completed",
        RequestOperationStatus.CancelledOps => "cancelled_ops",
        _ => "pending_ops",
    };

    /// <summary>DB değeri → durum. NULL/boş/bilinmeyen → null (talep henüz operasyona girmemiştir → ekranda "—").</summary>
    public static RequestOperationStatus? FromDb(string? v) => v switch
    {
        "pending_ops" => RequestOperationStatus.PendingOps,
        "under_review" => RequestOperationStatus.UnderReview,
        "from_warehouse" => RequestOperationStatus.FromWarehouse,
        "branch_transfer" => RequestOperationStatus.BranchTransfer,
        "purchasing" => RequestOperationStatus.Purchasing,
        "order_placed" => RequestOperationStatus.OrderPlaced,
        "order_preparing" => RequestOperationStatus.OrderPreparing,
        "shipped" => RequestOperationStatus.Shipped,
        "arrived_at_branch" => RequestOperationStatus.ArrivedAtBranch,
        "delivered" => RequestOperationStatus.Delivered,
        "partially_fulfilled" => RequestOperationStatus.PartiallyFulfilled,
        "completed" => RequestOperationStatus.Completed,
        "cancelled_ops" => RequestOperationStatus.CancelledOps,
        _ => null,
    };

    public static string Label(RequestOperationStatus s) => s switch
    {
        RequestOperationStatus.PendingOps => "Beklemede",
        RequestOperationStatus.UnderReview => "İnceleniyor",
        RequestOperationStatus.FromWarehouse => "Depodan Karşılanacak",
        RequestOperationStatus.BranchTransfer => "Şubeden Transfer Edilecek",
        RequestOperationStatus.Purchasing => "Satın Alma Sürecinde",
        RequestOperationStatus.OrderPlaced => "Tedarikçiye Sipariş Verildi",
        RequestOperationStatus.OrderPreparing => "Sipariş Hazırlanıyor",
        RequestOperationStatus.Shipped => "Gönderim İçin Yola Çıktı",
        RequestOperationStatus.ArrivedAtBranch => "Şubeye Ulaştı",
        RequestOperationStatus.Delivered => "Teslim Edildi",
        RequestOperationStatus.PartiallyFulfilled => "Kısmen Karşılandı",
        RequestOperationStatus.Completed => "Tamamlandı",
        RequestOperationStatus.CancelledOps => "İptal Edildi",
        _ => "—",
    };

    /// <summary>Operasyon durumu YOKSA (onaylanmamış talep) ekranda gösterilecek metin.</summary>
    public const string None = "—";

    public static string LabelOrDash(string? dbValue)
        => FromDb(dbValue) is { } s ? Label(s) : None;

    /// <summary>Renk anahtarı (madde 16) — platform-bağımsız: neutral | info | warning | primary | success | danger.
    /// Masaüstü rozet stiline, web ise MudBlazor rengine kendi tarafında eşler.</summary>
    public static string Color(RequestOperationStatus s) => s switch
    {
        RequestOperationStatus.PendingOps => "neutral",
        RequestOperationStatus.UnderReview => "info",
        RequestOperationStatus.FromWarehouse => "primary",
        RequestOperationStatus.BranchTransfer => "primary",
        RequestOperationStatus.Purchasing => "warning",
        RequestOperationStatus.OrderPlaced => "warning",
        RequestOperationStatus.OrderPreparing => "warning",
        RequestOperationStatus.Shipped => "info",
        RequestOperationStatus.ArrivedAtBranch => "info",
        RequestOperationStatus.Delivered => "success",
        RequestOperationStatus.PartiallyFulfilled => "warning",
        RequestOperationStatus.Completed => "success",
        RequestOperationStatus.CancelledOps => "danger",
        _ => "neutral",
    };

    public static string ColorOrNeutral(string? dbValue)
        => FromDb(dbValue) is { } s ? Color(s) : "neutral";
}

/// <summary>Öncelik için TEK doğru kaynak (DB değeri ↔ etiket ↔ renk). Varsayılan: Normal.</summary>
public static class RequestPriorityInfo
{
    public static readonly IReadOnlyList<RequestPriority> All = new[]
    {
        RequestPriority.Normal, RequestPriority.High, RequestPriority.Urgent, RequestPriority.Critical,
    };

    public static string ToDb(RequestPriority p) => p switch
    {
        RequestPriority.High => "high",
        RequestPriority.Urgent => "urgent",
        RequestPriority.Critical => "critical",
        _ => "normal",
    };

    public static RequestPriority FromDb(string? v) => v switch
    {
        "high" => RequestPriority.High,
        "urgent" => RequestPriority.Urgent,
        "critical" => RequestPriority.Critical,
        _ => RequestPriority.Normal,
    };

    public static string Label(RequestPriority p) => p switch
    {
        RequestPriority.High => "Yüksek",
        RequestPriority.Urgent => "Acil",
        RequestPriority.Critical => "Kritik",
        _ => "Normal",
    };

    public static string LabelOf(string? dbValue) => Label(FromDb(dbValue));

    public static string Color(RequestPriority p) => p switch
    {
        RequestPriority.High => "info",
        RequestPriority.Urgent => "warning",
        RequestPriority.Critical => "danger",
        _ => "neutral",
    };

    public static string ColorOf(string? dbValue) => Color(FromDb(dbValue));
}
