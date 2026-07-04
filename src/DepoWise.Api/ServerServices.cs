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
    public AuthService Auth { get; }
    public UserService Users { get; }
    public DepoWise.Infrastructure.Organization.CompanyService Companies { get; }
    public SyncServer Sync { get; }
    public ReleaseService Releases { get; }
    public EnrollmentService Enrollment { get; }
    public BackupStore Backups { get; }
    public ReleaseStore ReleasePackages { get; }
    public SyncValidator SyncValidator { get; }
    public BusinessSyncService BusinessSync { get; }

    // İş modülleri (web liste ekranları için)
    public DepoWise.Infrastructure.Materials.MaterialService Materials { get; }
    public DepoWise.Infrastructure.Materials.LookupService Lookups { get; }
    public DepoWise.Infrastructure.Materials.StockService Stock { get; }
    public DepoWise.Infrastructure.Materials.OpeningStockService OpeningStock { get; }
    public DepoWise.Infrastructure.Vehicles.VehicleService Vehicles { get; }
    public DepoWise.Infrastructure.Maintenance.MaintenanceService Maintenance { get; }
    public DepoWise.Infrastructure.Maintenance.InspectionService Inspection { get; }
    public DepoWise.Infrastructure.Operations.FuelService Fuel { get; }
    public DepoWise.Infrastructure.Operations.DailyActivityService DailyActivity { get; }
    public DepoWise.Infrastructure.Requests.RequestService Requests { get; }
    public DepoWise.Infrastructure.Organization.BranchService Branches { get; }
    public DepoWise.Infrastructure.Org.PersonnelService Personnel { get; }
    public DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService MaintenanceDefinitions { get; }
    public DepoWise.Infrastructure.Security.PermissionService Permissions { get; }
    public DepoWise.Infrastructure.Security.PermissionTemplateService PermissionTemplates { get; }
    public DepoWise.Infrastructure.Database.AuditLogService AuditLog { get; }
    public DepoWise.Infrastructure.Files.FileService Files { get; }
    public DepoWise.Application.Files.IFileStorageProvider Storage { get; }
    public DepoWise.Infrastructure.Vehicles.VehicleTemplateService VehicleTemplates { get; }
    public DepoWise.Infrastructure.Requests.RequestPdfService RequestPdf { get; }
    public DepoWise.Infrastructure.Reporting.ReportService Reports { get; }
    public DepoWise.Infrastructure.Reporting.DashboardService Dashboard { get; }
    public DepoWise.Infrastructure.Files.BackupService DbBackup { get; }
    public DepoWise.Infrastructure.Settings.SettingsService Settings { get; }

    public ServerServices(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        Factory = new SqliteConnectionFactory(Path.Combine(dataDir, "depowise-server.db"));
        new MigrationRunner(Factory).Run();

        var clock = new SystemClock();
        Auth = new AuthService(Factory, clock);
        Users = new UserService(Factory, clock);
        Companies = new DepoWise.Infrastructure.Organization.CompanyService(Factory, clock);
        Sync = new SyncServer(Factory, clock);
        Releases = new ReleaseService(Factory, clock);
        Enrollment = new EnrollmentService(Factory, clock);
        Backups = new BackupStore(Path.Combine(dataDir, "backups"));
        ReleasePackages = new ReleaseStore(Path.Combine(dataDir, "releases"));
        SyncValidator = new SyncValidator(Factory);
        BusinessSync = new BusinessSyncService(Factory, clock);

        Materials = new DepoWise.Infrastructure.Materials.MaterialService(Factory, clock);
        Lookups = new DepoWise.Infrastructure.Materials.LookupService(Factory, clock);
        Stock = new DepoWise.Infrastructure.Materials.StockService(Factory, clock);
        OpeningStock = new DepoWise.Infrastructure.Materials.OpeningStockService(Factory, clock);
        Vehicles = new DepoWise.Infrastructure.Vehicles.VehicleService(Factory, clock);
        Maintenance = new DepoWise.Infrastructure.Maintenance.MaintenanceService(Factory, clock);
        Inspection = new DepoWise.Infrastructure.Maintenance.InspectionService(Factory, clock);
        Fuel = new DepoWise.Infrastructure.Operations.FuelService(Factory, clock);
        DailyActivity = new DepoWise.Infrastructure.Operations.DailyActivityService(Factory, Maintenance, clock);
        Requests = new DepoWise.Infrastructure.Requests.RequestService(Factory, new DepoWise.Infrastructure.Materials.StockService(Factory, clock), clock);
        Branches = new DepoWise.Infrastructure.Organization.BranchService(Factory, clock);
        Personnel = new DepoWise.Infrastructure.Org.PersonnelService(Factory, new DepoWise.Infrastructure.Org.ScopeResolver(Factory), clock);
        MaintenanceDefinitions = new DepoWise.Infrastructure.Maintenance.MaintenanceDefinitionService(Factory, clock);
        Permissions = new DepoWise.Infrastructure.Security.PermissionService(Factory, clock);
        PermissionTemplates = new DepoWise.Infrastructure.Security.PermissionTemplateService(Factory, clock);
        AuditLog = new DepoWise.Infrastructure.Database.AuditLogService(Factory);
        Storage = new DepoWise.Infrastructure.Files.LocalFileStorageProvider(Path.Combine(dataDir, "files"));
        Files = new DepoWise.Infrastructure.Files.FileService(Factory, Storage, clock);
        VehicleTemplates = new DepoWise.Infrastructure.Vehicles.VehicleTemplateService(Factory, clock);
        RequestPdf = new DepoWise.Infrastructure.Requests.RequestPdfService();
        Reports = new DepoWise.Infrastructure.Reporting.ReportService(Factory);
        Dashboard = new DepoWise.Infrastructure.Reporting.DashboardService(Factory, Maintenance, Inspection);
        DbBackup = new DepoWise.Infrastructure.Files.BackupService(Factory, clock, Path.Combine(dataDir, "dbbackups"));
        Settings = new DepoWise.Infrastructure.Settings.SettingsService(Factory, clock);

        EnsureSeedAdmins();
    }

    private void EnsureSeedAdmins()
    {
        using var conn = Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users;";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            Users.EnsureInitialAdmin("DEPOWISE", "admin", "admin123", RoleKeys.CompanyAdmin);

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE r.role_key=$k;";
        cmd2.Parameters.AddWithValue("$k", RoleKeys.SuperAdmin);
        if (Convert.ToInt64(cmd2.ExecuteScalar()) == 0)
            Users.EnsureInitialAdmin("DEPOWISE", "superadmin", "superadmin", RoleKeys.SuperAdmin);
    }

    /// <summary>JWT'den (userId+companyId) tam oturumu SUNUCUDA yeniden kurar — yetkiler token'dan değil DB'den.</summary>
    public SessionContext? SessionFor(string? companyId, string? userId)
        => string.IsNullOrEmpty(companyId) || string.IsNullOrEmpty(userId)
            ? null : Auth.CreateSessionForUser(companyId, userId);
}
