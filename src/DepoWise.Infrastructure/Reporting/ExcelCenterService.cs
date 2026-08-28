using System;
using System.Collections.Generic;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>Excel Merkezi'nin TEK kaynağı: anahtar (API ucu) · etiket (UI listesi) · dosya adı.</summary>
public sealed record ExcelCenterSource(string Key, string Label, string FileName);

/// <summary>
/// ═══ EXL-01 — EXCEL MERKEZİ: merkezi dışa aktarım üreticisi ═══
///
/// TEK doğru kaynak: masaüstü Excel Merkezi ekranı, web Excel Merkezi sayfası ve
/// <c>GET /api/export/{entity}</c> uçları AYNI listeyi ve AYNI tablo üreticisini kullanır —
/// iki platform ne kaynak listesinde ne kolonlarda ayrışabilir (PK-M1/M2, ADR-176).
///
/// GÜVENLİK — çift kapı (Global Arama ilkesinin aynısı):
///  1) Çağıran uç/ekran <c>export</c> modül yetkisini ister (bu sınıf istemez — UI/uç sorumluluğu).
///  2) Veri HER ZAMAN kaynak modülün kendi servisiyle çekilir; servis kendi <c>Require</c> +
///     tenant + BranchAccess + silinmiş-kayıt süzmesini uygular. Bu sınıf HAM SQL YAZMAZ —
///     yetkisiz kaynak için servis fırlatır, merkez bir "yetki bypass" noktası OLAMAZ.
///
/// İlk 8 kaynağın kolonları eski ImportExportViewModel.BuildTable'dan AYNEN taşındı (davranış
/// birebir korunur; ilk 8'in sütunları içe aktarım şablonuyla uyumludur → "dışa aktar → düzelt →
/// geri içe aktar" döngüsü çalışır). Yeni 7 kaynak, kendi ekranlarının MEVCUT ToTableModel
/// üreticilerini kullanır (kolon mantığı ikinci kez YAZILMADI).
/// </summary>
public sealed class ExcelCenterService
{
    /// <summary>15 kaynak (PK-M2 = A). Sıra UI listesinin sırasıdır.</summary>
    public static readonly IReadOnlyList<ExcelCenterSource> Sources = new[]
    {
        new ExcelCenterSource("materials", "Malzemeler", "Malzemeler.xlsx"),
        new ExcelCenterSource("vehicles", "Araçlar", "Araclar.xlsx"),
        new ExcelCenterSource("personnel", "Personel", "Personel.xlsx"),
        new ExcelCenterSource("inspection", "Muayene / Sigorta", "Muayene_Sigorta.xlsx"),
        new ExcelCenterSource("maintenance", "Bakım", "Bakim.xlsx"),
        new ExcelCenterSource("requests", "Talepler", "Talepler.xlsx"),
        new ExcelCenterSource("fuel", "Yakıt Dağıtım", "Yakit_Dagitim.xlsx"),
        new ExcelCenterSource("fuel-depot", "Yakıt Depo Girişi", "Yakit_Depo_Girisi.xlsx"),
        new ExcelCenterSource("equipment", "Ekipman", "Ekipman.xlsx"),
        new ExcelCenterSource("assignments", "Zimmet", "Zimmet.xlsx"),
        new ExcelCenterSource("work-orders", "İş Emirleri", "IsEmirleri.xlsx"),
        new ExcelCenterSource("purchasing", "Satın Alma", "SatinAlma.xlsx"),
        new ExcelCenterSource("calendar", "Takvim (bu ay)", "Takvim.xlsx"),
        new ExcelCenterSource("announcements", "Duyurular", "Duyurular.xlsx"),
        new ExcelCenterSource("cost-centers", "Maliyet Merkezi (son 30 gün)", "MaliyetMerkezi.xlsx"),
    };

    /// <summary>Anahtarla kaynak; bilinmeyen anahtar → 400 (ortak hata katmanı ArgumentException'ı çevirir).</summary>
    public static ExcelCenterSource Find(string key)
        => Sources.FirstOrDefault(x => x.Key == key)
           ?? throw new ArgumentException($"Bilinmeyen dışa aktarım kaynağı: {key}");

    private readonly Materials.MaterialService _materials;
    private readonly Vehicles.VehicleService _vehicles;
    private readonly Org.PersonnelService _personnel;
    private readonly Maintenance.InspectionService _inspection;
    private readonly Maintenance.MaintenanceService _maintenance;
    private readonly Operations.FuelService _fuel;
    private readonly Requests.RequestService _requests;
    private readonly Security.UserService _users;
    private readonly Organization.BranchService _branches;
    private readonly Equipment.EquipmentService _equipment;
    private readonly Assignments.AssignmentService _assignments;
    private readonly WorkOrders.WorkOrderService _workOrders;
    private readonly Purchasing.PurchaseOrderService _purchasing;
    private readonly Calendars.CalendarService _calendar;
    private readonly Announcements.AnnouncementService _announcements;
    private readonly Accounting.CostCenterService _costCenters;
    private readonly VehicleImportService _vehicleImport;
    private readonly PersonnelImportService _personnelImport;
    private readonly FuelImportService _fuelImport;
    private readonly FuelDepotImportService _fuelDepotImport;

