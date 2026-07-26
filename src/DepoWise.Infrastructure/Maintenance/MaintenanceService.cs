using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using System.Data.Common;

namespace DepoWise.Infrastructure.Maintenance;

public sealed record MaintenanceMaterialLine(string MaterialId, decimal Quantity);

public sealed record NewMaintenance(
    string VehicleId, string DefinitionId, string? SubDefinitionId = null, string? TechnicianId = null,
    string? Description = null, string? SubDefinitionNote = null,
    decimal? PerformedKm = null, decimal? PerformedHour = null, long? PerformedDate = null,
    IReadOnlyList<MaintenanceMaterialLine>? Materials = null);

public sealed record MaintenanceAlert(
    string VehicleId, string DefinitionId, string DefinitionName, AlertLevel Level, double Progress, decimal Consumed, decimal Interval,
    bool NeverPerformed = false);   // araca atanmış ama HİÇ yapılmamış bakım → "ilk bakım bekliyor" (2026-07-25)

public sealed record MaintenanceRow(
    string Id, string VehicleCode, string DefinitionName, string? SubDefinitionName,
    decimal? PerformedKm, decimal? PerformedHour, long? PerformedDate,
    decimal? NextDueKm, decimal? NextDueHour, long? NextDueDate, bool IsCancelled, string VehicleId = "")
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

public sealed record MaintenanceMaterialRow(string Code, string Name, decimal Quantity);

