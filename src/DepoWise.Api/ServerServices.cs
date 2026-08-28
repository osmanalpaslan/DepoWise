using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Update;

namespace DepoWise.Api;

/// <summary>
/// Sunucu kompozisyon kökü (Option A — masaüstüyle AYNI Application/Infrastructure). Migration + servisler +
/// oturum token deposu. Kimlik doğrulama AuthService ile; cihaz senkron token'ları SyncServer/EnrollmentService'te.
/// Kimlik = JWT (durum tutmaz); yetkiler her istekte sunucuda yeniden yüklenir. DB = SQLite (Postgres'e taşınabilir).
/// </summary>
public sealed class ServerServices
{
    public IDbConnectionFactory Factory { get; }
    /// <summary>F0 (YET-01) — yetki fotoğrafı önbelleği. Auth okur; Users/Permissions/RoleGrants geçersiz kılar.
    /// TEK örnek olmak zorundadır, aksi halde yetki değişikliği yansımaz.</summary>
    public DepoWise.Application.Security.PermissionSnapshotCache PermissionSnapshots { get; }
    public AuthService Auth { get; }
    public UserService Users { get; }
    public DepoWise.Infrastructure.Organization.CompanyService Companies { get; }
    /// <summary>Firma KALICI silme (ADR-083) — Firma Tanım'ın pasife almasından farklı, geri alınamaz.</summary>
    public DepoWise.Infrastructure.Organization.CompanyPurgeService CompanyPurge { get; }
    /// <summary>Firma "yerel sıfırlama" isteği (ADR-084) — kalıcı silme DEĞİL, makinelerin yerel kopyasını
    /// bir kez temizletir (firma sunucuda durur, erişim engellenmez).</summary>
    public DepoWise.Infrastructure.Organization.CompanyLocalResetService CompanyLocalReset { get; }
    /// <summary>Makine "tanım sıfırlama" isteği (ADR-085) — makineyi TÜM firmalardan koparır (veriye dokunmaz).</summary>
    public DepoWise.Infrastructure.Sync.MachineResetService MachineReset { get; }
    /// <summary>"Özel kod" — Kalıcı Silme ekranının kilidi (yalnız süper admin).</summary>
    public SpecialCodeService SpecialCode { get; }
    public DepoWise.Infrastructure.Organization.CompanyGrantService CompanyGrants { get; }
    public DepoWise.Infrastructure.Organization.RoleGrantService RoleGrants { get; }
    /// <summary>G5 — ekran platform görünürlüğü (firma bazlı; katalog varsayılanını yalnız DARALTIR).</summary>
    public DepoWise.Infrastructure.Organization.ScreenVisibilityService ScreenVisibility { get; }
    /// <summary>MNU — menü düzeni: ekran adı / üst menüsü / sırası (firma bazlı; kimliği DEĞİŞTİRMEZ).</summary>
    public DepoWise.Infrastructure.Organization.MenuLayoutService MenuLayout { get; }
    /// <summary>G4-1 — ön muhasebe cari kartı ve hesap hareketi.</summary>
    public DepoWise.Infrastructure.Accounting.PartyService Parties { get; }
    public DepoWise.Infrastructure.Accounting.PartyLedgerService PartyLedger { get; }
    public DepoWise.Infrastructure.Accounting.InvoiceService Invoices { get; }
    public DepoWise.Infrastructure.Accounting.InvoiceQueryService InvoiceQueries { get; }
    public DepoWise.Infrastructure.Accounting.FinanceService Finance { get; }
    public DepoWise.Infrastructure.Accounting.FinanceQueryService FinanceQueries { get; }
    public SyncServer Sync { get; }
    public ReleaseService Releases { get; }
    public EnrollmentService Enrollment { get; }
    public BackupStore Backups { get; }
    /// <summary>Makine yedekleri: aylık zip arşivleme + 3 yıl saklama + disk koruması.</summary>
    public MachineBackupArchiver MachineBackups { get; }
    public ReleaseStore ReleasePackages { get; }
    public SyncValidator SyncValidator { get; }
    public BusinessSyncService BusinessSync { get; }