    public ExcelCenterService(
        Materials.MaterialService materials,
        Vehicles.VehicleService vehicles,
        Org.PersonnelService personnel,
        Maintenance.InspectionService inspection,
        Maintenance.MaintenanceService maintenance,
        Operations.FuelService fuel,
        Requests.RequestService requests,
        Security.UserService users,
        Organization.BranchService branches,
        Equipment.EquipmentService equipment,
        Assignments.AssignmentService assignments,
        WorkOrders.WorkOrderService workOrders,
        Purchasing.PurchaseOrderService purchasing,
        Calendars.CalendarService calendar,
        Announcements.AnnouncementService announcements,
        Accounting.CostCenterService costCenters,
        VehicleImportService vehicleImport,
        PersonnelImportService personnelImport,
        FuelImportService fuelImport,
        FuelDepotImportService fuelDepotImport)
    {
        _materials = materials; _vehicles = vehicles; _personnel = personnel;
        _inspection = inspection; _maintenance = maintenance; _fuel = fuel; _requests = requests;
        _users = users; _branches = branches; _equipment = equipment; _assignments = assignments;
        _workOrders = workOrders; _purchasing = purchasing; _calendar = calendar;
        _announcements = announcements; _costCenters = costCenters;
        _vehicleImport = vehicleImport; _personnelImport = personnelImport;
        _fuelImport = fuelImport; _fuelDepotImport = fuelDepotImport;
    }

