using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Application.Maintenance;
using Microsoft.Data.Sqlite;

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

    public DashboardService(IDbConnectionFactory factory, MaintenanceService maintenance, InspectionService inspection)
    {
        _factory = factory;
        _maintenance = maintenance;
        _inspection = inspection;
    }

    public DashboardSummary GetSummary(SessionContext s)
    {
        using var conn = _factory.Create();
        int vehicles = Count(conn, "vehicles", s.CompanyId);
        int materials = Count(conn, "materials", s.CompanyId);
        int personnel = Count(conn, "personnel", s.CompanyId);
        int lowStock = LowStockCount(conn, s.CompanyId);
        int pending = PendingRequests(conn, s.CompanyId);

        var alerts = new List<DashboardAlert>();

        // Bakım uyarıları (yalnız Normal olmayanlar)
        if (AccessControl.Can(s, "maintenance", PermissionAction.View))
        {
            foreach (var a in _maintenance.GetAlerts(s))
            {
                if (a.Level == AlertLevel.Normal) continue;
                alerts.Add(new DashboardAlert(AlertKind.Maintenance, a.DefinitionName,
                    $"%{a.Progress * 100:0} ({a.Level})", "maintenance:records", a.Level is AlertLevel.Critical or AlertLevel.Overdue, a.VehicleId));
            }
        }
        // Muayene/sigorta
        if (AccessControl.Can(s, "inspection", PermissionAction.View))
        {
            foreach (var a in _inspection.GetAlerts(s))
            {
                if (a.Level == DateAlertLevel.Normal) continue;
                alerts.Add(new DashboardAlert(AlertKind.Inspection, a.DocType,
                    a.Level.ToString(), "vehicles", a.Level == DateAlertLevel.Expired, a.VehicleId));
            }
        }
        // Düşük stok
        if (AccessControl.Can(s, "materials", PermissionAction.View) && lowStock > 0)
            alerts.Add(new DashboardAlert(AlertKind.LowStock, "Düşük Stok", $"{lowStock} malzeme", "materials", lowStock > 0));

        return new DashboardSummary(vehicles, materials, lowStock, pending, personnel, alerts);
    }

    private static int Count(SqliteConnection conn, string table, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$c", companyId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int LowStockCount(SqliteConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COUNT(*) FROM materials m
LEFT JOIN stock_balances b ON b.material_id = m.id
WHERE m.company_id=$c AND m.is_deleted=0
AND CAST(COALESCE(b.quantity,'0') AS REAL) <= CAST(m.min_stock AS REAL) AND CAST(m.min_stock AS REAL) > 0;";
        cmd.Parameters.AddWithValue("$c", companyId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static int PendingRequests(SqliteConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM material_requests WHERE company_id=$c AND status='pending' AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$c", companyId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