    // İş modülleri (web liste ekranları için)
    public DepoWise.Infrastructure.Materials.MaterialService Materials { get; }
    public DepoWise.Infrastructure.Materials.LookupService Lookups { get; }
    public DepoWise.Infrastructure.Materials.StockService Stock { get; }
    public DepoWise.Infrastructure.Materials.StockChangeLogService StockChangeLog { get; }
    public DepoWise.Infrastructure.Materials.OpeningStockService OpeningStock { get; }
    public DepoWise.Infrastructure.Vehicles.VehicleService Vehicles { get; }
    public DepoWise.Infrastructure.Maintenance.MaintenanceService Maintenance { get; }
    public DepoWise.Infrastructure.Maintenance.InspectionService Inspection { get; }
    public DepoWise.Infrastructure.Operations.FuelService Fuel { get; }
    public DepoWise.Infrastructure.Operations.DailyActivityService DailyActivity { get; }
    public DepoWise.Infrastructure.Requests.RequestService Requests { get; }
    /// <summary>Talep Operasyonları (Faz 2) — onaylı taleplerin operasyon süreci; stok DEĞİŞTİRMEZ.</summary>
    public DepoWise.Infrastructure.Requests.RequestOperationsService RequestOps { get; }
    public DepoWise.Infrastructure.Organization.BranchService Branches { get; }
    public DepoWise.Infrastructure.Organization.ProjectService Projects { get; }   // PRJ-01 (ADR-164)
    public DepoWise.Infrastructure.Files.DocumentService Documents { get; }        // EVR-01 (ADR-165)
    public DepoWise.Infrastructure.Equipment.EquipmentService Equipment { get; }   // EKP-01 (ADR-166)
    public DepoWise.Infrastructure.Assignments.AssignmentService Assignments { get; }   // ZMT-01 (ADR-167)
    public DepoWise.Infrastructure.Accounting.CostCenterService CostCenters { get; }   // MLY-01 (ADR-168)
    public DepoWise.Infrastructure.Purchasing.PurchaseOrderService Purchasing { get; }   // STN-01 (ADR-169)
    public DepoWise.Infrastructure.WorkOrders.WorkOrderService WorkOrders { get; }   // EMR-01 (ADR-170)
    public DepoWise.Infrastructure.Org.PersonnelService Personnel { get; }
    /// <summary>Şube kapsamı çözümleyici — içe aktarımda seçilen hedef şubenin kullanıcının
    /// kapsamında olduğunu doğrulamak için (fail-closed).</summary>
    public DepoWise.Infrastructure.Org.ScopeResolver Scopes { get; }
    public DepoWise.Infrastructure.Org.PersonnelTitleService PersonnelTitles { get; }
    public DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService MaintenanceDefinitions { get; }
    public DepoWise.Infrastructure.Security.PermissionService Permissions { get; }
    public DepoWise.Infrastructure.Security.PermissionTemplateService PermissionTemplates { get; }
    public DepoWise.Infrastructure.Database.AuditLogService AuditLog { get; }
    public DepoWise.Infrastructure.Files.FileService Files { get; }
    public DepoWise.Infrastructure.Files.TrashService Trash { get; }
    public DepoWise.Application.Files.IFileStorageProvider Storage { get; }
    public DepoWise.Infrastructure.Vehicles.VehicleTemplateService VehicleTemplates { get; }
    public DepoWise.Infrastructure.Materials.MaterialTemplateService MaterialTemplates { get; }
    public DepoWise.Infrastructure.Requests.RequestPdfService RequestPdf { get; }
    public DepoWise.Infrastructure.Reporting.ReportService Reports { get; }
    public DepoWise.Infrastructure.Reporting.DashboardService Dashboard { get; }
    /// <summary>Filtrelenmiş liste sonuçlarını Excel'e aktarma (kullanıcı isteği 2026-07-19).</summary>
    public DepoWise.Infrastructure.Reporting.ExcelExportService Excel { get; }

    // ── Excel İÇE AKTARIM (İş #7, 2026-08-09) — masaüstünde zaten vardı, web'e taşındı.
    // Aynı servisler kullanılır → iki platform BİREBİR aynı doğrulama ve iş kurallarını uygular.
    public DepoWise.Infrastructure.Reporting.MaterialImportService MaterialImport { get; }
    public DepoWise.Infrastructure.Reporting.VehicleImportService VehicleImport { get; }
    public DepoWise.Infrastructure.Reporting.PersonnelImportService PersonnelImport { get; }
    public DepoWise.Infrastructure.Reporting.MaintenanceImportService MaintenanceImport { get; }
    public DepoWise.Infrastructure.Reporting.InspectionImportService InspectionImport { get; }
    public DepoWise.Infrastructure.Reporting.FuelImportService FuelImport { get; }
    public DepoWise.Infrastructure.Reporting.FuelDepotImportService FuelDepotImport { get; }
    public DepoWise.Infrastructure.Files.BackupService DbBackup { get; }
    public DepoWise.Infrastructure.Settings.SettingsService Settings { get; }
    /// <summary>Liste ekranı kolon tercihi — KİŞİSEL (kullanıcı bazlı, firma bağımsız).</summary>
    public DepoWise.Infrastructure.Settings.UserListPreferenceService ListPrefs { get; }

