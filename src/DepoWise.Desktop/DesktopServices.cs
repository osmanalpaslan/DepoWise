using System;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Vehicles;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Settings;
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
    public static MaterialService Materials { get; private set; } = null!;
    public static OpeningStockService OpeningStock { get; private set; } = null!;
    public static DashboardService Dashboard { get; private set; } = null!;
    public static VehicleService Vehicles { get; private set; } = null!;
    public static MaintenanceService Maintenance { get; private set; } = null!;
    public static InspectionService Inspection { get; private set; } = null!;
    public static FuelService Fuel { get; private set; } = null!;
    public static RequestService Requests { get; private set; } = null!;
    public static ReportService Reports { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;
    public static BrandingSettings Branding { get; private set; } = BrandingSettings.Default;
    public static ThemeTokens Theme { get; private set; } = ThemeTokens.Default;

    /// <summary>Aktif oturum (login sonrası). Çıkışta null.</summary>
    public static SessionContext? Session { get; set; }

    public static void Initialize(BootstrapResult boot)
    {
        var clock = new SystemClock();
        Factory = SqliteConnectionFactory.ForEnvironment(DesktopBootstrap.Environment);
        Auth = new AuthService(Factory, clock);
        Users = new UserService(Factory, clock);
        Materials = new MaterialService(Factory, clock);
        OpeningStock = new OpeningStockService(Factory, clock);
        Maintenance = new MaintenanceService(Factory, clock);
        Inspection = new InspectionService(Factory, clock);
        Vehicles = new VehicleService(Factory, clock);
        Fuel = new FuelService(Factory, clock);
        Requests = new RequestService(Factory, new StockService(Factory, clock), clock);
        Reports = new ReportService(Factory);
        Settings = new SettingsService(Factory, clock);
        Dashboard = new DashboardService(Factory, Maintenance, Inspection);
        Branding = boot.Branding;
        Theme = boot.Theme;

        EnsureFirstRunAdmin();
    }

    /// <summary>İlk açılış: hiç kullanıcı yoksa varsayılan firma + admin (admin/admin123) oluştur.</summary>
    private static void EnsureFirstRunAdmin()
    {
        using var conn = Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users;";
        var count = Convert.ToInt64(cmd.ExecuteScalar());
        if (count == 0)
            Users.EnsureInitialAdmin(DefaultCompanyId, "admin", "admin123", RoleKeys.CompanyAdmin);
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
