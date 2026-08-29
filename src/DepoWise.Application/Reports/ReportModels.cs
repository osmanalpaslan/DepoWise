using DepoWise.Application.Security;

namespace DepoWise.Application.Reports;

/// <summary>Sayısal hücre (2026-08-08 revizesi): HAM değer + GÖRÜNTÜ metni ayrı. Rapor sayısal kolonlarda bunu
/// üretir → ortak tablo sıralama/filtreyi HAM <see cref="Value"/> ile yapar, ekranda <see cref="Display"/> gösterir
/// (ör. "₺ 12.345,67", "1.250 km", boş için "-"). Backend'de string'e çevrilmez; değer korunur (kullanıcı isteği).</summary>
public sealed record NumCell(double Value, string Display);

/// <summary>Genel tablo modeli (rapor + Excel export ortak).</summary>
/// <param name="Numeric">İsteğe bağlı: kolon başına "sayısal mı" bayrakları (Headers ile aynı sırada). Verilirse
/// ortak tablo kolon-tipini örneklemeye çalışmaz, bunu kullanır. null → eski davranış (örnekleme).</param>
/// <param name="TotalRow">İsteğe bağlı: en altta SABİT (pinned) toplam satırı — normal filtre/sıralamaya dahil
/// DEĞİL, kolon-hizalı, görsel ayrı. null → toplam satırı yok. Genel amaçlı (her rapor kullanabilir).</param>
public sealed record TableModel(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<bool>? Numeric = null,
    IReadOnlyList<object?>? TotalRow = null);

/// <summary>
/// Rapor isteği. Ağır rapor kullanıcı Sorgula/Filtrele demeden çalışmaz → <see cref="Executed"/>
/// yalnız kullanıcı tetiklemesiyle true olur.
/// </summary>
public sealed record ReportRequest(
    bool Executed,
    long? FromDate = null,
    long? ToDate = null,
    IReadOnlyList<string>? BranchIds = null,
    IReadOnlyList<string>? VehicleIds = null,
    string? CompanyId = null,
    IReadOnlyList<string>? VehicleTypeIds = null,
    IReadOnlyList<string>? MaintenanceDefIds = null,   // Bakım Raporu: bakım tanımı (ana) filtresi
    IReadOnlyList<string>? TechnicianIds = null,       // Bakım Raporu: teknisyen (personel) filtresi
    IReadOnlyList<string>? SupplierIds = null,         // Depo Girişi: tedarikçi filtresi
    IReadOnlyList<string>? RequesterIds = null,        // Talep Raporu: talep eden (personel) filtresi
    IReadOnlyList<string>? Statuses = null,            // Talep Raporu: durum (draft|pending|approved|rejected|cancelled)
    // STK-06: STOK LOKASYONU filtresi (depo/şantiye). Boş/null = TÜM ŞUBELER (firma toplamı, ATANMAMIŞ dahil).
    // Boş METİN ("") = 📦 ATANMAMIŞ (lokasyonu bilinmeyen geçmiş stok) — gerçek bir depo DEĞİLDİR.
    // ⚠️ BranchIds ile karıştırılmaz: o, kaydı işleyen şubedir; bu, stoğun fiziksel yeridir.
    IReadOnlyList<string>? LocationIds = null,

    // STK-10b-1: STOK HAREKET TÜRÜ filtresi. Boş/null = TÜM türler. Değerler KANONİK `movement_type`
    // anahtarlarıdır (kullanıcıya gösterilen ETİKET DEĞİL) — seçenekler
    // `DepoWise.Application.Ui.MovementTypeOptions`'tan gelir (STK-B1 tek kaynak).
    // ⚠️ ALAN SONA EKLENDİ: bu kayıt POZİSYONEL olarak da kuruluyor (API uçları) — araya eklemek
    // mevcut çağrıların argümanlarını sessizce kaydırırdı (ör. LocationIds → MovementTypes).
    IReadOnlyList<string>? MovementTypes = null,

    // STK-10b-2 (ADR-104 / KARAR-10): SERBEST METİN ARAMA — Stok Hareketleri raporunda
    // malzeme kodu · malzeme adı · not · fatura no · belge no üzerinde OR araması.
    // ⚠️ SKALER (tek metin), liste DEĞİL — diğer filtrelerden farkı budur.
    // Boş/yalnız-boşluk = FİLTRE YOK (mevcut `SearchMovements` semantiği birebir korundu).
    // ⚠️ ALAN SONA EKLENDİ (aynı gerekçe: kayıt API uçlarında POZİSYONEL de kuruluyor).
    string? SearchText = null,

    // STK-10b-3: MALZEME filtresi. Boş/null = TÜM malzemeler. Değerler `materials.id`'dir
    // (kullanıcıya gösterilen kod/ad DEĞİL) — arayüzde arama ile seçilir, listeler ÖNCEDEN İNDİRİLMEZ.
    // ⚠️ LİSTE olması bilinçlidir: diğer kimlik filtreleriyle (BranchIds/VehicleIds/…) aynı sözleşme;
    // arayüz bugün TEK malzeme seçtirir → 0 veya 1 elemanlı gelir. Yabancı firmanın malzeme kimliği
    // eşleşmez (sorgu zaten company_id'ye kilitli) → fail-closed.
    // ⚠️ ALAN SONA EKLENDİ (aynı gerekçe: kayıt API uçlarında POZİSYONEL de kuruluyor).
    IReadOnlyList<string>? MaterialIds = null,

    // G4-4: CARİ filtresi (ön muhasebe raporları). Boş/null = TÜM cariler.
    // ⚠️ ALAN SONA EKLENDİ (kayıt API uçlarında POZİSYONEL de kuruluyor — araya eklemek
    // mevcut çağrıların argümanlarını sessizce kaydırırdı).
    IReadOnlyList<string>? PartyIds = null,

    // ADR-182 (ARA İŞ 2 / S4, PK-D1=A): GÜNLÜK FAALİYET KAYIT TİPİ filtresi. Değerler SABİT listeden
    // gelir (DepoWise.Application.Ui.DailyActivityTypeOptions) — DB'den değil. Boş/null = TÜM tipler
    // (kullanıcı kuralı: hiçbir tip seçilmezse hepsi listelenir).
    // ⚠️ ALAN SONA EKLENDİ (kayıt API uçlarında POZİSYONEL de kuruluyor — araya eklemek mevcut
    // çağrıların argümanlarını sessizce kaydırırdı).
    IReadOnlyList<string>? ActivityTypes = null);

