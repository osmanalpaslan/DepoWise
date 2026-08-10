using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Maintenance;
using System.Data.Common;

namespace DepoWise.Infrastructure.Operations;

/// <summary>"Kayıt Tipi" seçenekleri — bakımla AYNI mekanizma (ortak MaintenanceService), yalnız "Bakım
/// Tanımı"/"Alt Bakım" alanları YOK (kullanıcı isteği 2026-07-19). Her tür, firma başına OTOMATİK oluşan
/// (IntervalValue=0 — asla "bakım vadesi geldi" uyarısı üretmez) sabit bir maintenance_definitions satırına
/// bağlanır; kullanıcı bunu hiç görmez/seçmez.</summary>
public static class ExtraActivityTypes
{
    public const string ExtraOil = "extra_oil";
    public const string ExtraFilter = "extra_filter";
    public const string Repair = "repair";

    public static readonly IReadOnlyDictionary<string, string> DefinitionNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ExtraOil] = "İlave Yağ",
        [ExtraFilter] = "İlave Filtre",
        [Repair] = "Tamir",
    };

    public static bool IsValid(string? type) => type is not null && DefinitionNames.ContainsKey(type);
}

public sealed record NewMovementActivity(
    string MovementKind, string? VehicleId = null, string? FromLocationId = null, string? ToLocationId = null,
    string? OperatorId = null, int? DurationDays = null, string? Description = null, long? ActivityDate = null);

public sealed record DailyActivityRecord(string Id, string ActivityType, string? MovementKind, string? VehicleId,
    string? MaintenanceId, string? Description, long ActivityDate);

public sealed record DailyActivityListRow(string Id, string ActivityType, string? MovementKind,
    string? VehicleCode, string? VehiclePlate, string? FromLocation, string? ToLocation, string? Operator,
    int? DurationDays, string? Description, long ActivityDate, string? MaintenanceId)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(ActivityDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string TypeText => ActivityType switch
    {
        "maintenance" => "Bakım",
        "extra_oil" => "İlave Yağ",
        "extra_filter" => "İlave Filtre",
        "repair" => "Tamir",
        _ => MovementKind == "transfer" ? "Transfer" : "Hareket",
    };
    public string VehicleText => string.IsNullOrEmpty(VehicleCode) ? "—"
        : string.IsNullOrEmpty(VehiclePlate) ? VehicleCode! : $"{VehicleCode} - {VehiclePlate}";
    public string RouteText => (FromLocation, ToLocation) switch
    {
        (null, null) => "—",
        (var f, null) => f ?? "—",
        (null, var t) => "→ " + t,
        var (f, t) => $"{f} → {t}"
    };
    public string OperatorText => string.IsNullOrEmpty(Operator) ? "—" : Operator!;
    public string DurationText => DurationDays is null ? "—" : $"{DurationDays} gün";
    public string DescriptionText => string.IsNullOrWhiteSpace(Description) ? "—" : Description!;
}

/// <summary>Günlük Faaliyet listesi (kolon-bazlı filtre + sayfalama) satırı — <see cref="DailyActivityListColumns"/>'taki
/// HER kolonun görüntü değerini taşır. "Tarih" ham zaman damgasından (<see cref="DateRaw"/>) yerel tarihe çevrilir.
/// Hesaplanan *Text alanları <see cref="DailyActivityListRow"/> ile AYNI adlandırılır — masaüstü ekranı
/// (DailyActivityView.axaml) satır şablonu hiç değişmeden bu tipe geçebilsin diye (kullanıcı isteği 2026-07-19).</summary>
public sealed record DailyActivityGridRow(
    string Id, long DateRaw, string Type, string Vehicle, string Route, string Operator, string Duration,
    string Description, string? MaintenanceId, bool IsCancelled = false,
    long Version = 0, string? OperatorId = null, int? DurationDays = null)   // İş #5: metadata düzenleme formu + kilit
{
    /// <summary>İptal edilen faaliyet listede ayırt edilir (kullanıcı kararı K3).</summary>
    public string StatusText => IsCancelled ? "İptal edildi" : "";
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(DateRaw).LocalDateTime.ToString("dd.MM.yyyy");
    public string TypeText => Dash(Type);
    public string VehicleText => Dash(Vehicle);
    public string RouteText => Dash(Route);
    public string OperatorText => Dash(Operator);
    public string DurationText => Dash(Duration);
    public string DescriptionText => Dash(Description);
    private static string Dash(string s) => string.IsNullOrEmpty(s) ? "—" : s;
}

