using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;
using VL = DepoWise.Application.Ui.VehicleListColumns;

namespace DepoWise.Infrastructure.Vehicles;

public sealed record NewVehicle(
    string InternalCode, string? Plate = null, int? ProductionYear = null,
    decimal CurrentMeter = 0m, string MeterUnit = "km", string? BranchId = null, string? DriverPersonnelId = null,
    string? ChassisNo = null, string? EngineNo = null, string Status = "active", string? StatusNote = null,
    string? VehicleTypeId = null, string? CategoryId = null, string? BrandId = null, string? VehicleModelId = null,
    string? TemplateId = null);

public sealed record VehicleRecord(
    string Id, string CompanyId, string InternalCode, string? Plate, decimal CurrentMeter, string MeterUnit,
    string Status, string? BrandId, string? VehicleModelId, int? ProductionYear, long CreatedAt);

public sealed record VehicleListRow(
    string Id, string InternalCode, string? Plate, string Status, decimal CurrentMeter, string MeterUnit, int? ProductionYear)
{
    /// <summary>Araç seçimi gösterimi: "İç Kod - Plaka" (plaka boşsa yalnız iç kod).</summary>
    public string Display => string.IsNullOrWhiteSpace(Plate) ? InternalCode : $"{InternalCode} - {Plate}";
    public override string ToString() => Display;
}

/// <summary>Araç listesi (kolon-bazlı filtre + sayfalama) satırı — <see cref="DepoWise.Application.Ui.VehicleListColumns"/>'taki
/// HER kolonun görüntü değerini taşır; "Bakım/Muayene" uyarısı BURADA yoktur (ekran kendi hesaplar).</summary>
public sealed record VehicleGridRow(
    string Id, string InternalCode, string? Plate, int? ProductionYear, decimal Meter, string MeterUnit,
    string Status, string StatusLabel, string? StatusNote, string? VehicleType, string? Category, string? Brand,
    string? Model, string? Branch, string? Driver, string? ChassisNo, string? EngineNo);

/// <summary>Her alan için kullanıcının o kolona yazdığı filtre metni. Sıra
/// <see cref="DepoWise.Application.Ui.VehicleListColumns.All"/> ile AYNIDIR.</summary>
public sealed record VehicleGridFilter(
    string? InternalCode = null, string? Plate = null, string? ProductionYear = null, string? Meter = null,
    string? Status = null, string? StatusNote = null, string? VehicleType = null, string? Category = null,
    string? Brand = null, string? Model = null, string? Branch = null, string? Driver = null,
    string? ChassisNo = null, string? EngineNo = null);

public sealed record VehicleDetail(
    string Id, string InternalCode, string? Plate, int? ProductionYear, decimal CurrentMeter, string MeterUnit,
    string Status, string? StatusNote, string? ChassisNo, string? EngineNo,
    string? VehicleTypeId, string? CategoryId, string? BrandId, string? VehicleModelId, string? BranchId, string? DriverPersonnelId,
    string? VehicleTypeName, string? CategoryName, string? BrandName, string? VehicleModelName, string? BranchName, string? DriverName,
    // DÜZENLEME KİLİDİ: formun açıldığı andaki sürüm (bkz. EditLockGuard).
    long Version = 0,
    string? TemplateId = null)   // bağlı olduğu araç şablonu (düzenlemede korunur/değiştirilebilir)
{
    public string MeterDisplay => $"{CurrentMeter:0.##} {MeterUnit}";
}

public sealed record UpdateVehicle(string? Plate, int? ProductionYear, string Status, string? StatusNote,
    string? ChassisNo = null, string? EngineNo = null,
    string? VehicleTypeId = null, string? CategoryId = null, string? BrandId = null, string? VehicleModelId = null,
    string? BranchId = null, string? DriverPersonnelId = null, string? TemplateId = null);

/// <summary>İşlem Geçmişi (madde 4, 2026-08-06): araç için tek satır — oluşturma/şube transferi/genel güncelleme
/// (audit_logs) + sayaç değişimi (vehicle_meter_logs, kaynağı yakıt/bakım/manuel) birleştirilip tarihe göre azalan
/// sıralanır. Salt-okunur; hiçbir alan bu listeden düzenlenemez.</summary>
public sealed record VehicleHistoryRow(long Date, string Label, string? Detail)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(Date).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
}

