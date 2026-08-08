using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System;
using System.Data.Common;
using System.Globalization;
using System.Linq;

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
        // Malzeme FİRMA-GENELİ katalog (ortak liste) → stok durumu firma-geneli listelenir. Şube-bazlı stok
        // ayrımı geldiğinde bu rapor şube stoğuna göre revize edilecek (kullanıcı kararı 2026-07-26).
        cmd.CommandText = @"
SELECT m.code, m.name, COALESCE(b.quantity,'0') AS qty, m.min_stock
FROM materials m LEFT JOIN stock_balances b ON b.material_id=m.id
WHERE m.company_id=@c AND m.is_deleted=0
ORDER BY m.code;";
        cmd.AddWithValue("@c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3) });
        return new TableModel("Stok Durumu", new[] { "Kod", "Malzeme", "Stok", "Min Stok" }, rows);
    }

    // ===== ŞABLONLU / ŞABLON-DIŞI YÖNETİCİ RAPORLARI (2026-07-24) =====
    // Şablon SEÇİLEREK oluşturulan kayıtlar "şablonlu" (kanonik); şablonsuz oluşturulanlar "şablon-dışı"
    // (serbest/hatalı — yönetici inceler, ilgili ekrandan düzeltir). Hepsi tenant-izole (yalnız kendi firması).

    /// <summary>Malzeme — ŞABLONLU (genel): şablona göre gruplu; her şablon TEK satır + firma-geneli TOPLAM stok.</summary>
    public TableModel MaterialsByTemplate(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COALESCE(t.code,''), t.name, CAST(COUNT(m.id) AS INTEGER),
       COALESCE(SUM(CAST(COALESCE(b.quantity,'0') AS REAL)),0)
FROM material_templates t
JOIN materials m ON m.template_id=t.id AND m.is_deleted=0
LEFT JOIN stock_balances b ON b.material_id=m.id
WHERE t.company_id=@c
GROUP BY t.id ORDER BY t.name;";   // t.id = PK → t.code/t.name bare-kolonu PG'de de geçerli (fonksiyonel bağımlılık)
        cmd.AddWithValue("@c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        int totCnt = 0; double totStock = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var cnt = r.GetInt32(2); var st = r.GetDouble(3);
                rows.Add(new object?[] { r.GetString(0), r.GetString(1), cnt, st });
                totCnt += cnt; totStock += st;
            }
        if (rows.Count > 0) rows.Add(new object?[] { "TOPLAM", "", totCnt, totStock });
        return new TableModel("Malzeme — Şablonlu (Genel)",
            new[] { "Şablon Kodu", "Şablon", "Kayıt Sayısı", "Toplam Stok" }, rows);
    }

    /// <summary>Malzeme — ŞABLON-DIŞI: şablona bağlı OLMAYAN malzemeler (serbest/hatalı; yönetici düzeltir).</summary>
    public TableModel MaterialsNonTemplate(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.code, m.name, COALESCE(mc.name,''), COALESCE(b.quantity,'0'), m.min_stock
FROM materials m
LEFT JOIN material_categories mc ON mc.id=m.category_id
LEFT JOIN stock_balances b ON b.material_id=m.id
WHERE m.company_id=@c AND m.is_deleted=0 AND m.template_id IS NULL
ORDER BY m.code;";
        cmd.AddWithValue("@c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(new object?[] { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4) });
        return new TableModel("Malzeme — Şablon Dışı (İncele/Düzelt)",
            new[] { "Kod", "Malzeme", "Kategori", "Stok", "Min Stok" }, rows);
    }

    /// <summary>Araç — ŞABLONLU (genel): şablona bağlı araçların şube bazlı listesi.</summary>
    public TableModel VehiclesByTemplate(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.name, v.internal_code, COALESCE(v.plate,''), COALESCE(br.name,''), v.status
FROM vehicles v
JOIN vehicle_templates t ON t.id=v.template_id
LEFT JOIN branches br ON br.id=v.branch_id
WHERE v.company_id=@c AND v.is_deleted=0
ORDER BY t.name, v.internal_code;";
        cmd.AddWithValue("@c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(new object?[] { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), VehStatusTr(r.GetString(4)) });
        return new TableModel("Araç — Şablonlu (Genel)",
            new[] { "Şablon", "İç Kod", "Plaka", "Şube", "Durum" }, rows);
    }

    /// <summary>Araç — ŞABLON-DIŞI: şablona bağlı OLMAYAN araçlar (serbest/hatalı; yönetici düzeltir).</summary>
    public TableModel VehiclesNonTemplate(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT v.internal_code, COALESCE(v.plate,''), COALESCE(br.name,''), v.status
FROM vehicles v LEFT JOIN branches br ON br.id=v.branch_id
WHERE v.company_id=@c AND v.is_deleted=0 AND v.template_id IS NULL
ORDER BY v.internal_code;";
        cmd.AddWithValue("@c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                rows.Add(new object?[] { r.GetString(0), r.GetString(1), r.GetString(2), VehStatusTr(r.GetString(3)) });
        return new TableModel("Araç — Şablon Dışı (İncele/Düzelt)",
            new[] { "İç Kod", "Plaka", "Şube", "Durum" }, rows);
    }

    private static string VehStatusTr(string s) => s switch
    {
        "active" => "Aktif", "passive" => "Pasif", "maintenance" => "Bakımda", "faulty" => "Arızalı", _ => s
    };

    /// <summary>
    /// Durum Rapor (2026-07-25) — YÖNETİCİ: şube bazlı SAYISAL özet. Şablon mantığı olan modüller (Malzeme/Araç)
    /// şablonlu/şablon-dışı ayrımıyla; diğerleri yalnız toplam kayıt sayısıyla ("—" şablon sütunları). Malzeme
    /// firma-genelidir (şube yok) → tek "Firma Geneli" satırı. Tarih filtresi kayıt/işlem tarihine göre daraltır.
    /// Tenant-izole (yalnız kendi firması). Tüm sayımlar PG-güvenli: CAST(... AS INTEGER) → GetInt32.
    /// </summary>
    public TableModel StatusReport(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        var rows = new List<IReadOnlyList<object?>>();

        // ── 1) Malzeme — FİRMA GENELİ (malzemede şube yok): şablonlu / şablon-dışı ──
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT CAST(COALESCE(SUM(CASE WHEN template_id IS NOT NULL THEN 1 ELSE 0 END),0) AS INTEGER),
       CAST(COALESCE(SUM(CASE WHEN template_id IS NULL THEN 1 ELSE 0 END),0) AS INTEGER)
FROM materials WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "created_at") + ";";
            cmd.AddWithValue("@c", companyId);
            BindDates(cmd, req);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                int tpl = r.GetInt32(0), non = r.GetInt32(1);
                rows.Add(new object?[] { "Firma Geneli", "Malzeme", tpl, non, tpl + non });
            }
        }

        // ── Şube listesi (ada göre sıralı) — per-branch modüller bu sırada dökülür ──
        var branchNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var branchOrder = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM branches WHERE company_id=@c AND is_deleted=0 ORDER BY name;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) { var id = r.GetString(0); branchNames[id] = r.GetString(1); branchOrder.Add(id); }
        }

        // Şube bazlı sayımlar. Araç şablon-ayrımlı (branch_id); diğerleri toplam.
        // Şube kaynağı: entity → branch_id; işlem (bakım/yakıt/günlük) → op_branch_id (işlenen şube, Migration027).
        var vehTpl = CountTplByBranch(conn, companyId, req);
        var personnel = CountByBranch(conn, "SELECT branch_id, CAST(COUNT(*) AS INTEGER) FROM personnel WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "created_at") + " GROUP BY branch_id;", companyId, req);
        var maintenance = CountByBranch(conn, "SELECT op_branch_id, CAST(COUNT(*) AS INTEGER) FROM vehicle_maintenances WHERE company_id=@c AND is_deleted=0 AND is_cancelled=0" + DateFilter(req, "performed_date") + " GROUP BY op_branch_id;", companyId, req);
        var fuel = CountByBranch(conn, "SELECT op_branch_id, CAST(COUNT(*) AS INTEGER) FROM fuel_distributions WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "distribution_date") + " GROUP BY op_branch_id;", companyId, req);
        var requests = CountByBranch(conn, "SELECT branch_id, CAST(COUNT(*) AS INTEGER) FROM material_requests WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "request_date") + " GROUP BY branch_id;", companyId, req);
        var daily = CountByBranch(conn, "SELECT op_branch_id, CAST(COUNT(*) AS INTEGER) FROM daily_activities WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "activity_date") + " GROUP BY op_branch_id;", companyId, req);

        var branchKeys = new List<string>(branchOrder);
        bool anyUnassigned = vehTpl.ContainsKey("") || personnel.ContainsKey("") || maintenance.ContainsKey("")
            || fuel.ContainsKey("") || requests.ContainsKey("") || daily.ContainsKey("");
        if (anyUnassigned) branchKeys.Add("");   // op_branch_id/branch_id NULL (eski kayıt) → "Şube atanmamış"

        foreach (var key in branchKeys)
        {
            var name = key.Length == 0 ? "Şube atanmamış" : (branchNames.TryGetValue(key, out var n) ? n : key);
            var vt = vehTpl.TryGetValue(key, out var v) ? v : (Tpl: 0, Non: 0);
            rows.Add(new object?[] { name, "Araç", vt.Tpl, vt.Non, vt.Tpl + vt.Non });
            rows.Add(new object?[] { name, "Personel", "—", "—", personnel.TryGetValue(key, out var p) ? p : 0 });
            rows.Add(new object?[] { name, "Bakım", "—", "—", maintenance.TryGetValue(key, out var m) ? m : 0 });
            rows.Add(new object?[] { name, "Yakıt Dağıtım", "—", "—", fuel.TryGetValue(key, out var f) ? f : 0 });
            rows.Add(new object?[] { name, "Talep", "—", "—", requests.TryGetValue(key, out var rq) ? rq : 0 });
            rows.Add(new object?[] { name, "Günlük Faaliyet", "—", "—", daily.TryGetValue(key, out var da) ? da : 0 });
        }

        return new TableModel("Durum Rapor",
            new[] { "Kapsam", "Modül", "Şablonlu", "Şablon Dışı", "Toplam" }, rows);
    }

    /// <summary>Şube (branch_id/op_branch_id) → kayıt sayısı. NULL şube → "" (Şube atanmamış). PG-güvenli GetInt32.</summary>
    private static Dictionary<string, int> CountByBranch(DbConnection conn, string sql, string companyId, ReportRequest req)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            d[r.IsDBNull(0) ? "" : r.GetString(0)] = r.GetInt32(1);
        return d;
    }

    /// <summary>Araç şube bazlı şablonlu/şablon-dışı sayımı (branch_id). NULL şube → "".</summary>
    private static Dictionary<string, (int Tpl, int Non)> CountTplByBranch(DbConnection conn, string companyId, ReportRequest req)
    {
        var d = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT branch_id,
       CAST(COALESCE(SUM(CASE WHEN template_id IS NOT NULL THEN 1 ELSE 0 END),0) AS INTEGER),
       CAST(COALESCE(SUM(CASE WHEN template_id IS NULL THEN 1 ELSE 0 END),0) AS INTEGER)
FROM vehicles WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "created_at") + @"
GROUP BY branch_id;";
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            d[r.IsDBNull(0) ? "" : r.GetString(0)] = (r.GetInt32(1), r.GetInt32(2));
        return d;
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
SELECT v.internal_code, CAST(COUNT(fd.id) AS INTEGER),
       COALESCE(SUM(CASE WHEN fd.prev_meter IS NOT NULL AND fd.current_meter IS NOT NULL
              THEN CAST(fd.current_meter AS REAL)-CAST(fd.prev_meter AS REAL) ELSE 0 END),0),
       COALESCE(SUM(CAST(fd.liters AS REAL)),0),
       COALESCE(SUM(CAST(fd.liters AS REAL)*CAST(fd.unit_price AS REAL)),0)
FROM fuel_distributions fd JOIN vehicles v ON v.id=fd.vehicle_id
WHERE fd.company_id=@c AND fd.is_deleted=0" + ReportScope.BranchSql(s, req, "fd.op_branch_id") + @"
" + DateFilter(req, "fd.distribution_date") + @"
GROUP BY v.id ORDER BY v.internal_code;";  // PK'ye grupla: PostgreSQL bare-kolon (v.internal_code) için PK bağımlılığı ister (fd.vehicle_id=v.id, sonuç aynı)
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req); ReportScope.BindBranch(cmd, s, req);
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

    /// <summary>
    /// ARAÇ RAPORU (kullanıcı isteği 2026-08-07) — "Genel Rapor"un YERİNE. Araç başına TEK satır: yakıt +
    /// bakım malzemesi + DOĞRUDAN parça (bakım-dışı stok çıkışı) maliyeti, sayaç birimine (km/saat) duyarlı
    /// birim maliyet ve ortalamalar. Karar destek raporu.
    ///
    /// PERFORMANS — N+1 YOK: her maliyet kaynağı araç bazında ÖNCEDEN toplanmış bir türetilmiş tabloda (derived
    /// table) hesaplanır ve araca 1:1 LEFT JOIN edilir → tek geçiş, satır çarpımı (fan-out) yok, dış GROUP BY yok.
    /// Yakıtı/bakımı/parçası olmayan araç 0 ile görünür (tam filo görünürlüğü). Tarih filtresi MALİYETLERE
    /// (kaynak tarih kolonu) uygulanır; araçlar her hâlde listelenir.
    ///
    /// GENİŞLETİLEBİLİRLİK (rule 10): yeni bir maliyet kalemi (sigorta/kasko/lastik/amortisman…) eklemek =
    /// 1 türetilmiş-tablo LEFT JOIN + 1 kolon + Toplam'a ekleme. Çekirdek yapı değişmez.
    ///
    /// meter_unit (rule 2): "Sayaç Birimi" kolonu araç kartından gelir; mesafe/tüketim/birim-maliyet aracın
    /// KENDİ biriminde (km ya da saat) hesaplanır (sayaç farkı zaten o birimdedir) → saat makinelerinde hiçbir
    /// hesap km'ye zorlanmaz; yalnız etiket (₺/km ↔ ₺/saat) birime göre yorumlanır.
    /// </summary>
    public TableModel VehicleReport(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var vehIn = InList("v.id", "@rv", req.VehicleIds);
        var typeIn = InList("v.vehicle_type_id", "@rt", req.VehicleTypeIds);
        // Mesafe hesabı: yakıt fişleri arasındaki sayaç farkı (rule 3) — dönem başı/sonu farkı DEĞİL.
        cmd.CommandText = @"
SELECT v.internal_code, COALESCE(v.plate,''),
       TRIM(COALESCE(br.name,'') || ' ' || COALESCE(vmd.name,'')) AS veh_name,
       COALESCE(bch.name,'') AS branch_name, v.meter_unit,
       COALESCE(f.km,0), COALESCE(f.litre,0), COALESCE(f.fuelcost,0),
       COALESCE(mt.matcost,0), COALESCE(di.partcost,0)
FROM vehicles v
LEFT JOIN brands br ON br.id=v.brand_id
LEFT JOIN vehicle_models vmd ON vmd.id=v.vehicle_model_id
LEFT JOIN branches bch ON bch.id=v.branch_id
LEFT JOIN (
    SELECT vehicle_id,
      SUM(CASE WHEN prev_meter IS NOT NULL AND current_meter IS NOT NULL
           THEN CAST(current_meter AS REAL)-CAST(prev_meter AS REAL) ELSE 0 END) AS km,
      SUM(CAST(liters AS REAL)) AS litre,
      SUM(CAST(liters AS REAL)*CAST(unit_price AS REAL)) AS fuelcost
    FROM fuel_distributions
    WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "distribution_date") + @"
    GROUP BY vehicle_id
) f ON f.vehicle_id=v.id
LEFT JOIN (
    SELECT vm.vehicle_id,
      SUM(CAST(mm.quantity AS REAL)*CAST(COALESCE(mm.unit_price,'0') AS REAL)) AS matcost
    FROM vehicle_maintenances vm JOIN maintenance_materials mm ON mm.maintenance_id=vm.id
    WHERE vm.company_id=@c AND vm.is_deleted=0 AND vm.is_cancelled=0" + DateFilter(req, "vm.performed_date") + @"
    GROUP BY vm.vehicle_id
) mt ON mt.vehicle_id=v.id
LEFT JOIN (
    SELECT sd.vehicle_id,
      SUM(CAST(sm.quantity AS REAL)*CAST(COALESCE(sm.unit_price,'0') AS REAL)) AS partcost
    FROM stock_documents sd JOIN stock_movements sm ON sm.document_id=sd.id
    WHERE sd.company_id=@c AND sd.is_deleted=0 AND sd.doc_type='out' AND sd.status='active'
          AND sd.vehicle_id IS NOT NULL" + DateFilter(req, "sd.doc_date") + @"
    GROUP BY sd.vehicle_id
) di ON di.vehicle_id=v.id
WHERE v.company_id=@c AND v.is_deleted=0" + ReportScope.BranchSql(s, req, "v.branch_id") + vehIn + typeIn + @"
ORDER BY COALESCE(bch.name,''), veh_name, v.internal_code;";   // varsayılan sıralama: Şube -> Araç Adı (rule 3)
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        ReportScope.BindBranch(cmd, s, req);
        BindList(cmd, "@rv", req.VehicleIds);
        BindList(cmd, "@rt", req.VehicleTypeIds);

        var rows = new List<IReadOnlyList<object?>>();
        double tLitre = 0, tFuel = 0, tMat = 0, tPart = 0, tTotal = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var meterUnit = r.GetString(4);
                var unitTr = meterUnit == "hour" ? "Saat" : "KM";
                double km = r.GetDouble(5), litre = r.GetDouble(6), fuel = r.GetDouble(7),
                       mat = r.GetDouble(8), part = r.GetDouble(9);
                double avgPrice = litre > 0 ? fuel / litre : 0;       // ağırlıklı ort. ₺/L
                double consumption = km > 0 ? litre / km : 0;         // L/birim (km ya da saat)
                double total = fuel + mat + part;                     // yakıt + bakım malzeme + doğrudan parça
                double perUnit = km > 0 ? total / km : 0;             // ₺/birim (km ya da saat)
                // NumCell: HAM değer (sıralama/filtre/karşılaştırma/aralık) + GÖRÜNTÜ (biçimli). Boş → görüntüde
                // "-", değer 0 (kullanıcı isteği: değer korunur, yalnız görünüm değişir). Birim (km/saat) araca özel.
                rows.Add(new object?[]
                {
                    r.GetString(0), r.GetString(1), r.GetString(2).Trim(), r.GetString(3), unitTr,
                    Num(km, x => FmtDistance(x, meterUnit)),
                    Num(litre, FmtLiter),
                    Num(avgPrice, FmtMoney),
                    Num(fuel, FmtMoney),
                    Num(consumption, x => FmtConsumption(x, meterUnit)),
                    Num(mat, FmtMoney),
                    Num(part, FmtMoney),
                    Num(total, FmtMoney),
                    Num(perUnit, x => FmtPerUnit(x, meterUnit)),
                });
                tLitre += litre; tFuel += fuel; tMat += mat; tPart += part; tTotal += total;
            }

        // Pinned toplam satırı (rule 9): yalnız birim-bağımsız para/litre toplamları; ortalamalar ve km↔saat
        // karışık kolonlar (mesafe/tüketim/birim-maliyet) boş bırakılır (toplanmaz).
        IReadOnlyList<object?>? totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM", "", "", "", "",
            "", Num(tLitre, FmtLiter), "", Num(tFuel, FmtMoney), "", Num(tMat, FmtMoney), Num(tPart, FmtMoney), Num(tTotal, FmtMoney), "",
        };

        // Kolon-tipi bayrakları: ilk 5 metin (İç Kod/Plaka/Ad/Şube/Sayaç Birimi), kalan 9 sayısal.
        var numeric = new[] { false, false, false, false, false, true, true, true, true, true, true, true, true, true };

        return new TableModel("Araç Raporu", new[]
        {
            "İç Kod", "Plaka", "Araç Adı", "Şube", "Sayaç Birimi", "Dönem Sayaç Mesafesi",
            "Toplam Yakıt (Litre)", "Ortalama Yakıt Fiyatı", "Yakıt Maliyeti", "Ortalama Yakıt Tüketimi",
            "Bakım Malzeme Tutarı", "Doğrudan Parça Tutarı", "Toplam Araç Maliyeti", "Birim Başına Maliyet",
        }, rows, numeric, totalRow);
    }

    // ── Araç Raporu görüntü biçimleri (kullanıcı isteği 2026-08-08). Yalnız GÖRÜNTÜ; HAM değer NumCell.Value'da. ──
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static NumCell Num(double v, Func<double, string> fmt) => v == 0 ? new NumCell(0, "-") : new NumCell(v, fmt(v));
    private static string FmtMoney(double v) => "₺ " + v.ToString("#,##0.00", Tr);
    private static string FmtLiter(double v) => v.ToString("#,##0.00", Tr) + " L";
    private static string FmtDistance(double v, string unit) => v.ToString("#,##0.##", Tr) + (unit == "hour" ? " Saat" : " km");
    private static string FmtConsumption(double v, string unit) => v.ToString("#,##0.00", Tr) + (unit == "hour" ? " L/Saat" : " L/km");
    private static string FmtPerUnit(double v, string unit) => "₺ " + v.ToString("#,##0.00", Tr) + (unit == "hour" ? "/Saat" : "/km");

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
WHERE vm.company_id=@c AND vm.is_deleted=0 AND vm.is_cancelled=0" + ReportScope.BranchSql(s, req, "vm.op_branch_id") + @"
" + DateFilter(req, "vm.performed_date") + @"
ORDER BY vm.performed_date DESC;";
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req); ReportScope.BindBranch(cmd, s, req);
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
WHERE company_id=@c AND is_deleted=0" + ReportScope.BranchSql(s, req, "op_branch_id") + @"
" + DateFilter(req, "entry_date") + @"
ORDER BY entry_date DESC;";
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req); ReportScope.BindBranch(cmd, s, req);
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
WHERE d.company_id=@c AND d.is_deleted=0 AND d.doc_type='count'
" + DateFilter(req, "d.doc_date") + @"
ORDER BY d.doc_date DESC, m.code;";
        cmd.AddWithValue("@c", companyId);
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
       (SELECT CAST(COUNT(*) AS INTEGER) FROM material_request_items i WHERE i.request_id = mr.id)
