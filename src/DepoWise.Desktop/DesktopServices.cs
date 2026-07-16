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
using Microsoft.Data.Sqlite;

namespace DepoWise.Desktop;

/// <summary>
/// Hafif servis tutucu (DI container yok). Açılışta bir kez kurulur; ekran VM'leri buradan servis alır.
/// Oturum login sonrası set edilir.
/// </summary>
public static class DesktopServices
{
    public const string DefaultCompanyId = "DEPOWISE";

    public static IDbConnectionFactory Factory { get; private set; } = null!;
    public static AuthService Auth { get; private set; } = null!;
    public static UserService Users { get; private set; } = null!;
    public static BranchService Branches { get; private set; } = null!;
    public static PermissionService Permissions { get; private set; } = null!;
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
    public static DailyActivityService DailyActivity { get; private set; } = null!;
    public static DashboardService Dashboard { get; private set; } = null!;
    public static VehicleService Vehicles { get; private set; } = null!;
    public static VehicleTemplateService VehicleTemplates { get; private set; } = null!;
    public static MaterialTemplateService MaterialTemplates { get; private set; } = null!;
    public static MaintenanceService Maintenance { get; private set; } = null!;
    public static MaintenanceDefinitionService MaintenanceDefs { get; private set; } = null!;
    public static InspectionService Inspection { get; private set; } = null!;
    public static DepoWise.Infrastructure.Org.PersonnelService Personnel { get; private set; } = null!;
    public static DepoWise.Infrastructure.Org.PersonnelTitleService PersonnelTitles { get; private set; } = null!;
    public static FuelService Fuel { get; private set; } = null!;
    public static RequestService Requests { get; private set; } = null!;
    public static IRequestPdfService RequestPdf { get; private set; } = null!;
    public static ReportService Reports { get; private set; } = null!;
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
    public static SettingsService Settings { get; private set; } = null!;
    public static LookupService Lookups { get; private set; } = null!;
    public static FileService Files { get; private set; } = null!;
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
            cmd.CommandText = "SELECT u.branch_id, b.name FROM users u LEFT JOIN branches b ON b.id=u.branch_id WHERE u.id=$id;";
            cmd.Parameters.AddWithValue("$id", userId);
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
        Auth = new AuthService(Factory, clock);
        Users = new UserService(Factory, clock);
        Materials = new MaterialService(Factory, clock);
        OpeningStock = new OpeningStockService(Factory, clock);
        Stock = new StockService(Factory, clock);
        DailyActivity = new DailyActivityService(Factory, Maintenance, clock);
        Maintenance = new MaintenanceService(Factory, clock);
        MaintenanceDefs = new MaintenanceDefinitionService(Factory, clock);
        Inspection = new InspectionService(Factory, clock);
        Personnel = new DepoWise.Infrastructure.Org.PersonnelService(Factory, new DepoWise.Infrastructure.Org.ScopeResolver(Factory), clock);
        PersonnelTitles = new DepoWise.Infrastructure.Org.PersonnelTitleService(Factory, clock);
        Vehicles = new VehicleService(Factory, clock);
        VehicleTemplates = new VehicleTemplateService(Factory, clock);
        MaterialTemplates = new MaterialTemplateService(Factory, clock);
        Fuel = new FuelService(Factory, clock);
        Requests = new RequestService(Factory, new StockService(Factory, clock), clock);
        RequestPdf = new RequestPdfService();
        Branches = new BranchService(Factory, clock);
        Permissions = new PermissionService(Factory, clock);
        PermissionTemplates = new PermissionTemplateService(Factory, clock);
        Companies = new CompanyService(Factory, clock);
        Releases = new ReleaseService(Factory, clock);
        Update = new UpdateService(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "update"));
        UpdateDownload = new UpdateDownloadService();
        UpdateApi = new UpdateApiClient();
        Enrollment = new DepoWise.Infrastructure.Sync.EnrollmentService(Factory, clock);
        Reports = new ReportService(Factory);
        Excel = new ExcelExportService();
        MaterialImport = new MaterialImportService(Materials);
        Trash = new DepoWise.Infrastructure.Files.TrashService(Factory, clock);
        Audit = new AuditLogService(Factory);
        Backup = new DepoWise.Infrastructure.Files.BackupService(Factory, clock);
        BackupUpload = new DepoWise.Infrastructure.Files.BackupUploadService();
        VehicleImport = new VehicleImportService(Vehicles);
        InspectionImport = new InspectionImportService(Inspection, Vehicles);
        MaintenanceImport = new MaintenanceImportService(Maintenance, MaintenanceDefs, Vehicles);
        Settings = new SettingsService(Factory, clock);
        Lookups = new LookupService(Factory, clock);
        // Yakıt import'ları Lookups'a bağlı (personel/tedarikçi ada göre eşlenir) → Lookups'tan SONRA kurulur.
        FuelImport = new FuelImportService(Fuel, Vehicles, Lookups);
        FuelDepotImport = new FuelDepotImportService(Fuel, Lookups);
        Storage = new LocalFileStorageProvider();
        Files = new FileService(Factory, Storage, clock);
        Dashboard = new DashboardService(Factory, Maintenance, Inspection);
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
        cmd.CommandText = "SELECT COALESCE(NULLIF(full_name,''), username) FROM users WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", userId);
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