/// <summary>Her alan için kullanıcının o kolona yazdığı filtre metni. "Tarih" bilinçli olarak burada YOK
/// (bkz. <see cref="DailyActivityListColumns"/> açıklaması — yalnız başlığa tıklayarak sıralanır).</summary>
public sealed record DailyActivityGridFilter(
    string? Type = null, string? Vehicle = null, string? Route = null, string? Operator = null,
    string? Duration = null, string? Description = null);

/// <summary>
/// Günlük faaliyet — bakım tipi ORTAK MaintenanceService'i kullanır: TEK bakım kaydı + TEK stok düşümü;
/// daily_activities yalnız REFERANS tutar (stok_processed=1, burada stok DÜŞMEZ). Aynı veri iki ekranda görünür.
/// Hareket/transfer tipi: transfer aracı otomatik pasife alır (ileri-yön).
/// </summary>
public sealed class DailyActivityService
{
    private const string Module = "daily_activity";
    private readonly IDbConnectionFactory _factory;
    private readonly MaintenanceService _maintenance;
    private readonly MaintenanceDefinitionService? _definitions;
    private readonly IClock _clock;

    /// <summary><paramref name="definitions"/> OPSİYONELDİR (geriye uyumlu — mevcut çağrı yerlerinin
    /// pozisyonel <c>clock</c> argümanı bozulmasın diye SONA eklendi): yalnız yeni "İlave Yağ/İlave
    /// Filtre/Tamir" türleri (<see cref="SaveExtraActivity"/>) için gerekir; verilmezse o metot çağrılamaz.</summary>
    public DailyActivityService(IDbConnectionFactory factory, MaintenanceService maintenance,
        IClock? clock = null, MaintenanceDefinitionService? definitions = null)
    {
        _factory = factory;
        _maintenance = maintenance;
        _definitions = definitions;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Bakım tipi: ortak bakım servisinde tek kayıt üretir; günlük faaliyet referansı ekler. Çift bakım YOK.</summary>
    public string SaveMaintenanceActivity(SessionContext s, NewMaintenance maintenance, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        // Günlük faaliyet zaten varsa idempotent
        var existing = FindActivity(operationId);
        if (existing is not null) return existing;

        // TEK bakım kaydı + TEK stok düşümü (MaintenanceService kendi transaction'ında)
        var maintenanceId = _maintenance.Save(s, maintenance, operationId + ":mnt");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        InsertActivity(conn, tx, id, s.CompanyId, "maintenance", null, maintenance.VehicleId, null, null, null, null,
            maintenance.Description, maintenanceId, stockProcessed: true, maintenance.PerformedDate ?? now, operationId, now, s.OperatingBranchId);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "daily_activity", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>
    /// "İlave Yağ / İlave Filtre / Tamir" (kullanıcı isteği 2026-07-19): "Bakım" ile TAM AYNI mekanizma
    /// (ortak MaintenanceService — sayaç/malzeme stok düşümü dahil), yalnız "Bakım Tanımı"/"Alt Bakım"
    /// kullanıcıya HİÇ sorulmaz — her tür firma başına otomatik oluşan sabit bir maintenance_definitions
    /// satırına (IntervalValue=0 → asla vade uyarısı üretmez) bağlanır.
    /// </summary>
    public string SaveExtraActivity(SessionContext s, string extraType, NewMaintenance dto, string operationId)
    {
        if (!ExtraActivityTypes.IsValid(extraType)) throw new ArgumentException("Geçersiz kayıt tipi.");
        if (_definitions is null) throw new InvalidOperationException("MaintenanceDefinitionService bağlı değil.");
        AccessControl.Require(s, Module, PermissionAction.Create);
        var existing = FindActivity(operationId);
        if (existing is not null) return existing;

        var defId = EnsureExtraDefinition(s, extraType);
        var withDef = dto with { DefinitionId = defId, SubDefinitionId = null, SubDefinitionNote = null };
        var maintenanceId = _maintenance.Save(s, withDef, operationId + ":mnt");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        InsertActivity(conn, tx, id, s.CompanyId, extraType, null, dto.VehicleId, null, null, null, null,
            dto.Description, maintenanceId, stockProcessed: true, dto.PerformedDate ?? now, operationId, now, s.OperatingBranchId);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "daily_activity", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Bu firma için o türün SABİT tanımını bulur, yoksa oluşturur (idempotent, harf duyarsız).
    /// Kullanıcı bu tanımı hiç görmez/seçmez — arayüzde "Bakım Tanımı" alanı bu türlerde gösterilmez.
    ///
    /// ⚠️ ATOMİK yoksa-oluştur (Opus incelemesi 2026-07-19): eskiden "önce SELECT, yoksa Create" ayrı adımdı;
    /// AYNI firmada AYNI türün İLK kaydını iki kullanıcı sunucuda eşzamanlı girerse ikisi de "yok" görüp İKİ
    /// sabit tanım oluşturabiliyordu (masaüstü tek-kullanıcı olduğundan etkilenmez; sunucu çok-istekli).
    /// Çözüm: TEK <c>INSERT ... SELECT ... WHERE NOT EXISTS</c> ifadesi — SQLite yazarları seri hale getirir
    /// (busy_timeout), ikinci istek NOT EXISTS'i yeniden değerlendirip 0 satır ekler. Yetki: bu metot yalnız
    /// <see cref="SaveExtraActivity"/>'den çağrılır; orada daily/Create + <c>_maintenance.Save</c> maintenance/Create
    /// zaten zorlanır — bu yüzden burada ayrı bir izin kontrolü gerekmez (ledger/stok yolu Save'de korunur).</summary>
    private string EnsureExtraDefinition(SessionContext s, string extraType)
    {
        var name = ExtraActivityTypes.DefinitionNames[extraType];
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var newId = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            // interval_value '0' + interval_unit 'km' → AlertRules.Progress(interval<=0)=0 → asla vade uyarısı.
            ins.CommandText = @"
INSERT INTO maintenance_definitions(id, company_id, parent_def_id, name, interval_value, interval_unit,
    description, created_at, updated_at, version, is_deleted)
SELECT @id, @c, NULL, @n, '0', 'km', NULL, @now, @now, 1, 0
WHERE NOT EXISTS (SELECT 1 FROM maintenance_definitions
    WHERE company_id=@c AND name=@n COLLATE NOCASE AND parent_def_id IS NULL AND is_deleted=0);";
            ins.AddWithValue("@id", newId);
            ins.AddWithValue("@c", s.CompanyId);
            ins.AddWithValue("@n", name);
            ins.AddWithValue("@now", now);
            if (ins.ExecuteNonQuery() > 0)
                AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "maintenance_definition", newId, AuditActions.Create, s.UserId), _clock);
        }
        string? id;
        using (var find = conn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id FROM maintenance_definitions WHERE company_id=@c AND name=@n COLLATE NOCASE " +
                                "AND parent_def_id IS NULL AND is_deleted=0 ORDER BY created_at LIMIT 1;";
            find.AddWithValue("@c", s.CompanyId);
            find.AddWithValue("@n", name);
            id = find.ExecuteScalar() as string;
        }
        tx.Commit();
        return id ?? newId;
    }