FROM material_requests mr
WHERE mr.company_id=@c AND mr.is_deleted=0" + ReportScope.BranchSql(s, req, "mr.branch_id") + @"
" + DateFilter(req, "mr.request_date") + @"
ORDER BY mr.request_date DESC;";
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req); ReportScope.BindBranch(cmd, s, req);
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
        if (req.FromDate is not null) sb += $" AND {col} >= @from";
        if (req.ToDate is not null) sb += $" AND {col} <= @to";
        return sb;
    }

    private static void BindDates(DbCommand cmd, ReportRequest req)
    {
        if (req.FromDate is not null) cmd.AddWithValue("@from", req.FromDate.Value);
        if (req.ToDate is not null) cmd.AddWithValue("@to", req.ToDate.Value);
    }


    /// <summary>Çoklu-seçim IN parçası: boş/null → ""; aksi halde "AND col IN (@px0,@px1,...)".</summary>
    private static string InList(string col, string prefix, IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count == 0) return "";
        var ps = string.Join(",", System.Linq.Enumerable.Range(0, ids.Count).Select(i => prefix + i));
        return $" AND {col} IN ({ps})";
    }

    private static void BindList(DbCommand cmd, string prefix, IReadOnlyList<string>? ids)
    {
        if (ids is null) return;
        for (int i = 0; i < ids.Count; i++) cmd.AddWithValue(prefix + i, ids[i]);
    }

    // ═══════════════ ORTAK YÜRÜTME (kullanıcı isteği 2026-08-07 — ortak rapor mimarisi) ═══════════════
    // TEK giriş noktası: katalog anahtarıyla dispatch + tarih varsayılanı (RequiresDate → Bu Ay) + maksimum
    // kayıt koruması. Hem masaüstü hem API buradan çağırır → aynı davranış/hesaplama (madde 9). Hesaplama
    // metotları (StockStatus/General/...) DEĞİŞMEDİ — yalnız ortak sarmalayıcı eklendi.

    /// <summary>Katalog anahtarına göre raporu çalıştırır. RequiresDate ise tarih yoksa Bu Ay'a düşürür
    /// (milyonlarca kayıt taraması engellenir). Sonuç <paramref name="maxRows"/> ile üstten sınırlanır.</summary>
    public TableModel Run(SessionContext s, string key, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
    {
        var desc = ReportCatalog.ByKey(key) ?? throw new ArgumentException("Bilinmeyen rapor tipi: " + key);

        // Tarih varsayılanı (sunucu-taraflı zorlama — istemci göndermese bile korur).
        if (desc.RequiresDate && (req.FromDate is null || req.ToDate is null))
        {
            var (from, to) = ReportCatalog.CurrentMonthRange();
            req = req with { FromDate = req.FromDate ?? from, ToDate = req.ToDate ?? to };
        }

        var table = Dispatch(s, key, req);

        // Maksimum kayıt koruması: patholojik sonuçta üstten kes (normal raporlar sınırın çok altında).
        if (maxRows > 0 && table.Rows.Count > maxRows)
            table = table with { Rows = table.Rows.Take(maxRows).ToList() };
        return table;
    }

    /// <summary>Katalog anahtarı → hesaplama metodu (tek switch — hem masaüstü hem API aynı eşleme).</summary>
    private TableModel Dispatch(SessionContext s, string key, ReportRequest req) => key switch
    {
        "stock" => StockStatus(s, req),
        "vehicle" => VehicleReport(s, req),
        "maintenance" => Maintenance(s, req),
        "fuel" => FuelConsumption(s, req),
        "fuel-depot" => FuelDepot(s, req),
        "stock-count" => StockCount(s, req),
        "requests" => Requests(s, req),
        "materials-template" => MaterialsByTemplate(s, req),
        "materials-nontemplate" => MaterialsNonTemplate(s, req),
        "vehicles-template" => VehiclesByTemplate(s, req),
        "vehicles-nontemplate" => VehiclesNonTemplate(s, req),
        "status" => StatusReport(s, req),
        _ => throw new ArgumentException("Bilinmeyen rapor tipi: " + key),
    };
}
