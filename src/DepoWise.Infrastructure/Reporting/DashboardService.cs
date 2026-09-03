using DepoWise.Application.Common;   // Money — DEN-D2: kesin toplama metni decimal olarak okunur
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Application.Maintenance;
using DepoWise.Infrastructure.Announcements;
using DepoWise.Infrastructure.Calendars;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.WorkOrders;
using System.Data.Common;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Ana ekran özeti (tenant filtreli KPI) + birleşik aktif uyarılar (bakım + muayene/sigorta + düşük stok).
/// Uyarılar permission/tenant'a tabi; karta tıklanınca NavigateKey ile ilgili modüle gidilir.
/// </summary>
public sealed class DashboardService
{
    private readonly IDbConnectionFactory _factory;
    private readonly MaintenanceService _maintenance;
    private readonly InspectionService _inspection;
    // BLD-01 (ADR-172): evrak sunucu-otoriteli — masaüstü null geçer, evrak bildirimi çevrimdışı ÜRETİLMEZ
    // (Takvim/Projeler emsali; veri uydurulmaz). İş emri kaynağı yereldir (çevrimdışı çalışır).
    private readonly DocumentService? _documents;
    private readonly WorkOrderService _workOrders;

    /// <summary>Evrak/muayene ile aynı "yaklaşıyor" eşiği (gün).</summary>
    public const int DocumentApproachingDays = InspectionService.ApproachingDays;

    public DashboardService(IDbConnectionFactory factory, MaintenanceService maintenance, InspectionService inspection,
        DocumentService? documents = null)
    {
        _factory = factory;
        _maintenance = maintenance;
        _inspection = inspection;
        _documents = documents;
        _workOrders = new WorkOrderService(factory);
        _announcements = new AnnouncementService(factory);
        _purchasing = new PurchaseOrderService(factory);
        _calendar = new CalendarService(factory, documents);   // PAN-01: masaüstünde documents=null → evrak öğesi şeritte de atlanır (tutarlı)
    }

    private readonly AnnouncementService _announcements;
    private readonly PurchaseOrderService _purchasing;
    private readonly CalendarService _calendar;