    /// <summary>Sunucu veri klasörü (yedekler, fotoğraflar, yayın paketleri ve SQLite'a düşüldüyse
    /// veritabanı burada). YOL-01 testleri bu kökün dışına çıkılamadığını buradan doğrular.</summary>
    public string DataDir { get; }

    public ServerServices(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        DataDir = dataDir;

        // PostgreSQL geçişi (Faz 3): DEPOWISE_PG_URL tanımlıysa sunucu PostgreSQL kullanır; TANIMSIZSA
        // eskisi gibi SQLite → babanın canlı sunucusu birebir aynı çalışır (varsayılan değişmedi).
        // Geçiş ancak açıkça bu değişken verilerek (ayrı/kopya DB'ye) etkinleşir — canlı veriye dokunmaz.
        var pgUrl = Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");
        Factory = string.IsNullOrWhiteSpace(pgUrl)
            ? new SqliteConnectionFactory(Path.Combine(dataDir, "depowise-server.db"))
            : new PostgresConnectionFactory(pgUrl);
        new MigrationRunner(Factory).Run();

        var clock = new SystemClock();
        // F0 (YET-01): yetki fotoğrafı önbelleği — okuyan (Auth) ve geçersiz kılan (Users, Permissions,
        // RoleGrants) servisler AYNI örneği paylaşmalıdır; aksi halde yetki değişimi yansımaz.
        // Sunucuda en kritik yer burasıdır: Session() HER API isteğinde çalışıyor.
        PermissionSnapshots = new DepoWise.Application.Security.PermissionSnapshotCache();
        Auth = new AuthService(Factory, clock, PermissionSnapshots);
        Users = new UserService(Factory, clock, PermissionSnapshots);
        Companies = new DepoWise.Infrastructure.Organization.CompanyService(Factory, clock);
        CompanyPurge = new DepoWise.Infrastructure.Organization.CompanyPurgeService(Factory, clock);
        CompanyLocalReset = new DepoWise.Infrastructure.Organization.CompanyLocalResetService(Factory, clock);
        MachineReset = new DepoWise.Infrastructure.Sync.MachineResetService(Factory, clock);
        SpecialCode = new SpecialCodeService(Factory, clock);
        CompanyGrants = new DepoWise.Infrastructure.Organization.CompanyGrantService(Factory, clock);
        ScreenVisibility = new DepoWise.Infrastructure.Organization.ScreenVisibilityService(Factory, clock);
        MenuLayout = new DepoWise.Infrastructure.Organization.MenuLayoutService(Factory, clock);
        Parties = new DepoWise.Infrastructure.Accounting.PartyService(Factory, clock);
        PartyLedger = new DepoWise.Infrastructure.Accounting.PartyLedgerService(Factory, clock);
        RoleGrants = new DepoWise.Infrastructure.Organization.RoleGrantService(Factory, clock, PermissionSnapshots);
        Sync = new SyncServer(Factory, clock);
        Releases = new ReleaseService(Factory, clock);
        Enrollment = new EnrollmentService(Factory, clock);
        Backups = new BackupStore(Path.Combine(dataDir, "backups"));
        MachineBackups = new MachineBackupArchiver(Path.Combine(dataDir, "backups"));
        ReleasePackages = new ReleaseStore(Path.Combine(dataDir, "releases"));
        SyncValidator = new SyncValidator(Factory);
        BusinessSync = new BusinessSyncService(Factory, clock);

        Materials = new DepoWise.Infrastructure.Materials.MaterialService(Factory, clock);
        Lookups = new DepoWise.Infrastructure.Materials.LookupService(Factory, clock);
        Stock = new DepoWise.Infrastructure.Materials.StockService(Factory, clock);
        StockChangeLog = new DepoWise.Infrastructure.Materials.StockChangeLogService(Factory, Stock, clock);
        OpeningStock = new DepoWise.Infrastructure.Materials.OpeningStockService(Factory, clock);
        // G4-2: fatura, stok ve cari servislerini KULLANIR (paralel defter yok) - bu yuzden onlardan SONRA kurulur.
        Invoices = new DepoWise.Infrastructure.Accounting.InvoiceService(Factory, Stock, PartyLedger, clock);
        InvoiceQueries = new DepoWise.Infrastructure.Accounting.InvoiceQueryService(Factory, clock);
        // G4-3: kasa/banka cari servisini KULLANIR (paralel cari defteri yok) - ondan SONRA kurulur.
        Finance = new DepoWise.Infrastructure.Accounting.FinanceService(Factory, PartyLedger, clock);
        FinanceQueries = new DepoWise.Infrastructure.Accounting.FinanceQueryService(Factory);
        Vehicles = new DepoWise.Infrastructure.Vehicles.VehicleService(Factory, clock);
        Maintenance = new DepoWise.Infrastructure.Maintenance.MaintenanceService(Factory, clock);
        Inspection = new DepoWise.Infrastructure.Maintenance.InspectionService(Factory, clock);
        Fuel = new DepoWise.Infrastructure.Operations.FuelService(Factory, clock);
        MaintenanceDefinitions = new DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService(Factory, clock);
        DailyActivity = new DepoWise.Infrastructure.Operations.DailyActivityService(Factory, Maintenance, clock, MaintenanceDefinitions);
        Requests = new DepoWise.Infrastructure.Requests.RequestService(Factory, new DepoWise.Infrastructure.Materials.StockService(Factory, clock), clock);
        RequestOps = new DepoWise.Infrastructure.Requests.RequestOperationsService(Factory, clock);
        Branches = new DepoWise.Infrastructure.Organization.BranchService(Factory, clock);
        Projects = new DepoWise.Infrastructure.Organization.ProjectService(Factory, clock);
        Documents = new DepoWise.Infrastructure.Files.DocumentService(Factory, Storage, clock);
        Equipment = new DepoWise.Infrastructure.Equipment.EquipmentService(Factory, clock);
        Assignments = new DepoWise.Infrastructure.Assignments.AssignmentService(Factory, clock);
        CostCenters = new DepoWise.Infrastructure.Accounting.CostCenterService(Factory, clock);
        Purchasing = new DepoWise.Infrastructure.Purchasing.PurchaseOrderService(Factory, clock);
        WorkOrders = new DepoWise.Infrastructure.WorkOrders.WorkOrderService(Factory, clock);
        Scopes = new DepoWise.Infrastructure.Org.ScopeResolver(Factory);
        Personnel = new DepoWise.Infrastructure.Org.PersonnelService(Factory, Scopes, clock);
        PersonnelTitles = new DepoWise.Infrastructure.Org.PersonnelTitleService(Factory, clock);
        Permissions = new DepoWise.Infrastructure.Security.PermissionService(Factory, clock, PermissionSnapshots);
        PermissionTemplates = new DepoWise.Infrastructure.Security.PermissionTemplateService(Factory, clock);
        AuditLog = new DepoWise.Infrastructure.Database.AuditLogService(Factory);
        Storage = new DepoWise.Infrastructure.Files.LocalFileStorageProvider(Path.Combine(dataDir, "files"));
        Files = new DepoWise.Infrastructure.Files.FileService(Factory, Storage, clock);
        Trash = new DepoWise.Infrastructure.Files.TrashService(Factory, clock);
        VehicleTemplates = new DepoWise.Infrastructure.Vehicles.VehicleTemplateService(Factory, clock);
        MaterialTemplates = new DepoWise.Infrastructure.Materials.MaterialTemplateService(Factory, clock);
        RequestPdf = new DepoWise.Infrastructure.Requests.RequestPdfService();
        Reports = new DepoWise.Infrastructure.Reporting.ReportService(Factory);
        Dashboard = new DepoWise.Infrastructure.Reporting.DashboardService(Factory, Maintenance, Inspection);
        Excel = new DepoWise.Infrastructure.Reporting.ExcelExportService();
        // İçe aktarım servisleri — masaüstündeki (DesktopServices) bağlamayla BİREBİR aynı.
        MaterialImport = new DepoWise.Infrastructure.Reporting.MaterialImportService(Materials, Lookups, OpeningStock, Vehicles);
        VehicleImport = new DepoWise.Infrastructure.Reporting.VehicleImportService(Vehicles, Lookups);
        InspectionImport = new DepoWise.Infrastructure.Reporting.InspectionImportService(Inspection, Vehicles);
        MaintenanceImport = new DepoWise.Infrastructure.Reporting.MaintenanceImportService(Maintenance, MaintenanceDefinitions, Vehicles, Lookups);
        FuelImport = new DepoWise.Infrastructure.Reporting.FuelImportService(Fuel, Vehicles, Lookups);
        FuelDepotImport = new DepoWise.Infrastructure.Reporting.FuelDepotImportService(Fuel, Lookups);
        PersonnelImport = new DepoWise.Infrastructure.Reporting.PersonnelImportService(Personnel, PersonnelTitles, Users, Lookups);
        DbBackup = new DepoWise.Infrastructure.Files.BackupService(Factory, clock, Path.Combine(dataDir, "dbbackups"));
        Settings = new DepoWise.Infrastructure.Settings.SettingsService(Factory, clock);
        ListPrefs = new DepoWise.Infrastructure.Settings.UserListPreferenceService(Factory, clock);

        EnsureSeedAdmins();
    }

