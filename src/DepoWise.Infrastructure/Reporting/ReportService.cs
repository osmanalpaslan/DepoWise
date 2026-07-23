using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

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
        cmd.AddWithValue("$c", companyId);
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
        // KM = Σ(current-prev) [geçerli sayaç çiftlerinde]; Ort. Tüketim (L/km) = toplam litre / toplam km.
        cmd.CommandText = @"
SELECT v.internal_code, COUNT(fd.id),
       COALESCE(SUM(CASE WHEN fd.prev_meter IS NOT NULL AND fd.current_meter IS NOT NULL
              THEN CAST(fd.current_meter AS REAL)-CAST(fd.prev_meter AS REAL) ELSE 0 END),0),
       COALESCE(SUM(CAST(fd.liters AS REAL)),0),
       COALESCE(SUM(CAST(fd.liters AS REAL)*CAST(fd.unit_price AS REAL)),0)
FROM fuel_distributions fd JOIN vehicles v ON v.id=fd.vehicle_id
WHERE fd.company_id=$c AND fd.is_deleted=0
" + DateFilter(req, "fd.distribution_date") + @"
GROUP BY fd.vehicle_id ORDER BY v.internal_code;";
        cmd.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        int totIslem = 0; double totKm = 0, totLitre = 0, totTutar = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var islem = r.GetInt32(1); var km = r.GetDouble(2); var litre = r.GetDouble(3); var tutar = r.GetDouble(4);
                var lkm = km > 0 ? litre / km : 0;
                rows.Add(new object?[] { r.GetString(0), islem, km, litre, lkm, tutar });
                totIslem += islem; totKm += km; totLitre += litre; totTutar += tutar;
            }
        if (rows.Count > 0)
            rows.Add(new object?[] { "TOPLAM", totIslem, totKm, totLitre, totKm > 0 ? totLitre / totKm : 0, totTutar });
        return new TableModel("Yakıt Tüketim",
            new[] { "Araç", "İşlem", "KM", "Litre", "Ort. Tüketim (L/km)", "Tutar" }, rows);
    }

    /// <summary>Genel Rapor — araç başına birleşik: KM, Litre, L/km, Malzeme Maliyeti, Yakıt Maliyeti, Toplam.</summary>
    public TableModel General(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT v.internal_code, COALESCE(v.plate,''),
  COALESCE(SUM(CASE WHEN fd.prev_meter IS NOT NULL AND fd.current_meter IS NOT NULL
       THEN CAST(fd.current_meter AS REAL)-CAST(fd.prev_meter AS REAL) ELSE 0 END),0) AS km,
  COALESCE(SUM(CAST(fd.liters AS REAL)),0) AS litre,
  COALESCE(SUM(CAST(fd.liters AS REAL)*CAST(fd.unit_price AS REAL)),0) AS fuelcost,
  (SELECT COALESCE(SUM(CAST(mm.quantity AS REAL)*CAST(COALESCE(mm.unit_price,'0') AS REAL)),0)
   FROM vehicle_maintenances vm JOIN maintenance_materials mm ON mm.maintenance_id=vm.id
   WHERE vm.vehicle_id=v.id AND vm.is_deleted=0 AND vm.is_cancelled=0" + DateFilter(req, "vm.performed_date") + @") AS matcost
FROM vehicles v
LEFT JOIN fuel_distributions fd ON fd.vehicle_id=v.id AND fd.is_deleted=0" + DateFilter(req, "fd.distribution_date") + @"
WHERE v.company_id=$c AND v.is_deleted=0
GROUP BY v.id ORDER BY v.internal_code;";
        cmd.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        double tKm = 0, tLitre = 0, tFuel = 0, tMat = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var km = r.GetDouble(2); var litre = r.GetDouble(3); var fuel = r.GetDouble(4); var mat = r.GetDouble(5);
                var lkm = km > 0 ? litre / km : 0;
                rows.Add(new object?[] { r.GetString(0), r.GetString(1), km, litre, lkm, mat, fuel, mat + fuel });
                tKm += km; tLitre += litre; tFuel += fuel; tMat += mat;
            }
        if (rows.Count > 0)
            rows.Add(new object?[] { "TOPLAM", "", tKm, tLitre, tKm > 0 ? tLitre / tKm : 0, tMat, tFuel, tMat + tFuel });
        return new TableModel("Genel Rapor",
            new[] { "Araç", "Plaka", "KM", "Litre", "L/km", "Malzeme Maliyeti", "Yakıt Maliyeti", "Toplam" }, rows);
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
        cmd.AddWithValue("$c", companyId);
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
        cmd.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { D(r.GetInt64(0)), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetString(4) });
        return new TableModel("Depo Girişi Raporu", new[] { "Tarih", "Litre", "Birim Fiyat", "Tutar", "Fatura No" }, rows);
    }

    /// <summary>Stok Sayım Raporu — her sayım satırı: sistem/sayılan/fark (fark 0 olanlar dahil).</summary>
    public TableModel StockCount(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT d.doc_date, m.code, m.name,
       CAST(scl.system_qty AS REAL), CAST(scl.counted_qty AS REAL), CAST(scl.diff_qty AS REAL),
       COALESCE(scl.reason,'')
FROM stock_count_lines scl
JOIN stock_documents d ON d.id = scl.document_id
JOIN materials m ON m.id = scl.material_id
WHERE d.company_id=$c AND d.is_deleted=0 AND d.doc_type='count'
" + DateFilter(req, "d.doc_date") + @"
ORDER BY d.doc_date DESC, m.code;";
        cmd.AddWithValue("$c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var diff = r.GetDouble(5);
            rows.Add(new object?[] { D(r.GetInt64(0)), r.GetString(1), r.GetString(2),
                r.GetDouble(3), r.GetDouble(4), diff, diff == 0 ? "Fark yok" : (diff > 0 ? "Fazla" : "Eksik"), r.GetString(6) });
        }
        return new TableModel("Stok Sayım Raporu",
            new[] { "Tarih", "Kod", "Malzeme", "Sistem", "Sayılan", "Fark", "Durum", "Gerekçe" }, rows);
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
        cmd.AddWithValue("$c", companyId);
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

    private static void BindDates(DbCommand cmd, ReportRequest req)
    {
        if (req.FromDate is not null) cmd.AddWithValue("$from", req.FromDate.Value);
        if (req.ToDate is not null) cmd.AddWithValue("$to", req.ToDate.Value);
    }
}