    public DashboardSummary GetSummary(SessionContext s)
    {
        using var conn = _factory.Create();
        // Malzeme FİRMA-GENELİ katalog (ortak liste) → malzeme sayısı ve düşük stok firma-geneli
        // (kullanıcı kararı 2026-07-26: "ortak liste + şube-bazlı stok"; stok ayrımı ayrıca planlanıyor).
        int vehicles = Count(conn, "vehicles", s.CompanyId);
        int materials = Count(conn, "materials", s.CompanyId);
        int personnel = Count(conn, "personnel", s.CompanyId);
        int lowStock = LowStockCount(conn, s.CompanyId);
        int pending = PendingRequests(conn, s.CompanyId);

        var alerts = new List<DashboardAlert>();

        // ⭐ 2026-09-02 (kullanıcı isteği + ekran görüntüsü): ARAÇLA İLGİLİ uyarılarda araç KODU ve
        // PLAKA görünür. Panelde yalnız "MOTOR BAKIMI · %2486 (Overdue)" yazıyordu; hangi araç
        // olduğu anlaşılmıyordu. Araç metni TEK sorguyla hazırlanır (satır başına sorgu YOK).
        var aracMetni = VehicleLabels(conn, s.CompanyId);

        // Bakım uyarıları (yalnız Normal olmayanlar)
        if (AccessControl.Can(s, "maintenance", PermissionAction.View))
        {
            foreach (var a in _maintenance.GetAlerts(s))
            {
                if (a.Level == AlertLevel.Normal) continue;
                // Seviye etiketi TÜRKÇE ve TEK KAYNAKTAN (eskiden enum adı basılıyordu → "(Overdue)").
                var durum = a.NeverPerformed ? "İlk bakım yapılmadı"
                    : $"%{a.Progress * 100:0} ({AlertRules.LevelText(a.Level)})";
                alerts.Add(new DashboardAlert(AlertKind.Maintenance, a.DefinitionName,
                    Birlestir(aracMetni, a.VehicleId, durum),
                    "maintenance:records", a.Level is AlertLevel.Critical or AlertLevel.Overdue, a.VehicleId));
            }
        }
        // Muayene/sigorta
        if (AccessControl.Can(s, "inspection", PermissionAction.View))
        {
            foreach (var a in _inspection.GetAlerts(s))
            {
                if (a.Level == DateAlertLevel.Normal) continue;
                var docText = a.DocType switch
                {
                    "inspection" => "Muayene", "insurance" => "Sigorta",
                    "kasko" => "Kasko", "calibration" => "Kalibrasyon", _ => a.DocType
                };
                var levelText = a.Level == DateAlertLevel.Expired ? "Süresi Doldu" : "Yaklaşıyor";
                // Sigorta/muayene uyarısı ilgili BELGE kaydına köprülenir (araç değil).
                // Araç kodu + plaka detaya eklenir (bakım uyarısıyla aynı biçim).
                alerts.Add(new DashboardAlert(AlertKind.Inspection, docText,
                    Birlestir(aracMetni, a.VehicleId, levelText),
                    "inspection", a.Level == DateAlertLevel.Expired, a.VehicleId));
            }
        }
        // Düşük stok — malzeme bazlı (tıklayınca ilgili malzemenin detayı açılır); şube-bazlı
        if (AccessControl.Can(s, "materials", PermissionAction.View))
            foreach (var (id, name) in LowStockList(conn, s.CompanyId))
                alerts.Add(new DashboardAlert(AlertKind.LowStock, name, "Düşük stok", "materials", true, id));

        // Yakıt — depo kalanı toplam alınanın %20'si ve altına düşünce (Özet'te kalanı gör)
        if (AccessControl.Can(s, "fuel", PermissionAction.View))
        {
            var (received, remaining) = FuelStatus(conn, s.CompanyId);
            if (received > 0 && remaining <= received * 0.20)
            {
                var pct = received > 0 ? remaining / received * 100 : 0;
                alerts.Add(new DashboardAlert(AlertKind.Fuel,
                    remaining <= 0 ? "Yakıt Tükendi" : "Yakıt Azaldı",
                    $"Kalan depo: {remaining:0.##} L (%{pct:0})", "fuel:summary", true));
            }
        }

        // ═══ BLD-01 (ADR-172) — YENİ TÜRETİLMİŞ KAYNAKLAR (PK-I1: evrak + geciken iş emri + bekleyen talep).
        // Fiziksel bildirim kaydı YOK: her çağrıda kaynaktan hesaplanır (kopya imkânsız; kaynak düzelince
        // bildirim düşer). Her kaynak KENDİ modül yetkisiyle sarılı (yan kapı yok — mevcut desen aynen).
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Evrak geçerlilik — DocumentService.List İKİ KAPI + şube/proje kapsamını İÇERİDE uygular.
        if (_documents is not null && AccessControl.Can(s, DocumentService.Module, PermissionAction.View))
        {
            foreach (var d in _documents.List(s))
            {
                if (d.ValidUntil is not { } vu) continue;
                var kalanGun = (vu - nowMs) / 86_400_000.0;
                if (kalanGun > DocumentApproachingDays) continue;
                var etiket = d.EntityLabel == "—" ? "" : $" ({d.EntityLabel})";
                alerts.Add(new DashboardAlert(AlertKind.Document, d.Title + etiket,
                    vu < nowMs ? "Geçerlilik süresi doldu" : "Geçerlilik yaklaşıyor", "documents", vu < nowMs, d.Id));
            }
        }

        // Geciken iş emri — WorkOrderService.List BranchAccess kapsamını İÇERİDE uygular; terminal
        // (Tamamlandı/İptal) emirler gecikme SAYILMAZ. PAN-01: aynı TEK listeden açık/geciken sayıları
        // da türetilir (ikinci sorgu yok) — yetki yoksa sayılar NULL kalır (kart hiç gösterilmez).
        int? openWo = null, overdueWo = null;
        if (AccessControl.Can(s, WorkOrderService.Module, PermissionAction.View))
        {
            openWo = 0; overdueWo = 0;
            foreach (var w in _workOrders.List(s))
            {
                if (w.Status is "completed" or "cancelled") continue;
                openWo++;
                if (w.PlannedEnd is not { } pe || pe >= nowMs) continue;
                overdueWo++;
                alerts.Add(new DashboardAlert(AlertKind.WorkOrder, $"{w.WoNo} · {w.Title}",
                    "Plan bitişi geçti", "work_orders", true, w.Id));
            }
        }

        // Bekleyen talepler — kalem bazlı; şube kapsamı uygulanır (kapsam dışı şubenin talebi SIZMAZ;
        // şubesiz talep gizlenmez — sınıf kuralı). KPI sayacı (PendingRequests) DEĞİŞMEDİ.
        if (AccessControl.Can(s, "requests", PermissionAction.View))
        {
            var izinli = BranchAccess.Allowed(s);
            var set = izinli?.ToHashSet(StringComparer.Ordinal);
            foreach (var (id, docNo, branchId) in PendingRequestList(conn, s.CompanyId))
            {
                if (set is not null && branchId is not null && !set.Contains(branchId)) continue;
                alerts.Add(new DashboardAlert(AlertKind.Request, $"Talep {docNo}",
                    "Onay bekliyor", "requests:approve", false, id));
            }
        }

        // ═══ DYR-01 (ADR-173) — DUYURULAR: yayın penceresi İÇİNDEKİ duyurular bildirime kalem olarak
        // düşer (pencere dışına çıkan kendiliğinden düşer — türetilmiş model). Okuma HERKESE (PK-J1;
        // Rol Yetki Kontrol kapatması Can içinde işler); şube hedefi List İÇİNDE süzülür (yan kapı yok).
        // İmza=version → duyuru DÜZENLENİNCE herkes için yeniden okunmamış olur.
        // PAN-01: AKTİF duyuru listesi TEK çağrıyla hem bildirime hem ana ekran şeridine.
        List<DashboardAnnouncementRow>? annSerit = null;
        if (AccessControl.Can(s, AnnouncementService.Module, PermissionAction.View))
        {
            try
            {
                annSerit = new List<DashboardAnnouncementRow>();
                foreach (var d in _announcements.List(s))
                {
                    alerts.Add(new DashboardAlert(AlertKind.Announcement, d.Title,
                        d.IsImportant ? "Önemli duyuru" : "Duyuru", "announcements", d.IsImportant, d.Id,
                        SignatureOverride: "v" + d.Version));
                    if (annSerit.Count < 5) annSerit.Add(new DashboardAnnouncementRow(d.Title, d.IsImportant));
                }
            }
            catch { annSerit = null; }   // tablo henüz yoksa (eski şema) ana ekran çalışmaya devam eder
        }

        // ═══ PAN-01 (ADR-175) — yeni özet alanları (PK-L1). Her biri KAYNAK yetkisiyle sarılı;
        // yetki yoksa NULL → kart/şerit HİÇ gösterilmez (yan kapı yok). Salt-okunur türetme.
        int? openPo = null;
        if (AccessControl.Can(s, "purchasing", PermissionAction.View))
        {
            // PurchaseOrderService.List teslim şubesi kapsamını İÇERİDE uygular (kapsam dışı sipariş SAYILMAZ).
            try { openPo = _purchasing.List(s, null, "open").Count; } catch { openPo = null; }
        }

        List<DashboardCalendarRow>? bugun = null;
        if (AccessControl.Can(s, CalendarService.Module, PermissionAction.View))
        {
            try
            {
                var gunBasi = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
                bugun = _calendar.Items(s, gunBasi, gunBasi + 86_400_000 - 1)
                    .Take(6).Select(i => new DashboardCalendarRow(i.SourceDisplay, i.Title)).ToList();
            }
            catch { bugun = null; }
        }

        // #18: kullanıcının "okundu" işaretleri — imza eşleşiyorsa Read=true (ana ekranda gizlenir).
        var reads = LoadAlertReads(conn, s.UserId);
        for (int i = 0; i < alerts.Count; i++)
            if (reads.TryGetValue(alerts[i].Key, out var sig) && sig == alerts[i].Signature)
                alerts[i] = alerts[i] with { Read = true };

        return new DashboardSummary(vehicles, materials, lowStock, pending, personnel, alerts,
            openWo, overdueWo, openPo, bugun, annSerit);
    }

