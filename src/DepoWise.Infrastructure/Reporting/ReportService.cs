using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;   // STK-B1: MovementTypeOptions — hareket türü etiketi TEK kaynak
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;   // STK-10b-4: StockMovementFilterSql — ekran+rapor ORTAK filtre üreteci
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
    /// <summary>G4-4: "vadesi geçti" hesabı için ŞİMDİ. Test edilebilirlik için enjekte edilir.</summary>
    private readonly IClock _clock;

    public ReportService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>⭐ ARA İŞ 4 (ADR-186 / PK-CR-03=A) — CUSTOM RAPOR BAĞLAYICISI.
    ///
    /// İkinci bir rapor motoru KURULMAZ: custom raporlar da bu servisin <see cref="Run"/> metodundan,
    /// AYNI dört güvenlik kapısından geçerek çalışır. Bağlayıcı verilmezse custom rapor anahtarları
    /// eskisi gibi "bilinmeyen rapor" sayılır → mevcut davranış birebir korunur (geriye uyumluluk).</summary>
    public CustomReportService? Custom { get; set; }

    public TableModel StockStatus(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();

        // STK-06 — İKİ MOD (kullanıcı kararı: eski davranış korunur):
        //  • Depo seçilmemişse → FİRMA GENELİ toplam, malzeme başına TEK satır. Sorgu ve kolonlar
        //    STK-02'deki hâliyle BİREBİR aynıdır → mevcut rapor davranışı hiç değişmez (regresyon yok).
        //  • Depo seçilmişse → yalnız o depo(lar)daki kalemler + "Depo" kolonu (kırılım).
        // Her iki modda da TEK sorgu vardır; malzeme × depo döngüsü (N+1) kurulmaz ve satır çoğaltan
        // JOIN yapılmaz. DISTINCT ile gizleme YOK — mod ayrımı sorgunun kendisinde.
        // 🔴 DEN-E2 (2026-08-18) DÜZELTMESİ — ŞUBE KAPSAMI BU RAPORDA HİÇ UYGULANMIYORDU.
        // Eskiden `req.LocationIds` AYNEN kullanılıyordu: (a) filtre boşken FİRMA GENELİ toplam
        // dönüyor, şubeyle sınırlı kullanıcı tüm firmanın stoğunu görüyordu; (b) istek gövdesine
        // BAŞKA şubenin depo kimliği yazılırsa o depo okunuyordu (parametre manipülasyonu, fail-open).
        // Kardeş rapor StockMovements bunu ReportScope ile zaten doğru yapıyordu.
        // Tek otorite BranchAccess'tir; ikinci bir kapsam mantığı KURULMADI.
        // ⚠️ BURADA BİLEREK `Allowed` KULLANILIR, `Effective` DEĞİL — ve bu bir eksik değildir.
        // (Denetim 2026-08-26'da `Effective`e çevrilmesi DENENDİ ve GERİ ALINDI; gerekçe:)
        //
        // Bu raporun filtre boyutu ŞUBE değil, STOĞUN FİZİKSEL YERİDİR (`stock_balances.location_id`).
        // `Effective`, oturumun ÇALIŞMA şubesini de uygular — yani "Depo A ile giriş yapan" birinin
        // Depo B'nin stoğunu SORGULAMASINI engellerdi. Oysa ürün bunu bilinçli olarak destekler:
        // kullanıcı Depo A'da çalışırken Depo B'den malzeme çekebilir (STK-04/05/06 + bakım stok
        // lokasyonu). İki kavramın karıştırılmaması katalogda da ayrıca uyarılır (ReportFilters.Location).
        // MaintenanceStockLocationTests.Stok_Durumu_Raporu_Bakim_Tuketimini_Secilen_Depoda_Gosterir
        // bu kararı kilitler.
        //
        // `Allowed` yine de GERÇEK bir güvenlik kapısıdır: kullanıcı YETKİSİ OLMAYAN bir depoyu
        // isteyemez (DEN-E2, fail-closed). Kapsam yetkiyle sınırlıdır; görünüm tercihiyle DEĞİL.
        var izinli = BranchAccess.Allowed(s);                 // null = sınırsız (admin / tüm şubeler)
        var locations = NormalizeLocations(req.LocationIds);

        if (izinli is not null)
        {
            var izinliSet = new HashSet<string>(izinli, StringComparer.Ordinal);
            // ATANMAMIŞ ("") kovası, şubesiz kayıtlarla aynı ilkeyle GİZLENMEZ (BranchAccess ile tutarlı).
            var suzulen = locations.Where(x => x.Length == 0 || izinliSet.Contains(x)).ToList();
            // FAIL-CLOSED: yalnız kapsam dışı depo istendiyse boş sonuç döner — filtre sessizce KALKMAZ.
            if (locations.Count > 0 && suzulen.Count == 0)
                return new TableModel("Stok Durumu — Depo Bazlı",
                    new[] { "Kod", "Malzeme", "Depo / Şantiye", "Stok", "Min Stok" },
                    Array.Empty<IReadOnlyList<object?>>());
            locations = suzulen;
        }

        return locations.Count == 0
            ? StockStatusCompanyTotal(conn, companyId, izinli)
            : StockStatusByLocation(conn, companyId, locations);
    }

    /// <summary>STK-06 — lokasyon filtresini normalleştirir. Boş liste = "Tüm Şubeler" (filtre yok).
    /// Listedeki boş metin ("") = ATANMAMIŞ kovası ve GEÇERLİ bir seçimdir (gerçek depo değildir ama
    /// gösterilebilir/filtrelenebilir olmalıdır — KARAR-8 çözülene kadar veri orada duruyor).</summary>
    private static IReadOnlyList<string> NormalizeLocations(IReadOnlyList<string>? ids)
        => ids is null ? Array.Empty<string>() : ids.Where(x => x is not null).Distinct().ToList();

    /// <param name="scope">DEN-E2 — kullanıcının izinli şubeleri. <c>null</c> = sınırsız (eski davranış:
    /// firma geneli toplam). Doluysa toplam YALNIZ o depolar (+ ATANMAMIŞ) üzerinden hesaplanır;
    /// böylece "filtre seçilmedi" durumu artık tüm firmayı açmaz.</param>
    private static TableModel StockStatusCompanyTotal(DbConnection conn, string companyId, IReadOnlyList<string>? scope)
    {
        using var cmd = conn.CreateCommand();
        var locWhere = "";
        if (scope is not null)
        {
            var names = new List<string>(scope.Count);
            for (int i = 0; i < scope.Count; i++) { var p = "@sc" + i; names.Add(p); cmd.AddWithValue(p, scope[i]); }
            // Boş küme → yalnız ATANMAMIŞ görünür (fail-closed; filtre KALKMAZ).
            locWhere = names.Count > 0
                ? $" AND (location_id IN ({string.Join(",", names)}) OR location_id='')"
                : " AND location_id=''";
        }
        // STK-02: bakiye (malzeme + lokasyon) anahtarlı → düz JOIN her malzemeyi depo sayısı kadar
        // TEKRARLARDI. Firma geneli modda lokasyonlar toplanarak tek satıra indirilir.
        cmd.CommandText = @"
SELECT m.code, m.name, COALESCE(b.quantity,'0') AS qty, m.min_stock
FROM materials m LEFT JOIN " + SqlDialect.StockTotalSubquery(conn, locWhere) + @" b
     ON b.material_id=m.id AND b.company_id=m.company_id
WHERE m.company_id=@c AND m.is_deleted=0
ORDER BY m.code;";
        cmd.AddWithValue("@c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new object?[] { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3) });
        return new TableModel("Stok Durumu", new[] { "Kod", "Malzeme", "Stok", "Min Stok" }, rows);
    }

    /// <summary>
    /// STK-06 — SEÇİLİ DEPO(LAR)IN kırılımı. Satır = (malzeme × o depodaki bakiye satırı).
    /// Bakiye satırı OLMAYAN malzeme listelenmez: "Depo A'da ne var?" sorusunun cevabı, o depoda hiç
    /// bulunmamış 2400 malzemeyi 0 ile listelemek değildir.
    ///
    /// Toplam satırı C#'ta <c>decimal</c> ile hesaplanır (SQL SUM/REAL kullanılmaz — Money kuralı).
    /// Böylece "seçili depoların toplamı" ile firma toplamı arasındaki ilişki kesin kalır.
    /// </summary>
    private static TableModel StockStatusByLocation(DbConnection conn, string companyId, IReadOnlyList<string> locations)
    {
        using var cmd = conn.CreateCommand();
        var names = new List<string>(locations.Count);
        for (int i = 0; i < locations.Count; i++)
        {
            var p = "@loc" + i;
            names.Add(p);
            cmd.AddWithValue(p, locations[i]);
        }
        // Doğrudan stock_balances: satır zaten (malzeme, lokasyon) anahtarlı → ÇOĞALMA YOK.
        // Depo adı AYNI sorguda JOIN ile gelir (satır başına ad sorgusu yasak).
        cmd.CommandText = $@"
SELECT m.code, m.name, COALESCE(br.name, ''), sb.quantity, m.min_stock
FROM stock_balances sb
JOIN materials m ON m.id = sb.material_id AND m.company_id = sb.company_id AND m.is_deleted = 0
LEFT JOIN branches br ON br.id = sb.location_id AND br.company_id = sb.company_id
WHERE sb.company_id=@c AND sb.location_id IN ({string.Join(",", names)})
ORDER BY m.code;";
        cmd.AddWithValue("@c", companyId);

        var rows = new List<IReadOnlyList<object?>>();
        decimal total = 0m;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var qty = r.GetString(3);
                total += Money.Parse(qty);
                // Adı boş olan tek durum ATANMAMIŞ'tır (gerçek şubenin adı vardır) → gerçek depo gibi
                // değil, açıklayıcı etiketle gösterilir.
                var loc = r.GetString(2);
                rows.Add(new object?[] { r.GetString(0), r.GetString(1),
                    loc.Length == 0 ? "Atanmamış (depo girilmemiş)" : loc, qty, r.GetString(4) });
            }

        return new TableModel("Stok Durumu — Depo Bazlı",
            new[] { "Kod", "Malzeme", "Depo / Şantiye", "Stok", "Min Stok" }, rows,
            TotalRow: rows.Count == 0 ? null
                : new object?[] { "TOPLAM", "", "", Money.Serialize(total), "" });
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
        // DEN-D2 (2026-08-18): "Toplam Stok" kolonu HAM double olarak yazılıyordu (biçimlendirici YOK) →
        // kullanıcı 1234,5600000000002 gibi bir değer görebiliyordu. Diğer raporlardaki "Stok" kolonu gibi
        // artık kesin toplama + Money metni kullanılır (PG'de numeric ile tam, SQLite'ta 6 ondalığa yuvarlı).
        cmd.CommandText = @"
SELECT COALESCE(t.code,''), t.name, CAST(COUNT(m.id) AS INTEGER),
       " + SqlDialect.ExactSumText(conn, "COALESCE(b.quantity,'0')") + @"
FROM material_templates t
JOIN materials m ON m.template_id=t.id AND m.is_deleted=0
LEFT JOIN " + SqlDialect.StockTotalSubquery(conn) + @" b ON b.material_id=m.id AND b.company_id=m.company_id
WHERE t.company_id=@c
GROUP BY t.id ORDER BY t.name;";   // t.id = PK → t.code/t.name bare-kolonu PG'de de geçerli (fonksiyonel bağımlılık)
        cmd.AddWithValue("@c", companyId);
        var rows = new List<IReadOnlyList<object?>>();
        int totCnt = 0; decimal totStock = 0m;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var cnt = r.GetInt32(2);
                var stText = r.IsDBNull(3) ? "0" : r.GetString(3);
                rows.Add(new object?[] { r.GetString(0), r.GetString(1), cnt, stText });
                totCnt += cnt; totStock += Money.Parse(stText);
            }
        if (rows.Count > 0) rows.Add(new object?[] { "TOPLAM", "", totCnt, Money.Serialize(totStock) });
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
LEFT JOIN " + SqlDialect.StockTotalSubquery(conn) + @" b ON b.material_id=m.id AND b.company_id=m.company_id
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
-- 🔴 RPR-02 (denetim 2026-08-25): rapor ŞUBE kolonunu GÖSTERİYOR ama YETKİ kapısını UYGULAMIYORDU →
-- tek şubeye yetkili kullanıcı tüm firmanın araçlarını ve PLAKALARINI görüyordu.
-- ⚠️ Burada BİLEREK AllowedSql kullanılır, ReportScope.BranchSql DEĞİL: bu bir YÖNETİCİ raporudur ve
-- (Şube 2 ile giriş yapılsa bile tüm şubeleri gösterir) sözleşmesi korunmalıdır — BranchScopeTests ile
-- kilitli, ürün kararı. Yani YETKİ uygulanır, oturumun görünüm tercihi uygulanmaz.
" + BranchAccess.AllowedSql(s, "v.branch_id") + @"
ORDER BY t.name, v.internal_code;";
        cmd.AddWithValue("@c", companyId);
        BranchAccess.BindAllowed(cmd, s);
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
-- 🔴 RPR-01 (denetim 2026-08-25): şablonlu rapordaki eksiğin aynısı — bkz. VehiclesByTemplate
-- (YÖNETİCİ raporu → yetki kapısı uygulanır, oturumun çalışma şubesi uygulanmaz).
" + BranchAccess.AllowedSql(s, "v.branch_id") + @"
ORDER BY v.internal_code;";
        cmd.AddWithValue("@c", companyId);
        BranchAccess.BindAllowed(cmd, s);
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
            // 🟠 DEN-E1 (2026-08-18) DÜZELTMESİ — bu rapor ŞUBE KAPSAMINI HİÇ UYGULAMIYORDU:
            // yalnız "Şube A"ya yetkili kullanıcı firmadaki BÜTÜN şubelerin adlarını ve
            // araç/personel/bakım/yakıt/talep/faaliyet kayıt SAYILARINI görüyordu.
            // Satırlar aşağıda YALNIZ bu listeden üretildiği için, kapsamı burada süzmek yeterlidir:
            // kapsam dışı şubenin adı da sayısı da çıktıya HİÇ girmez.
            var izinli = BranchAccess.Allowed(s);   // null = sınırsız (admin / tüm şubeler)
            var izinliSet = izinli is null ? null : new HashSet<string>(izinli, StringComparer.Ordinal);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                if (izinliSet is not null && !izinliSet.Contains(id)) continue;
                branchNames[id] = r.GetString(1);
                branchOrder.Add(id);
            }
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

    /// <summary>
    /// YAKIT TÜKETİM RAPORU (kullanıcı isteği 2026-08-08) — Araç Raporu standardına taşındı. Araç başına TEK satır:
    /// işlem sayısı, dönem sayaç mesafesi, litre, ortalama tüketim, AĞIRLIKLI ortalama yakıt fiyatı, toplam yakıt
    /// maliyeti ve birim başına maliyet; sayaç birimine (km/saat) duyarlı.
    ///
    /// ⭐ KAPSAM (ADR-182, 2026-08-29 · PK-T1=A — DAVRANIŞ DEĞİŞTİ): yalnız SEÇİLEN TARİH ARALIĞINDA yakıt
    /// fişi OLAN araçlar listelenir (derived table'a INNER JOIN). Önceki davranış "tam filo" idi: yakıt
    /// almayan araç da 0 / "-" ile görünürdü; kullanıcı gün bazlı incelemede bu boş satırların gürültü
    /// yarattığını ve raporu okunamaz kıldığını bildirdi.
    /// <b>Bu değişiklik YALNIZ bu rapora aittir</b> — <c>vehicle</c> (Araç Raporu) ve <c>vehicle-daily</c>
    /// (Araç Raporu — Günlük) TAM FİLO davranışını KORUR; ikisi de regresyon testleriyle kilitlidir.
    /// Tarih filtresi yine YAKIT fişlerine uygulanır (araç kartının kendi tarihine değil).
    ///
    /// PERFORMANS — N+1 YOK: yakıt maliyeti/mesafe/litre/işlem araç bazında ÖNCEDEN TEK türetilmiş tabloda toplanır
    /// ve araca 1:1 JOIN edilir (satır çarpımı yok, dış GROUP BY yok). PG + SQLite ORTAK: yalnız CAST(... AS REAL)
    /// + COALESCE + standart JOIN kullanılır (DB'ye özel sözdizimi yok); işlem sayısı REAL alınır (PG bigint/SQLite
    /// int ayrımı GetDouble ile güvenli okunur).
    ///
    /// PARA BİRİMİ (rule 4): sistemde ORTAK kur dönüşümü YOK (Money varsayılan TRY; genel bir çevrim yardımcısı
    /// bulunmuyor). Bu yüzden tutarlar MEVCUT davranışla işlem para biriminde (litre×birim fiyat) toplanır; farklı
    /// para birimleri kur ile dönüştürülmez. Durum InfoNote'ta kullanıcıya belirtilir (yeni varsayım uydurulmadı).
    ///
    /// TOPLAM (rule 9, "A" akıllı toplam): İşlem/Litre/Toplam Maliyet/Ort. Fiyat HER ZAMAN toplanır (birimden
    /// bağımsız). Mesafe/Ort. Tüketim/Birim Maliyet YALNIZ tüm satırlar aynı sayaç birimindeyse hesaplanır; km↔saat
    /// karışımında bu üç hücre boş bırakılır (yanlış birimli toplam üretilmez).
    /// </summary>
    public TableModel FuelConsumption(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var vehIn = InList("v.id", "@rv", req.VehicleIds);
        var typeIn = InList("v.vehicle_type_id", "@rt", req.VehicleTypeIds);
        // Mesafe: yakıt fişleri arasındaki sayaç farkı (rule 3). Türetilmiş tabloda araç bazında önceden toplanır.
        cmd.CommandText = @"
SELECT COALESCE(bch.name,'') AS branch_name, v.internal_code, COALESCE(v.plate,''),
       TRIM(COALESCE(br.name,'') || ' ' || COALESCE(vmd.name,'')) AS veh_name,
       COALESCE(vt.name,'') AS type_name, v.meter_unit,
       COALESCE(f.cnt,0), COALESCE(f.km,0), COALESCE(f.litre,0), COALESCE(f.fuelcost,0)
FROM vehicles v
LEFT JOIN brands br ON br.id=v.brand_id
LEFT JOIN vehicle_models vmd ON vmd.id=v.vehicle_model_id
LEFT JOIN vehicle_types vt ON vt.id=v.vehicle_type_id
LEFT JOIN branches bch ON bch.id=v.branch_id
JOIN (   -- ADR-182 (PK-T1=A): INNER — aralıkta fişi OLMAYAN araç listelenmez (eskiden LEFT = tam filo)
    SELECT vehicle_id,
      CAST(COUNT(*) AS REAL) AS cnt,
      SUM(CASE WHEN prev_meter IS NOT NULL AND current_meter IS NOT NULL
           THEN CAST(current_meter AS REAL)-CAST(prev_meter AS REAL) ELSE 0 END) AS km,
      SUM(CAST(liters AS REAL)) AS litre,
      SUM(CAST(liters AS REAL)*CAST(unit_price AS REAL)) AS fuelcost
    FROM fuel_distributions
    WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "distribution_date") + @"
    GROUP BY vehicle_id
) f ON f.vehicle_id=v.id
WHERE v.company_id=@c AND v.is_deleted=0" + ReportScope.BranchSql(s, req, "v.branch_id") + vehIn + typeIn + @"
ORDER BY COALESCE(bch.name,''), veh_name, v.internal_code;";   // varsayilan siralama: Sube -> Arac Adi (rule 11)
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        ReportScope.BindBranch(cmd, s, req);
        BindList(cmd, "@rv", req.VehicleIds);
        BindList(cmd, "@rt", req.VehicleTypeIds);

        var rows = new List<IReadOnlyList<object?>>();
        double tCnt = 0, tKm = 0, tLitre = 0, tFuel = 0;
        var units = new HashSet<string>(StringComparer.Ordinal);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var meterUnit = r.GetString(5);
                var unitTr = meterUnit == "hour" ? "Saat" : "KM";
                double cnt = r.GetDouble(6), km = r.GetDouble(7), litre = r.GetDouble(8), fuel = r.GetDouble(9);
                double consumption = km > 0 ? litre / km : 0;   // L/birim (km ya da saat)
                double avgPrice = litre > 0 ? fuel / litre : 0;  // ağırlıklı ort. ₺/L
                double perUnit = km > 0 ? fuel / km : 0;         // ₺/birim (km ya da saat)
                rows.Add(new object?[]
                {
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3).Trim(), r.GetString(4), unitTr,
                    Num(cnt, FmtCount),
                    Num(km, x => FmtDistance(x, meterUnit)),
                    Num(litre, FmtLiter),
                    Num(consumption, x => FmtConsumption(x, meterUnit)),
                    Num(avgPrice, FmtMoney),
                    Num(fuel, FmtMoney),
                    Num(perUnit, x => FmtPerUnit(x, meterUnit)),
                });
                tCnt += cnt; tKm += km; tLitre += litre; tFuel += fuel;
                units.Add(meterUnit);
            }

        // Pinned toplam (rule 9, "A"): homojen birimde mesafe/tüketim/birim-maliyet hesaplanır; karışıkta boş.
        IReadOnlyList<object?>? totalRow = null;
        if (rows.Count > 0)
        {
            bool homo = units.Count <= 1;
            var unit = units.Count == 1 ? units.First() : "km";
            double totConsumption = tKm > 0 ? tLitre / tKm : 0;
            double totAvgPrice = tLitre > 0 ? tFuel / tLitre : 0;
            double totPerUnit = tKm > 0 ? tFuel / tKm : 0;
            totalRow = new object?[]
            {
                "TOPLAM", "", "", "", "", "",
                Num(tCnt, FmtCount),
                homo ? Num(tKm, x => FmtDistance(x, unit)) : (object?)"",
                Num(tLitre, FmtLiter),
                homo ? Num(totConsumption, x => FmtConsumption(x, unit)) : (object?)"",
                Num(totAvgPrice, FmtMoney),
                Num(tFuel, FmtMoney),
                homo ? Num(totPerUnit, x => FmtPerUnit(x, unit)) : (object?)"",
            };
        }

        // Kolon-tipi: ilk 6 metin (Şube/İç Kod/Plaka/Araç Adı/Araç Türü/Sayaç Birimi), kalan 7 sayısal.
        var numeric = new[] { false, false, false, false, false, false, true, true, true, true, true, true, true };

        return new TableModel("Yakıt Tüketim", new[]
        {
            "Şube", "Araç İç Kod", "Plaka", "Araç Adı", "Araç Türü", "Sayaç Birimi",
            "İşlem Sayısı", "Mesafe", "Litre", "Ortalama Yakıt Tüketimi", "Ortalama Yakıt Fiyatı",
            "Toplam Yakıt Maliyeti", "Birim Başına Yakıt Maliyeti",
        }, rows, numeric, totalRow);
    }

    /// <summary>
    /// YAKIT TÜKETİM — GÜNLÜK (ARA İŞ 2 / S3, 2026-08-29 · ADR-182 · PK-G1=A).
    /// Dönem raporunun (<see cref="FuelConsumption"/>) GÜN GÜN kırılımı: her satır bir (ARAÇ, GÜN).
    ///
    /// KAPSAM (PK-G1=A): yalnız o gün yakıt fişi OLAN araçlar. "Tüm filo × tüm günler" görünümü bilinçli
    /// olarak <see cref="VehicleDailyReport"/>'a bırakıldı — buradaki amaç hatalı/eksik GÜNLÜK girişleri
    /// gürültüsüz görmektir (kullanıcı isteği). Bu yüzden boş gün satırı ÜRETİLMEZ.
    ///
    /// GÜN ANAHTARI: <c>distribution_date / 86400000</c> — TAM SAYI bölmesi. Lehçeye özel tarih işlevi
    /// KULLANILMAZ; SQLite ve PostgreSQL birebir aynı kovayı üretir ve kova sınırı RPR-06'nın UTC gün
    /// sınırıyla (00:00:00.000 – 23:59:59.999, iki uç dahil) hizalıdır.
    ///
    /// PERFORMANS: TEK sorgu, veritabanında GROUP BY — gün başına ya da araç başına sorgu YOKTUR (N+1 yok).
    ///
    /// TOPLAM: satır sınırına (<paramref name="maxRows"/>) takılsa bile toplamlar TÜM dönemden hesaplanır →
    /// "günlerin toplamı = dönem toplamı" güvencesi bozulmaz (testle kilitli). Oranlar (ortalama tüketim/
    /// fiyat/birim maliyet) her gün için O GÜNÜN değerlerinden yeniden hesaplanır — asla toplanmaz.
    /// </summary>
    public TableModel FuelDailyConsumption(SessionContext s, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        const long GunMs = 86_400_000;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var vehIn = InList("v.id", "@rv", req.VehicleIds);
        var typeIn = InList("v.vehicle_type_id", "@rt", req.VehicleTypeIds);
        cmd.CommandText = @"
SELECT f.distribution_date / 86400000 AS gun,
       COALESCE(bch.name,'') AS branch_name, v.internal_code, COALESCE(v.plate,''),
       TRIM(COALESCE(br.name,'') || ' ' || COALESCE(vmd.name,'')) AS veh_name,
       COALESCE(vt.name,'') AS type_name, v.meter_unit,
       CAST(COUNT(*) AS REAL) AS cnt,
       COALESCE(SUM(CASE WHEN f.prev_meter IS NOT NULL AND f.current_meter IS NOT NULL
            THEN CAST(f.current_meter AS REAL)-CAST(f.prev_meter AS REAL) ELSE 0 END),0) AS km,
       COALESCE(SUM(CAST(f.liters AS REAL)),0) AS litre,
       COALESCE(SUM(CAST(f.liters AS REAL)*CAST(f.unit_price AS REAL)),0) AS fuelcost
FROM fuel_distributions f
JOIN vehicles v ON v.id = f.vehicle_id AND v.company_id = f.company_id AND v.is_deleted = 0
LEFT JOIN brands br ON br.id = v.brand_id
LEFT JOIN vehicle_models vmd ON vmd.id = v.vehicle_model_id
LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
LEFT JOIN branches bch ON bch.id = v.branch_id
WHERE f.company_id=@c AND f.is_deleted=0" + DateFilter(req, "f.distribution_date")
            + ReportScope.BranchSql(s, req, "v.branch_id") + vehIn + typeIn + @"
GROUP BY f.distribution_date / 86400000, v.id, COALESCE(bch.name,''), v.internal_code, COALESCE(v.plate,''),
         TRIM(COALESCE(br.name,'') || ' ' || COALESCE(vmd.name,'')), COALESCE(vt.name,''), v.meter_unit
ORDER BY gun, branch_name, veh_name, v.internal_code;";
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        ReportScope.BindBranch(cmd, s, req);
        BindList(cmd, "@rv", req.VehicleIds);
        BindList(cmd, "@rt", req.VehicleTypeIds);

        var rows = new List<IReadOnlyList<object?>>();
        double tCnt = 0, tKm = 0, tLitre = 0, tFuel = 0;
        var units = new HashSet<string>(StringComparer.Ordinal);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var gun = Convert.ToInt64(r.GetValue(0));
                var meterUnit = r.GetString(6);
                double cnt = r.GetDouble(7), km = r.GetDouble(8), litre = r.GetDouble(9), fuel = r.GetDouble(10);

                // TOPLAM önce toplanır: satır sınırına takılan kayıtlar da dönem toplamına dâhildir.
                tCnt += cnt; tKm += km; tLitre += litre; tFuel += fuel;
                units.Add(meterUnit);
                if (rows.Count >= maxRows) continue;

                double consumption = km > 0 ? litre / km : 0;    // L/birim — GÜNÜN değerlerinden
                double avgPrice = litre > 0 ? fuel / litre : 0;   // ağırlıklı ort. ₺/L
                double perUnit = km > 0 ? fuel / km : 0;          // ₺/birim
                rows.Add(new object?[]
                {
                    DateTimeOffset.FromUnixTimeMilliseconds(gun * GunMs).UtcDateTime.ToString("dd.MM.yyyy", Tr),
                    r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4).Trim(), r.GetString(5),
                    meterUnit == "hour" ? "Saat" : "KM",
                    Num(cnt, FmtCount),
                    Num(km, x => FmtDistance(x, meterUnit)),
                    Num(litre, FmtLiter),
                    Num(consumption, x => FmtConsumption(x, meterUnit)),
                    Num(avgPrice, FmtMoney),
                    Num(fuel, FmtMoney),
                    Num(perUnit, x => FmtPerUnit(x, meterUnit)),
                });
            }

        // Dönem raporuyla AYNI akıllı toplam kuralı: karışık birimde mesafe/tüketim/birim-maliyet BOŞ.
        IReadOnlyList<object?>? totalRow = null;
        if (rows.Count > 0)
        {
            bool homo = units.Count <= 1;
            var unit = units.Count == 1 ? units.First() : "km";
            double totConsumption = tKm > 0 ? tLitre / tKm : 0;
            double totAvgPrice = tLitre > 0 ? tFuel / tLitre : 0;
            double totPerUnit = tKm > 0 ? tFuel / tKm : 0;
            totalRow = new object?[]
            {
                "TOPLAM (DÖNEM)", "", "", "", "", "", "",
                Num(tCnt, FmtCount),
                homo ? Num(tKm, x => FmtDistance(x, unit)) : (object?)"",
                Num(tLitre, FmtLiter),
                homo ? Num(totConsumption, x => FmtConsumption(x, unit)) : (object?)"",
                Num(totAvgPrice, FmtMoney),
                Num(tFuel, FmtMoney),
                homo ? Num(totPerUnit, x => FmtPerUnit(x, unit)) : (object?)"",
            };
        }

        var numeric = new[] { false, false, false, false, false, false, false, true, true, true, true, true, true, true };

        return new TableModel("Yakıt Tüketim — Günlük", new[]
        {
            "Tarih", "Şube", "Araç İç Kod", "Plaka", "Araç Adı", "Araç Türü", "Sayaç Birimi",
            "İşlem Sayısı", "Mesafe", "Litre", "Ortalama Yakıt Tüketimi", "Ortalama Yakıt Fiyatı",
            "Toplam Yakıt Maliyeti", "Birim Başına Yakıt Maliyeti",
        }, rows, numeric, totalRow);
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
    /// <summary>TEXT decimal (invariant) → double; boş/geçersiz → 0. performed_km/hour gibi metin sayaç alanları için
    /// (PG'de CAST('' AS REAL) hata verir; bu yüzden C# tarafında güvenli ayrıştırılır).</summary>
    private static double PNum(string s) => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;
    private static string FmtCount(double v) => v.ToString("#,##0", Tr);
    private static string FmtMoney(double v) => "₺ " + v.ToString("#,##0.00", Tr);
    private static string FmtLiter(double v) => v.ToString("#,##0.00", Tr) + " L";
    private static string FmtDistance(double v, string unit) => v.ToString("#,##0.##", Tr) + (unit == "hour" ? " Saat" : " km");
    private static string FmtConsumption(double v, string unit) => v.ToString("#,##0.00", Tr) + (unit == "hour" ? " L/Saat" : " L/km");
    private static string FmtPerUnit(double v, string unit) => "₺ " + v.ToString("#,##0.00", Tr) + (unit == "hour" ? "/Saat" : "/km");

    /// <summary>
    /// ⭐ RPT-GUNLUK (2026-08-29, PK-R1=A) — ARAÇ RAPORU · GÜNLÜK KIRILIM. Amaç: afaki/hatalı günlük
    /// veri girişinin (tek güne sıkışmış aşırı yakıt/bakım/sayaç) GÖRÜNÜR olması — alarm/eşik/otomatik
    /// düzeltme YOK, yalnız görünürlük.
    ///
    /// TEMEL İLKE: bu rapor <see cref="VehicleReport"/>'un YENİ bir hesap mantığı DEĞİL, aynı üç maliyet
    /// kaynağının (yakıt fişi · bakım malzemesi · doğrudan parça) TARİH ekseninde ayrıntılı gösterimidir.
    /// Mevcut dönem raporuna TEK SATIR dokunulmadı; günlük değerlerin toplamı dönem raporuyla tutarlıdır
    /// (VehicleDailyReportTests kilitler).
    ///
    /// GÜN ANAHTARI: <c>tarih_ms / 86400000</c> TAM SAYI bölmesi — SQLite ve PostgreSQL'de birebir aynı
    /// (BIGINT unix ms; lehçe fonksiyonu GEREKMEZ) ve RPR-06'nın UTC gün sınırıyla (00:00:00.000 —
    /// 23:59:59.999) örtüşür; mevcut tarih semantiği DEĞİŞMEZ (DateFilter/BindDates aynen kullanılır).
    ///
    /// PERFORMANS: gün başına sorgu YOK — sabit 5 sorgu (araçlar + 3 gün-gruplu toplam + gün-içi son
    /// sayaç için ham yakıt fişleri), birleştirme bellekte (kullanıcı onaylı desen). Satır sayısı
    /// gün×araç olduğundan maxRows koruması ÜRETİM SIRASINDA uygulanır; TOPLAM satırı yine TÜM dönemin
    /// toplamlarını taşır (kesmeden etkilenmez).
    ///
    /// ⭐ KAPSAM — ADR-183 (2026-08-29, KULLANICI DÜZELTMESİ): o gün HİÇ verisi olmayan (araç, gün)
    /// satırı ÜRETİLMEZ. Önce boş günler de 0/"-" ile listeleniyordu; kullanıcı bunun raporu okunamaz
    /// hâle getirdiğini bildirdi ("verisi olmayan araçları listelemeni istemedim"). Kimlik sütunları
    /// (Tarih/İç Kod/Plaka/Araç Adı/Şube/Sayaç Birimi) kaydın kendi bilgisidir ve "veri" sayılmaz;
    /// ÖLÇÜM sütunlarından EN AZ BİRİNDE değer varsa satır gelir (ör. yakıt yok ama bakım malzemesi var).
    ///
    /// ORANLAR (ort. fiyat/tüketim/birim maliyet): TOPLANMAZ — o günün değerlerinden aynı formülle
    /// yeniden hesaplanır (dönem raporuyla aynı iş anlamı). "Gün İçi Son Sayaç" = o günkü SON yakıt
    /// fişindeki sayaç (fiş yoksa "-"); "günlük tüketim" ile "gün sonu sayaç" ayrımını netleştirir.
    /// Veri modeline dokunulmadı: yeni kolon/tablo/yazma YOK, salt-okunur.
    /// </summary>
    public TableModel VehicleDailyReport(SessionContext s, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        const long GunMs = 86_400_000;
        // RequiresDate=true → Run tarih varsayılanını doldurur; yine de savunmacı davran.
        long fromMs = req.FromDate ?? 0, toMs = req.ToDate ?? 0;
        long gunBas = fromMs / GunMs, gunSon = toMs / GunMs;

        using var conn = _factory.Create();

        // 1) Araç listesi — DÖNEM raporuyla AYNI kapsam/filtre/sıralama (şube kapsamı dahil).
        var araclar = new List<(string Id, string Kod, string Plaka, string Ad, string Sube, string Birim)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT v.id, v.internal_code, COALESCE(v.plate,''),
       TRIM(COALESCE(br.name,'') || ' ' || COALESCE(vmd.name,'')) AS veh_name,
       COALESCE(bch.name,'') AS branch_name, v.meter_unit
FROM vehicles v
LEFT JOIN brands br ON br.id=v.brand_id
LEFT JOIN vehicle_models vmd ON vmd.id=v.vehicle_model_id
LEFT JOIN branches bch ON bch.id=v.branch_id
WHERE v.company_id=@c AND v.is_deleted=0" + ReportScope.BranchSql(s, req, "v.branch_id")
                + InList("v.id", "@rv", req.VehicleIds) + InList("v.vehicle_type_id", "@rt", req.VehicleTypeIds) + @"
ORDER BY COALESCE(bch.name,''), veh_name, v.internal_code;";
            cmd.AddWithValue("@c", companyId);
            ReportScope.BindBranch(cmd, s, req);
            BindList(cmd, "@rv", req.VehicleIds);
            BindList(cmd, "@rt", req.VehicleTypeIds);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                araclar.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3).Trim(), r.GetString(4), r.GetString(5)));
        }

        // 2-4) Gün-gruplu toplamlar — DÖNEM raporundaki ÜÇ alt-sorgunun birebir aynısı + gün anahtarı.
        var yakit = new Dictionary<(string, long), (double Km, double Litre, double Maliyet)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT vehicle_id, distribution_date / 86400000 AS gun,
  COALESCE(SUM(CASE WHEN prev_meter IS NOT NULL AND current_meter IS NOT NULL
       THEN CAST(current_meter AS REAL)-CAST(prev_meter AS REAL) ELSE 0 END),0) AS km,
  COALESCE(SUM(CAST(liters AS REAL)),0) AS litre,
  COALESCE(SUM(CAST(liters AS REAL)*CAST(unit_price AS REAL)),0) AS fuelcost
