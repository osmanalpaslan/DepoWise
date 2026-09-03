using System;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Vehicles;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Settings;
using DepoWise.Infrastructure.Update;
using System.Data.Common;

namespace DepoWise.Desktop;

/// <summary>
/// Hafif servis tutucu (DI container yok). Açılışta bir kez kurulur; ekran VM'leri buradan servis alır.
/// Oturum login sonrası set edilir.
/// </summary>
public static class DesktopServices
{
    public const string DefaultCompanyId = "DEPOWISE";

    public static IDbConnectionFactory Factory { get; private set; } = null!;
    /// <summary>F0 (YET-01) — yetki fotoğrafı önbelleği; Auth okur, Users/Permissions geçersiz kılar.</summary>
    public static DepoWise.Application.Security.PermissionSnapshotCache PermissionSnapshots { get; private set; } = null!;
    public static AuthService Auth { get; private set; } = null!;
    public static UserService Users { get; private set; } = null!;
    public static BranchService Branches { get; private set; } = null!;
    public static PermissionService Permissions { get; private set; } = null!;
    /// <summary>G5 — ekran platform görünürlüğü (firma bazlı). Çevrimdışı da çalışır: yerel DB okunur.</summary>
    public static DepoWise.Infrastructure.Organization.ScreenVisibilityService ScreenVisibility { get; private set; } = null!;
    public static DepoWise.Infrastructure.Organization.FieldRequirementService FieldRequirements { get; private set; } = null!;   // 2026-09-03: alan zorunluluğu
    /// <summary>MNU — menü düzeni (ad · üst menü · sıra). Sunucudan tanım senkronuyla iner.</summary>
    public static DepoWise.Infrastructure.Organization.MenuLayoutService MenuLayout { get; private set; } = null!;
    /// <summary>G4-1 — ön muhasebe cari (çevrimdışı da çalışır: yerel DB).</summary>
    public static DepoWise.Infrastructure.Accounting.PartyService Parties { get; private set; } = null!;
    public static DepoWise.Infrastructure.Accounting.PartyLedgerService PartyLedger { get; private set; } = null!;
    public static DepoWise.Infrastructure.Accounting.InvoiceService Invoices { get; private set; } = null!;
    public static DepoWise.Infrastructure.Accounting.InvoiceQueryService InvoiceQueries { get; private set; } = null!;
    public static DepoWise.Infrastructure.Accounting.FinanceService Finance { get; private set; } = null!;
    public static DepoWise.Infrastructure.Accounting.FinanceQueryService FinanceQueries { get; private set; } = null!;
    public static PermissionTemplateService PermissionTemplates { get; private set; } = null!;
    public static CompanyService Companies { get; private set; } = null!;
    public static ReleaseService Releases { get; private set; } = null!;
    public static UpdateService Update { get; private set; } = null!;
    public static UpdateDownloadService UpdateDownload { get; private set; } = null!;
    public static UpdateApiClient UpdateApi { get; private set; } = null!;
    public static DepoWise.Infrastructure.Sync.EnrollmentService Enrollment { get; private set; } = null!;
    public static MaterialService Materials { get; private set; } = null!;
    public static OpeningStockService OpeningStock { get; private set; } = null!;
    public static StockService Stock { get; private set; } = null!;
    public static StockChangeLogService StockChangeLog { get; private set; } = null!;
    public static DailyActivityService DailyActivity { get; private set; } = null!;
    public static DashboardService Dashboard { get; private set; } = null!;
    public static VehicleService Vehicles { get; private set; } = null!;
    public static VehicleTemplateService VehicleTemplates { get; private set; } = null!;
    public static MaterialTemplateService MaterialTemplates { get; private set; } = null!;
    public static MaintenanceService Maintenance { get; private set; } = null!;
    public static MaintenanceDefinitionService MaintenanceDefs { get; private set; } = null!;
    public static InspectionService Inspection { get; private set; } = null!;
    /// <summary>7b (ADR-191): ekipman bakim/muayene hatti - arac servislerinin paraleli.</summary>
    public static EquipmentMaintenanceService EquipmentMaintenance { get; private set; } = null!;
    public static EquipmentInspectionService EquipmentInspection { get; private set; } = null!;
    public static DepoWise.Infrastructure.Org.PersonnelService Personnel { get; private set; } = null!;
    public static DepoWise.Infrastructure.Org.PersonnelTitleService PersonnelTitles { get; private set; } = null!;
    public static FuelService Fuel { get; private set; } = null!;
    public static RequestService Requests { get; private set; } = null!;
    /// <summary>Talep Operasyonları (Faz 2) — onaylı taleplerin operasyon süreci; stok DEĞİŞTİRMEZ.</summary>
    public static RequestOperationsService RequestOps { get; private set; } = null!;
    public static IRequestPdfService RequestPdf { get; private set; } = null!;
    public static ReportService Reports { get; private set; } = null!;
    /// <summary>⭐ ARA İŞ 4 (ADR-186): custom rapor tanımları — çevrimdışı da çalışır (yerel SQLite).</summary>
    public static CustomReportService CustomReports { get; private set; } = null!;
    /// <summary>ARA IS 5 / ALT FAZ 1 (ADR-187): ekip aynasi. Masaustunde SALT OKUNUR kullanilir --
    /// ekip verisi sunucu otoritelidir, yerelde degistirilmez.</summary>
    public static DepoWise.Infrastructure.Teams.TeamService Teams { get; private set; } = null!;
    public static ExcelExportService Excel { get; private set; } = null!;
    public static MaterialImportService MaterialImport { get; private set; } = null!;
    public static DepoWise.Infrastructure.Files.TrashService Trash { get; private set; } = null!;
    public static AuditLogService Audit { get; private set; } = null!;
    public static DepoWise.Infrastructure.Files.BackupService Backup { get; private set; } = null!;
    public static DepoWise.Infrastructure.Files.BackupUploadService BackupUpload { get; private set; } = null!;
    public static VehicleImportService VehicleImport { get; private set; } = null!;
    public static InspectionImportService InspectionImport { get; private set; } = null!;
    public static MaintenanceImportService MaintenanceImport { get; private set; } = null!;
    /// <summary>Yakıt DAĞITIM içe aktarımı (araca yakıt verme) — Excel'deki geçmiş kayıtlar.</summary>
    public static FuelImportService FuelImport { get; private set; } = null!;
    /// <summary>Yakıt DEPO GİRİŞİ içe aktarımı (satın alma) — dağıtımların kaynağı; depo yetersizse dağıtım reddedilir.</summary>
    public static FuelDepotImportService FuelDepotImport { get; private set; } = null!;
    /// <summary>Personel içe aktarımı — "Saha Personeli" + MEVCUT hesabı bağlama ("Kullanıcı Adı").</summary>
    public static PersonnelImportService PersonnelImport { get; private set; } = null!;
    /// <summary>EXL-01 — Excel Merkezi: merkezi dışa aktarım üreticisi (15 kaynak; API/web ile ORTAK).</summary>
    public static ExcelCenterService ExcelCenter { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    /// <summary>Liste ekranı kolon tercihi — KİŞİSEL (bu makinede giriş yapan kullanıcıya özel).</summary>
    public static DepoWise.Infrastructure.Settings.UserListPreferenceService ListPrefs { get; private set; } = null!;
    public static LookupService Lookups { get; private set; } = null!;
    public static FileService Files { get; private set; } = null!;
    public static DepoWise.Infrastructure.Equipment.EquipmentService Equipment { get; private set; } = null!;   // EKP-01
    public static DepoWise.Infrastructure.Assignments.AssignmentService Assignments { get; private set; } = null!;   // ZMT-01
    public static DepoWise.Infrastructure.Accounting.CostCenterService CostCenters { get; private set; } = null!;   // MLY-01
    public static DepoWise.Infrastructure.Purchasing.PurchaseOrderService Purchasing { get; private set; } = null!;   // STN-01
    public static DepoWise.Infrastructure.WorkOrders.WorkOrderService WorkOrders { get; private set; } = null!;   // EMR-01
    // TKV-01: evrak sunucu-otoriteli olduğundan documents=null — evrak/proje kaynakları çevrimiçiyken API'den eklenir.
    public static DepoWise.Infrastructure.Calendars.CalendarService Calendar { get; private set; } = null!;   // TKV-01
    public static DepoWise.Infrastructure.Announcements.AnnouncementService Announcements { get; private set; } = null!;   // DYR-01
    // ARA-01: evrak sunucu-otoriteli olduğundan documents=null — Proje+Evrak sonuçları çevrimiçiyken API'den eklenir.
    public static DepoWise.Infrastructure.Search.SearchService Search { get; private set; } = null!;   // ARA-01
    public static IFileStorageProvider Storage { get; private set; } = null!;
    public static BrandingSettings Branding { get; private set; } = BrandingSettings.Default;
    public static ThemeTokens Theme { get; private set; } = ThemeTokens.Default;

    /// <summary>Aktif oturum (login sonrası). Çıkışta null.</summary>
    public static SessionContext? Session { get; set; }
    /// <summary>Login'de seçilen şube (branch_id) — yeni kayıtlar bununla etiketlenecek + okuma buna göre filtrelenecek.</summary>
    public static string? CurrentBranchId { get; set; }
    /// <summary>Login'de seçilen şube adı (ana ekranda gösterim).</summary>
    public static string? CurrentBranchName { get; set; }
    /// <summary>"Tüm Şubeler" modunda giriş yapıldı mı (yetkili kullanıcı) — okuma tüm şubeleri kapsar.</summary>
    public static bool CurrentAllBranches { get; set; }

    /// <summary>Bu makineye ADMIN'in web'den atadığı şube (id) — ana ekranda gösterilir; çevrimdışı otomatik giriş
    /// bununla yapılır. Kayıt/heartbeat yanıtından gelir (MachineGate), çevrimdışı için önbelleğe alınır.</summary>
    public static string? MachineBranchId { get; set; }
    /// <summary>Makineye atanmış şubenin adı (ana ekran gösterimi).</summary>
    public static string? MachineBranchName { get; set; }

    /// <summary>Makinenin (kayıtlı olduğu) firması — süper admin "makine firması ile giriş" seçeneği için.</summary>
    public static string? MachineCompanyId { get; set; }
    /// <summary>Makinenin firmasının adı.</summary>
    public static string? MachineCompanyName { get; set; }

    /// <summary>Kullanıcının (admin'in atadığı) kendi şubesini + adını okur (yereldeki users.branch_id).</summary>
    public static (string? BranchId, string? BranchName) LoadUserBranch(string userId)
    {
        try
        {
            using var conn = Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT u.branch_id, b.name FROM users u LEFT JOIN branches b ON b.id=u.branch_id WHERE u.id=@id;";
            cmd.AddWithValue("@id", userId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (null, null);
            return (r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1));
        }
        catch { return (null, null); }
    }

    public static void Initialize(BootstrapResult boot)
    {
        var clock = new SystemClock();
        Factory = SqliteConnectionFactory.ForEnvironment(DesktopBootstrap.Environment);
        // F0 (YET-01): yetki fotoğrafı önbelleği — okuyan ve geçersiz kılan servisler AYNI örneği paylaşır.
        // Masaüstünde oturum girişte bir kez kurulur (sunucudaki gibi istek başına değil); önbellek burada
        // performanstan çok TUTARLILIK sağlar: yetki değişince fotoğraf hemen düşer.
        PermissionSnapshots = new DepoWise.Application.Security.PermissionSnapshotCache();
        Auth = new AuthService(Factory, clock, PermissionSnapshots);
        Users = new UserService(Factory, clock, PermissionSnapshots);
        Materials = new MaterialService(Factory, clock);
        OpeningStock = new OpeningStockService(Factory, clock);
        Stock = new StockService(Factory, clock);
        StockChangeLog = new StockChangeLogService(Factory, Stock, clock);
        Maintenance = new MaintenanceService(Factory, clock);
        EquipmentMaintenance = new EquipmentMaintenanceService(Factory, clock);
        EquipmentInspection = new EquipmentInspectionService(Factory, clock);
        MaintenanceDefs = new MaintenanceDefinitionService(Factory, clock);
        // ⚠️ SIRA ÖNEMLİ: DailyActivity, Maintenance/MaintenanceDefs'i constructor'da SAKLAR (readonly alan) —
        // bu yüzden ikisi de ATANDIKTAN SONRA oluşturulmalı (eskiden Maintenance henüz null'ken geçiliyordu).
        DailyActivity = new DailyActivityService(Factory, Maintenance, clock, MaintenanceDefs);
        Inspection = new InspectionService(Factory, clock);
        Personnel = new DepoWise.Infrastructure.Org.PersonnelService(Factory, new DepoWise.Infrastructure.Org.ScopeResolver(Factory), clock);
        PersonnelTitles = new DepoWise.Infrastructure.Org.PersonnelTitleService(Factory, clock);
        Vehicles = new VehicleService(Factory, clock);
        VehicleTemplates = new VehicleTemplateService(Factory, clock);
        MaterialTemplates = new MaterialTemplateService(Factory, clock);
        Fuel = new FuelService(Factory, clock);
        Requests = new RequestService(Factory, new StockService(Factory, clock), clock);
        RequestOps = new RequestOperationsService(Factory, clock);
        RequestPdf = new RequestPdfService();
        Branches = new BranchService(Factory, clock);
        Permissions = new PermissionService(Factory, clock, PermissionSnapshots);
        ScreenVisibility = new DepoWise.Infrastructure.Organization.ScreenVisibilityService(Factory, clock);
        FieldRequirements = new DepoWise.Infrastructure.Organization.FieldRequirementService(Factory, clock);   // 2026-09-03
        MenuLayout = new DepoWise.Infrastructure.Organization.MenuLayoutService(Factory, clock);
        Parties = new DepoWise.Infrastructure.Accounting.PartyService(Factory, clock);
        PartyLedger = new DepoWise.Infrastructure.Accounting.PartyLedgerService(Factory, clock);
        // G4-2: fatura stok+cari servislerini KULLANIR (paralel defter yok) - onlardan SONRA kurulur.
        Invoices = new DepoWise.Infrastructure.Accounting.InvoiceService(Factory, Stock, PartyLedger, clock);
        InvoiceQueries = new DepoWise.Infrastructure.Accounting.InvoiceQueryService(Factory, clock);
        // G4-3: kasa/banka cari servisini KULLANIR (paralel cari defteri yok) - ondan SONRA kurulur.
        Finance = new DepoWise.Infrastructure.Accounting.FinanceService(Factory, PartyLedger, clock);
        FinanceQueries = new DepoWise.Infrastructure.Accounting.FinanceQueryService(Factory);
        PermissionTemplates = new PermissionTemplateService(Factory, clock);
        Companies = new CompanyService(Factory, clock);
        Releases = new ReleaseService(Factory, clock);
        Update = new UpdateService(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alpnex", "update"));
        UpdateDownload = new UpdateDownloadService();
        UpdateApi = new UpdateApiClient();
        Enrollment = new DepoWise.Infrastructure.Sync.EnrollmentService(Factory, clock);
        Reports = new ReportService(Factory);
        // ⭐ ARA İŞ 4 (ADR-186 / PK-CR-02=A): masaüstü raporu YEREL çalıştırdığı için custom rapor
        // bağlayıcısı burada da kurulur → tanım senkronla yerele indiğinde rapor ÇEVRİMDIŞI çalışır.
        CustomReports = new CustomReportService(Factory, Materials, Vehicles, DailyActivity, clock);
        Reports.Custom = CustomReports;
        Teams = new DepoWise.Infrastructure.Teams.TeamService(Factory, clock);
        Excel = new ExcelExportService();
        Trash = new DepoWise.Infrastructure.Files.TrashService(Factory, clock);
        Audit = new AuditLogService(Factory);
        Backup = new DepoWise.Infrastructure.Files.BackupService(Factory, clock);
        BackupUpload = new DepoWise.Infrastructure.Files.BackupUploadService();
        Settings = new SettingsService(Factory, clock);
        ListPrefs = new DepoWise.Infrastructure.Settings.UserListPreferenceService(Factory, clock);
        Lookups = new LookupService(Factory, clock);

        // ── İÇE AKTARIM servisleri — HEPSİ Lookups'a bağlıdır (tanım adları isimle çözülür, yoksa
        //    otomatik oluşturulur) → Lookups kurulduktan SONRA gelmeleri ZORUNLUDUR.
        MaterialImport = new MaterialImportService(Materials, Lookups, OpeningStock, Vehicles);
        VehicleImport = new VehicleImportService(Vehicles, Lookups);
        InspectionImport = new InspectionImportService(Inspection, Vehicles);
        MaintenanceImport = new MaintenanceImportService(Maintenance, MaintenanceDefs, Vehicles, Lookups);
        FuelImport = new FuelImportService(Fuel, Vehicles, Lookups);
        FuelDepotImport = new FuelDepotImportService(Fuel, Lookups);
        PersonnelImport = new PersonnelImportService(Personnel, PersonnelTitles, Users, Lookups);
        Storage = new LocalFileStorageProvider();
        Files = new FileService(Factory, Storage, clock);
        Equipment = new DepoWise.Infrastructure.Equipment.EquipmentService(Factory, clock);
        Assignments = new DepoWise.Infrastructure.Assignments.AssignmentService(Factory, clock);
        CostCenters = new DepoWise.Infrastructure.Accounting.CostCenterService(Factory, clock);
        Purchasing = new DepoWise.Infrastructure.Purchasing.PurchaseOrderService(Factory, clock);
        WorkOrders = new DepoWise.Infrastructure.WorkOrders.WorkOrderService(Factory, clock);
        Calendar = new DepoWise.Infrastructure.Calendars.CalendarService(Factory, documents: null, clock);
        Announcements = new DepoWise.Infrastructure.Announcements.AnnouncementService(Factory, clock);
        Search = new DepoWise.Infrastructure.Search.SearchService(Factory, documents: null, clock);
        Dashboard = new DashboardService(Factory, Maintenance, Inspection);
        // EXL-01: tüm kaynak servisler kurulduktan SONRA — merkez, veriyi HEP bu servislerden okur.
        ExcelCenter = new ExcelCenterService(Materials, Vehicles, Personnel, Inspection, Maintenance,
            Fuel, Requests, Users, Branches, Equipment, Assignments, WorkOrders, Purchasing,
            Calendar, Announcements, CostCenters, VehicleImport, PersonnelImport, FuelImport, FuelDepotImport);
        Branding = boot.Branding;
        Theme = boot.Theme;

        // NOT: Masaüstünde artık YEREL admin/superadmin SEED EDİLMEZ. İlk açılışta DB boştur; giriş yalnız
        // web'te tanımlı kullanıcılarla yapılır (yerel login başarısız → sunucu sync-login → kullanıcı yerele
        // çekilir). Web'de hiç kullanıcı yoksa (ya da sunucuya erişilemiyorsa) giriş yapılamaz — istenen davranış.
    }

    /// <summary>Kullanıcının görünen adı (full_name ya da username; GUID değil).</summary>
    public static string DisplayName(string userId)
    {
        using var conn = Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(NULLIF(full_name,''), username) FROM users WHERE id=@id;";
        cmd.AddWithValue("@id", userId);
        return cmd.ExecuteScalar() as string ?? "Kullanıcı";
    }

    /// <summary>Login için kullanılacak firma id'si (tek firma varsa o, yoksa varsayılan).</summary>
    public static string ResolveCompanyId()
    {
        using var conn = Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM companies WHERE is_deleted=0 ORDER BY created_at LIMIT 1;";
        return cmd.ExecuteScalar() as string ?? DefaultCompanyId;
    }
}