    private static Dictionary<string, string> LoadAlertReads(DbConnection conn, string userId)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT alert_key, signature FROM alert_reads WHERE user_id=@u;";
        cmd.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetString(1);
        return map;
    }

    /// <summary>#18 — Uyarıyı kullanıcı için "okundu" işaretler (imzayla; hali değişirse yeniden görünür).</summary>
    public void MarkAlertRead(SessionContext s, string alertKey, string signature)
    {
        if (string.IsNullOrWhiteSpace(alertKey)) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO alert_reads(id, company_id, user_id, alert_key, signature, created_at)
VALUES(@id,@c,@u,@k,@sig,@now)
ON CONFLICT(user_id, alert_key) DO UPDATE SET signature=@sig, created_at=@now;";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@u", s.UserId);
        cmd.AddWithValue("@k", alertKey);
        cmd.AddWithValue("@sig", signature ?? "");
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    // branch != null → yalnız o şubenin (+ şubesiz) kayıtları (materials için branch_id kolonu).
    private static string BranchAnd(string? branch, string col = "branch_id")
        => branch is null ? "" : $" AND ({col} = @opb OR {col} IS NULL)";
    private static void BindBranch(DbCommand cmd, string? branch)
    { if (branch is not null) cmd.AddWithValue("@opb", branch); }

    /// <summary>
    /// Araç kimliği → "İÇ KOD · PLAKA" etiketi (kullanıcı isteği 2026-09-02).
    /// TEK sorgu; uyarı satırı başına sorgu açılmaz (panel yüzlerce uyarı taşıyabilir — N+1 yasak).
    /// Plakası olmayan araçta yalnız iç kod döner.
    /// </summary>
    private static Dictionary<string, string> VehicleLabels(DbConnection conn, string companyId)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, internal_code, COALESCE(plate,'') FROM vehicles WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var kod = r.GetString(1);
                var plaka = r.GetString(2);
                map[r.GetString(0)] = string.IsNullOrWhiteSpace(plaka) ? kod : $"{kod} · {plaka}";
            }
        }
        catch { /* araç etiketi okunamazsa uyarılar etiketsiz gösterilir — panel çalışmaya devam eder */ }
        return map;
    }

    /// <summary>"KOD · PLAKA · durum" — araç bilinmiyorsa yalnız durum döner (boş ayraç bırakmaz).</summary>
    private static string Birlestir(Dictionary<string, string> etiketler, string? vehicleId, string durum)
        => vehicleId is not null && etiketler.TryGetValue(vehicleId, out var etiket)
            ? $"{etiket} · {durum}"
            : durum;

    private static int Count(DbConnection conn, string table, string companyId, string? branch = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE company_id=@c AND is_deleted=0{BranchAnd(branch)};";
        cmd.AddWithValue("@c", companyId);
        BindBranch(cmd, branch);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static IReadOnlyList<(string Id, string Name)> LowStockList(DbConnection conn, string companyId, string? branch = null)
    {
        using var cmd = conn.CreateCommand();
        // STK-02: bakiye (malzeme + lokasyon) anahtarlı → düz JOIN malzemeyi depo sayısı kadar TEKRARLARDI
        // (aynı malzeme düşük-stok listesinde birden çok kez çıkardı). Düşük stok uyarısı FİRMA GENELİ
        // toplama bakar (kullanıcı bir depoda azalmayı değil, elindeki toplamı görmek ister) → toplayan alt sorgu.
        cmd.CommandText = @"
SELECT m.id, m.name FROM materials m
LEFT JOIN " + SqlDialect.StockTotalSubquery(conn) + @" b ON b.material_id = m.id AND b.company_id = m.company_id
WHERE m.company_id=@c AND m.is_deleted=0" + BranchAnd(branch, "m.branch_id") + @"
  AND CAST(COALESCE(b.quantity,'0') AS REAL) <= CAST(m.min_stock AS REAL) AND CAST(m.min_stock AS REAL) > 0
ORDER BY m.name LIMIT 20;";
        cmd.AddWithValue("@c", companyId);
        BindBranch(cmd, branch);
        var list = new List<(string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>(toplam alınan, kalan) — kalan = alınan − dağıtılan.
    /// DEN-D2 (2026-08-18): eskiden <c>SUM(CAST(liters AS REAL))</c> ile hesaplanıyordu → ana ekranda
    /// <c>1234,5600000000002</c> gibi değerler görünebiliyordu. Artık kesin toplama
    /// (<see cref="SqlDialect.ExactSumText"/>): PG'de <c>numeric</c> ile tam, SQLite'ta 6 ondalığa yuvarlı.</summary>
    private static (double Received, double Remaining) FuelStatus(DbConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"SELECT (SELECT {SqlDialect.ExactSumText(conn, "liters")} FROM fuel_depot_entries WHERE company_id=@c AND is_deleted=0), " +
            $"       (SELECT {SqlDialect.ExactSumText(conn, "liters")} FROM fuel_distributions WHERE company_id=@c AND is_deleted=0);";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (0, 0);
        // Metin → decimal (Money kuralı), gösterim için double'a çevrilir.
        var received = Money.Parse(r.IsDBNull(0) ? null : r.GetString(0));
        var distributed = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
        return ((double)received, (double)(received - distributed));
    }

    private static int LowStockCount(DbConnection conn, string companyId, string? branch = null)
    {
        using var cmd = conn.CreateCommand();
        // STK-02: LowStockList ile AYNI tanım (sayı ile liste kopmamalı) → aynı toplayan alt sorgu.
        cmd.CommandText = @"
SELECT COUNT(*) FROM materials m
LEFT JOIN " + SqlDialect.StockTotalSubquery(conn) + @" b ON b.material_id = m.id AND b.company_id = m.company_id
WHERE m.company_id=@c AND m.is_deleted=0" + BranchAnd(branch, "m.branch_id") + @"
AND CAST(COALESCE(b.quantity,'0') AS REAL) <= CAST(m.min_stock AS REAL) AND CAST(m.min_stock AS REAL) > 0;";
        cmd.AddWithValue("@c", companyId);
        BindBranch(cmd, branch);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int PendingRequests(DbConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM material_requests WHERE company_id=@c AND status='pending' AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ═══ BLD-01 (ADR-172) yardımcıları ═══

    private static IReadOnlyList<(string Id, string DocNo, string? BranchId)> PendingRequestList(DbConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, doc_no, branch_id FROM material_requests " +
                          "WHERE company_id=@c AND status='pending' AND is_deleted=0 ORDER BY created_at DESC LIMIT 50;";
        cmd.AddWithValue("@c", companyId);
        var list = new List<(string, string, string?)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>BLD-01: aktif VE okunmamış bildirim sayısı (üst bar çan sayacı). Aynı GetSummary hesabı —
    /// ayrı üretim yolu YOK (sayı ile liste kopamaz).</summary>
    public int UnreadAlertCount(SessionContext s)
        => GetSummary(s).Alerts.Count(a => !a.Read);

    /// <summary>BLD-01: TÜM aktif bildirimleri okundu işaretler (upsert — tekrar çağrı kopya üretmez).</summary>
    public void MarkAllAlertsRead(SessionContext s, IEnumerable<DashboardAlert>? extra = null)
    {
        foreach (var a in GetSummary(s).Alerts.Concat(extra ?? Array.Empty<DashboardAlert>()))
            if (!a.Read) MarkAlertRead(s, a.Key, a.Signature);
    }

    /// <summary>BLD-01: dışarıdan gelen bildirimlere (masaüstünün ÇEVRİMİÇİ aldığı evrak bildirimleri)
    /// bu cihazın YEREL okundu işaretlerini uygular — okundu davranışı cihaz-yerel kalır (PK-I4).</summary>
    public IReadOnlyList<DashboardAlert> ApplyReads(SessionContext s, IEnumerable<DashboardAlert> alerts)
    {
        using var conn = _factory.Create();
        var reads = LoadAlertReads(conn, s.UserId);
        return alerts.Select(a => reads.TryGetValue(a.Key, out var sig) && sig == a.Signature
            ? a with { Read = true } : a).ToList();
    }
}