    /// <summary>Seçilen kaynağın güncel verisini tablo olarak üretir (kaynak servis yetkileriyle).</summary>
    public TableModel Build(SessionContext s, string key)
    {
        var rows = new List<IReadOnlyList<object?>>();
        switch (Find(key).Key)
        {
            case "materials":
                foreach (var m in AllPages(c => _materials.List(s, new PageRequest { Limit = PageRequest.MaxLimit, Cursor = c })))
                    rows.Add(new object?[] { m.Code, m.Name, m.Type, m.MinStock, m.UnitPrice, m.Currency });
                return new TableModel("Malzemeler", new[] { "Kod", "Ad", "Tür", "Min Stok", "Birim Fiyat", "Para Birimi" }, rows);

            case "vehicles":
                foreach (var v in _vehicles.List(s))
                {
                    // Detay ayrı sorgu: liste satırında tanım ADLARI (marka/model/şube…) yok.
                    Vehicles.VehicleDetail? d = null;
                    try { d = _vehicles.Get(s, v.Id); } catch { }
                    rows.Add(new object?[]
                    {
                        v.InternalCode, v.Plate, v.ProductionYear,
                        DepoWise.Application.Ui.VehicleStatus.Label(v.Status), d?.StatusNote,
                        v.CurrentMeter, v.MeterUnit,
                        d?.VehicleTypeName, d?.CategoryName, d?.BrandName, d?.VehicleModelName,
                        d?.BranchName, d?.DriverName, d?.ChassisNo, d?.EngineNo,
                    });
                }
                return new TableModel("Araçlar", _vehicleImport.SampleHeaders().ToArray(), rows);

            case "personnel":
            {
                // Bağlı kullanıcı adı: personel id → kullanıcı adı (tek sorgu; satır başına sorgu YOK).
                var accounts = _users.AccountsByPersonnel(s.CompanyId);
                var branchNames = _branches.List(s).ToDictionary(b => b.Id, b => b.Name, StringComparer.Ordinal);
                foreach (var p in AllPages(c => _personnel.List(s, new PageRequest { Limit = PageRequest.MaxLimit, Cursor = c })))
                {
                    accounts.TryGetValue(p.Id, out var acc);
                    rows.Add(new object?[]
                    {
                        p.FullName, p.Title, p.Phone,
                        p.BranchId is not null && branchNames.TryGetValue(p.BranchId, out var bn) ? bn : null,
                        p.IsActive ? "Evet" : "Hayır",
                        p.IsFieldStaff ? "Evet" : "Hayır",
                        acc?.Username,
                    });
                }
                return new TableModel("Personel", _personnelImport.SampleHeaders().ToArray(), rows);
            }

            case "inspection":
                foreach (var i in _inspection.List(s))
                    rows.Add(new object?[] { i.VehicleText, i.DocTypeText, i.LastText, i.NextText, i.Place, i.Result, i.StatusText });
                return new TableModel("Muayene Sigorta", new[] { "Araç", "Belge", "Son", "Sonraki", "Yer", "Sonuç", "Durum" }, rows);

            case "maintenance":
                foreach (var m in _maintenance.ListMaintenances(s))
                    rows.Add(new object?[] { m.VehicleCode, m.DefinitionName, m.SubDisplay, m.PerformedDisplay, m.NextDueDisplay, m.StatusText });
                return new TableModel("Bakım", new[] { "Araç", "Bakım", "Alt Bakım", "Yapılma", "Sonraki", "Durum" }, rows);

            case "requests":
                foreach (var r in _requests.List(s))
                    rows.Add(new object?[] { r.DocNo,
                        DateTimeOffset.FromUnixTimeMilliseconds(r.RequestDate).LocalDateTime.ToString("dd.MM.yyyy"),
                        RequestStatusOptions.Label(r.Status.ToString().ToLowerInvariant()), r.ItemCount });
                return new TableModel("Talepler", new[] { "Belge No", "Tarih", "Durum", "Kalem" }, rows);

            case "fuel":
                foreach (var f in _fuel.ListDistributions(s, 5000))
                    rows.Add(new object?[] { f.VehicleCode,
                        DateTimeOffset.FromUnixTimeMilliseconds(f.DistributionDate).LocalDateTime.ToString("dd.MM.yyyy"),
                        f.Liters, f.CurrentMeter, f.UnitPrice, null, null });
                return new TableModel("Yakıt Dağıtım", _fuelImport.SampleHeaders().ToArray(), rows);

            case "fuel-depot":
                foreach (var d in _fuel.ListDepotEntries(s, 5000))
                    rows.Add(new object?[] {
                        DateTimeOffset.FromUnixTimeMilliseconds(d.EntryDate).LocalDateTime.ToString("dd.MM.yyyy"),
                        d.Liters, d.UnitPrice, null, d.InvoiceNo, null });
                return new TableModel("Yakıt Depo Girişi", _fuelDepotImport.SampleHeaders().ToArray(), rows);

            case "equipment":
                return Equipment.EquipmentService.ToTableModel(_equipment.List(s));

            case "assignments":
                return Assignments.AssignmentService.ToTableModel(_assignments.Holdings(s));

            case "work-orders":
                return WorkOrders.WorkOrderService.ToTableModel(_workOrders.List(s));

            case "purchasing":
                return Purchasing.PurchaseOrderService.ToTableModel(_purchasing.List(s));

            case "calendar":
            {
                // Ekrandaki varsayılanla aynı pencere: içinde bulunulan AY (etiket bunu söyler).
                var ay1 = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var from = Ms(ay1);
                var to = Ms(ay1.AddMonths(1)) - 1;
                return Calendars.CalendarService.ToTableModel(_calendar.Items(s, from, to));
            }

            case "announcements":
            {
                // all=true: yönetici tüm listeyi alır; yönetici-dışı servis içinde aktif-olanlara kapanır (DYR2).
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Announcements.AnnouncementService.ToTableModel(_announcements.List(s, includeInactive: true), now);
            }

            case "cost-centers":
            {
                // Ekrandaki varsayılanla aynı pencere: son 30 gün (etiket bunu söyler). Ağır rapor kuralına
                // aykırı değildir: kullanıcı bu exportu butonla bilinçli tetikler.
                var from = Ms(DateTime.Today.AddDays(-30));
                var to = Ms(DateTime.Today.AddDays(1)) - 1;
                return Accounting.CostCenterService.SummaryTable(_costCenters.Summary(s, from, to));
            }

            default:
                throw new ArgumentException($"Bilinmeyen dışa aktarım kaynağı: {key}");
        }
    }

    /// <summary>Yerel gün başlangıcı → Unix ms (CostCenters ekranındaki Aralik() dönüşümünün aynısı).</summary>
    private static long Ms(DateTime localDate)
        => new DateTimeOffset(DateTime.SpecifyKind(localDate.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    /// <summary>
    /// Sayfalı bir listenin TÜM kayıtlarını dolaşır (keyset imleciyle).
    ///
    /// ⚠️ NEDEN GEREKLİ: <c>PageRequest.MaxLimit = 200</c>'dür → <c>new PageRequest { Limit = 5000 }</c>
    /// yazmak İŞE YARAMAZ, yine 200 satır döner. Dışa aktarım eskiden böyleydi: 2600 personeli/malzemesi
    /// olan firma "dışa aktar" deyince sessizce yalnız 200 satır alıyordu. Artık tüm sayfalar dolaşılır.
    /// </summary>
    private static IEnumerable<T> AllPages<T>(Func<string?, PagedResult<T>> fetch)
    {
        string? cursor = null;
        var guard = 0;
        do
        {
            var page = fetch(cursor);
            foreach (var item in page.Items) yield return item;
            cursor = page.NextCursor;
            // Sonsuz döngü koruması (imleç ilerlemezse): 200 × 5000 = 1.000.000 kayıt tavanı.
            if (++guard > 5000) yield break;
        } while (cursor is not null);
    }
}