    /// <summary>Hareket/transfer kaydı. Transfer → araç otomatik pasif (yalnız aktifse; ileri-yön).</summary>
    public string SaveMovement(SessionContext s, NewMovementActivity dto, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.MovementKind is not ("movement" or "transfer")) throw new ArgumentException("Geçersiz hareket tipi.");
        var existing = FindActivity(operationId);
        if (existing is not null) return existing;

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        // B-3 (PRT-01 Grup 5, 2026-08-10): araç id'si İSTEMCİDEN gelir → firmaya ait olduğu doğrulanır.
        // Bakım ve "ilave" akışları bu korumayı MaintenanceService.Save üzerinden ZATEN alıyor; hareket/transfer
        // akışı ise araç id'sini doğrudan yazıyordu → yabancı araca REFERANS veren kayıt oluşabiliyordu.
        // (Transfer'in aracı pasife alan UPDATE'i zaten firma süzgeçliydi; boşluk yalnız bu referanstaydı.)
        if (dto.VehicleId is not null) EnsureVehicleOwned(conn, tx, s.CompanyId, dto.VehicleId);
        InsertActivity(conn, tx, id, s.CompanyId, "movement", dto.MovementKind, dto.VehicleId, dto.FromLocationId,
            dto.ToLocationId, dto.OperatorId, dto.DurationDays, dto.Description, null, stockProcessed: false,
            dto.ActivityDate ?? now, operationId, now, s.OperatingBranchId);

