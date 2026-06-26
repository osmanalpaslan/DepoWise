using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Salt-okuma raporlar — tenant + permission fail-closed. Süper Admin firma seçebilir; diğerleri
/// kendi firmasına kilitli. Ağır rapor `ReportGate.EnsureRunnable` ile yalnız Sorgula/Filtrele sonrası çalışır.
/// </summary>
public sealed class ReportService
{
    private const string Module = "reports";
    private readonly IDbConnectionFactory _factory;

    public ReportService(IDbConnectionFactory factory) => _factory = factory;

    public TableModel StockStatus(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.code, m.name, COALESCE(b.quantity,'0') AS qty, m.min_stock
FROM materials m LEFT JOIN stock_balances b ON b.material_id=m.id
WHERE m.company_id=$c AND m.is_deleted=0
ORDER BY m.code;";
        cmd.Parameters.AddWithValue("$c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3) });
        return new TableModel("Stok Durumu", new[] { "Kod", "Malzeme", "Stok", "Min Stok" }, rows);
    }

    public TableModel FuelConsumption(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT v.internal_code, COUNT(fd.id), COALESCE(SUM(CAST(fd.liters AS REAL)),0),
       COALESCE(SUM(CAST(fd.liters AS REAL)*CAST(fd.unit_price AS REAL)),0)
FROM fuel_distributions fd JOIN vehicles v ON v.id=fd.vehicle_id
WHERE fd.company_id=$c AND fd.is_deleted=0
" + DateFilter(req, "fd.distribution_date") + @"
GROUP BY fd.vehicle_id ORDER BY v.internal_code;";
        cmd.Parameters.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { r.GetString(0), r.GetInt32(1), r.GetDouble(2), r.GetDouble(3) });
        return new TableModel("Yakıt Tüketim", new[] { "Araç", "İşlem", "Litre", "Tutar" }, rows);
    }

    private static string DateFilter(ReportRequest req, string col)
    {
        var sb = "";
        if (req.FromDate is not null) sb += $" AND {col} >= $from";
        if (req.ToDate is not null) sb += $" AND {col} <= $to";
        return sb;
    }

    private static void BindDates(SqliteCommand cmd, ReportRequest req)
    {
        if (req.FromDate is not null) cmd.Parameters.AddWithValue("$from", req.FromDate.Value);
        if (req.ToDate is not null) cmd.Parameters.AddWithValue("$to", req.ToDate.Value);
    }
}
