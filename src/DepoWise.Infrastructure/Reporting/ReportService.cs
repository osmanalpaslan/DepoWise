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

    public TableModel Maintenance(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT vm.performed_date, v.internal_code, d.name, COALESCE(p.full_name,''),
       (SELECT COALESCE(SUM(CAST(mm.quantity AS REAL)*CAST(COALESCE(mm.unit_price,'0') AS REAL)),0)
        FROM maintenance_materials mm WHERE mm.maintenance_id = vm.id)
FROM vehicle_maintenances vm
JOIN vehicles v ON v.id = vm.vehicle_id
JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
LEFT JOIN personnel p ON p.id = vm.technician_id
WHERE vm.company_id=$c AND vm.is_deleted=0 AND vm.is_cancelled=0
" + DateFilter(req, "vm.performed_date") + @"
ORDER BY vm.performed_date DESC;";
        cmd.Parameters.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { D(r.IsDBNull(0) ? null : r.GetInt64(0)), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDouble(4) });
        return new TableModel("Bakım Raporu", new[] { "Tarih", "Araç", "Bakım", "Teknisyen", "Malzeme Maliyeti" }, rows);
    }

    public TableModel FuelDepot(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT entry_date, CAST(liters AS REAL), CAST(unit_price AS REAL),
       CAST(liters AS REAL)*CAST(unit_price AS REAL), COALESCE(invoice_no,'')
FROM fuel_depot_entries
WHERE company_id=$c AND is_deleted=0
" + DateFilter(req, "entry_date") + @"
ORDER BY entry_date DESC;";
        cmd.Parameters.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { D(r.GetInt64(0)), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetString(4) });
        return new TableModel("Depo Girişi Raporu", new[] { "Tarih", "Litre", "Birim Fiyat", "Tutar", "Fatura No" }, rows);
    }

    public TableModel Requests(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT mr.doc_no, mr.request_date, mr.status,
       (SELECT COUNT(*) FROM material_request_items i WHERE i.request_id = mr.id)
FROM material_requests mr
WHERE mr.company_id=$c AND mr.is_deleted=0
" + DateFilter(req, "mr.request_date") + @"
ORDER BY mr.request_date DESC;";
        cmd.Parameters.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { r.GetString(0), D(r.GetInt64(1)), StatusTr(r.GetString(2)), r.GetInt32(3) });
        return new TableModel("Talep Raporu", new[] { "Belge No", "Tarih", "Durum", "Kalem" }, rows);
    }

    private static string D(long? ms) => ms is null or 0 ? "" : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime.ToString("dd.MM.yyyy");

    private static string StatusTr(string s) => s switch
    {
        "draft" => "Taslak", "pending" => "Beklemede", "approved" => "Onaylı",
        "rejected" => "Reddedildi", "cancelled" => "İptal", _ => s
    };

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