/// <summary>
/// Araç kartı — iç kod benzersiz; şablondan doldurma + şablon malzemelerini araca kopyalama (aynı transaction);
/// sayaç geriye gidemez (MeterRule) ve tüm değişimler vehicle_meter_logs'a yazılır.
/// </summary>
public sealed class VehicleService
{
    private const string Module = "vehicles";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public VehicleService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext s, NewVehicle dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.InternalCode)) throw new ArgumentException("İç kod zorunlu.");

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (CodeExists(conn, tx, s.CompanyId, dto.InternalCode))
            throw new InvalidOperationException($"İç kod zaten kullanılıyor: {dto.InternalCode}");

        // Şablon seçildiyse boş alanları doldur (kullanıcı değeri öncelikli)
        var applied = ApplyTemplate(conn, tx, s.CompanyId, dto);

        // Plaka benzersiz (kullanıcı isteği 2026-08-05): aynı firmada bir plaka tek araçta olabilir.
        // YEREL kontrol — sync ayrı upsert yolundan gider, offline çakışmada patlamaz.
        if (!string.IsNullOrWhiteSpace(applied.Plate) && PlateExists(conn, tx, s.CompanyId, applied.Plate!, excludeId: null))
            throw new InvalidOperationException($"Bu plaka zaten kayıtlı: {applied.Plate}");

        // B-7 (PRT-01 Grup 5, 2026-08-11): şube ve sürücü id'leri İSTEMCİDEN gelir → firmaya ait oldukları
        // doğrulanır. Aksi halde başka firmanın şubesine/personeline bağlı araç oluşturulabiliyordu ve
        // liste JOIN'leri o kaydın ADINI gösteriyordu. Emsal: PersonnelService (ScopeResolver) ve
        // B-2/B-3'teki EnsureVehicleOwned deseni.
        EnsureBranchOwned(conn, tx, s.CompanyId, applied.BranchId);
        EnsurePersonnelOwned(conn, tx, s.CompanyId, applied.DriverPersonnelId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO vehicles(id, company_id, internal_code, plate, production_year, current_meter, meter_unit,
    branch_id, driver_personnel_id, chassis_no, engine_no, status, status_note,
    vehicle_type_id, category_id, brand_id, vehicle_model_id, template_id,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@ic,@plate,@yr,@meter,@mu,@br,@drv,@ch,@en,@st,@note,@vt,@cat,@brand,@vm,@tpl,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@ic", applied.InternalCode.Trim());
            cmd.AddWithValue("@plate", (object?)applied.Plate ?? DBNull.Value);
            cmd.AddWithValue("@yr", (object?)applied.ProductionYear ?? DBNull.Value);
            cmd.AddWithValue("@meter", Money.Serialize(applied.CurrentMeter));
            cmd.AddWithValue("@mu", applied.MeterUnit);
            cmd.AddWithValue("@br", (object?)applied.BranchId ?? DBNull.Value);
            cmd.AddWithValue("@drv", (object?)applied.DriverPersonnelId ?? DBNull.Value);
            cmd.AddWithValue("@ch", (object?)applied.ChassisNo ?? DBNull.Value);
            cmd.AddWithValue("@en", (object?)applied.EngineNo ?? DBNull.Value);
            cmd.AddWithValue("@st", applied.Status);
            // Durum açıklaması yalnız "çalışmıyor" durumlarında saklanır (Bakımda + Arızalı — ortak kural).
            cmd.AddWithValue("@note", DepoWise.Application.Ui.VehicleStatus.NeedsNote(applied.Status) ? (object?)applied.StatusNote ?? DBNull.Value : DBNull.Value);
            cmd.AddWithValue("@vt", (object?)applied.VehicleTypeId ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)applied.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@brand", (object?)applied.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@vm", (object?)applied.VehicleModelId ?? DBNull.Value);
            cmd.AddWithValue("@tpl", (object?)applied.TemplateId ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }

        // Şablonun uyumlu malzemeleri yeni aracın material_compatible_vehicles kayıtlarına kopyalanır
        if (applied.TemplateId is not null)
            CopyTemplateMaterials(conn, tx, applied.TemplateId, id);

        // Açılış sayacı > 0 ise log
        if (applied.CurrentMeter > 0)
            WriteMeterLog(conn, tx, s.CompanyId, id, 0m, applied.CurrentMeter, "vehicle_create", now);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>İleri-yön sayaç: yeni &gt; mevcut ise ilerletir + loglar (true). Aksi halde no-op (false).
    /// Bakım/yakıt geçmiş kayıtlarını ENGELLEMEZ. Tüm ilerlemeler loglanır.</summary>
    public bool AdvanceMeter(SessionContext s, string vehicleId, decimal value, string source)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var current = ReadMeter(conn, tx, s.CompanyId, vehicleId);
        if (!MeterRule.ShouldAdvance(current, value)) { tx.Commit(); return false; }
        UpdateMeter(conn, tx, vehicleId, value, now);
        WriteMeterLog(conn, tx, s.CompanyId, vehicleId, current, value, source, now);
        tx.Commit();
        return true;
    }

    /// <summary>Doğrudan sayaç düzenleme (araç formu). Geriye gitme YASAK → MeterBackwardException.</summary>
    public void SetMeter(SessionContext s, string vehicleId, decimal value, string source = "vehicle_form")
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var current = ReadMeter(conn, tx, s.CompanyId, vehicleId);
        if (!MeterRule.IsValidDirectSet(current, value))
            throw new MeterBackwardException($"Sayaç geriye alınamaz: mevcut {current}, girilen {value}.");
        if (value != current)
        {
            UpdateMeter(conn, tx, vehicleId, value, now);
            WriteMeterLog(conn, tx, s.CompanyId, vehicleId, current, value, source, now);
        }
        tx.Commit();
    }

    public decimal GetMeter(SessionContext s, string vehicleId)
    {
        using var conn = _factory.Create();
        return ReadMeter(conn, null, s.CompanyId, vehicleId);
    }

    /// <summary>
    /// Araç sayaç geçmişi. ⭐ SEC-02 (denetim 2026-08-25): metot FİRMA FİLTRESİ OLMADAN ve oturum
    /// almadan yazılmıştı. Bugün hiçbir yerden çağrılmıyor (ölü kod) ama bir ekrana bağlandığı anda
    /// başka firmanın sayaç geçmişini döndürürdü — sessiz bir tuzak. Kapı şimdi kapalı: oturum zorunlu,
    /// firma filtresi sorguda.
    /// </summary>
    public IReadOnlyList<(decimal Old, decimal New, string Source)> MeterHistory(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT old_value, new_value, source FROM vehicle_meter_logs " +
                          "WHERE vehicle_id=@v AND company_id=@c ORDER BY created_at;";
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<(decimal, decimal, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((Money.Parse(r.GetString(0)), Money.Parse(r.GetString(1)), r.GetString(2)));
        return list;
    }

    /// <summary>Araç listesi (salt okuma) — iç kod/plaka araması; firma kapsamı + is_deleted=0.</summary>
    public IReadOnlyList<VehicleListRow> List(SessionContext s, string? search = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT id, internal_code, plate, status, current_meter, meter_unit, production_year
FROM vehicles
WHERE company_id=@c AND is_deleted=0
  AND (CAST(@s AS TEXT) IS NULL OR {SqlDialect.LikeTr(conn, "internal_code", "@like")} OR {SqlDialect.LikeTr(conn, "COALESCE(plate,'')", "@like")})
ORDER BY internal_code LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.AddWithValue("@s", (object?)term ?? DBNull.Value);
        cmd.AddWithValue("@like", term is null ? "%" : "%" + term + "%");
        cmd.AddWithValue("@lim", limit);
        var list = new List<VehicleListRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VehicleListRow(
                r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3), Money.Parse(r.GetString(4)), r.GetString(5),
                r.IsDBNull(6) ? (int?)null : r.GetInt32(6)));
        return list;
    }

    private const string GridInnerSql = @"