/// <summary>
/// Bakım kaydı — TEK transaction: kayıt + malzeme stok düşümü (negatif guard, tek düşüm) + sayaç ileri +
/// sonraki hedef + audit; operation_id idempotent. İptal = ters stok + hedef yeniden hesaplama.
/// Uyarı eşikleri AlertRules (%85/95/100). Yeni bakım → en-son kayıt değişir → uyarı otomatik temizlenir.
/// </summary>
public sealed class MaintenanceService
{
    private const string Module = "maintenance";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MaintenanceService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Save(SessionContext s, NewMaintenance dto, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate(); // negatif stok / sayaç için serialize

        var existing = FindByOperation(conn, tx, operationId);
        if (existing is not null) { tx.Commit(); return existing; } // idempotent

        EnsureVehicleOwned(conn, tx, s.CompanyId, dto.VehicleId);
        var def = LoadDefinition(conn, tx, s.CompanyId, dto.DefinitionId)
            ?? throw new ForbiddenException("Bakım tanımı bulunamadı veya başka firmaya ait.");

        var id = Guid.NewGuid().ToString("N");

        // Sonraki hedef
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

        InsertMaintenance(conn, tx, s.CompanyId, id, dto, nextKm, nextHour, nextDate, operationId, now, s.OperatingBranchId);

        // Malzeme stok düşümü — TEK düşüm, negatif guard, fiyat snapshot
        for (int i = 0; i < (dto.Materials?.Count ?? 0); i++)
        {
            var line = dto.Materials![i];
            if (line.Quantity <= 0) throw new ArgumentException("Malzeme miktarı pozitif olmalı.");
            EnsureMaterialOwned(conn, tx, s.CompanyId, line.MaterialId);
            var price = ReadMaterialPrice(conn, tx, line.MaterialId);
            ApplyDelta(conn, tx, s.CompanyId, line.MaterialId, -line.Quantity, now);
            InsertUsageMovement(conn, tx, s.CompanyId, line.MaterialId, id, line.Quantity, price, $"{operationId}:mat:{i}", now);
            InsertMaintenanceMaterial(conn, tx, id, line.MaterialId, line.Quantity, price);
        }

        // Sayaç ileri (geçmiş kaydı engellemez)
        AdvanceMeterInTx(conn, tx, s.CompanyId, dto.VehicleId, def.IntervalUnit, dto.PerformedKm, dto.PerformedHour, now);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_maintenance", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>İptal: malzeme stoğu ters hareketle geri eklenir + kayıt iptal işaretlenir (silinmez).</summary>
    public void Cancel(SessionContext s, string maintenanceId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var status = LoadMaintenanceStatus(conn, tx, s.CompanyId, maintenanceId)
            ?? throw new ForbiddenException("Bakım kaydı bulunamadı veya başka firmaya ait.");
        if (status) { tx.Commit(); return; } // zaten iptal — idempotent

        // Malzemeleri geri ekle (ters hareket)
        int i = 0;
        foreach (var (materialId, qty, price) in LoadMaintenanceMaterials(conn, tx, maintenanceId))
        {
            ApplyDelta(conn, tx, s.CompanyId, materialId, +qty, now, allowNegative: true);
            InsertUsageMovement(conn, tx, s.CompanyId, materialId, maintenanceId, qty, price, $"cancel:{maintenanceId}:{i}", now, reverse: true);
            i++;
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vehicle_maintenances SET is_cancelled=1, version=version+1, updated_at=@now WHERE id=@id;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", maintenanceId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_maintenance", maintenanceId, AuditActions.Reverse, s.UserId,
            AfterJson: $"{{\"reason\":\"{reason}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>Her (araç,tanım) için EN SON iptal edilmemiş bakımdan ilerleme + uyarı seviyesi.</summary>
    public IReadOnlyList<MaintenanceAlert> GetAlerts(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // Her (araç,tanım) için EN SON iptal edilmemiş bakım. Eski sorgu SQLite'a özel "MAX ile bare-kolon"
        // davranışına dayanıyordu (GROUP BY'da olmayan kolonlar max satırdan gelir); PostgreSQL bunu reddeder
        // (42803). Standart, her iki DB'de de çalışan pencere fonksiyonu (ROW_NUMBER) ile yeniden yazıldı.
        // Kolon sırası AYNI (okuyucu değişmedi): 0..10 = vehicle_id..created_at.
        cmd.CommandText = @"
SELECT vehicle_id, maintenance_def_id, name, interval_value, interval_unit,
       performed_km, performed_hour, performed_date, current_meter, meter_unit, created_at
FROM (
    SELECT vm.vehicle_id, vm.maintenance_def_id, d.name, d.interval_value, d.interval_unit,
           vm.performed_km, vm.performed_hour, vm.performed_date,
           v.current_meter, v.meter_unit, vm.created_at,
           ROW_NUMBER() OVER (PARTITION BY vm.vehicle_id, vm.maintenance_def_id ORDER BY vm.created_at DESC) AS rn
    FROM vehicle_maintenances vm
    JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
    JOIN vehicles v ON v.id = vm.vehicle_id
    WHERE vm.company_id = @c AND vm.is_cancelled = 0 AND vm.is_deleted = 0
) t
WHERE rn = 1;";
        cmd.AddWithValue("@c", s.CompanyId);

        var list = new List<MaintenanceAlert>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var vehicleId = r.GetString(0);
            var defId = r.GetString(1);
            var defName = r.GetString(2);
            var interval = Money.Parse(r.GetString(3));
            var unit = AlertRules.ParseUnit(r.GetString(4));
            var perfKm = r.IsDBNull(5) ? (decimal?)null : Money.Parse(r.GetString(5));
            var perfHour = r.IsDBNull(6) ? (decimal?)null : Money.Parse(r.GetString(6));
            var perfDate = r.IsDBNull(7) ? (long?)null : r.GetInt64(7);
            var currentMeter = Money.Parse(r.GetString(8));

            decimal consumed = unit switch
            {
                IntervalUnit.Km => perfKm is null ? 0 : currentMeter - perfKm.Value,
                IntervalUnit.Hour => perfHour is null ? 0 : currentMeter - perfHour.Value,
                IntervalUnit.Day => perfDate is null ? 0 :
                    (decimal)(DateTimeOffset.FromUnixTimeMilliseconds(_clock.UtcNow.ToUnixTimeMilliseconds())
                        - DateTimeOffset.FromUnixTimeMilliseconds(perfDate.Value)).TotalDays,
                _ => 0,
            };
            if (consumed < 0) consumed = 0;
            var progress = AlertRules.Progress(consumed, interval);
            list.Add(new MaintenanceAlert(vehicleId, defId, defName, AlertRules.Level(progress), progress, consumed, interval));
        }

        // HİÇ YAPILMAMIŞ atanmış bakımlar (2026-07-25 kullanıcı bulgusu: "bakım periyodu doldu ama uyarı çıkmadı"):
        // bir bakım tanımı araca ATANMIŞ ama o araç için HİÇ (iptal edilmemiş) bakım kaydı YOKSA, ilk bakım
        // bekliyor demektir → "İlk bakım yapılmadı" (Overdue). Baz metre/tarih tutulmadığından yüzde hesaplanmaz.
        using (var cmd2 = conn.CreateCommand())
        {
            cmd2.CommandText = @"
SELECT mdv.vehicle_id, d.id, d.name, d.interval_value
FROM maintenance_definition_vehicles mdv
JOIN maintenance_definitions d ON d.id = mdv.definition_id AND d.is_deleted = 0
JOIN vehicles v ON v.id = mdv.vehicle_id AND v.is_deleted = 0 AND v.company_id = @c
WHERE NOT EXISTS (
    SELECT 1 FROM vehicle_maintenances vm
    WHERE vm.vehicle_id = mdv.vehicle_id AND vm.maintenance_def_id = mdv.definition_id
      AND vm.is_cancelled = 0 AND vm.is_deleted = 0);";
            cmd2.AddWithValue("@c", s.CompanyId);
            using var r2 = cmd2.ExecuteReader();
            while (r2.Read())
            {
                var interval = Money.Parse(r2.GetString(3));
                list.Add(new MaintenanceAlert(r2.GetString(0), r2.GetString(1), r2.GetString(2),
                    AlertLevel.Overdue, 1.0, 0, interval, NeverPerformed: true));
            }
        }
        return list;
    }

    /// <summary>Bakım kayıtları (salt okuma) — araç/tanım/alt-tanım adları + yapılma/sonraki; araç filtresi.</summary>
    public IReadOnlyList<MaintenanceRow> ListMaintenances(SessionContext s, string? vehicleId = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT vm.id, v.internal_code, d.name, sd.name,
       vm.performed_km, vm.performed_hour, vm.performed_date,
       vm.next_due_km, vm.next_due_hour, vm.next_due_date, vm.is_cancelled, vm.vehicle_id
FROM vehicle_maintenances vm
JOIN vehicles v ON v.id = vm.vehicle_id
JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
LEFT JOIN maintenance_definitions sd ON sd.id = vm.sub_definition_id
WHERE vm.company_id=@c AND vm.is_deleted=0" + DepoWise.Application.Security.BranchScope.Sql(s, "vm.op_branch_id") + @"
  AND (CAST(@vid AS TEXT) IS NULL OR vm.vehicle_id=@vid)
ORDER BY vm.created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        if (DepoWise.Application.Security.BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        cmd.AddWithValue("@vid", (object?)vehicleId ?? DBNull.Value);
        cmd.AddWithValue("@lim", limit);
        decimal? D(DbDataReader r, int i) => r.IsDBNull(i) ? (decimal?)null : Money.Parse(r.GetString(i));
        long? L(DbDataReader r, int i) => r.IsDBNull(i) ? (long?)null : r.GetInt64(i);
        var list = new List<MaintenanceRow>();
        using var rr = cmd.ExecuteReader();
        while (rr.Read())
            list.Add(new MaintenanceRow(rr.GetString(0), rr.GetString(1), rr.GetString(2),
                rr.IsDBNull(3) ? null : rr.GetString(3),
                D(rr, 4), D(rr, 5), L(rr, 6), D(rr, 7), D(rr, 8), L(rr, 9), Convert.ToInt64(rr.GetValue(10)) == 1, rr.GetString(11)));
        return list;
    }

    /// <summary>Bir bakım kaydında kullanılan malzemeler (kod/ad/miktar).</summary>
    public IReadOnlyList<MaintenanceMaterialRow> GetMaintenanceMaterials(SessionContext s, string maintenanceId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.code, m.name, mm.quantity FROM maintenance_materials mm
JOIN materials m ON m.id = mm.material_id
WHERE mm.maintenance_id=@mt ORDER BY m.code;";
        cmd.AddWithValue("@mt", maintenanceId);
        var list = new List<MaintenanceMaterialRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MaintenanceMaterialRow(r.GetString(0), r.GetString(1), Money.Parse(r.GetString(2))));
        return list;
    }

    // ================= çekirdek =================
    private sealed record DefRow(decimal IntervalValue, string IntervalUnit);

    private static DefRow? LoadDefinition(DbConnection conn, DbTransaction tx, string companyId, string defId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT interval_value, interval_unit FROM maintenance_definitions WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", defId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DefRow(Money.Parse(r.GetString(0)), r.GetString(1)) : null;
    }

    private static void InsertMaintenance(DbConnection conn, DbTransaction tx, string companyId, string id,
        NewMaintenance dto, decimal? nextKm, decimal? nextHour, long? nextDate, string operationId, long now, string? opBranchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO vehicle_maintenances(id, company_id, vehicle_id, maintenance_def_id, sub_definition_id, technician_id,
    description, sub_definition_note, performed_km, performed_hour, performed_date,
    next_due_km, next_due_hour, next_due_date, operation_id, op_branch_id, is_cancelled, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@v,@d,@sd,@tech,@desc,@sdn,@pk,@ph,@pd,@nk,@nh,@nd,@op,@opb,0,@now,@now,1,0);";
        cmd.AddWithValue("@opb", (object?)opBranchId ?? DBNull.Value);
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", dto.VehicleId);
        cmd.AddWithValue("@d", dto.DefinitionId);
        cmd.AddWithValue("@sd", (object?)dto.SubDefinitionId ?? DBNull.Value);
        cmd.AddWithValue("@tech", (object?)dto.TechnicianId ?? DBNull.Value);
        cmd.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
        cmd.AddWithValue("@sdn", dto.SubDefinitionId is null ? DBNull.Value : (object?)dto.SubDefinitionNote ?? DBNull.Value);
        cmd.AddWithValue("@pk", dto.PerformedKm is null ? DBNull.Value : Money.Serialize(dto.PerformedKm.Value));
        cmd.AddWithValue("@ph", dto.PerformedHour is null ? DBNull.Value : Money.Serialize(dto.PerformedHour.Value));
        cmd.AddWithValue("@pd", (object?)dto.PerformedDate ?? DBNull.Value);
        cmd.AddWithValue("@nk", nextKm is null ? DBNull.Value : Money.Serialize(nextKm.Value));
        cmd.AddWithValue("@nh", nextHour is null ? DBNull.Value : Money.Serialize(nextHour.Value));
        cmd.AddWithValue("@nd", (object?)nextDate ?? DBNull.Value);
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void ApplyDelta(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        decimal signedQty, long now, bool allowNegative = false)
    {
        decimal current;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=@m;";
            read.AddWithValue("@m", materialId);
            current = Money.Parse(read.ExecuteScalar() as string);
        }
        var updated = current + signedQty;
        if (!allowNegative && updated < 0)
            throw new NegativeStockException($"Negatif stok engellendi: mevcut {current}, talep {-signedQty}.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_balances(company_id, material_id, quantity, updated_at) VALUES(@c,@m,@q,@now)
ON CONFLICT(material_id) DO UPDATE SET quantity=excluded.quantity, updated_at=excluded.updated_at;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@q", Money.Serialize(updated));
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertUsageMovement(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        string maintenanceId, decimal qty, decimal? price, string operationId, long now, bool reverse = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, movement_type, direction, quantity,
    unit_price, currency_code, fx_rate, operation_id, note, created_at, document_id, is_reversed, reverses_movement_id)
VALUES(@id,@c,@m,NULL,@type,@dir,@q,@price,'TRY',NULL,@op,@note,@now,NULL,0,NULL);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@type", reverse ? "usage_reverse" : "usage");
        cmd.AddWithValue("@dir", reverse ? 1 : -1);
        cmd.AddWithValue("@q", Money.Serialize(qty));
        cmd.AddWithValue("@price", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@note", maintenanceId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertMaintenanceMaterial(DbConnection conn, DbTransaction tx, string maintenanceId,
        string materialId, decimal qty, decimal? price)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO maintenance_materials(id, maintenance_id, material_id, quantity, unit_price) VALUES(@id,@mt,@m,@q,@p);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@mt", maintenanceId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@q", Money.Serialize(qty));
        cmd.AddWithValue("@p", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.ExecuteNonQuery();
    }

    private void AdvanceMeterInTx(DbConnection conn, DbTransaction tx, string companyId, string vehicleId,
        string unit, decimal? performedKm, decimal? performedHour, long now)
    {
        var incoming = unit == "hour" ? performedHour : performedKm;
        if (incoming is null) return;
        decimal current;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT current_meter FROM vehicles WHERE id=@id AND company_id=@c;";
            read.AddWithValue("@id", vehicleId);
            read.AddWithValue("@c", companyId);
            current = Money.Parse(read.ExecuteScalar() as string);
        }
        if (!MeterRule.ShouldAdvance(current, incoming.Value)) return;
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE vehicles SET current_meter=@m, version=version+1, updated_at=@now WHERE id=@id;";
            upd.AddWithValue("@m", Money.Serialize(incoming.Value));
            upd.AddWithValue("@now", now);
            upd.AddWithValue("@id", vehicleId);
            upd.ExecuteNonQuery();
        }
        using var log = conn.CreateCommand();
        log.Transaction = tx;
        log.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES(@id,@c,@v,@o,@n,'maintenance',@now);";
        log.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        log.AddWithValue("@c", companyId);
        log.AddWithValue("@v", vehicleId);
        log.AddWithValue("@o", Money.Serialize(current));
        log.AddWithValue("@n", Money.Serialize(incoming.Value));
        log.AddWithValue("@now", now);
        log.ExecuteNonQuery();
    }

    private static decimal ReadMaterialPrice(DbConnection conn, DbTransaction tx, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT unit_price FROM materials WHERE id=@m;";
        cmd.AddWithValue("@m", materialId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private static string? FindByOperation(DbConnection conn, DbTransaction tx, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM vehicle_maintenances WHERE operation_id=@op;";
        cmd.AddWithValue("@op", operationId);
        return cmd.ExecuteScalar() as string;
    }

    private static bool? LoadMaintenanceStatus(DbConnection conn, DbTransaction tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT is_cancelled FROM vehicle_maintenances WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        return v is null ? null : Convert.ToInt64(v) == 1;
    }

    private static IEnumerable<(string MaterialId, decimal Qty, decimal? Price)> LoadMaintenanceMaterials(
        DbConnection conn, DbTransaction tx, string maintenanceId)
    {
        var list = new List<(string, decimal, decimal?)>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT material_id, quantity, unit_price FROM maintenance_materials WHERE maintenance_id=@mt;";
        cmd.AddWithValue("@mt", maintenanceId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), Money.Parse(r.GetString(1)), r.IsDBNull(2) ? null : Money.Parse(r.GetString(2))));
        return list;
    }

    private static void EnsureVehicleOwned(DbConnection conn, DbTransaction tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
    }

    private static void EnsureMaterialOwned(DbConnection conn, DbTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", materialId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }
}