public static class ReportGate
{
    /// <summary>Filtre/Sorgula tıklanmadan (Executed=false) rapor çalıştırılamaz.</summary>
    public static void EnsureRunnable(ReportRequest req)
    {
        if (!req.Executed)
            throw new InvalidOperationException("Rapor, Sorgula/Filtrele tıklanmadan çalışmaz.");
    }

    /// <summary>Firma alanı yalnız Süper Admin'e gösterilir; diğer adminler kendi firmasına kilitli.</summary>
    public static bool ShowCompanyFilter(SessionContext s) => s.IsSuperAdmin;

    /// <summary>Hedef firma: Süper Admin seçebilir; diğerleri yalnız oturum firması (fail-closed).</summary>
    public static string ResolveCompany(SessionContext s, string? requested)
        => TenantAccessGuard.ResolveCompanyId(s, requested);
}

// BLD-01 (ADR-172) / DYR-01 (ADR-173): yeni türler SONA eklenir — mevcut değerlerin sırası/serileştirmesi DEĞİŞMEZ.
public enum AlertKind { Maintenance, Inspection, LowStock, Fuel, Document, WorkOrder, Request, Announcement }

public sealed record DashboardAlert(AlertKind Kind, string Title, string Detail, string NavigateKey, bool IsCritical, string? EntityId = null, bool Read = false, string? SignatureOverride = null)
{
    /// <summary>Uyarı tipine göre ikon (emoji) — ana ekran uyarı listesinde gösterilir.</summary>
    public string Icon => Kind switch
    {
        AlertKind.Maintenance => "🔧",
        AlertKind.Inspection => "🛡️",
        AlertKind.LowStock => "📦",
        AlertKind.Fuel => "⛽",
        AlertKind.Document => "📁",
        AlertKind.WorkOrder => "📋",
        AlertKind.Request => "📄",
        AlertKind.Announcement => "📢",
        _ => "⚠️",
    };

    /// <summary>BLD-01: okunmuş satır listede soluk görünür (okundu ayrımı).</summary>
    public double RowOpacity => Read ? 0.55 : 1.0;

    /// <summary>Kalıcı kimlik (kullanıcı "okundu" işaretini buna göre saklar). #18</summary>
    public string Key => $"{Kind}|{EntityId}|{Title}";
    /// <summary>Uyarının o anki hali — değişince (kötüleşince) "okundu" düşer, uyarı ana ekranda yeniden görünür.
    /// DYR-01: Detail'i sabit olan kaynaklar (duyuru) imzayı AYRICA verir (SignatureOverride = sürüm) —
    /// duyuru DÜZENLENİNCE herkes için yeniden okunmamış olur. Override yoksa davranış eskisiyle BİREBİR.</summary>
    public string Signature => SignatureOverride ?? Detail;
}

/// <summary>PAN-01 (ADR-175): ana ekran "Bugünün Takvimi" şerit satırı (salt gösterim).</summary>
public sealed record DashboardCalendarRow(string SourceDisplay, string Title);

/// <summary>PAN-01 (ADR-175): ana ekran "Aktif Duyurular" şerit satırı (salt gösterim).</summary>
public sealed record DashboardAnnouncementRow(string Title, bool IsImportant);

/// <summary>
/// PAN-01 (ADR-175): yeni alanlar SONA, default'lu ve NULLABLE eklendi — mevcut çağrılar/istemciler
/// bozulmaz. <b>null = kullanıcının o KAYNAĞA yetkisi yok → kart/şerit HİÇ gösterilmez</b> (yan kapı yok);
/// yetki varken boş liste "bugün öğe yok" bilgisidir.
/// </summary>
public sealed record DashboardSummary(
    int VehicleCount, int MaterialCount, int LowStockCount, int PendingRequestCount, int PersonnelCount,
    IReadOnlyList<DashboardAlert> Alerts,
    int? OpenWorkOrderCount = null, int? OverdueWorkOrderCount = null, int? OpenPurchaseOrderCount = null,
    IReadOnlyList<DashboardCalendarRow>? TodayCalendar = null,
    IReadOnlyList<DashboardAnnouncementRow>? ActiveAnnouncements = null);