        if (dto.MovementKind == "transfer" && dto.VehicleId is not null)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE vehicles SET status='passive', version=version+1, updated_at=@now " +
                "WHERE id=@v AND company_id=@c AND status<>'passive';";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@v", dto.VehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "daily_activity", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    public IReadOnlyList<DailyActivityRecord> GetForVehicle(SessionContext s, string vehicleId, string? activityType = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, activity_type, movement_kind, vehicle_id, maintenance_id, description, activity_date " +
            "FROM daily_activities WHERE company_id=@c AND vehicle_id=@v AND is_deleted=0 " +
            (activityType is null ? "" : "AND activity_type=@t ") +
            "ORDER BY activity_date DESC;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@v", vehicleId);
        if (activityType is not null) cmd.AddWithValue("@t", activityType);
        var list = new List<DailyActivityRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DailyActivityRecord(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetInt64(6)));
        return list;
    }

    /// <summary>Tüm günlük faaliyetler (salt okuma) — araç/şube/operatör adlarıyla. Tür filtresi opsiyonel.</summary>
    public IReadOnlyList<DailyActivityListRow> List(SessionContext s, string? activityType = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT da.id, da.activity_type, da.movement_kind, v.internal_code, v.plate,
       fb.name, tb.name, p.full_name, da.duration_days, da.description, da.activity_date, da.maintenance_id
FROM daily_activities da
LEFT JOIN vehicles v ON v.id = da.vehicle_id AND v.company_id = da.company_id
LEFT JOIN branches fb ON fb.id = da.from_location_id
LEFT JOIN branches tb ON tb.id = da.to_location_id
LEFT JOIN personnel p ON p.id = da.operator_id
WHERE da.company_id = @c AND da.is_deleted = 0" + BranchScope.Sql(s, "da.op_branch_id") + @"
  AND (CAST(@t AS TEXT) IS NULL OR da.activity_type = @t)
ORDER BY da.activity_date DESC, da.created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        if (BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        cmd.AddWithValue("@t", (object?)activityType ?? DBNull.Value);
        cmd.AddWithValue("@lim", limit);
        string? S(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        var list = new List<DailyActivityListRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DailyActivityListRow(r.GetString(0), r.GetString(1), S(r, 2),
                S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7),
                r.IsDBNull(8) ? (int?)null : r.GetInt32(8), S(r, 9), r.GetInt64(10), S(r, 11)));
        return list;
    }

    private const string GridInnerSql = @"
