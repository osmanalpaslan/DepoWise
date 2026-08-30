using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Infrastructure.Maintenance;

/// <summary>Ekipman bakım kaydı girdisi — araç tarafındaki <see cref="NewMaintenance"/> karşılığı.
/// <c>VehicleId</c> yerine <c>EquipmentId</c> taşır; diğer alanların anlamı BİREBİR aynıdır.</summary>
public sealed record NewEquipmentMaintenance(
    string EquipmentId, string DefinitionId, string? SubDefinitionId = null, string? TechnicianId = null,
    string? Description = null, string? SubDefinitionNote = null,
    decimal? PerformedKm = null, decimal? PerformedHour = null, long? PerformedDate = null,
    IReadOnlyList<MaintenanceMaterialLine>? Materials = null,
    /// <summary>Malzemenin ÇEKİLDİĞİ depo (BKM-04/KARAR-9) — araç tarafıyla aynı anlam ve aynı doğrulama.</summary>
    string? StockLocationId = null);

/// <summary>Ekipman bakım listesi satırı — araç tarafındaki <see cref="MaintenanceRow"/> karşılığı.</summary>
public sealed record EquipmentMaintenanceRow(
    string Id, string EquipmentCode, string DefinitionName, string? SubDefinitionName,
    decimal? PerformedKm, decimal? PerformedHour, long? PerformedDate,
    decimal? NextDueKm, decimal? NextDueHour, long? NextDueDate, bool IsCancelled, string EquipmentId = "",
    long Version = 0, string? Description = null, string? SubDefinitionNote = null, string? TechnicianId = null)
{
    private static string Fmt(decimal? km, decimal? hour, long? date) =>
        km is not null ? $"{km:0.##} km"
        : hour is not null ? $"{hour:0.##} saat"
        : date is not null ? DateTimeOffset.FromUnixTimeMilliseconds(date.Value).LocalDateTime.ToString("dd.MM.yyyy")
        : "—";
    public string PerformedDisplay => Fmt(PerformedKm, PerformedHour, PerformedDate);
    public string NextDueDisplay => Fmt(NextDueKm, NextDueHour, NextDueDate);
    public string SubDisplay => string.IsNullOrEmpty(SubDefinitionName) ? "—" : SubDefinitionName!;
    public string StatusText => IsCancelled ? "İptal" : "Aktif";
}

/// <summary>
/// ═══ 7b — EKİPMAN BAKIM SERVİSİ (PK-F9, ADR-191) ═══
///
/// <b>Araç bakımının PARALEL hattıdır; onun yerine geçmez.</b> <see cref="MaintenanceService"/>
/// hiç değiştirilmedi (FAZ 2 kararı: mevcut araç bakımı sıfıra yakın regresyon riskiyle korunur).
///
/// <b>Ortaklaştırma:</b> stok defteri/bakiye yazımı için mevcut <see cref="StockBalanceWriter"/>,
/// uyarı eşikleri için <see cref="AlertRules"/> AYNEN kullanılır — ikinci bir stok ya da uyarı
/// mekanizması KURULMAZ. Yalnız bakım kaydının kendi tabloları ayrıdır.
///
/// <b>Sayaç YOK (PK-F8):</b> ekipmanda sayaç/kullanım kaydı yoktur; araç tarafındaki
/// <c>AdvanceMeterInTx</c>'in karşılığı bilinçli olarak UYGULANMAZ. Kullanıcının girdiği
/// <c>performed_km/hour</c> yalnız KAYIT olarak saklanır ve sonraki hedef hesabında kullanılır.
///
/// <b>İdempotency:</b> <c>operation_id</c> FİRMA KAPSAMLIDIR (FIN-B1/Migration082 sözleşmesi);
/// aynı firma + aynı op-id ikinci kez gelirse yeni kayıt ve İKİNCİ stok düşümü OLUŞMAZ.
///
/// <b>Tenant:</b> firma DAİMA oturumdan; ekipman/malzeme/depo/personel sahipliği serviste doğrulanır
/// (masaüstü bu servisi ÇEVRİMDIŞI da çağırır — kapı API'de olsaydı o yol korumasız kalırdı).
/// </summary>
public sealed class EquipmentMaintenanceService
{
    /// <summary>Yetki modülü — bakım hattının mevcut modülü. Yeni yetki modülü AÇILMAZ.</summary>
    private const string Module = "maintenance";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public EquipmentMaintenanceService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    // ══════════════════════════════════════ KAYIT ══════════════════════════════════════