    private void EnsureSeedAdmins()
    {
        using var conn = Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users;";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            var pw = SeedPassword("DEPOWISE_SEED_ADMIN_PASSWORD", "admin");
            // GUV-01: tohum parolası geçicidir (env verilmediyse rastgele üretilip konsola yazılır) →
            // hesap ilk girişte parolasını değiştirmek ZORUNDA. Kolon Migration042'de mevcut.
            Users.EnsureInitialAdmin("DEPOWISE", "admin", pw, RoleKeys.CompanyAdmin, mustChangePassword: true);
        }

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE r.role_key=@k;";
        cmd2.AddWithValue("@k", RoleKeys.SuperAdmin);
        if (Convert.ToInt64(cmd2.ExecuteScalar()) == 0)
        {
            var pw = SeedPassword("DEPOWISE_SEED_SUPERADMIN_PASSWORD", "superadmin");
            Users.EnsureInitialAdmin("DEPOWISE", "superadmin", pw, RoleKeys.SuperAdmin, mustChangePassword: true);   // GUV-01
        }

        // SELF-HEAL (kilit kurtarma): pasife düşmüş süper admin(ler)i her açılışta yeniden aktifleştir. Süper admin
        // platform sahibidir, hiçbir koşulda pasif kalmamalı. Firma silme artık süper admini pasife almıyor
        // (CompanyService.Delete); bu satır geçmişte kilitlenmiş kurulumları da bir redeploy ile kurtarır.
        using var heal = conn.CreateCommand();
        heal.CommandText =
            "UPDATE users SET is_active=1 WHERE is_deleted=0 AND is_active=0 " +
            "AND id IN (SELECT ur.user_id FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE r.role_key=@k);";
        heal.AddWithValue("@k", RoleKeys.SuperAdmin);
        var healed = heal.ExecuteNonQuery();
        if (healed > 0) Console.WriteLine($"[DepoWise] Self-heal: {healed} pasif süper admin yeniden aktifleştirildi.");
    }

    /// <summary>İlk kurulum şifresi: env'den; yoksa RASTGELE üretilir ve bir kez konsola/loga yazılır.
    /// Sabit "admin123"/"superadmin" varsayılanları kaldırıldı (bilinen kimlikle ele geçirme riski).</summary>
    private static string SeedPassword(string envName, string user)
    {
        var fromEnv = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var pw = new string(System.Security.Cryptography.RandomNumberGenerator.GetItems<char>(chars, 16));
        Console.WriteLine($"[DepoWise] İlk '{user}' kullanıcısı oluşturuldu. Geçici şifre: {pw} — hemen değiştirin. ({envName} ile önceden belirlenebilir.)");
        return pw;
    }

    /// <summary>JWT'den (userId+companyId) tam oturumu SUNUCUDA yeniden kurar — yetkiler token'dan değil DB'den.</summary>
    public SessionContext? SessionFor(string? companyId, string? userId)
        => string.IsNullOrEmpty(companyId) || string.IsNullOrEmpty(userId)
            ? null : Auth.CreateSessionForUser(companyId, userId);
}