SELECT da.id AS id,
       da.activity_date AS date_raw,
       CASE da.activity_type
         WHEN 'maintenance' THEN 'Bakım'
         WHEN 'extra_oil' THEN 'İlave Yağ'
         WHEN 'extra_filter' THEN 'İlave Filtre'
         WHEN 'repair' THEN 'Tamir'
         ELSE CASE WHEN da.movement_kind='transfer' THEN 'Transfer' ELSE 'Hareket' END
       END AS type_text,
       CASE WHEN v.internal_code IS NULL THEN ''
            WHEN v.plate IS NULL OR v.plate='' THEN v.internal_code
            ELSE v.internal_code || ' - ' || v.plate END AS vehicle_text,
       CASE WHEN fb.name IS NULL AND tb.name IS NULL THEN ''
            WHEN tb.name IS NULL THEN fb.name
            WHEN fb.name IS NULL THEN '→ ' || tb.name
            ELSE fb.name || ' → ' || tb.name END AS route_text,
       COALESCE(p.full_name, '') AS operator_text,
       CASE WHEN da.duration_days IS NULL THEN '' ELSE CAST(da.duration_days AS TEXT) || ' gün' END AS duration_text,
       COALESCE(da.description, '') AS description,
       da.maintenance_id AS maintenance_id,
       da.is_deleted AS is_cancelled,
       da.version AS row_version,
       da.operator_id AS operator_id,
       da.duration_days AS duration_days