    public string Save(SessionContext s, NewEquipmentMaintenance dto, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        return StockBalanceWriter.Run(() => SaveOnce(s, dto, operationId), $"equipment-maintenance:save op={operationId}");
    }

    private string SaveOnce(SessionContext s, NewEquipmentMaintenance dto, string operationId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();          // negatif stok yarışı için serialize (araç tarafıyla aynı)

        var existing = FindByOperation(conn, tx, s.CompanyId, operationId);
        if (existing is not null) { tx.Commit(); return existing; }   // ⭐ idempotent

        EnsureEquipmentOwned(conn, tx, s.CompanyId, dto.EquipmentId);
        var def = LoadDefinition(conn, tx, s.CompanyId, dto.DefinitionId)
                  ?? throw new ForbiddenException("Bakım tanımı bulunamadı veya başka firmaya ait.");
        if (dto.TechnicianId is not null) EnsurePersonnelOwned(conn, tx, s.CompanyId, dto.TechnicianId);

        var id = Guid.NewGuid().ToString("N");

        // Sonraki hedef — araç tarafındaki hesabın BİREBİR aynısı (AlertRules tek kaynak).
        decimal? nextKm = null, nextHour = null; long? nextDate = null;
        switch (AlertRules.ParseUnit(def.IntervalUnit))
        {
            case IntervalUnit.Km when dto.PerformedKm is not null: nextKm = dto.PerformedKm + def.IntervalValue; break;
            case IntervalUnit.Hour when dto.PerformedHour is not null: nextHour = dto.PerformedHour + def.IntervalValue; break;
            case IntervalUnit.Day when dto.PerformedDate is not null:
                nextDate = DateTimeOffset.FromUnixTimeMilliseconds(dto.PerformedDate.Value)
                    .AddDays((double)def.IntervalValue).ToUnixTimeMilliseconds();
                break;
        }

        Insert(conn, tx, s.CompanyId, id, dto, nextKm, nextHour, nextDate, operationId, now, s.OperatingBranchId);

        // Malzeme tüketimi — araç tarafındaki kuralların AYNISI:
        //  • negatif stok ENGELLENMEZ (ADR-086; bakım iş akışı stok yüzünden durmaz),
        //  • "bakım ekibi stoğu" işaretli satır maliyete girer ama merkez stoğa DOKUNMAZ,
        //  • defter ve bakiye AYNI lokasyonu kullanır (ayrışırsa stok sessizce tutarsızlaşır).
        var stockLocation = string.IsNullOrWhiteSpace(dto.StockLocationId) ? null : dto.StockLocationId!.Trim();
        EnsureLocationOwned(conn, tx, s.CompanyId, stockLocation);
        var locationKey = stockLocation ?? StockBalanceWriter.Unassigned;

        var teamStockUsed = new List<string>();
        for (int i = 0; i < (dto.Materials?.Count ?? 0); i++)
        {
            var line = dto.Materials![i];
            if (line.Quantity <= 0) throw new ArgumentException("Malzeme miktarı pozitif olmalı.");
            EnsureMaterialOwned(conn, tx, s.CompanyId, line.MaterialId);
            var price = ReadMaterialPrice(conn, tx, line.MaterialId);
            if (!line.FromTeamStock)
            {
                StockBalanceWriter.ApplyDelta(conn, tx, s.CompanyId, line.MaterialId, locationKey,
                    -line.Quantity, now, allowNegative: true);
                InsertUsageMovement(conn, tx, s.CompanyId, line.MaterialId, id, line.Quantity, price,
                    $"{operationId}:mat:{i}", now, stockLocation);
            }
            else teamStockUsed.Add(line.MaterialId);
            InsertMaterial(conn, tx, s.CompanyId, id, line.MaterialId, line.Quantity, price, line.FromTeamStock);
        }

        // ⚠️ SAYAÇ İLERLETME YOK (PK-F8): ekipmanda sayaç kavramı yoktur.

        var afterJson = teamStockUsed.Count == 0 ? null
            : "{\"teamStockMaterials\":[" + string.Join(",", teamStockUsed.Select(m => "\"" + m + "\"")) + "]}";
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment_maintenance", id,
            AuditActions.Create, s.UserId, AfterJson: afterJson), _clock);
        tx.Commit();
        return id;
    }

    // ══════════════════════════════════════ İPTAL ══════════════════════════════════════

    /// <summary>İptal: malzeme stoğu TERS hareketle geri eklenir, kayıt iptal işaretlenir (SİLİNMEZ).
    /// Ekip stoğundan kullanılan satır için ters hareket üretilmez (girişte de düşülmemişti).</summary>
    public void Cancel(SessionContext s, string maintenanceId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");
        StockBalanceWriter.Run(() => CancelOnce(s, maintenanceId, reason), $"equipment-maintenance:cancel id={maintenanceId}");
    }

    private void CancelOnce(SessionContext s, string maintenanceId, string reason)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var durum = LoadStatus(conn, tx, s.CompanyId, maintenanceId)
                    ?? throw new ForbiddenException("Bakım kaydı bulunamadı veya başka firmaya ait.");
        if (durum) { tx.Commit(); return; }            // zaten iptal → idempotent

        // Orijinal tüketim hareketlerini defterden oku ve TERSLE (lokasyon orijinalden gelir, yeniden hesaplanmaz).
        var hareketler = new List<(string Id, string MaterialId, string? BranchId, decimal Qty, decimal? Price)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT id, material_id, branch_id, quantity, unit_price FROM stock_movements " +
                "WHERE company_id=@c AND note=@n AND movement_type='usage' AND is_reversed=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", maintenanceId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                hareketler.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                    Money.Parse(r.GetString(3)), r.IsDBNull(4) ? null : Money.Parse(r.GetString(4))));
        }

        foreach (var h in hareketler)
        {
            StockBalanceWriter.ApplyDelta(conn, tx, s.CompanyId, h.MaterialId,
                h.BranchId ?? StockBalanceWriter.Unassigned, h.Qty, now, allowNegative: true);
            InsertUsageMovement(conn, tx, s.CompanyId, h.MaterialId, maintenanceId, h.Qty, h.Price,
                $"eqm-cancel:{maintenanceId}:{h.Id}", now, h.BranchId, reverse: true, reversesMovementId: h.Id);
            using var upd = conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = "UPDATE stock_movements SET is_reversed=1, updated_at=@now WHERE id=@id AND company_id=@c;";
            upd.AddWithValue("@now", now);
            upd.AddWithValue("@id", h.Id);
            upd.AddWithValue("@c", s.CompanyId);
            upd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE equipment_maintenances SET is_cancelled=1, version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", maintenanceId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
                throw new ForbiddenException("Bakım kaydı bulunamadı veya başka firmaya ait.");
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment_maintenance", maintenanceId,
            AuditActions.Reverse, s.UserId, AfterJson: $"{{\"reason\":\"{reason.Trim()}\"}}"), _clock);
        tx.Commit();
    }

    // ══════════════════════════════════════ GÜNCELLEME ══════════════════════════════════════

    public void UpdateMetadata(SessionContext s, string maintenanceId, string? description,
        string? subDefinitionNote, string? technicianId, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (technicianId is not null) EnsurePersonnelOwned(conn, tx, s.CompanyId, technicianId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE equipment_maintenances SET description=@d, sub_definition_note=@n, technician_id=@t, " +
                "version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0 AND is_cancelled=0"
                + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.AddWithValue("@d", (object?)Trim(description) ?? DBNull.Value);
            cmd.AddWithValue("@n", (object?)Trim(subDefinitionNote) ?? DBNull.Value);
            cmd.AddWithValue("@t", (object?)technicianId ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", maintenanceId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "equipment_maintenances", maintenanceId, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Bakım kaydı bulunamadı, iptal edilmiş veya başka firmaya ait.");
            }
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment_maintenance", maintenanceId,
            AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    // ══════════════════════════════════════ OKUMA ══════════════════════════════════════

    public IReadOnlyList<EquipmentMaintenanceRow> List(SessionContext s, string? equipmentId = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var list = new List<EquipmentMaintenanceRow>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.id, e.code, d.name, sd.name, m.performed_km, m.performed_hour, m.performed_date,
       m.next_due_km, m.next_due_hour, m.next_due_date, m.is_cancelled, m.equipment_id, m.version,
       m.description, m.sub_definition_note, m.technician_id
FROM equipment_maintenances m
JOIN equipment e ON e.id = m.equipment_id
JOIN maintenance_definitions d ON d.id = m.maintenance_def_id
LEFT JOIN maintenance_definitions sd ON sd.id = m.sub_definition_id
WHERE m.company_id=@c AND m.is_deleted=0
  AND (CAST(@eq AS TEXT) IS NULL OR m.equipment_id=@eq)
ORDER BY m.created_at DESC
LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@eq", (object?)equipmentId ?? DBNull.Value);
        cmd.AddWithValue("@lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new EquipmentMaintenanceRow(
                r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : Money.Parse(r.GetString(4)),
                r.IsDBNull(5) ? null : Money.Parse(r.GetString(5)),
                r.IsDBNull(6) ? null : Convert.ToInt64(r.GetValue(6)),
                r.IsDBNull(7) ? null : Money.Parse(r.GetString(7)),
                r.IsDBNull(8) ? null : Money.Parse(r.GetString(8)),
                r.IsDBNull(9) ? null : Convert.ToInt64(r.GetValue(9)),
                Convert.ToInt64(r.GetValue(10)) == 1, r.GetString(11), Convert.ToInt64(r.GetValue(12)),
                r.IsDBNull(13) ? null : r.GetString(13),
                r.IsDBNull(14) ? null : r.GetString(14),
                r.IsDBNull(15) ? null : r.GetString(15)));
        return list;
    }

    public IReadOnlyList<MaintenanceMaterialRow> Materials(SessionContext s, string maintenanceId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var list = new List<MaintenanceMaterialRow>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT mt.code, mt.name, mm.quantity, mm.from_team_stock
FROM equipment_maintenance_materials mm
JOIN materials mt ON mt.id = mm.material_id
WHERE mm.maintenance_id=@m AND mm.company_id=@c;";
        cmd.AddWithValue("@m", maintenanceId);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaintenanceMaterialRow(r.GetString(0), r.GetString(1), Money.Parse(r.GetString(2)),
                Convert.ToInt64(r.GetValue(3)) == 1));
        return list;
    }

    // ══════════════════════════════════════ YARDIMCI ══════════════════════════════════════

    private sealed record DefRow(decimal IntervalValue, string IntervalUnit);

    private static DefRow? LoadDefinition(DbConnection conn, DbTransaction tx, string companyId, string defId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT interval_value, interval_unit FROM maintenance_definitions " +
            "WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", defId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DefRow(Money.Parse(r.GetString(0)), r.GetString(1)) : null;
    }

    private static void Insert(DbConnection conn, DbTransaction tx, string companyId, string id,
        NewEquipmentMaintenance dto, decimal? nextKm, decimal? nextHour, long? nextDate,
        string operationId, long now, string? opBranchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO equipment_maintenances(id, company_id, equipment_id, maintenance_def_id, sub_definition_id,
    technician_id, description, sub_definition_note, performed_km, performed_hour, performed_date,
    next_due_km, next_due_hour, next_due_date, op_branch_id, operation_id, is_cancelled,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@e,@d,@sd,@tech,@desc,@sdn,@pk,@ph,@pd,@nk,@nh,@nd,@opb,@op,0,@now,@now,1,0);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@e", dto.EquipmentId);
        cmd.AddWithValue("@d", dto.DefinitionId);
        cmd.AddWithValue("@sd", (object?)dto.SubDefinitionId ?? DBNull.Value);
        cmd.AddWithValue("@tech", (object?)dto.TechnicianId ?? DBNull.Value);
        cmd.AddWithValue("@desc", (object?)Trim(dto.Description) ?? DBNull.Value);
        cmd.AddWithValue("@sdn", (object?)Trim(dto.SubDefinitionNote) ?? DBNull.Value);
        cmd.AddWithValue("@pk", dto.PerformedKm is null ? DBNull.Value : Money.Serialize(dto.PerformedKm.Value));
        cmd.AddWithValue("@ph", dto.PerformedHour is null ? DBNull.Value : Money.Serialize(dto.PerformedHour.Value));
        cmd.AddWithValue("@pd", (object?)dto.PerformedDate ?? DBNull.Value);
        cmd.AddWithValue("@nk", nextKm is null ? DBNull.Value : Money.Serialize(nextKm.Value));
        cmd.AddWithValue("@nh", nextHour is null ? DBNull.Value : Money.Serialize(nextHour.Value));
        cmd.AddWithValue("@nd", (object?)nextDate ?? DBNull.Value);
        cmd.AddWithValue("@opb", (object?)opBranchId ?? DBNull.Value);
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertMaterial(DbConnection conn, DbTransaction tx, string companyId,
        string maintenanceId, string materialId, decimal qty, decimal? price, bool fromTeamStock)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO equipment_maintenance_materials(id, company_id, maintenance_id, material_id, " +
            "quantity, unit_price, from_team_stock) VALUES(@id,@c,@mt,@m,@q,@p,@ts);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@mt", maintenanceId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@q", Money.Serialize(qty));
        cmd.AddWithValue("@p", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.AddWithValue("@ts", fromTeamStock ? 1L : 0L);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Stok defteri kaydı — araç bakımıyla AYNI hareket tipleri ve aynı sözleşme.</summary>
    private static void InsertUsageMovement(DbConnection conn, DbTransaction tx, string companyId,
        string materialId, string maintenanceId, decimal qty, decimal? price, string operationId, long now,
        string? branchId, bool reverse = false, string? reversesMovementId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, movement_type, direction, quantity,
    unit_price, currency_code, fx_rate, operation_id, note, created_at, document_id, is_reversed,
    reverses_movement_id, updated_at)
VALUES(@id,@c,@m,@br,@type,@dir,@q,@price,'TRY',NULL,@op,@note,@now,NULL,0,@rev,@now);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
        cmd.AddWithValue("@type", reverse ? "usage_reverse" : "usage");
        cmd.AddWithValue("@dir", reverse ? 1 : -1);
        cmd.AddWithValue("@q", Money.Serialize(qty));
        cmd.AddWithValue("@price", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@note", maintenanceId);
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@rev", (object?)reversesMovementId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static decimal? ReadMaterialPrice(DbConnection conn, DbTransaction tx, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT unit_price FROM materials WHERE id=@id;";
        cmd.AddWithValue("@id", materialId);
        var v = cmd.ExecuteScalar();
        return v is string sv && sv.Length > 0 ? Money.Parse(sv) : null;
    }

    /// <summary>⭐ Firma kapsamlı idempotency (FIN-B1/Migration082 sözleşmesi).</summary>
    private static string? FindByOperation(DbConnection conn, DbTransaction tx, string companyId, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM equipment_maintenances WHERE company_id=@c AND operation_id=@op;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", operationId);
        return cmd.ExecuteScalar() as string;
    }

    private static bool? LoadStatus(DbConnection conn, DbTransaction tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT is_cancelled FROM equipment_maintenances WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? null : Convert.ToInt64(v) == 1;
    }

    /// <summary>Ekipman bu firmanın mı ve silinmemiş mi? (IDOR kapısı — istemciden gelen kimliğe güvenilmez.)</summary>
    internal static void EnsureEquipmentOwned(DbConnection conn, DbTransaction? tx, string companyId, string equipmentId)
    {
        if (string.IsNullOrWhiteSpace(equipmentId)) throw new ArgumentException("Ekipman seçilmedi.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM equipment WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", equipmentId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Ekipman bulunamadı veya başka firmaya ait.");
    }

    private static void EnsureMaterialOwned(DbConnection conn, DbTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", materialId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }

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

    private static void EnsureLocationOwned(DbConnection conn, DbTransaction tx, string companyId, string? locationId)
    {
        if (locationId is null) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", locationId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Depo bulunamadı veya başka firmaya ait.");
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