FROM fuel_distributions
WHERE company_id=@c AND is_deleted=0" + DateFilter(req, "distribution_date") + @"
GROUP BY vehicle_id, gun;";
            cmd.AddWithValue("@c", companyId);
            BindDates(cmd, req);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                yakit[(r.GetString(0), Convert.ToInt64(r.GetValue(1)))] = (r.GetDouble(2), r.GetDouble(3), r.GetDouble(4));
        }

        var bakim = new Dictionary<(string, long), double>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT vm.vehicle_id, vm.performed_date / 86400000 AS gun,
  COALESCE(SUM(CAST(mm.quantity AS REAL)*CAST(COALESCE(mm.unit_price,'0') AS REAL)),0) AS matcost
FROM vehicle_maintenances vm JOIN maintenance_materials mm ON mm.maintenance_id=vm.id
WHERE vm.company_id=@c AND vm.is_deleted=0 AND vm.is_cancelled=0" + DateFilter(req, "vm.performed_date") + @"
GROUP BY vm.vehicle_id, gun;";
            cmd.AddWithValue("@c", companyId);
            BindDates(cmd, req);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                bakim[(r.GetString(0), Convert.ToInt64(r.GetValue(1)))] = r.GetDouble(2);
        }

        var parca = new Dictionary<(string, long), double>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT sd.vehicle_id, sd.doc_date / 86400000 AS gun,
  COALESCE(SUM(CAST(sm.quantity AS REAL)*CAST(COALESCE(sm.unit_price,'0') AS REAL)),0) AS partcost