FROM daily_activities da
LEFT JOIN vehicles v ON v.id = da.vehicle_id AND v.company_id = da.company_id
LEFT JOIN branches fb ON fb.id = da.from_location_id
LEFT JOIN branches tb ON tb.id = da.to_location_id
LEFT JOIN personnel p ON p.id = da.operator_id
WHERE da.company_id = @c";

    /// <summary>Kolon bazlı filtre + numaralı sayfalama + sıralama + Excel'e aktar (kullanıcı isteği
    /// 2026-07-19: Malzemeler/Araçlar'a yapılan geliştirmenin AYNISI — ADR-087/088/089 deseni).
    /// "Tarih" YALNIZ sıralanır (bkz. <see cref="DailyActivityGridFilter"/>), filtre kutusu yoktur.</summary>
    /// <param name="includeCancelled">K3: varsayılan GİZLİ; true ise iptal edilen faaliyetler de gelir.</param>
    public GridResult<DailyActivityGridRow> SearchGrid(SessionContext s, DailyActivityGridFilter filter, int page, int pageSize,
        string? sortColumn = null, bool sortDesc = false, bool includeCancelled = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        var byKey = new (string Key, GridQuery.ColumnFilter Col)[]
        {
            (DailyActivityListColumns.Date, new GridQuery.ColumnFilter("t.date_raw", null, GridQuery.ColumnKind.Numeric, "t.date_raw")),
            (DailyActivityListColumns.Type, new GridQuery.ColumnFilter("t.type_text", filter.Type)),
            (DailyActivityListColumns.Vehicle, new GridQuery.ColumnFilter("t.vehicle_text", filter.Vehicle)),
            (DailyActivityListColumns.Route, new GridQuery.ColumnFilter("t.route_text", filter.Route)),
            (DailyActivityListColumns.Operator, new GridQuery.ColumnFilter("t.operator_text", filter.Operator)),
            (DailyActivityListColumns.Duration, new GridQuery.ColumnFilter("t.duration_text", filter.Duration)),
            (DailyActivityListColumns.Description, new GridQuery.ColumnFilter("t.description", filter.Description)),
        };
        var cols = System.Array.ConvertAll(byKey, x => x.Col);
        GridQuery.ColumnFilter? sort = null;
        if (sortColumn is not null)
            foreach (var x in byKey) if (x.Key == sortColumn) { sort = x.Col; break; }
        using var conn = _factory.Create();
        var (whereSql, orderSql, ps) = GridQuery.Build(cols, "t.id", sort, sortDesc, SqlDialect.IsSqlite(conn));
        // Varsayılan sıra (kullanıcı başlığa tıklamadıysa): en yeni faaliyet üstte — mevcut List() davranışıyla
        // AYNI (bu ekran bir kronolojik günlük; Malzemeler/Araçlar'daki "filtrelerin doldurulma sırası"
        // önceliği burada anlamsız — tarih her zaman kazanır).
        if (sort is null) orderSql = "ORDER BY t.date_raw DESC, t.id ";
        // ŞUBE KAPSAMI: belirli şubeyle girişte yalnız o şubede işlenen (+ şubesiz eski) faaliyetler; Tüm Şubeler → hepsi.
        var inner = GridInnerSql + (includeCancelled ? "" : " AND da.is_deleted = 0") + BranchScope.Sql(s, "da.op_branch_id");

        int total;
        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM ({inner}) t {whereSql};";
            cnt.AddWithValue("@c", s.CompanyId);
            if (BranchScope.Active(s) is { } b0) cnt.AddWithValue("@opb", b0);
            GridQuery.AddParams(cnt, ps);
            total = Convert.ToInt32(cnt.ExecuteScalar());
        }

        var items = new List<DailyActivityGridRow>();
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
                items.Add(new DailyActivityGridRow(
                    r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3),
                    r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                    r.IsDBNull(8) ? null : r.GetString(8), r.GetInt64(9) == 1,
                    // İş #5: düzenleme formu + düzenleme kilidi için sürüm ve ham id/süre.
                    r.IsDBNull(10) ? 0L : Convert.ToInt64(r.GetValue(10)),
                    r.IsDBNull(11) ? null : r.GetString(11),
                    r.IsDBNull(12) ? null : (int?)Convert.ToInt32(r.GetValue(12))));
        }
        return new GridResult<DailyActivityGridRow>(items, total, page, pageSize);
    }

    /// <summary>Filtrelenmiş/sıralanmış TÜM sonuçları (sayfalama sınırı YOK) döner — "Excel'e Aktar" butonu için.</summary>
    public IReadOnlyList<DailyActivityGridRow> SearchGridAll(SessionContext s, DailyActivityGridFilter filter, string? sortColumn = null, bool sortDesc = false, bool includeCancelled = false)
    {
        var all = new List<DailyActivityGridRow>();
        int page = 1;
        while (true)
        {
            var res = SearchGrid(s, filter, page, 500, sortColumn, sortDesc, includeCancelled);
            all.AddRange(res.Items);
            if (page >= res.TotalPages || res.Items.Count == 0) break;
            page++;
        }
        return all;
    }

    /// <summary>Grid satırlarını Excel tablosuna çevirir — kolon sırası <see cref="DailyActivityListColumns.All"/> ile AYNIDIR.</summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<DailyActivityGridRow> rows)
    {
        var headers = DailyActivityListColumns.All.Select(c => c.Label).ToList();
        var body = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.DateText, r.Type, r.Vehicle, r.Route, r.Operator, r.Duration, r.Description,
        }).ToList();
        return new Application.Reports.TableModel("Günlük Faaliyet", headers, body);
    }

    /// <summary>
    /// Günlük faaliyeti İPTAL eder (fiziksel silme YOK — <c>is_deleted=1</c>).
    ///
    /// TUTARLILIK (kullanıcı kararları K1–K4, 2026-08-09 · İş 2): Faaliyete BAĞLI bakım kaydı varsa
    /// (bakım / ilave yağ / ilave filtre / tamir türleri) o da AYNI TRANSACTION içinde iptal edilir ve
    /// bakımın düşürdüğü malzemeler ters hareketle stoğa geri döner. Böylece Günlük Faaliyet, Bakım ve
    /// Stok raporları aynı gerçeği gösterir.
    ///
    /// Eski davranış: yalnız faaliyet gizleniyor, bakım ve stok yerinde kalıyordu → üç rapor birbirini
    /// tutmuyordu (ekranlar bunu "bağlı bakım Bakım ekranında kalır" diye uyarıyordu).
    ///
    /// ⚠️ Hareket/Sevkiyat türleri (<c>movement</c>) bakım ve stok ÜRETMEZ → davranışları DEĞİŞMEDİ.
    /// ⚠️ Araç sayacı GERİ ALINMAZ (proje kuralı; sayaç yalnız ileri gider).
    /// ⚠️ Yetki (K2): yalnız <c>daily_activity</c>/Delete aranır. Bakım iptali bunun doğal sonucudur;
    ///    ayrıca bakım/Ters Kayıt yetkisi istenmez. Kontrol SERVİS katmanındadır — arayüz değiştirilerek
    ///    ya da uç nokta doğrudan çağrılarak atlatılamaz.
    /// </summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        // Bakım iptali stok bakiyesine dokunduğu için eşzamanlılık koruması altında çalışır
        // (Faz 3-Ön: CAS çakışmasında işlemin TAMAMI geri alınıp yeniden denenir).
        StockBalanceWriter.Run(() => DeleteOnce(s, id), $"daily-activity:cancel id={id}");
    }

    private void DeleteOnce(SessionContext s, string id)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();   // TEK ATOMİK İŞLEM (K1)

        // 1) Faaliyeti kilitle/oku: var mı, zaten iptal mi, bağlı bakımı var mı?
        string? maintenanceId;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT maintenance_id, is_deleted FROM daily_activities WHERE id=@id AND company_id=@c;";
            read.AddWithValue("@id", id);
            read.AddWithValue("@c", s.CompanyId);
            using var r = read.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Faaliyet bulunamadı veya başka firmaya ait.");
            maintenanceId = r.IsDBNull(0) ? null : r.GetString(0);
            if (r.GetInt64(1) == 1) throw new InvalidOperationException("Bu faaliyet kaydı zaten iptal edilmiş.");
        }

        // 2) Bağlı bakım varsa AYNI transaction'da iptal et (stok ters hareketle geri döner).
        //    Bakım başka yerden zaten iptal edilmişse ikinci kez geri eklemez (idempotent).
        if (!string.IsNullOrEmpty(maintenanceId))
            _maintenance.CancelInTransaction(conn, tx, s, maintenanceId!, "Günlük faaliyet iptali");

        // 3) Faaliyeti iptal işaretle (version + updated_at — senkron LWW tutarlılığı).
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE daily_activities SET is_deleted=1, version=version+1, updated_at=@now " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Faaliyet iptal edilemedi.");
        }

        // 4) Denetim kaydı (eskiden HİÇ yazılmıyordu).
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "daily_activity", id, AuditActions.Reverse, s.UserId,
            AfterJson: maintenanceId is null ? null : $"{{\"maintenanceCancelled\":\"{maintenanceId}\"}}"), _clock);

        tx.Commit();
    }

    /// <summary>
    /// İptal ONAYI için özet: bu faaliyete bağlı bakım var mı, kaç malzeme satırı stoktan düşmüş?
    /// Ekranlar kullanıcıya "…bağlı bakım kaydı ve N adet malzeme çıkışı da iptal edilecek" diyebilsin diye.
    /// Salt-okuma; hiçbir şey değiştirmez.
    /// </summary>
    /// <summary>
    /// Günlük faaliyetin YAN ETKİSİZ (metadata) alanlarını günceller — İş #5 (2026-08-09), seçenek A.
    ///
    /// NEDEN YALNIZ BU ALANLAR: <c>SaveMovement</c>'ta <c>MovementKind="transfer"</c> + <c>VehicleId</c>
    /// aracı PASİFE ALIR; bakım tipli faaliyetlerde ise bağlı bakım kaydı stok defterini değiştirir.
    /// Bu yan etkileri geriye dönük düzenlemek yerine mevcut yol korunur: <b>iptal + yeniden oluştur</b>
    /// (bkz. <see cref="Delete"/>). Burada yalnız açıklama/operatör/süre düzeltilir — hiçbir hareket üretmez.
    ///
    /// Firma izolasyonu + düzenleme kilidi UPDATE koşulundadır; iptal edilmiş kayıt düzenlenemez.
    /// </summary>
    public void UpdateMetadata(SessionContext s, string id, string? description, string? operatorId,
        int? durationDays, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (durationDays is < 0) throw new ArgumentException("Süre negatif olamaz.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (operatorId is not null) EnsurePersonnelOwned(conn, tx, s.CompanyId, operatorId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE daily_activities SET description=@d, operator_id=@o, duration_days=@dur, " +
                "version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0"
                + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.AddWithValue("@d", (object?)TrimOrNull(description) ?? DBNull.Value);
            cmd.AddWithValue("@o", (object?)operatorId ?? DBNull.Value);
            cmd.AddWithValue("@dur", durationDays is { } dd ? dd : DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "daily_activities", id, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Faaliyet kaydı bulunamadı, iptal edilmiş veya başka firmaya ait.");
            }
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "daily_activity", id,
            AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    private static string? TrimOrNull(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Operatör (personel) oturumun firmasına mı ait? (İş #5 — yabancı personel atanamaz.)</summary>
    private static void EnsurePersonnelOwned(DbConnection conn, DbTransaction tx, string companyId, string personnelId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM personnel WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", personnelId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Personel bulunamadı veya başka firmaya ait.");
    }

    public (bool HasMaintenance, int MaterialLines, decimal TotalQuantity) GetCancelImpact(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        string? maintenanceId;
        using (var read = conn.CreateCommand())
        {
            read.CommandText = "SELECT maintenance_id FROM daily_activities WHERE id=@id AND company_id=@c AND is_deleted=0;";
            read.AddWithValue("@id", id);
            read.AddWithValue("@c", s.CompanyId);
            maintenanceId = read.ExecuteScalar() as string;
        }
        if (string.IsNullOrEmpty(maintenanceId)) return (false, 0, 0m);

        using var cmd = conn.CreateCommand();
        // "Bakım ekibi stoğu" satırları merkez depodan düşmemişti → etkide sayılmaz.
        cmd.CommandText = "SELECT quantity, from_team_stock FROM maintenance_materials WHERE maintenance_id=@m AND company_id=@c;";   // M-S1a
        cmd.AddWithValue("@m", maintenanceId);
        cmd.AddWithValue("@c", s.CompanyId);
        int lines = 0; decimal total = 0m;
        using var rr = cmd.ExecuteReader();
        while (rr.Read())
        {
            if (rr.GetInt64(1) == 1) continue;
            lines++;
            total += Money.Parse(rr.GetString(0));
        }
        return (true, lines, total);
    }

    private string? FindActivity(string operationId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM daily_activities WHERE operation_id=@op;";
        cmd.AddWithValue("@op", operationId);
        return cmd.ExecuteScalar() as string;
    }

    private static void InsertActivity(DbConnection conn, DbTransaction tx, string id, string companyId,
        string activityType, string? movementKind, string? vehicleId, string? fromLoc, string? toLoc, string? operatorId,
        int? durationDays, string? description, string? maintenanceId, bool stockProcessed, long activityDate,
        string operationId, long now, string? opBranchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO daily_activities(id, company_id, activity_type, movement_kind, vehicle_id, from_location_id, to_location_id,
    operator_id, duration_days, description, maintenance_id, source_module, stock_processed, activity_date, operation_id,
    op_branch_id, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@at,@mk,@v,@from,@to,@op2,@dur,@desc,@mid,'daily_activity',@sp,@ad,@op,@opb,@now,@now,1,0);";
        cmd.AddWithValue("@opb", (object?)opBranchId ?? DBNull.Value);
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@at", activityType);
        cmd.AddWithValue("@mk", (object?)movementKind ?? DBNull.Value);
        cmd.AddWithValue("@v", (object?)vehicleId ?? DBNull.Value);
        cmd.AddWithValue("@from", (object?)fromLoc ?? DBNull.Value);
        cmd.AddWithValue("@to", (object?)toLoc ?? DBNull.Value);
        cmd.AddWithValue("@op2", (object?)operatorId ?? DBNull.Value);
        cmd.AddWithValue("@dur", (object?)durationDays ?? DBNull.Value);
        cmd.AddWithValue("@desc", (object?)description ?? DBNull.Value);
        cmd.AddWithValue("@mid", (object?)maintenanceId ?? DBNull.Value);
        cmd.AddWithValue("@sp", stockProcessed ? 1 : 0);
        cmd.AddWithValue("@ad", activityDate);
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>B-3: araç bu firmaya ait mi? (MaintenanceService/InspectionService ile aynı desen.)</summary>
    private static void EnsureVehicleOwned(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
    }
}
