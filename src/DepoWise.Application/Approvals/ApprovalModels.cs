namespace DepoWise.Application.Approvals;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187 + ADR-188) — ONAY ZİNCİRİ SÖZLEŞMESİ ═══
///
/// Onay motorunun TANIDIĞI varlık türleri. <b>PK-EK-01:</b> kapsam Malzeme Talebi + Satın Alma'dır;
/// <b>İş Emri KAPSAM DIŞIDIR</b> ve buraya EKLENMEZ.
/// </summary>
public static class ApprovalEntityTypes
{
    public const string MaterialRequest = "material_request";
    public const string PurchaseOrder = "purchase_order";

    public static readonly string[] All = { MaterialRequest, PurchaseOrder };

    public static bool IsKnown(string? t) => t is not null && Array.IndexOf(All, t) >= 0;
}

/// <summary>Süreç ve adım durumları. Yeni değer eklemek ürün kararıdır — kendiliğinden genişletilmez.</summary>
public static class ApprovalStatus
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    /// <summary>Bir adım reddedildiğinde ARDINDAKİ bekleyen adımlar bu duruma geçer (silinmez — İK-10
    /// gereği geçmiş görünür kalır).</summary>
    public const string Skipped = "skipped";
}

/// <summary>
/// Hiyerarşi kuralları. <b>İK-2:</b> azami derinlik <b>4</b>.
///
/// ADR-187'deki örnek bağlayıcıdır: <c>A → B → C → D</c> GEÇERLİ, <c>A → B → C → D → E</c> GEÇERSİZ.
/// Yani bir zincirde <b>en çok 4 DÜĞÜM</b> bulunur → bir kullanıcının üstünde en çok <b>3 onaycı</b>
/// olabilir. Bu ayrım bilinçlidir ve testle kilitlenir.
/// </summary>
public static class HierarchyRules
{
    /// <summary>Bir zincirdeki azami düğüm sayısı (kullanıcı DAHİL).</summary>
    public const int MaxChainNodes = 4;

    /// <summary>Bir kullanıcının üzerindeki azami onaycı sayısı = düğüm sayısı − 1 (kullanıcının kendisi).</summary>
    public const int MaxApprovers = MaxChainNodes - 1;
}

/// <summary>Kullanıcı → üst ilişkisi. <b>PK-EK-02:</b> `users` tablosunda DEĞİL, ayrı tabloda tutulur.</summary>
public sealed record HierarchyEdge(
    string Id,
    string CompanyId,
    string UserId,
    string ManagerUserId,
    long CreatedAt,
    long UpdatedAt);

/// <summary>Bir onay süreci (snapshot başlığı).</summary>
public sealed record ApprovalInstance(
    string Id,
    string CompanyId,
    string EntityType,
    string EntityId,
    string Status,
    string? StartedBy,
    long StartedAt,
    long SnapshotAt,
    long? ClosedAt);

/// <summary>
/// ═══ ALT FAZ 3 — "ONAYLAMALARIM" SATIRI ═══
///
/// Kullanıcıya düşen ve SIRASI GELMİŞ tek bir onay adımının ekran görünümü. Alanların tamamı MEVCUT
/// veri modelinden gelir (uydurma alan yoktur): belge/sipariş no ve tarih ilgili varlık tablosundan,
/// sıra bilgisi <c>approval_step</c>'ten okunur.
///
/// <b>Yeni bir onay kataloğu/motoru DEĞİLDİR</b> — yalnız mevcut motorun verisinin projeksiyonudur.
/// </summary>
/// <param name="StepNo">Kaçıncı adımdayız (1'den başlar).</param>
/// <param name="TotalSteps">Zincirin toplam adım sayısı — kullanıcı "3 adımdan 2.si" bilgisini görür.</param>
/// <param name="DocNo">Malzeme Talebi belge no ya da Satın Alma sipariş no (varlığa göre).</param>
/// <param name="EntityDate">Talebin/siparişin İŞ GÜNÜ tarihi (kayıt zaman damgası değil — ADR-184).</param>
/// <param name="StartedBy">Süreci başlatan kullanıcı (self-approval kapısının dayanağı).</param>
public sealed record PendingApprovalRow(
    string InstanceId,
    string StepId,
    string EntityType,
    string EntityId,
    long StepNo,
    long TotalSteps,
    string? DocNo,
    long? EntityDate,
    string? StartedBy,
    long StartedAt)
{
    /// <summary>Kullanıcıya gösterilen süreç türü adı.</summary>
    public string EntityLabel => EntityType switch
    {
        ApprovalEntityTypes.MaterialRequest => "Malzeme Talebi",
        ApprovalEntityTypes.PurchaseOrder => "Satın Alma",
        _ => EntityType,
    };

    /// <summary>"2 / 3" biçiminde sıra göstergesi.</summary>
    public string StepLabel => $"{StepNo} / {TotalSteps}";
}

/// <summary>Zincirin tek adımı. <c>ApproverUserId</c> <b>SNAPSHOT</b>'tır — sonradan asla yeniden
/// hesaplanmaz (PK-EK-04).</summary>
public sealed record ApprovalStepRow(
    string Id,
    string CompanyId,
    string InstanceId,
    long StepNo,
    string ApproverUserId,
    string Status,
    string? ActedBy,
    long? ActedAt,
    string? Reason);