FROM stock_documents sd JOIN stock_movements sm ON sm.document_id=sd.id
WHERE sd.company_id=@c AND sd.is_deleted=0 AND sd.doc_type='out' AND sd.status='active'
      AND sd.vehicle_id IS NOT NULL" + DateFilter(req, "sd.doc_date") + @"
GROUP BY sd.vehicle_id, gun;";
            cmd.AddWithValue("@c", companyId);
            BindDates(cmd, req);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                parca[(r.GetString(0), Convert.ToInt64(r.GetValue(1)))] = r.GetDouble(2);
        }

        // 5) Gün içi SON sayaç — o günkü en geç yakıt fişinin current_meter'ı. TEXT sayaç alanı C#'ta
        // ayrıştırılır (PNum — PG'de CAST('' AS REAL) patlar; mevcut desen).
        var sonSayac = new Dictionary<(string, long), (long Ts, double Deger)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT vehicle_id, distribution_date, COALESCE(current_meter,'')
FROM fuel_distributions
WHERE company_id=@c AND is_deleted=0 AND current_meter IS NOT NULL" + DateFilter(req, "distribution_date") + ";";
            cmd.AddWithValue("@c", companyId);
            BindDates(cmd, req);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var anahtar = (r.GetString(0), r.GetInt64(1) / GunMs);
                var ts = r.GetInt64(1);
                var deger = PNum(Convert.ToString(r.GetValue(2), CultureInfo.InvariantCulture) ?? "");
                if (deger <= 0) continue;
                if (!sonSayac.TryGetValue(anahtar, out var mevcut) || ts >= mevcut.Ts)
                    sonSayac[anahtar] = (ts, deger);
            }
        }

        // TOPLAM satırı TÜM dönemden hesaplanır (satır kesmesinden ETKİLENMEZ) — dönem raporuyla tutarlılık.
        double tLitre = yakit.Values.Sum(x => x.Litre), tFuel = yakit.Values.Sum(x => x.Maliyet),
               tMat = bakim.Values.Sum(), tPart = parca.Values.Sum();
        double tTotal = tFuel + tMat + tPart;

        // Satırlar: GÜN → ARAÇ (aralıktaki HER GÜN, boş günler dahil). maxRows üretim sırasında korur
        // (gün×araç çarpımı patholojik aralıkta büyüyebilir; bellekte gereksiz üretim yapılmaz).
        var rows = new List<IReadOnlyList<object?>>();
        var kesildi = false;
        for (var gun = gunBas; gun <= gunSon && !kesildi; gun++)
        {
            var tarih = DateTimeOffset.FromUnixTimeMilliseconds(gun * GunMs).UtcDateTime.ToString("dd.MM.yyyy", Tr);
            foreach (var a in araclar)
            {
                if (rows.Count >= maxRows) { kesildi = true; break; }
                yakit.TryGetValue((a.Id, gun), out var f);
                bakim.TryGetValue((a.Id, gun), out var mat);
                parca.TryGetValue((a.Id, gun), out var part);
                var sayacVar = sonSayac.TryGetValue((a.Id, gun), out var sayac);

                // ⭐ ADR-183 (2026-08-29, kullanıcı düzeltmesi): O GÜN HİÇ VERİSİ OLMAYAN satır ÜRETİLMEZ.
                // Kimlik sütunları (Tarih/İç Kod/Plaka/Araç Adı/Şube/Sayaç Birimi) kaydın KENDİ bilgisidir,
                // "veri" sayılmaz. ÖLÇÜM sütunlarının hepsi boşsa satır gürültüdür (kullanıcı: "verisi
                // olmayan araçları listelemeni istemedim"). Bir tanesinde bile değer varsa satır GELİR —
                // ör. yakıt yok ama bakım malzemesi varsa listelenir.
                if (f.Km == 0 && f.Litre == 0 && f.Maliyet == 0 && mat == 0 && part == 0 && !sayacVar) continue;
                double avgPrice = f.Litre > 0 ? f.Maliyet / f.Litre : 0;
                double consumption = f.Km > 0 ? f.Litre / f.Km : 0;
                double total = f.Maliyet + mat + part;
                double perUnit = f.Km > 0 ? total / f.Km : 0;
                var unitTr = a.Birim == "hour" ? "Saat" : "KM";
                rows.Add(new object?[]
                {
                    tarih, a.Kod, a.Plaka, a.Ad, a.Sube, unitTr,
                    Num(f.Km, x => FmtDistance(x, a.Birim)),
                    Num(f.Litre, FmtLiter),
                    Num(avgPrice, FmtMoney),
                    Num(f.Maliyet, FmtMoney),
                    Num(consumption, x => FmtConsumption(x, a.Birim)),
                    Num(mat, FmtMoney),
                    Num(part, FmtMoney),
                    Num(total, FmtMoney),
                    Num(perUnit, x => FmtPerUnit(x, a.Birim)),
                    sayacVar ? Num(sayac.Deger, x => FmtDistance(x, a.Birim)) : Num(0, _ => "-"),
                });
            }
        }

        // Dönem raporuyla AYNI kural: ortalamalar ve km↔saat karışabilen kolonlar toplam satırında BOŞ.
        IReadOnlyList<object?>? totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM (DÖNEM)", "", "", "", "", "",
            "", Num(tLitre, FmtLiter), "", Num(tFuel, FmtMoney), "", Num(tMat, FmtMoney), Num(tPart, FmtMoney), Num(tTotal, FmtMoney), "", "",
        };

        var numeric = new[] { false, false, false, false, false, false, true, true, true, true, true, true, true, true, true, true };

        return new TableModel("Araç Raporu — Günlük", new[]
        {
            "Tarih", "İç Kod", "Plaka", "Araç Adı", "Şube", "Sayaç Birimi", "Günlük Sayaç Mesafesi",
            "Yakıt (Litre)", "Ortalama Yakıt Fiyatı", "Yakıt Maliyeti", "Ortalama Yakıt Tüketimi",
            "Bakım Malzeme Tutarı", "Doğrudan Parça Tutarı", "Toplam Maliyet", "Birim Başına Maliyet",
            "Gün İçi Son Sayaç",
        }, rows, numeric, totalRow);
    }

    /// <summary>
    /// BAKIM RAPORU (kullanıcı isteği 2026-08-08) — ortak standarda taşındı. Her bakım kaydı TEK satır (detay/işlem
    /// listesi; araç başına toplu DEĞİL). İptal edilen (is_cancelled) kayıtlar hariç. Şube = bakımın İŞLENDİĞİ şube
    /// (op_branch_id). Sayaç, bakımın yapıldığı andaki değerdir; araç birimine (km/saat) duyarlı. Maliyet YALNIZ
    /// bakım malzemelerini kapsar (işçilik/servis alanı yok — rule 5).
    ///
    /// PERFORMANS — correlated subquery KALDIRILDI (rule 9): malzeme maliyeti + kalem sayısı maintenance_id bazında
    /// TEK derived-table'da toplanıp bakıma 1:1 LEFT JOIN edilir. PG + SQLite ORTAK: yalnız CAST(... AS REAL) +
    /// COALESCE + standart JOIN + GROUP BY; sayım REAL alınır (bigint/int ayrımı GetDouble ile güvenli).
    ///
    /// TOPLAM (rule 6): bakım kayıt sayısı (TOPLAM etiketinde), malzeme kalem sayısı ve malzeme maliyeti toplanır;
    /// Sayaç TOPLANMAZ (km↔saat karışımı anlamsız toplam üretir). Filtre/sıralama dışı pinned satır.
    /// </summary>
    public TableModel Maintenance(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var vehIn = InList("v.id", "@mv", req.VehicleIds);
        var typeIn = InList("v.vehicle_type_id", "@mt", req.VehicleTypeIds);
        var defIn = InList("vm.maintenance_def_id", "@md", req.MaintenanceDefIds);
        var techIn = InList("vm.technician_id", "@tp", req.TechnicianIds);
        cmd.CommandText = @"
SELECT COALESCE(bch.name,'') AS branch_name, vm.performed_date,
       v.internal_code, COALESCE(v.plate,''),
       TRIM(COALESCE(br.name,'') || ' ' || COALESCE(vmd.name,'')) AS veh_name,
       COALESCE(vt.name,'') AS type_name, COALESCE(d.name,'') AS def_name, COALESCE(sd.name,'') AS sub_name,
       v.meter_unit, COALESCE(vm.performed_km,''), COALESCE(vm.performed_hour,''),
       COALESCE(p.full_name,''),
       COALESCE(mm.itemcount,0), COALESCE(mm.matcost,0)
FROM vehicle_maintenances vm
JOIN vehicles v ON v.id = vm.vehicle_id
LEFT JOIN branches bch ON bch.id = vm.op_branch_id
LEFT JOIN brands br ON br.id = v.brand_id
LEFT JOIN vehicle_models vmd ON vmd.id = v.vehicle_model_id
LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id
LEFT JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
LEFT JOIN maintenance_definitions sd ON sd.id = vm.sub_definition_id
LEFT JOIN personnel p ON p.id = vm.technician_id
LEFT JOIN (
    SELECT mm.maintenance_id,
      CAST(COUNT(*) AS REAL) AS itemcount,
      SUM(CAST(mm.quantity AS REAL) * CAST(COALESCE(mm.unit_price,'0') AS REAL)) AS matcost
    FROM maintenance_materials mm
    JOIN vehicle_maintenances vmx ON vmx.id = mm.maintenance_id
    WHERE vmx.company_id=@c
    GROUP BY mm.maintenance_id
) mm ON mm.maintenance_id = vm.id
WHERE vm.company_id=@c AND vm.is_deleted=0 AND vm.is_cancelled=0"
            + ReportScope.BranchSql(s, req, "vm.op_branch_id") + vehIn + typeIn + defIn + techIn
            + DateFilter(req, "vm.performed_date") + @"
ORDER BY branch_name, vm.performed_date DESC, v.internal_code;";   // varsayılan: Şube -> Tarih (yeni önce)
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        ReportScope.BindBranch(cmd, s, req);
        BindList(cmd, "@mv", req.VehicleIds);
        BindList(cmd, "@mt", req.VehicleTypeIds);
        BindList(cmd, "@md", req.MaintenanceDefIds);
        BindList(cmd, "@tp", req.TechnicianIds);

        var rows = new List<IReadOnlyList<object?>>();
        double tItems = 0, tCost = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var meterUnit = r.GetString(8);
                double sayac = meterUnit == "hour" ? PNum(r.GetString(10)) : PNum(r.GetString(9));
                double items = r.GetDouble(12), cost = r.GetDouble(13);
                rows.Add(new object?[]
                {
                    r.GetString(0),                                   // Şube (işlenen)
                    D(r.IsDBNull(1) ? null : r.GetInt64(1)),          // Tarih
                    r.GetString(2),                                   // Araç İç Kod
                    r.GetString(3),                                   // Plaka
                    r.GetString(4).Trim(),                            // Araç Adı (marka+model)
                    r.GetString(5),                                   // Araç Türü
                    r.GetString(6),                                   // Bakım
                    r.GetString(7),                                   // Alt Bakım
                    Num(sayac, x => FmtDistance(x, meterUnit)),       // Sayaç (km/saat)
                    r.GetString(11),                                  // Teknisyen
                    Num(items, FmtCount),                             // Malzeme Kalem Sayısı
                    Num(cost, FmtMoney),                              // Malzeme Maliyeti
                });
                tItems += items; tCost += cost;
            }

        // Pinned toplam (rule 6): kayıt sayısı TOPLAM etiketinde; kalem + maliyet toplanır; Sayaç toplanmaz (karışık birim).
        IReadOnlyList<object?>? totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM (" + FmtCount(rows.Count) + " kayıt)",
            "", "", "", "", "", "", "", "",              // Tarih..Sayaç (indeks 1-8) boş
            "",                                           // Teknisyen (indeks 9) boş
            Num(tItems, FmtCount),                        // Kalem toplamı
            Num(tCost, FmtMoney),                         // Maliyet toplamı
        };

        // Kolon-tipi: yalnız Sayaç(8) + Kalem(10) + Maliyet(11) sayısal; kalanlar metin (Tarih dâhil — biçimli metin).
        var numeric = new[] { false, false, false, false, false, false, false, false, true, false, true, true };

        return new TableModel("Bakım Raporu", new[]
        {
            "Şube", "Tarih", "Araç İç Kod", "Plaka", "Araç Adı", "Araç Türü",
            "Bakım", "Alt Bakım", "Sayaç", "Teknisyen", "Malzeme Kalem Sayısı", "Malzeme Maliyeti",
        }, rows, numeric, totalRow);
    }

    /// <summary>
    /// DEPO GİRİŞİ RAPORU (kullanıcı isteği 2026-08-08) — ortak standarda taşındı. Depoya alınan yakıt alım kayıtları;
    /// her giriş TEK satır. Şube = girişin İŞLENDİĞİ şube (op_branch_id). Tutar = litre × birim fiyat.
    ///
    /// PERFORMANS: tek tablo taraması + tedarikçi/şube adı için 1:1 LEFT JOIN (N+1 / correlated subquery YOK).
    /// PG + SQLite ORTAK: yalnız CAST(... AS REAL) + COALESCE + standart JOIN. liters/unit_price her zaman geçerli
    /// decimal metin (NOT NULL) → CAST güvenli.
    ///
    /// PARA BİRİMİ (rule): ortak kur dönüşümü YOK → tutarlar işlem para biriminde toplanır, farklı para birimleri
    /// kur ile dönüştürülmez (InfoNote'ta belirtilir). Para Birimi kolonu bilgi amaçlı gösterilir.
    ///
    /// TOPLAM: litre + tutar toplanır; birim fiyat = AĞIRLIKLI ortalama (toplam tutar / toplam litre). Pinned, filtre/
    /// sıralama dışı.
    /// </summary>
    public TableModel FuelDepot(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var supIn = InList("fde.supplier_id", "@sup", req.SupplierIds);
        cmd.CommandText = @"
SELECT COALESCE(bch.name,'') AS branch_name, fde.entry_date, COALESCE(sup.name,'') AS supplier_name,
       CAST(fde.liters AS REAL), CAST(fde.unit_price AS REAL),
       CAST(fde.liters AS REAL) * CAST(fde.unit_price AS REAL),
       COALESCE(fde.invoice_no,''), COALESCE(fde.currency_code,'TRY')
FROM fuel_depot_entries fde
LEFT JOIN branches bch ON bch.id = fde.op_branch_id
LEFT JOIN suppliers sup ON sup.id = fde.supplier_id
WHERE fde.company_id=@c AND fde.is_deleted=0"
            + ReportScope.BranchSql(s, req, "fde.op_branch_id") + supIn
            + DateFilter(req, "fde.entry_date") + @"
ORDER BY branch_name, fde.entry_date DESC;";   // varsayılan: Şube -> Tarih (yeni önce)
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        ReportScope.BindBranch(cmd, s, req);
        BindList(cmd, "@sup", req.SupplierIds);

        var rows = new List<IReadOnlyList<object?>>();
        double tLitre = 0, tTutar = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                double litre = r.GetDouble(3), price = r.GetDouble(4), tutar = r.GetDouble(5);
                rows.Add(new object?[]
                {
                    r.GetString(0),                 // Şube (işlenen)
                    D(r.GetInt64(1)),               // Tarih
                    r.GetString(2),                 // Tedarikçi
                    Num(litre, FmtLiter),           // Litre
                    Num(price, FmtMoney),           // Birim Fiyat
                    Num(tutar, FmtMoney),           // Tutar
                    r.GetString(6),                 // Fatura No
                    r.GetString(7),                 // Para Birimi
                });
                tLitre += litre; tTutar += tutar;
            }

        // Pinned toplam: litre + tutar toplanır; birim fiyat = ağırlıklı ort. (toplam tutar / toplam litre).
        IReadOnlyList<object?>? totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM", "", "",
            Num(tLitre, FmtLiter),
            Num(tLitre > 0 ? tTutar / tLitre : 0, FmtMoney),
            Num(tTutar, FmtMoney),
            "", "",
        };

        // Kolon-tipi: Litre(3) + Birim Fiyat(4) + Tutar(5) sayısal; kalanlar metin.
        var numeric = new[] { false, false, false, true, true, true, false, false };

        return new TableModel("Yakıt Depo Girişi Raporu", new[]   // RPR-V3: katalog adıyla aynı (yalnız YAKIT deposu)
        {
            "Şube", "Tarih", "Tedarikçi", "Litre", "Birim Fiyat", "Tutar", "Fatura No", "Para Birimi",
        }, rows, numeric, totalRow);
    }

    // ═══════════════ STK-10a — STOK HAREKETLERİ RAPORU (2026-08-11) ═══════════════

    /// <summary>
    /// STOK HAREKETLERİ RAPORU (`STK-10a`) — hareket defterinin kataloglanmış, filtrelenebilir ve
    /// Excel'e aktarılabilir dökümü. Daha önce yalnız bir EKRAN vardı; katalogda rapor olmadığı için
    /// dışa aktarımı yoktu.
    ///
    /// <b>Bu artımın filtreleri:</b> <c>Date</c> + <c>Location</c> (ikisi de STK-06'dan beri var ve
    /// RPR-01 tarafından 6 katmanda güvence altında). <c>Search</c>/<c>Material</c>/<c>MovementType</c>
    /// **STK-10b'nindir** ve burada BİLİNÇLİ olarak eklenmemiştir.
    ///
    /// <b>KAYNAK / HEDEF semantiği</b> (yeni anlam icat edilmedi — defterden okundu):
    /// <list type="bullet">
    ///   <item><c>direction &gt; 0</c> → <c>branch_id</c> = <b>HEDEF</b>; kaynak = <c>branch_from_id</c>
    ///         (farklıysa; transferin giriş bacağında kaynak depo buradadır)</item>
    ///   <item><c>direction &lt; 0</c> → <c>branch_id</c> = <b>KAYNAK</b>; hedef yok</item>
    /// </list>
    /// Transfer defterde <b>İKİ AYRI SATIR</b>dır ve öyle kalır — tek satıra indirgenmez.
    ///
    /// <b>🔒 BranchScope × Location kesişimi (plan §14):</b> kapsam <b>DIŞ SINIRDIR</b>, lokasyon
    /// filtresi onun <b>İÇİNDE</b> daraltır — ikisi <c>AND</c>'lenir, asla <c>OR</c>'lanmaz. Sonuç:
    /// Depo A oturumundaki kullanıcı Depo B filtresiyle <b>BOŞ</b> sonuç alır; lokasyon filtresi
    /// hiçbir koşulda yetki sınırını genişletemez.
    ///
    /// <b>⚡ Performans (plan §13/D-2):</b> <see cref="Run"/> satır tavanını <b>bellekte</b>, Dispatch'ten
    /// SONRA uygular. Bu yüzden burada <b>filtre → sıralama → LIMIT sırası SQL İÇİNDE</b> kurulur;
    /// tüm defterin belleğe çekilmesi engellenir. <c>Run</c>'ın kesmesi ikinci bir emniyet ağıdır.
    /// Lokasyon ve malzeme adları AYNI sorguda JOIN ile gelir → satır başına sorgu (N+1) YOKTUR.
    /// </summary>
    /// <param name="maxRows">SQL'e inen satır tavanı (<see cref="ReportLimits"/>). Ekran ve export
    /// AYNI değeri kullanır (plan §13/D-1) → ikisi aynı kümeyi üretir.</param>
    public TableModel StockMovements(SessionContext s, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();

        // ── Filtreler: lokasyon (STK-06) · tür (10b-1) · arama (10b-2) · malzeme (10b-3) ────────
        // 🔴 STK-10b-4: bu dört filtrenin SQL'i artık TEK KAYNAKTAN gelir
        // (<see cref="StockMovementFilterSql"/>) — Stok Hareketleri EKRANI da (web + masaüstü)
        // aynı üreteci kullanır. Böylece ekran ile rapor aynı satır kümesini vermek ZORUNDADIR;
        // ikinci bir hareket sorgulama mantığı YOKTUR.
        var filtre = StockMovementFilterSql.Build(req.LocationIds, req.MovementTypes, req.SearchText, req.MaterialIds);

        // ── Sorgu: filtre → sıralama → LIMIT (hepsi SQL'de) ────────────────────────────────────
        // Şube kapsamı (ReportScope.BranchSql) DIŞ SINIRDIR; lokasyon, tür, arama ve malzeme filtreleri
        // AND ile İÇERİDE daraltır — hiçbiri kapsamı genişletemez.
        // STK-11: rapor da EKRANLA aynı işlem tarihini gösterir/süzer (tek kaynak: IslemTarihiSql).
        cmd.CommandText = @"
SELECT " + StockMovementFilterSql.IslemTarihiSql + @", sm.movement_type, sm.direction, sm.quantity,
       m.code, m.name, COALESCE(u.name,'') AS unit,
       sm.branch_id, bl.name AS loc_name, sm.branch_from_id, bf.name AS from_name,
       COALESCE(d.doc_no,'') AS doc_no, COALESCE(d.invoice_no,'') AS invoice_no,
       sm.is_reversed, COALESCE(sm.note,'') AS note
FROM stock_movements sm
JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.company_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN stock_documents d ON d.id = sm.document_id
LEFT JOIN branches bl ON bl.id = sm.branch_id      AND bl.company_id = sm.company_id
LEFT JOIN branches bf ON bf.id = sm.branch_from_id AND bf.company_id = sm.company_id
WHERE sm.company_id = @c"
            + ReportScope.BranchSql(s, req, "sm.branch_id")
            + DateFilter(req, StockMovementFilterSql.IslemTarihiSql)
            + filtre.Sql
            + $" ORDER BY sm.created_at DESC, {SqlDialect.RowTieBreaker(conn, "sm")} DESC LIMIT @lim;";

        cmd.AddWithValue("@c", companyId);
        ReportScope.BindBranch(cmd, s, req);
        BindDates(cmd, req);
        filtre.Bind(cmd);
        cmd.AddWithValue("@lim", maxRows > 0 ? maxRows : ReportLimits.DefaultMaxRows);

        var rows = new List<IReadOnlyList<object?>>();
        decimal toplamGiris = 0m, toplamCikis = 0m;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var type = r.GetString(1);
                var direction = r.GetInt32(2);
                var qty = Money.Parse(r.GetString(3));
                var locId = r.IsDBNull(7) ? null : r.GetString(7);
                var locName = r.IsDBNull(8) ? null : r.GetString(8);
                var fromId = r.IsDBNull(9) ? null : r.GetString(9);
                var fromName = r.IsDBNull(10) ? null : r.GetString(10);

                // KAYNAK/HEDEF türetimi — yönden okunur, uydurulmaz.
                string kaynak, hedef;
                if (direction > 0)
                {
                    hedef = LocName(locId, locName);
                    kaynak = string.IsNullOrEmpty(fromId) || fromId == locId ? Bos : LocName(fromId, fromName);
                }
                else
                {
                    kaynak = LocName(locId, locName);
                    hedef = Bos;
                }

                // Miktar İŞARETLİ: giriş +, çıkış −. Ham değer korunur (STK-11 kapsamı değil).
                var signed = direction > 0 ? qty : -qty;
                if (direction > 0) toplamGiris += qty; else toplamCikis += qty;

                rows.Add(new object?[]
                {
                    DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0)).LocalDateTime.ToString("dd.MM.yyyy HH:mm"),
                    // STK-B1: hareket türü etiketi TEK KAYNAKTAN. İkinci bir harita KURULMAZ.
                    MovementTypeOptions.Label(type),
                    r.GetString(4), r.GetString(5),
                    new NumCell((double)signed, (signed >= 0 ? "+" : "") + signed.ToString("0.##")),
                    r.GetString(6),
                    kaynak, hedef,
                    r.GetString(11), r.GetString(12),
                    Convert.ToInt64(r.GetValue(13)) == 1 ? "İptal edildi" : "",
                    r.GetString(14),
                });
            }

        var numeric = new[] { false, false, false, false, true, false, false, false, false, false, false, false };
        var totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM", "", "", "",
            new NumCell((double)(toplamGiris - toplamCikis),
                $"+{toplamGiris:0.##} / -{toplamCikis:0.##}"),
            "", "", "", "", "", "", "",
        };

        return new TableModel("Stok Hareketleri Raporu", new[]
        {
            "Tarih", "Tür", "Kod", "Malzeme", "Miktar", "Birim",
            "Kaynak", "Hedef", "Belge No", "Fatura No", "Durum", "Açıklama",
        }, rows, numeric, totalRow);
    }

    /// <summary>
    /// STOK HAREKETLERİ — GÜNLÜK (ARA İŞ 2 / S3 · ADR-182, ⭐ ADR-183 ile YENİDEN YAZILDI 2026-08-29).
    ///
    /// 🔴 <b>Düzeltilen hata.</b> İlk sürüm gün × tür ÖZETİ üretiyordu ("26.08.2026 · Giriş · 20 işlem").
    /// Kullanıcı bunun işe yaramadığını bildirdi: <i>"o gün kaç tane giriş yapılmışsa tek tek giriş
    /// yapılan malzemeler listelenmeli"</i>. Rapor artık GÜN GÜN ilerler ve o günün HER hareketini
    /// MALZEMESİYLE tek tek listeler; 20 giriş varsa 20 satır gelir.
    ///
    /// DETAY RAPORDAN FARKI (<see cref="StockMovements"/>): detay rapor KAYIT ANINA göre tersten sıralıdır
    /// (en son girilen üstte, "az önce kaydettiğim görünsün" gerekçesiyle). Bu rapor İŞ GÜNÜNE göre
    /// KRONOLOJİK sıralanır (gün → tür → malzeme) → gün gün okunan bir döküm verir. Detay rapor
    /// DEĞİŞMEDİ ve aynen durur.
    ///
    /// TEK FİLTRE KAYNAĞI: lokasyon/tür/arama/malzeme süzgeçleri <see cref="StockMovementFilterSql"/>'den
    /// gelir — Stok Hareketleri EKRANI ve DETAY raporuyla aynı üreteç → üçü ayrışamaz. Şube kapsamı
    /// (<see cref="ReportScope"/>) DIŞ SINIRDIR; filtreler yalnız AND ile daraltır.
    ///
    /// TARİH: işlem tarihi tek kaynaktan (<see cref="StockMovementFilterSql.IslemTarihiSql"/>); gün kovası
    /// tam sayı bölmesidir (ms/86.400.000) → iki lehçede birebir, UTC gün sınırıyla hizalı.
    /// MİKTAR: <see cref="Money.Parse"/> ile TAM ondalık okunur; giriş +, çıkış − olarak işaretlenir.
    /// </summary>
    public TableModel StockMovementsDaily(SessionContext s, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        const long GunMs = 86_400_000;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var filtre = StockMovementFilterSql.Build(req.LocationIds, req.MovementTypes, req.SearchText, req.MaterialIds);
        var gunSql = StockMovementFilterSql.IslemTarihiSql + " / 86400000";

        cmd.CommandText = @"
SELECT " + gunSql + @" AS gun, sm.movement_type, sm.direction, sm.quantity,
       m.code, m.name, COALESCE(u.name,'') AS unit,
       sm.branch_id, bl.name AS loc_name, sm.branch_from_id, bf.name AS from_name,
       COALESCE(d.doc_no,'') AS doc_no, sm.is_reversed
FROM stock_movements sm
JOIN materials m ON m.id = sm.material_id AND m.company_id = sm.company_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN stock_documents d ON d.id = sm.document_id
LEFT JOIN branches bl ON bl.id = sm.branch_id      AND bl.company_id = sm.company_id
LEFT JOIN branches bf ON bf.id = sm.branch_from_id AND bf.company_id = sm.company_id
WHERE sm.company_id = @c"
            + ReportScope.BranchSql(s, req, "sm.branch_id")
            + DateFilter(req, StockMovementFilterSql.IslemTarihiSql)
            + filtre.Sql + @"
ORDER BY gun, sm.movement_type, m.code LIMIT @lim;";

        cmd.AddWithValue("@c", companyId);
        ReportScope.BindBranch(cmd, s, req);
        BindDates(cmd, req);
        filtre.Bind(cmd);
        cmd.AddWithValue("@lim", maxRows > 0 ? maxRows : ReportLimits.DefaultMaxRows);

        var rows = new List<IReadOnlyList<object?>>();
        decimal tGiris = 0m, tCikis = 0m;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var gun = Convert.ToInt64(r.GetValue(0));
                var tur = r.GetString(1);
                var direction = r.GetInt32(2);
                var qty = Money.Parse(r.GetString(3));
                var locId = r.IsDBNull(7) ? null : r.GetString(7);
                var locName = r.IsDBNull(8) ? null : r.GetString(8);
                var fromId = r.IsDBNull(9) ? null : r.GetString(9);
                var fromName = r.IsDBNull(10) ? null : r.GetString(10);

                // KAYNAK/HEDEF türetimi — detay raporla AYNI kural (yönden okunur, uydurulmaz).
                string kaynak, hedef;
                if (direction > 0)
                {
                    hedef = LocName(locId, locName);
                    kaynak = string.IsNullOrEmpty(fromId) || fromId == locId ? Bos : LocName(fromId, fromName);
                }
                else
                {
                    kaynak = LocName(locId, locName);
                    hedef = Bos;
                }

                var signed = direction > 0 ? qty : -qty;
                if (direction > 0) tGiris += qty; else tCikis += qty;

                rows.Add(new object?[]
                {
                    DateTimeOffset.FromUnixTimeMilliseconds(gun * GunMs).UtcDateTime.ToString("dd.MM.yyyy", Tr),
                    MovementTypeOptions.Label(tur),          // STK-B1: etiket TEK KAYNAKTAN
                    r.GetString(4), r.GetString(5),
                    new NumCell((double)signed, (signed >= 0 ? "+" : "") + signed.ToString("0.##")),
                    r.GetString(6),
                    kaynak, hedef,
                    r.GetString(11),
                    Convert.ToInt64(r.GetValue(12)) == 1 ? "İptal edildi" : "",
                });
            }

        var numeric = new[] { false, false, false, false, true, false, false, false, false, false };
        var totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM (DÖNEM)", "", "", "",
            new NumCell((double)(tGiris - tCikis), $"+{tGiris:0.##} / -{tCikis:0.##}"),
            "", "", "", "", "",
        };

        return new TableModel("Stok Hareketleri — Günlük", new[]
        {
            "Tarih", "Tür", "Kod", "Malzeme", "Miktar", "Birim", "Kaynak", "Hedef", "Belge No", "Durum",
        }, rows, numeric, totalRow);
    }

    /// <summary>
    /// GÜNLÜK FAALİYET — DETAY (ARA İŞ 2 / S4, 2026-08-29 · ADR-182 · PK-D1=A).
    /// Günlük Faaliyet ekranındaki kayıtların gün gün dökümü; her satır BİR kayıttır (en yeni gün üstte).
    ///
    /// KAYIT TİPİ FİLTRESİ: sabit listeden çoklu seçim (<see cref="DailyActivityTypeOptions"/>).
    /// <b>Hiçbir tip seçilmezse TÜM tipler listelenir</b> (kullanıcı kuralı — boş liste "filtre yok"tur).
    /// Tip iki sütunla kodlandığı için (activity_type + movement_kind) eşleme SQL'de tek yerde yapılır:
    /// "Hareket" = movement ∧ kind≠transfer · "Transfer" = movement ∧ kind=transfer. Bilinmeyen anahtar
    /// parametre olarak bağlanır ve hiçbir satırla eşleşmez (fail-closed; enjeksiyon yüzeyi yok).
    ///
    /// KAPSAM: iptal/silinmiş kayıtlar HARİÇ (<c>is_deleted=0</c> — ekranla aynı varsayılan). Şube kapsamı
    /// diğer raporlarla aynı yoldan (<see cref="ReportScope"/>) ve kaydın İŞLENDİĞİ şube (<c>op_branch_id</c>)
    /// üzerinden uygulanır. Tarih ZORUNLUDUR (RequiresDate) — defter sürekli büyür, tarihsiz tam tarama yok.
    /// </summary>
    public TableModel DailyActivityDetail(SessionContext s, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT da.activity_date, da.activity_type, COALESCE(da.movement_kind,'') AS kind,
       COALESCE(ob.name,'') AS branch_text,
       CASE WHEN v.internal_code IS NULL THEN ''
            WHEN v.plate IS NULL OR v.plate='' THEN v.internal_code
            ELSE v.internal_code || ' - ' || v.plate END AS vehicle_text,
       CASE WHEN fb.name IS NULL AND tb.name IS NULL THEN ''
            WHEN tb.name IS NULL THEN fb.name
            WHEN fb.name IS NULL THEN '→ ' || tb.name
            ELSE fb.name || ' → ' || tb.name END AS route_text,
       COALESCE(p.full_name,'') AS operator_text,
       da.duration_days, COALESCE(da.description,'') AS description
FROM daily_activities da
LEFT JOIN vehicles v ON v.id = da.vehicle_id AND v.company_id = da.company_id
LEFT JOIN branches fb ON fb.id = da.from_location_id AND fb.company_id = da.company_id
LEFT JOIN branches tb ON tb.id = da.to_location_id AND tb.company_id = da.company_id
LEFT JOIN branches ob ON ob.id = da.op_branch_id AND ob.company_id = da.company_id
LEFT JOIN personnel p ON p.id = da.operator_id AND p.company_id = da.company_id
WHERE da.company_id = @c AND da.is_deleted = 0"
            + ReportScope.BranchSql(s, req, "da.op_branch_id")
            + DateFilter(req, "da.activity_date")
            + InList("da.vehicle_id", "@rv", req.VehicleIds)
            + ActivityTypeSql(req.ActivityTypes)
            + $" ORDER BY da.activity_date DESC, {SqlDialect.RowTieBreaker(conn, "da")} DESC LIMIT @lim;";

        cmd.AddWithValue("@c", companyId);
        ReportScope.BindBranch(cmd, s, req);
        BindDates(cmd, req);
        BindList(cmd, "@rv", req.VehicleIds);
        BindActivityTypes(cmd, req.ActivityTypes);
        cmd.AddWithValue("@lim", maxRows > 0 ? maxRows : ReportLimits.DefaultMaxRows);

        var rows = new List<IReadOnlyList<object?>>();
        double tGun = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var tip = r.GetString(1);
                var kind = r.GetString(2);
                // Etiket TEK KAYNAKTAN: ikinci bir Türkçe eşleme kurulmaz (STK-B1 dersi).
                var tipAnahtar = tip == "movement"
                    ? (kind == DailyActivityTypeOptions.Transfer ? DailyActivityTypeOptions.Transfer : DailyActivityTypeOptions.Movement)
                    : tip;
                double gun = r.IsDBNull(7) ? 0 : Convert.ToDouble(r.GetValue(7));
                tGun += gun;

                rows.Add(new object?[]
                {
                    DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0)).UtcDateTime.ToString("dd.MM.yyyy", Tr),
                    DailyActivityTypeOptions.Label(tipAnahtar),
                    r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6),
                    Num(gun, x => x.ToString("0.##", Tr)),
                    r.GetString(8),
                });
            }

        var numeric = new[] { false, false, false, false, false, false, true, false };
        var totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM", $"{rows.Count} kayıt", "", "", "", "", Num(tGun, x => x.ToString("0.##", Tr)), "",
        };

        return new TableModel("Günlük Faaliyet — Detay", new[]
        {
            "Tarih", "Kayıt Tipi", "Şube", "Araç", "Nereden → Nereye", "Operatör", "Süre (gün)", "Açıklama",
        }, rows, numeric, totalRow);
    }

    /// <summary>ADR-182 — kayıt tipi filtresinin SQL'i. Boş seçim → filtre YOK (tüm tipler).
    /// "Hareket"/"Transfer" aynı <c>activity_type='movement'</c> satırlarının <c>movement_kind</c> ile
    /// ayrılmış hâlleridir; diğer tipler doğrudan eşleşir ve PARAMETRE olarak bağlanır.</summary>
    private static string ActivityTypeSql(IReadOnlyList<string>? secilen)
    {
        var keys = Temizle(secilen);
        if (keys.Count == 0) return "";
        var parcalar = new List<string>();
        var dogrudan = Dogrudan(keys);
        for (int i = 0; i < dogrudan.Count; i++) parcalar.Add($"da.activity_type=@at{i}");
        if (keys.Contains(DailyActivityTypeOptions.Movement))
            parcalar.Add("(da.activity_type='movement' AND COALESCE(da.movement_kind,'') <> 'transfer')");
        if (keys.Contains(DailyActivityTypeOptions.Transfer))
            parcalar.Add("(da.activity_type='movement' AND da.movement_kind='transfer')");
        return " AND (" + string.Join(" OR ", parcalar) + ")";
    }

    private static void BindActivityTypes(DbCommand cmd, IReadOnlyList<string>? secilen)
    {
        var dogrudan = Dogrudan(Temizle(secilen));
        for (int i = 0; i < dogrudan.Count; i++) cmd.AddWithValue($"@at{i}", dogrudan[i]);
    }

    private static List<string> Temizle(IReadOnlyList<string>? v)
        => (v ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();

    private static List<string> Dogrudan(List<string> keys)
        => keys.Where(k => k != DailyActivityTypeOptions.Movement && k != DailyActivityTypeOptions.Transfer).ToList();

    /// <summary>Lokasyon adı: boş kimlik → "Atanmamış" (gerçek depo gibi gösterilmez, STK-06 standardı);
    /// adı okunamayan kimlik → kimliğin kendisi (sessizce gizlenmez).</summary>
    private static string LocName(string? id, string? name)
        => string.IsNullOrEmpty(id) ? "Atanmamış" : (string.IsNullOrEmpty(name) ? id! : name!);

    /// <summary>Kaynak/Hedef boş hücresi — kullanıcı "veri yok" ile "Atanmamış"ı karıştırmasın.</summary>
    private const string Bos = "—";

    /// <summary>Stok Sayım Raporu — her sayım satırı: sistem/sayılan/fark (fark 0 olanlar dahil).</summary>
    public TableModel StockCount(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // STK-06: SAYIM ARTIK BİR DEPOYA AİTTİR (STK-04/05). Rapor bunu göstermezse "Sistem 10, Sayılan 12"
        // satırı hangi depoya ait belli olmaz — farklı depolarda yapılan sayımlar okunamaz hâle gelir.
        // Sayılan depo, sayım belgesinin to_branch_id'sidir (StockService.Count → RunDocument(..., branchId)).
        // Depo adı AYNI sorguda JOIN ile gelir (satır başına ad sorgusu yasak).
        // 🔴 RPR-03 (denetim 2026-08-25) DÜZELTMESİ — ŞUBE KAPSAMI BU RAPORDA DA UYGULANMIYORDU.
        // Kardeş rapor Stok Durumu bu açığı DEN-E2'de kapatmıştı; sayım raporu aynı hatayla kalmıştı:
        // (a) filtre boşken şubeyle sınırlı kullanıcı TÜM şubelerin sayımlarını görüyordu;
        // (b) isteğe BAŞKA şubenin depo kimliği yazılırsa o depo okunuyordu (parametre manipülasyonu).
        // Tek otorite BranchAccess'tir — DEN-E2 ile BİREBİR aynı kalıp kullanıldı, yeni kural YOK.
        // ⚠️ Kardeş rapor Stok Durumu ile AYNI karar: burada `Allowed` kullanılır, `Effective` DEĞİL.
        // Filtre boyutu şube değil, SAYILAN DEPOdur; çalışma şubesi bunu daraltmamalıdır. Ayrıntılı
        // gerekçe StockStatus'taki açıklamadadır (denetim 2026-08-26'da denendi ve geri alındı).
        var izinli = BranchAccess.Allowed(s);                 // null = sınırsız (admin / tüm şubeler)
        var locations = NormalizeLocations(req.LocationIds);
        if (izinli is not null)
        {
            var izinliSet = new HashSet<string>(izinli, StringComparer.Ordinal);
            if (locations.Count > 0)
            {
                // ATANMAMIŞ ("") kovası şubesiz kayıtlarla aynı ilkeyle GİZLENMEZ.
                var suzulen = locations.Where(x => x.Length == 0 || izinliSet.Contains(x)).ToList();
                // FAIL-CLOSED: yalnız kapsam dışı depo istendiyse boş sonuç — filtre sessizce KALKMAZ.
                if (suzulen.Count == 0)
                    return new TableModel("Stok Sayım Raporu",
                        new[] { "Tarih", "Sayılan Depo", "Kod", "Malzeme", "Sistem", "Sayılan", "Fark", "Durum", "Gerekçe" },
                        Array.Empty<IReadOnlyList<object?>>());
                locations = suzulen;
            }
            else
            {
                // Filtre verilmedi → kapsam KENDİSİ filtredir (+ ATANMAMIŞ).
                locations = izinli.Concat(new[] { "" }).Distinct(StringComparer.Ordinal).ToList();
            }
        }

        var locFilter = "";
        if (locations.Count > 0)
        {
            var names = new List<string>(locations.Count);
            for (int i = 0; i < locations.Count; i++)
            {
                var p = "@cloc" + i;
                names.Add(p);
                // ATANMAMIŞ ("") sayım belgesinde to_branch_id = NULL olarak durur → COALESCE ile eşlenir.
                cmd.AddWithValue(p, locations[i]);
            }
            locFilter = $" AND COALESCE(d.to_branch_id,'') IN ({string.Join(",", names)})";
        }
        cmd.CommandText = @"
SELECT d.doc_date, m.code, m.name,
       CAST(scl.system_qty AS REAL), CAST(scl.counted_qty AS REAL), CAST(scl.diff_qty AS REAL),
       COALESCE(scl.reason,''), COALESCE(br.name,'')
FROM stock_count_lines scl
JOIN stock_documents d ON d.id = scl.document_id
JOIN materials m ON m.id = scl.material_id
LEFT JOIN branches br ON br.id = d.to_branch_id AND br.company_id = d.company_id
WHERE d.company_id=@c AND d.is_deleted=0 AND d.doc_type='count'
" + DateFilter(req, "d.doc_date") + locFilter + @"
ORDER BY d.doc_date DESC, m.code;";
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        var rows = new List<IReadOnlyList<object?>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var diff = r.GetDouble(5);
            var loc = r.GetString(7);
            rows.Add(new object?[] { D(r.GetInt64(0)),
                loc.Length == 0 ? "Atanmamış (depo girilmemiş)" : loc,
                r.GetString(1), r.GetString(2),
                r.GetDouble(3), r.GetDouble(4), diff, diff == 0 ? "Fark yok" : (diff > 0 ? "Fazla" : "Eksik"), r.GetString(6) });
        }
        return new TableModel("Stok Sayım Raporu",
            new[] { "Tarih", "Sayılan Depo", "Kod", "Malzeme", "Sistem", "Sayılan", "Fark", "Durum", "Gerekçe" }, rows);
    }

    /// <summary>
    /// TALEP RAPORU (kullanıcı isteği 2026-08-08) — ortak standarda taşındı. Her malzeme talebi TEK satır (belge
    /// listesi). Şube = talebin şubesi (material_requests.branch_id — bu tabloda op_branch_id YOKTUR). Reddedilen ve
    /// iptal edilen talepler LİSTEDE KALIR (durum birer statüdür, silme değil); kullanıcı Durum filtresiyle daraltır.
    ///
    /// PERFORMANS — correlated subquery KALDIRILDI: kalem sayısı request_id bazında TEK derived-table'da sayılıp
    /// talebe 1:1 LEFT JOIN edilir. PG + SQLite ORTAK: CAST(COUNT(*) AS REAL) + COALESCE + standart LEFT JOIN
    /// (sayım REAL alınır → PG bigint / SQLite int ayrımı GetDouble ile güvenli).
    ///
    /// Bu raporda PARA/ARAÇ yoktur → ₺, km/saat, ağırlıklı ortalama gibi standartlar UYGULANMAZ (kullanıcı kararı).
    /// Kalem sayısı NumCell'dir: HAM değer sıralama/filtrede, görüntü hücrede (0 → "-").
    /// </summary>
    public TableModel Requests(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var reqIn = InList("mr.requester_id", "@rq", req.RequesterIds);
        var stIn = InList("mr.status", "@rs", req.Statuses);
        cmd.CommandText = @"
SELECT COALESCE(b.name,'') AS branch_name, mr.doc_no, mr.request_date,
       COALESCE(pr.full_name,''), COALESCE(pa.full_name,''),
       mr.status, COALESCE(it.cnt,0), COALESCE(mr.description,'')
FROM material_requests mr
LEFT JOIN branches b ON b.id = mr.branch_id
LEFT JOIN personnel pr ON pr.id = mr.requester_id
LEFT JOIN personnel pa ON pa.id = mr.approver_id
LEFT JOIN (
    SELECT i.request_id, CAST(COUNT(*) AS REAL) AS cnt
    FROM material_request_items i
    JOIN material_requests mrx ON mrx.id = i.request_id
    WHERE mrx.company_id=@c
    GROUP BY i.request_id
) it ON it.request_id = mr.id
WHERE mr.company_id=@c AND mr.is_deleted=0"
            + ReportScope.BranchSql(s, req, "mr.branch_id") + reqIn + stIn
            + DateFilter(req, "mr.request_date") + @"
ORDER BY branch_name, mr.request_date DESC;";   // varsayılan: Şube -> Tarih (yeni önce)
        cmd.AddWithValue("@c", companyId);
        BindDates(cmd, req);
        ReportScope.BindBranch(cmd, s, req);
        BindList(cmd, "@rq", req.RequesterIds);
        BindList(cmd, "@rs", req.Statuses);

        var rows = new List<IReadOnlyList<object?>>();
        double tItems = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                double items = r.GetDouble(6);
                rows.Add(new object?[]
                {
                    r.GetString(0),                       // Şube
                    r.GetString(1),                       // Belge No
                    D(r.IsDBNull(2) ? null : r.GetInt64(2)), // Tarih
                    r.GetString(3),                       // Talep Eden
                    r.GetString(4),                       // Onaylayan
                    StatusTr(r.GetString(5)),             // Durum (Türkçe etiket)
                    Num(items, FmtCount),                 // Kalem Sayısı (HAM + görüntü)
                    r.GetString(7),                       // Açıklama
                });
                tItems += items;
            }

        // Pinned toplam: talep sayısı (TOPLAM etiketinde) + toplam kalem sayısı; diğer kolonlar boş (kullanıcı kararı).
        IReadOnlyList<object?>? totalRow = rows.Count == 0 ? null : new object?[]
        {
            "TOPLAM (" + FmtCount(rows.Count) + " talep)",
            "", "", "", "", "",
            Num(tItems, FmtCount),
            "",
        };

        // Kolon-tipi: yalnız Kalem Sayısı(6) sayısal; kalanlar metin (Tarih dâhil — biçimli metin).
        var numeric = new[] { false, false, false, false, false, false, true, false };

        return new TableModel("Talep Raporu", new[]
        {
            "Şube", "Belge No", "Tarih", "Talep Eden", "Onaylayan", "Durum", "Kalem Sayısı", "Açıklama",
        }, rows, numeric, totalRow);
    }

    private static string D(long? ms) => ms is null or 0 ? "" : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime.ToString("dd.MM.yyyy");

    /// <summary>Talep durumu → Türkçe etiket. TEK doğru kaynak: <see cref="RequestStatusOptions"/> (filtre listesiyle aynı).</summary>
    private static string StatusTr(string s) => RequestStatusOptions.Label(s);

    private static string DateFilter(ReportRequest req, string col)
    {
        var sb = "";
        if (req.FromDate is not null) sb += $" AND {col} >= @from";
        if (req.ToDate is not null) sb += $" AND {col} <= @to";
        return sb;
    }

    /// <summary>
    /// ⭐ RPR-T4 (kapsamlı rapor taraması 2026-08-27) — TARİHİ BOŞ OLABİLEN kolon için tarih süzgeci.
    ///
    /// <b>Kapatılan hata.</b> <see cref="DateFilter"/> düz karşılaştırma yazar ve SQL'de <c>NULL</c>
    /// karşılaştırması DAİMA false döner. Muayene/Sigorta raporu "sonraki tarih" üzerinden süzüyor,
    /// bu alan ise ekranda İSTEĞE BAĞLI: sonraki tarihi girilmemiş bir belge <b>hiçbir tarih
    /// aralığında listelenmiyordu</b> — ekranda duruyor, raporda hiç yok. Kullanıcı bunu "girdiğim
    /// kayıt raporda çıkmıyor" olarak yaşar.
    ///
    /// Raporun kendi sıralaması (<c>ORDER BY (next_date IS NULL), …</c>) ve durum hesabı
    /// (<c>next is null → Normal</c>) bu satırların VAR OLMASINI zaten bekliyordu; eksik olan tek şey
    /// süzgecin NULL'a izin vermesiydi. Tarihi olan kayıtlar için davranış AYNIDIR.
    /// </summary>
    private static string DateFilterNullable(ReportRequest req, string col)
    {
        if (req.FromDate is null && req.ToDate is null) return "";
        var kosul = "";
        if (req.FromDate is not null) kosul += $" AND {col} >= @from";
        if (req.ToDate is not null) kosul += $" AND {col} <= @to";
        // "Tarihi yok" ya da "aralıkta" — tarihi olanlar için sonuç değişmez.
        return $" AND ({col} IS NULL OR (1=1{kosul}))";
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
    /// <summary>
    /// ⭐ RPR-10 — MUAYENE / SİGORTA RAPORU (denetim 2026-08-26).
    ///
    /// Yeni iş kuralı ÜRETİLMEDİ: satırlar mevcut "Muayene/Sigorta" ekranıyla aynı kaynaktan
    /// (<c>vehicle_inspections</c>) gelir, belge adları ve DURUM eşiği ekranın kullandığı TEK sabitten
    /// (<see cref="DepoWise.Infrastructure.Maintenance.InspectionService.ApproachingDays"/>) okunur.
    ///
    /// Ekranın YAPMADIĞI ama raporun yaptığı tek şey ŞUBE KAPSAMIDIR: rapor, aracın şubesine göre
    /// <see cref="ReportScope"/> ile daraltılır (diğer operasyon raporlarıyla aynı kalıp).
    /// </summary>
    public TableModel Inspections(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        // RPR-12: rapor, Muayene/Sigorta ekranının verisini gösterir → O ekranın izni de gerekir
        // (ön muhasebe raporlarındaki desenin aynısı; "reports" tek başına yeterli değildir).
        AccessControl.Require(s, "inspection", PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var vehIn = InList("v.id", "@iv", req.VehicleIds);
        cmd.CommandText = @"
SELECT COALESCE(br.name,''), v.internal_code, COALESCE(v.plate,''), vi.doc_type,
       vi.last_date, vi.next_date, COALESCE(vi.place,''), COALESCE(vi.result,'')
FROM vehicle_inspections vi
JOIN vehicles v ON v.id = vi.vehicle_id AND v.company_id = vi.company_id AND v.is_deleted=0
LEFT JOIN branches br ON br.id = v.branch_id AND br.company_id = v.company_id
WHERE vi.company_id=@c AND vi.is_deleted=0"
            + ReportScope.BranchSql(s, req, "v.branch_id") + vehIn
            + DateFilterNullable(req, "vi.next_date") + @"
ORDER BY (vi.next_date IS NULL), vi.next_date, v.internal_code;";
        cmd.AddWithValue("@c", companyId);
        ReportScope.BindBranch(cmd, s, req);
        BindList(cmd, "@iv", req.VehicleIds);
        BindDates(cmd, req);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var rows = new List<IReadOnlyList<object?>>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                long? next = r.IsDBNull(5) ? null : r.GetInt64(5);
                // Ekranla BİREBİR aynı kural (InspectionService.List içindeki hesap).
                var level = next is null ? DepoWise.Infrastructure.Maintenance.DateAlertLevel.Normal
                    : next.Value < now ? DepoWise.Infrastructure.Maintenance.DateAlertLevel.Expired
                    : next.Value - now <= (long)DepoWise.Infrastructure.Maintenance.InspectionService.ApproachingDays * 86_400_000
                        ? DepoWise.Infrastructure.Maintenance.DateAlertLevel.Approaching
                        : DepoWise.Infrastructure.Maintenance.DateAlertLevel.Normal;
                var kod = r.GetString(1);
                var plaka = r.GetString(2);
                rows.Add(new object?[]
                {
                    r.GetString(0).Length == 0 ? "Atanmamış" : r.GetString(0),
                    plaka.Length == 0 ? kod : kod + " - " + plaka,
                    DocTypeTr(r.GetString(3)),
                    D(r.IsDBNull(4) ? null : r.GetInt64(4)),
                    D(next),
                    next is null ? "" : ((next.Value - now) / 86_400_000L).ToString(Tr),
                    r.GetString(6),
                    r.GetString(7),
                    level switch
                    {
                        DepoWise.Infrastructure.Maintenance.DateAlertLevel.Expired => "Süresi geçti",
                        DepoWise.Infrastructure.Maintenance.DateAlertLevel.Approaching => "Yaklaşıyor",
                        _ => "Normal",
                    },
                });
            }

        return new TableModel("Muayene / Sigorta Raporu",
            new[] { "Şube", "Araç", "Belge", "Son Tarih", "Sonraki Tarih", "Kalan Gün", "Yer", "Sonuç", "Durum" }, rows);
    }

    /// <summary>Belge türü → Türkçe etiket. Kaynak: Muayene/Sigorta ekranının kullandığı aynı eşleme.</summary>
    private static string DocTypeTr(string t) => t switch
    {
        "inspection" => "Muayene", "insurance" => "Sigorta", "kasko" => "Kasko", "calibration" => "Kalibrasyon", _ => t,
    };

    /// <summary>
    /// ⭐ RPR-11 — PERSONEL RAPORU (denetim 2026-08-26).
    ///
    /// Kolonlar Personel ekranından alındı; "Erişim" rozeti de ekranla AYNI kuraldır (bağlı kullanıcı →
    /// Admin/Kullanıcı, yoksa saha personeli işareti, o da yoksa "Kullanıcı yok"). Kullanıcı adı-rol
    /// eşlemesi <c>UserService.AccountsByPersonnel</c> ile aynı sorgudur; N+1 yoktur (tek geçiş).
    ///
    /// Şube kapsamı diğer operasyon raporlarıyla aynı kalıptadır (İZİNLİ ∩ ÇALIŞMA ŞUBESİ).
    /// </summary>
    public TableModel Personnel(SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        // RPR-12: rapor KİŞİSEL VERİ gösterir (ad, telefon, kullanıcı adı) → Personel ekranının izni
        // olmadan açılmaz. Aksi halde yalnız "reports" izni verilen biri personel listesini okurdu.
        AccessControl.Require(s, "personnel", PermissionAction.View);
        ReportGate.EnsureRunnable(req);
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT COALESCE(br.name,''), p.full_name, COALESCE(p.title,''), COALESCE(p.phone,''),
       COALESCE(u.username,''),
       CAST(COALESCE((SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id
                      WHERE ur.user_id=u.id AND r.role_key IN (@ca,@sa)),0) AS INTEGER),
       p.is_active, p.is_field_staff
FROM personnel p
LEFT JOIN branches br ON br.id = p.branch_id AND br.company_id = p.company_id
LEFT JOIN users u ON u.personnel_id = p.id AND u.company_id = p.company_id AND u.is_deleted=0
WHERE p.company_id=@c AND p.is_deleted=0"
            + ReportScope.BranchSql(s, req, "p.branch_id") + @"
ORDER BY br.name, p.full_name;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@ca", RoleKeys.CompanyAdmin);
        cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
        ReportScope.BindBranch(cmd, s, req);

        var rows = new List<IReadOnlyList<object?>>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var kullanici = r.GetString(4);
                var adminMi = r.GetInt32(5) > 0;
                var sahaMi = r.GetInt64(7) == 1;
                var erisim = kullanici.Length > 0
                    ? (adminMi ? "Admin · " : "Kullanıcı · ") + kullanici
                    : (sahaMi ? "Saha personeli" : "Kullanıcı yok");
                rows.Add(new object?[]
                {
                    r.GetString(0).Length == 0 ? "Atanmamış" : r.GetString(0),
                    r.GetString(1), r.GetString(2), r.GetString(3),
                    erisim,
                    r.GetInt64(6) == 1 ? "Aktif" : "Pasif",
                });
            }

        return new TableModel("Personel Raporu",
            new[] { "Şube", "Ad Soyad", "Unvan", "Telefon", "Erişim", "Durum" }, rows);
    }

    public TableModel Run(SessionContext s, string key, ReportRequest req, int maxRows = ReportLimits.DefaultMaxRows)
    {
        // ⭐ ARA İŞ 4 (ADR-186) — CUSTOM RAPOR ÇÖZÜMLEMESİ.
        //
        // Sabit katalogda bulunmayan anahtar, ÖNCE custom rapor olarak çözülmeye çalışılır. Çözülürse
        // kaynak kayıt defterinden (CustomReportSources) bir ReportDescriptor ÜRETİLİR ve aşağıdaki
        // DÖRT KAPININ TAMAMINDAN aynen geçer — kapılar tek yerde kalır, custom yol onları ATLAMAZ.
        // Güvenlik meta verisi (DataModule · Category · IsManager) TANIMDAN DEĞİL kaynaktan gelir →
        // kullanıcı tanımı düzenleyerek yetkisini genişletemez.
        // Çözülemezse (bağlayıcı yok, tanım yok, başka firmanın tanımı, pasif) davranış eskisi gibi:
        // "Bilinmeyen rapor tipi" — istisna üzerinden kapı atlatılamaz.
        CustomReportDefinition? customDef = null;
        ReportDescriptor? desc;
        if (CustomReportDefinition.IdFromKey(key) is { } customId)
        {
            customDef = Custom?.ById(s, customId);
            var kaynak = customDef is { IsActive: true } ? CustomReportSources.ByKey(customDef.SourceKey) : null;
            desc = kaynak is null || customDef is null ? null : new ReportDescriptor(
                key, customDef.Name, "Kullanıcı tanımlı rapor", kaynak.Category,
                // IsManager türetilmiştir (Group == Manager) → yönetici kapısı kaynak meta verisinden gelir.
                kaynak.IsManager ? ReportGroup.Manager : ReportGroup.Standard,
                kaynak.RequiresDate ? ReportFilters.Date : ReportFilters.None,
                RequiresDate: kaynak.RequiresDate,
                ExportButton: "btn-export-report",
                InfoNote: null,
                DataModule: kaynak.DataModule);
        }
        else desc = ReportCatalog.ByKey(key);

        if (desc is null) throw new ArgumentException("Bilinmeyen rapor tipi: " + key);

        // ⭐ RPR-07 (denetim 2026-08-25) — YÖNETİCİ RAPORU KAPISI (tek nokta: hem masaüstü hem API buradan geçer).
        //
        // Katalog raporları ikiye ayırıyordu (Standard / Manager) ve bu ayrım MENÜDE ("Yönetici Raporları",
        // web'de @admin kapısı) ve EXCEL yetkisinde (ExportManagerReports) zaten uygulanıyordu. Ama raporu
        // ÇALIŞTIRMAK hiçbir yerde ayrılmıyordu → ayrım fiilen kozmetikti.
        //
        // Bu neden önemli: yönetici raporları (Araç/Malzeme şablon dökümleri, Şube Bazlı Özet) bilinçli
        // olarak oturumun ÇALIŞMA ŞUBESİNİ yok sayar (BranchScopeTests ile kilitli ürün kararı) → depo
        // personeli için istenen "yalnız giriş yapılan şube" kuralı bu raporlarda SAĞLANAMAZ. Bu yüzden
        // yönetici raporları yönetici kapısına alındı; kapsam kuralları böylece çelişmez.
        //
        // ⚠️ Firma/şube İZOLASYONU bu kapıdan bağımsızdır ve raporların kendi sorgularında zaten vardır.
        if (desc.IsManager && !AccessControl.IsAdmin(s))
            throw new ForbiddenException(
                "Bu rapor Yönetici Raporları grubundadır; görüntülemek için yönetici yetkisi gerekir.");

        // ⭐ RPR-15 (denetim 2026-08-26) — "ROL YETKİ KONTROL" İLE KAPATILAN EKRANIN VERİSİ RAPORDAN
        // OKUNAMAZ.
        //
        // <b>İhlal edilen güvence:</b> RoleGrantService sözleşmesi, süper adminin bir ROLE kapattığı modül
        // için "admin bypass'ı dahil API/UI erişimi kapanır" der. Rapor yolu bunu deliyordu: kapı yalnız
        // "reports" modülünü soruyor, raporun OKUDUĞU ekranın kapalı olup olmadığına bakmıyordu. Süper
        // admin "Stok" ekranını Personel rolüne kapatsa bile, o roldeki kullanıcı Stok Hareketleri
        // raporunu çalıştırıp aynı veriyi satır satır okuyabiliyor, hatta Excel'e aktarabiliyordu.
        //
        // <b>Kural neden DAR:</b> bu raporlarda ekranın TAM iznini istemek, bugün yalnız "Raporlar"
        // yetkisi verilmiş kullanıcıların erişimini KESERDİ (çalışan davranış bozulurdu). Bu yüzden
        // yalnız AÇIKÇA KAPATILMIŞ modül engellenir — kapatma yoksa hiçbir şey değişmez.
        //
        // Süper admin ve geliştirici modu muaftır (AccessControl.Can ile aynı ilke: platform sahibi
        // kendini kilitlemez). Tek nokta: hem masaüstü hem API hem Excel dışa aktarma buradan geçer.
        if (desc.DataModule is { } veriModulu
            && !s.IsSuperAdmin && !DeveloperMode.IsActive
            && s.BlockedModules.Contains(veriModulu))
            throw new ForbiddenException(
                $"Bu rapor «{AppModules.Label(veriModulu)}» ekranının verisini gösterir; o ekran rolünüze kapatılmıştır.");

        // ⭐ RPT-YETKI (2026-08-29, PK-R2=A) — RAPOR TÜRÜ (KATEGORİ) İKİNCİ KAPISI.
        //
        // "reports" ÜST KAPI olarak kalır (her rapor metodu başında zaten istenir); buna EK olarak
        // raporun kategorisine bağlı modül izni istenir (eşleme TEK merkezden: ReportCatalog.CategoryModule —
        // katalog süzmeleri de aynısını kullanır, tür adı değiştirerek atlatılamaz). Tek nokta: masaüstü,
        // API ve Excel dışa aktarma hepsi buradan geçer. Admin/firma admini mevcut bypass kuralıyla geçer;
        // deny-by-default gereği yeni anahtarlar atanana dek normal kullanıcıda kapalıdır (PK-R3=A).
        // Mevcut kapılar (tenant · BranchAccess · manager · RequiredModule · DataModule · export butonu)
        // AYNEN korunur — bu kapı hiçbirini gevşetmez, yalnız EKLENİR.
        AccessControl.Require(s, ReportCatalog.CategoryModule(desc.Category), PermissionAction.View);

        if (customDef is not null)
        {
            // ⭐ "reports" ÜST KAPISI — custom yolda AÇIKÇA istenir.
            //
            // Sabit raporlarda bu kapı her rapor METODUNUN başında uygulanır (AccessControl.Require(s,
            // Module, View)). Custom rapor gövdesi ise alttaki SearchGrid servislerine gider ve onlar
            // yalnız KENDİ modüllerini (materials/vehicles/daily_activity) ister → üst kapı boşta kalırdı.
            // Bu kontrol o boşluğu kapatır: rapor yetkisi olmayan kullanıcı, ekran yetkisiyle custom
            // rapor çalıştıramaz. (Testle yakalandı: CR19.)
            AccessControl.Require(s, Module, PermissionAction.View);

            // ⭐ PK-CR-04=A — RAPOR BAŞINA DİNAMİK YETKİ ANAHTARI (deny-by-default).
            // `user_permissions.module_key` serbest metin olduğu için MIGRATION GEREKTİRMEZ.
            AccessControl.Require(s, customDef.PermissionKey, PermissionAction.View);

            // ⭐ PK-CR-10=A — OLAY VERİSİNDE TARİH ARALIĞI AÇIKÇA ZORUNLUDUR.
            // Bu kontrol, aşağıdaki "RequiresDate → Bu Ay varsayılanı" bloğundan ÖNCE çalışır; aksi
            // hâlde varsayılan devreye girer ve "tarihsiz custom rapor çalışmaz" kuralı fiilen
            // uygulanmamış olurdu. Sabit raporların varsayılan davranışı DEĞİŞMEZ.
            var kural = CustomReportRules.ValidateRun(customDef, req.FromDate, req.ToDate);
            if (!kural.Ok) throw new ArgumentException(kural.Error);
        }

        // Tarih varsayılanı (sunucu-taraflı zorlama — istemci göndermese bile korur).
        if (desc.RequiresDate && (req.FromDate is null || req.ToDate is null))
        {
            var (from, to) = ReportCatalog.CurrentMonthRange();
            req = req with { FromDate = req.FromDate ?? from, ToDate = req.ToDate ?? to };
        }

        // Custom rapor: mevcut TableModel'e projeksiyon (ikinci motor YOK — PK-CR-03=A).
        var table = customDef is not null
            ? Custom!.Run(s, customDef, req.FromDate, req.ToDate, maxRows)
            : Dispatch(s, key, req, maxRows);

        // Maksimum kayıt koruması: patholojik sonuçta üstten kes (normal raporlar sınırın çok altında).
        // ⚠️ STK-10a/D-2: bu kesme BELLEKTE yapılır — sorgu zaten tüm eşleşen satırları getirmiş olur.
        // Defter gibi BÜYÜYEN tablolarda tavanın SQL'e inmesi gerekir; `stock-movements` bunu kendi
        // sorgusunda yapar (LIMIT @lim) ve buradaki kesme onun için ikinci bir emniyet ağıdır.
        if (maxRows > 0 && table.Rows.Count > maxRows)
            table = table with { Rows = table.Rows.Take(maxRows).ToList() };
        return table;
    }

    /// <summary>Katalog anahtarı → hesaplama metodu (tek switch — hem masaüstü hem API aynı eşleme).
    /// <paramref name="maxRows"/> yalnız tavanı SQL'e indiren raporlara geçirilir (bkz. StockMovements);
    /// diğerlerinin davranışı DEĞİŞMEDİ.</summary>
    private TableModel Dispatch(SessionContext s, string key, ReportRequest req, int maxRows) => key switch
    {
        // ═══ G4-4 — ÖN MUHASEBE RAPORLARI ═══ (hesaplama AccountingReports'ta; ikinci framework YOK)
        "acc-statement" => AccountingReports.Statement(_factory, s, req),
        "acc-balances" => AccountingReports.Balances(_factory, s, req),
        "acc-invoices" => AccountingReports.Invoices(_factory, s, req),
        "acc-open-invoices" => AccountingReports.OpenInvoices(_factory, s, req, _clock),
        "acc-payments" => AccountingReports.Payments(_factory, s, req),
        "acc-cash" => AccountingReports.Cash(_factory, s, req),

        "stock-movements" => StockMovements(s, req, maxRows),   // STK-10a
        "stock-movements-daily" => StockMovementsDaily(s, req, maxRows),   // ADR-182 (PK-G2=A): gün×tür özeti
        "daily-activity" => DailyActivityDetail(s, req, maxRows),   // ADR-182 (PK-D1=A): faaliyet detayı
        "stock" => StockStatus(s, req),
        "vehicle" => VehicleReport(s, req),
        "vehicle-daily" => VehicleDailyReport(s, req, maxRows),   // RPT-GUNLUK (PK-R1=A): gün×araç kırılımı
        "maintenance" => Maintenance(s, req),
        "fuel" => FuelConsumption(s, req),
        "fuel-daily" => FuelDailyConsumption(s, req, maxRows),   // ADR-182 (PK-G1=A): gün×araç kırılımı
        "fuel-depot" => FuelDepot(s, req),
        "stock-count" => StockCount(s, req),
        "requests" => Requests(s, req),
        "inspection" => Inspections(s, req),      // RPR-10
        "personnel" => Personnel(s, req),         // RPR-11
        "materials-template" => MaterialsByTemplate(s, req),
        "materials-nontemplate" => MaterialsNonTemplate(s, req),
        "vehicles-template" => VehiclesByTemplate(s, req),
        "vehicles-nontemplate" => VehiclesNonTemplate(s, req),
        "status" => StatusReport(s, req),
        _ => throw new ArgumentException("Bilinmeyen rapor tipi: " + key),
    };
}