SELECT v.id AS id, v.internal_code AS internal_code, v.plate AS plate, v.production_year AS production_year,
       v.current_meter AS meter_raw, v.meter_unit AS meter_unit,
       printf('%.2f', CAST(v.current_meter AS REAL)) || ' ' || v.meter_unit AS meter_text,
       v.status AS status,
       CASE v.status WHEN 'active' THEN 'Aktif' WHEN 'passive' THEN 'Pasif' WHEN 'maintenance' THEN 'Bakımda'
            WHEN 'faulty' THEN 'Arızalı' ELSE v.status END AS status_label,
       COALESCE(v.status_note,'') AS status_note,
       COALESCE(vt.name,'') AS vehicle_type, COALESCE(vc.name,'') AS category, COALESCE(b.name,'') AS brand,
       COALESCE(vm.name,'') AS model, COALESCE(br.name,'') AS branch, COALESCE(p.full_name,'') AS driver,
       COALESCE(v.chassis_no,'') AS chassis_no, COALESCE(v.engine_no,'') AS engine_no
FROM vehicles v
LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id AND vt.company_id = v.company_id
LEFT JOIN vehicle_categories vc ON vc.id = v.category_id AND vc.company_id = v.company_id
LEFT JOIN brands b ON b.id = v.brand_id
LEFT JOIN vehicle_models vm ON vm.id = v.vehicle_model_id
LEFT JOIN branches br ON br.id = v.branch_id AND br.company_id = v.company_id
LEFT JOIN personnel p ON p.id = v.driver_personnel_id AND p.company_id = v.company_id
WHERE v.company_id = @c AND v.is_deleted = 0";

    /// <summary>Kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17) — bkz.
    /// <see cref="Materials.MaterialService.SearchGrid"/> (aynı desen, <c>GridQuery</c> paylaşılır).
    /// "Durum" filtresi Türkçe ETİKETE göre arar (status_label, ör. "Aktif") — ekran zaten yalnız etiketi
    /// gösterir, kullanıcı ham koda ("active") hiç erişmez.</summary>
    public GridResult<VehicleGridRow> SearchGrid(SessionContext s, VehicleGridFilter filter, int page, int pageSize,
        string? sortColumn = null, bool sortDesc = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        var byKey = new (string Key, GridQuery.ColumnFilter Col)[]
        {
            (VL.InternalCode, new GridQuery.ColumnFilter("t.internal_code", filter.InternalCode)),
            (VL.Plate, new GridQuery.ColumnFilter("t.plate", filter.Plate)),
            (VL.ProductionYear, new GridQuery.ColumnFilter("t.production_year", filter.ProductionYear, GridQuery.ColumnKind.Numeric)),
            (VL.Meter, new GridQuery.ColumnFilter("t.meter_text", filter.Meter, GridQuery.ColumnKind.Numeric, "t.meter_raw")),
            (VL.Status, new GridQuery.ColumnFilter("t.status_label", filter.Status)),
            (VL.StatusNote, new GridQuery.ColumnFilter("t.status_note", filter.StatusNote)),
            (VL.VehicleType, new GridQuery.ColumnFilter("t.vehicle_type", filter.VehicleType)),
            (VL.Category, new GridQuery.ColumnFilter("t.category", filter.Category)),
            (VL.Brand, new GridQuery.ColumnFilter("t.brand", filter.Brand)),
            (VL.Model, new GridQuery.ColumnFilter("t.model", filter.Model)),
            (VL.Branch, new GridQuery.ColumnFilter("t.branch", filter.Branch)),
            (VL.Driver, new GridQuery.ColumnFilter("t.driver", filter.Driver)),
            (VL.ChassisNo, new GridQuery.ColumnFilter("t.chassis_no", filter.ChassisNo)),
            (VL.EngineNo, new GridQuery.ColumnFilter("t.engine_no", filter.EngineNo)),
        };
        var cols = System.Array.ConvertAll(byKey, x => x.Col);
        GridQuery.ColumnFilter? sort = null;
        if (sortColumn is not null)
            foreach (var x in byKey) if (x.Key == sortColumn) { sort = x.Col; break; }
        using var conn = _factory.Create();
        var (whereSql, orderSql, ps) = GridQuery.Build(cols, "t.internal_code", sort, sortDesc, SqlDialect.IsSqlite(conn));
        // ŞUBE KAPSAMI: belirli şubeyle girişte yalnız o şubenin (+ şubesiz eski kayıtların) araçları; "Tüm Şubeler" → hepsi.
        var inner = SqlDialect.PortableSql(conn, GridInnerSql) + BranchScope.Sql(s, "v.branch_id");

        int total;
        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM ({inner}) t {whereSql};";
            cnt.AddWithValue("@c", s.CompanyId);
            if (BranchScope.Active(s) is { } b0) cnt.AddWithValue("@opb", b0);
            GridQuery.AddParams(cnt, ps);
            total = Convert.ToInt32(cnt.ExecuteScalar());
        }

        var items = new List<VehicleGridRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT * FROM ({inner}) t {whereSql}{orderSql}LIMIT @lim OFFSET @off;";
            cmd.AddWithValue("@c", s.CompanyId);
            if (BranchScope.Active(s) is { } b1) cmd.AddWithValue("@opb", b1);
            GridQuery.AddParams(cmd, ps);
            cmd.AddWithValue("@lim", pageSize);
            cmd.AddWithValue("@off", (page - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                items.Add(new VehicleGridRow(
                    r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? (int?)null : r.GetInt32(3), Money.Parse(r.GetString(4)), r.GetString(5),
                    r.GetString(7), r.GetString(8), r.GetString(9),
                    r.GetString(10), r.GetString(11), r.GetString(12), r.GetString(13), r.GetString(14),
                    r.GetString(15), r.GetString(16), r.GetString(17)));
        }
        return new GridResult<VehicleGridRow>(items, total, page, pageSize);
    }

    /// <summary>Filtrelenmiş/sıralanmış TÜM sonuçları (sayfalama sınırı YOK) döner — "Excel'e Aktar" butonu
    /// için (bkz. MaterialService.SearchGridAll — aynı desen). SearchGrid'i 500'lük sayfalarla gezer.</summary>
    public IReadOnlyList<VehicleGridRow> SearchGridAll(SessionContext s, VehicleGridFilter filter, string? sortColumn = null, bool sortDesc = false)
    {
        var all = new System.Collections.Generic.List<VehicleGridRow>();
        int page = 1;
        while (true)
        {
            var res = SearchGrid(s, filter, page, 500, sortColumn, sortDesc);
            all.AddRange(res.Items);
            if (page >= res.TotalPages || res.Items.Count == 0) break;
            page++;
        }
        return all;
    }

    /// <summary>Grid satırlarını Excel tablosuna çevirir — kolon sırası <see cref="VL"/>.All ile AYNIDIR
    /// (bkz. MaterialService.ToTableModel — aynı desen). Sayaç "değer birim" olarak tek metne birleşir.</summary>
    public static Application.Reports.TableModel ToTableModel(System.Collections.Generic.IReadOnlyList<VehicleGridRow> rows)
    {
        var headers = VL.All.Select(c => c.Label).ToList();
        var body = rows.Select(r => (System.Collections.Generic.IReadOnlyList<object?>)new object?[]
        {
            r.InternalCode, r.Plate, r.ProductionYear, $"{r.Meter} {r.MeterUnit}".Trim(), r.StatusLabel, r.StatusNote,
            r.VehicleType, r.Category, r.Brand, r.Model, r.Branch, r.Driver, r.ChassisNo, r.EngineNo,
        }).ToList();
        return new Application.Reports.TableModel("Araçlar", headers, body);
    }

    /// <summary>Tek araç detayı (salt okuma) — düzenleme formu için.</summary>
    public VehicleDetail Get(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT v.id, v.internal_code, v.plate, v.production_year, v.current_meter, v.meter_unit, v.status, v.status_note,
       v.chassis_no, v.engine_no,
       v.vehicle_type_id, v.category_id, v.brand_id, v.vehicle_model_id, v.branch_id, v.driver_personnel_id,
       vt.name, vc.name, b.name, vm.name, br.name, p.full_name, v.version, v.template_id
FROM vehicles v
LEFT JOIN vehicle_types vt ON vt.id = v.vehicle_type_id AND vt.company_id = v.company_id
LEFT JOIN vehicle_categories vc ON vc.id = v.category_id AND vc.company_id = v.company_id
LEFT JOIN brands b ON b.id = v.brand_id
LEFT JOIN vehicle_models vm ON vm.id = v.vehicle_model_id
LEFT JOIN branches br ON br.id = v.branch_id AND br.company_id = v.company_id
LEFT JOIN personnel p ON p.id = v.driver_personnel_id AND p.company_id = v.company_id
WHERE v.id=@id AND v.company_id=@c AND v.is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        return new VehicleDetail(
            r.GetString(0), r.GetString(1), S(2),
            r.IsDBNull(3) ? (int?)null : r.GetInt32(3), Money.Parse(r.GetString(4)), r.GetString(5),
            r.GetString(6), S(7), S(8), S(9),
            S(10), S(11), S(12), S(13), S(14), S(15),
            S(16), S(17), S(18), S(19), S(20), S(21), r.GetInt64(22), S(23));
    }

    /// <summary>Araç alanlarını günceller (plaka/yıl/durum/durum notu). Sayaç burada DEĞİL (SetMeter ile, geriye gitmez).
    /// Durum notu yalnız 'Bakımda' / 'Arızalı' durumunda saklanır (Create ile aynı kural).</summary>
    /// <param name="expectedVersion">DÜZENLEME KİLİDİ: formun açıldığı andaki <c>version</c>. Verilirse ve kayıt
    /// o andan beri değiştiyse <see cref="ConcurrencyException"/> atılır. null = kontrol yok (geriye uyumlu).</param>
    public void Update(SessionContext s, string vehicleId, UpdateVehicle dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Plaka benzersiz (kullanıcı isteği 2026-08-05) — kendi kaydı hariç aynı firmada başka araçta olamaz.
        if (!string.IsNullOrWhiteSpace(dto.Plate) && PlateExists(conn, tx, s.CompanyId, dto.Plate!, excludeId: vehicleId))
            throw new InvalidOperationException($"Bu plaka zaten kayıtlı: {dto.Plate}");

        // İşlem Geçmişi (madde 4/1, 2026-08-06): şube DEĞİŞİYORSA (transfer) audit kaydına isim bilgisiyle
        // yazılır → geçmiş listesinde "X Şubesinden Y Şubesine transfer edildi." Değişmiyorsa normal güncelleme.
        // B-7: düzenlemede de şube/sürücü firmaya ait olmalı (bkz. Create).
        EnsureBranchOwned(conn, tx, s.CompanyId, dto.BranchId);
        EnsurePersonnelOwned(conn, tx, s.CompanyId, dto.DriverPersonnelId);

        string? oldBranchId;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT branch_id FROM vehicles WHERE id=@id AND company_id=@c;";
            read.AddWithValue("@id", vehicleId);
            read.AddWithValue("@c", s.CompanyId);
            oldBranchId = read.ExecuteScalar() as string;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE vehicles SET plate=@p, production_year=@y, status=@st, status_note=@note,
    chassis_no=@ch, engine_no=@en, vehicle_type_id=@vt, category_id=@cat,
    brand_id=@brand, vehicle_model_id=@vm, branch_id=@br, driver_personnel_id=@drv, template_id=@tpl,
    version=version+1, updated_at=@now
WHERE id=@id AND company_id=@c AND is_deleted=0" + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.AddWithValue("@p", (object?)dto.Plate ?? DBNull.Value);
            cmd.AddWithValue("@y", (object?)dto.ProductionYear ?? DBNull.Value);
            cmd.AddWithValue("@st", dto.Status);
            cmd.AddWithValue("@note", DepoWise.Application.Ui.VehicleStatus.NeedsNote(dto.Status) ? (object?)dto.StatusNote ?? DBNull.Value : DBNull.Value);
            cmd.AddWithValue("@ch", (object?)dto.ChassisNo ?? DBNull.Value);
            cmd.AddWithValue("@en", (object?)dto.EngineNo ?? DBNull.Value);
            cmd.AddWithValue("@vt", (object?)dto.VehicleTypeId ?? DBNull.Value);
            cmd.AddWithValue("@cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.AddWithValue("@brand", (object?)dto.BrandId ?? DBNull.Value);
            cmd.AddWithValue("@vm", (object?)dto.VehicleModelId ?? DBNull.Value);
            cmd.AddWithValue("@br", (object?)dto.BranchId ?? DBNull.Value);
            cmd.AddWithValue("@drv", (object?)dto.DriverPersonnelId ?? DBNull.Value);
            cmd.AddWithValue("@tpl", (object?)dto.TemplateId ?? DBNull.Value);   // düzenlemede şablona bağla/koru
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "vehicles", vehicleId, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
            }
        }
        string? afterJson = null;
        if (!string.Equals(oldBranchId, dto.BranchId, StringComparison.Ordinal))
        {
            var fromName = BranchName(conn, tx, s.CompanyId, oldBranchId);
            var toName = BranchName(conn, tx, s.CompanyId, dto.BranchId);
            afterJson = $"{{\"branchFromId\":{JsonStr(oldBranchId)},\"branchFromName\":{JsonStr(fromName)}," +
                        $"\"branchToId\":{JsonStr(dto.BranchId)},\"branchToName\":{JsonStr(toName)}}}";
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId,
            AfterJson: afterJson), _clock);
        tx.Commit();
    }

    /// <summary>
    /// YALNIZ durum + durum notunu günceller (bakım ekranı: "bu araç arızalı" işaretlemek için).
    /// Update() ile karıştırılmamalı: Update TÜM alanları yazar → bakım ekranından çağrılsa marka/model/şube
    /// gibi doldurulmamış alanları NULL'a çekerdi. Bu metot araç kartının geri kalanına DOKUNMAZ.
    /// Not, yalnız "çalışmıyor" durumlarında (Bakımda/Arızalı) saklanır — diğer durumlarda temizlenir.
    /// </summary>
    public void SetStatus(SessionContext s, string vehicleId, string status, string? statusNote = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(status)) throw new ArgumentException("Durum zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE vehicles SET status=@st, status_note=@note, version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@st", status);
            cmd.AddWithValue("@note",
                DepoWise.Application.Ui.VehicleStatus.NeedsNote(status) ? (object?)statusNote ?? DBNull.Value : DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Araç soft-delete (is_deleted=1). Geçmiş kayıtlar korunur.</summary>
    public void Delete(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vehicles SET is_deleted=1, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>İşlem Geçmişi (madde 4, 2026-08-06): audit_logs (oluşturma/şube transferi/genel güncelleme/silme)
    /// + vehicle_meter_logs (sayaç değişimi; kaynağa göre yakıt/bakım/manuel etiketlenir) birleştirilip tarihe
    /// göre azalan sıralanır. Salt-okunur; malzeme kartındaki "Son Hareketler"in araç karşılığı.</summary>
    public IReadOnlyList<VehicleHistoryRow> RecentHistory(SessionContext s, string vehicleId, int take = 100)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (take < 1) take = 1; if (take > 300) take = 300;
        using var conn = _factory.Create();
        EnsureVehicleOwned(conn, s.CompanyId, vehicleId);

        var rows = new List<VehicleHistoryRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT a.created_at, a.action, a.after_json
FROM audit_logs a
WHERE a.company_id=@c AND a.entity_type='vehicle' AND a.entity_id=@v
ORDER BY a.created_at DESC LIMIT @lim;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@v", vehicleId);
            cmd.AddWithValue("@lim", take);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var action = r.GetString(1);
                var afterJson = r.IsDBNull(2) ? null : r.GetString(2);
                rows.Add(new VehicleHistoryRow(r.GetInt64(0), AuditLabel(action, afterJson), null));
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT created_at, old_value, new_value, source
FROM vehicle_meter_logs
WHERE company_id=@c AND vehicle_id=@v AND source <> 'vehicle_create'
ORDER BY created_at DESC LIMIT @lim;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@v", vehicleId);
            cmd.AddWithValue("@lim", take);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var oldV = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
                var newV = Money.Parse(r.IsDBNull(2) ? null : r.GetString(2));
                var source = r.GetString(3);
                rows.Add(new VehicleHistoryRow(r.GetInt64(0), MeterLabel(source), $"{oldV:0.##} → {newV:0.##}"));
            }
        }
        return rows.OrderByDescending(x => x.Date).Take(take).ToList();
    }

    private static string AuditLabel(string action, string? afterJson)
    {
        if (action == AuditActions.Create) return "Araç oluşturuldu.";
        if (action == AuditActions.Delete) return "Araç silindi (çöp kutusuna alındı).";
        if (action == AuditActions.Update)
        {
            if (!string.IsNullOrEmpty(afterJson))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(afterJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("branchToId", out _))
                    {
                        var from = root.TryGetProperty("branchFromName", out var f) && f.ValueKind == System.Text.Json.JsonValueKind.String ? f.GetString() : null;
                        var to = root.TryGetProperty("branchToName", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString() : null;
                        return (string.IsNullOrEmpty(from) ? "Şube atanmamış durumdan" : $"{from} Şubesinden") +
                               " " + (string.IsNullOrEmpty(to) ? "şube ataması kaldırıldı." : $"{to} Şubesine transfer edildi.");
                    }
                }
                catch { /* json bozuksa genel metne düş */ }
            }
            return "Araç bilgileri güncellendi.";
        }
        return "Araç işlemi.";
    }

    private static string MeterLabel(string source) => source switch
    {
        "fuel_distribution" => "Sayaç güncellendi (Yakıt dağıtımı)",
        "maintenance" => "Sayaç güncellendi (Bakım)",
        _ => "Sayaç güncellendi (Manuel)"
    };

    private static void EnsureVehicleOwned(DbConnection conn, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
    }

    private static string? BranchName(DbConnection conn, DbTransaction tx, string companyId, string? branchId)
    {
        if (string.IsNullOrEmpty(branchId)) return null;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT name FROM branches WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", branchId);
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() as string;
    }

    private static string JsonStr(string? v)
        => v is null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // ---- yardımcılar ----
    private NewVehicle ApplyTemplate(DbConnection conn, DbTransaction tx, string companyId, NewVehicle dto)
    {
        if (dto.TemplateId is null) return dto;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT vehicle_type_id, category_id, brand_id, vehicle_model_id, production_year, default_meter_unit
FROM vehicle_templates WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", dto.TemplateId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
        // Kullanıcı değeri öncelikli (?? ile yalnız boş alanlar doldurulur)
        return dto with
        {
            VehicleTypeId = dto.VehicleTypeId ?? (r.IsDBNull(0) ? null : r.GetString(0)),
            CategoryId = dto.CategoryId ?? (r.IsDBNull(1) ? null : r.GetString(1)),
            BrandId = dto.BrandId ?? (r.IsDBNull(2) ? null : r.GetString(2)),
            VehicleModelId = dto.VehicleModelId ?? (r.IsDBNull(3) ? null : r.GetString(3)),
            ProductionYear = dto.ProductionYear ?? (r.IsDBNull(4) ? (int?)null : r.GetInt32(4)),
            MeterUnit = dto.MeterUnit == "km" && !r.IsDBNull(5) ? r.GetString(5) : dto.MeterUnit,
        };
    }

    private static void CopyTemplateMaterials(DbConnection conn, DbTransaction tx, string templateId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO material_compatible_vehicles(material_id, vehicle_id) " +
            "SELECT material_id, @v FROM vehicle_template_materials WHERE template_id=@t ON CONFLICT DO NOTHING;";
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@t", templateId);
        cmd.ExecuteNonQuery();
    }

    private static decimal ReadMeter(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT current_meter FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        if (v is null) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        return Money.Parse(v as string);
    }

    private static void UpdateMeter(DbConnection conn, DbTransaction tx, string vehicleId, decimal value, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE vehicles SET current_meter=@m, version=version+1, updated_at=@now WHERE id=@id;";
        cmd.AddWithValue("@m", Money.Serialize(value));
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@id", vehicleId);
        cmd.ExecuteNonQuery();
    }

    private static void WriteMeterLog(DbConnection conn, DbTransaction tx, string companyId, string vehicleId,
        decimal oldVal, decimal newVal, string source, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES(@id,@c,@v,@o,@n,@src,@now);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", vehicleId);
        cmd.AddWithValue("@o", Money.Serialize(oldVal));
        cmd.AddWithValue("@n", Money.Serialize(newVal));
        cmd.AddWithValue("@src", source);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static bool CodeExists(DbConnection conn, DbTransaction tx, string companyId, string code)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE company_id=@c AND internal_code=@ic;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@ic", code.Trim());
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    // Plaka benzersizlik kontrolü (silinmiş araçlar hariç; excludeId=düzenlenen kayıt). Yalnız interaktif
    // create/update yolunda çağrılır — sync upsert bu kontrole girmez (offline çakışma sistemi çökertmez).
    private static bool PlateExists(DbConnection conn, DbTransaction tx, string companyId, string plate, string? excludeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE company_id=@c AND is_deleted=0 AND plate=@p" +
                          (excludeId is null ? ";" : " AND id<>@ex;");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@p", plate.Trim());
        if (excludeId is not null) cmd.AddWithValue("@ex", excludeId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>B-7: şube bu firmaya ait mi? null = şubesiz kayıt (serbest).</summary>
    private static void EnsureBranchOwned(DbConnection conn, DbTransaction? tx, string companyId, string? branchId)
        => EnsureOwned(conn, tx, "branches", companyId, branchId, "Şube bulunamadı veya başka firmaya ait.");

    /// <summary>B-7: personel bu firmaya ait mi? null = sürücü atanmamış (serbest).</summary>
    private static void EnsurePersonnelOwned(DbConnection conn, DbTransaction? tx, string companyId, string? personnelId)
        => EnsureOwned(conn, tx, "personnel", companyId, personnelId, "Personel bulunamadı veya başka firmaya ait.");

    /// <summary>B-7: istemciden gelen bir id'nin firmaya ait olduğunu doğrular (B-2/B-3 deseni).
    /// Tablo adı YALNIZ bu sınıftaki sabitlerden gelir — dışarıdan parametre alınmaz.</summary>
    private static void EnsureOwned(DbConnection conn, DbTransaction? tx, string table,
        string companyId, string? id, string error)
    {
        if (string.IsNullOrEmpty(id)) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException(error);
    }
}
