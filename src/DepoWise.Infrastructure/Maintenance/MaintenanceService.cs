using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Maintenance;

public sealed record MaintenanceMaterialLine(string MaterialId, decimal Quantity);

public sealed record NewMaintenance(
    string VehicleId, string DefinitionId, string? SubDefinitionId = null, string? TechnicianId = null,
    string? Description = null, string? SubDefinitionNote = null,
    decimal? PerformedKm = null, decimal? PerformedHour = null, long? PerformedDate = null,
    IReadOnlyList<MaintenanceMaterialLine>? Materials = null);

public sealed record MaintenanceAlert(
    string VehicleId, string DefinitionId, string DefinitionName, AlertLevel Level, double Progress, decimal Consumed, decimal Interval);

public sealed record MaintenanceRow(
    string Id, string VehicleCode, string DefinitionName, string? SubDefinitionName,
    decimal? PerformedKm, decimal? PerformedHour, long? PerformedDate,
    decimal? NextDueKm, decimal? NextDueHour, long? NextDueDate, bool IsCancelled)
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
        using var tx = conn.BeginTransaction(deferred: false); // negatif stok / sayaç için serialize

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

        InsertMaintenance(conn, tx, s.CompanyId, id, dto, nextKm, nextHour, nextDate, operationId, now);

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
        using var tx = conn.BeginTransaction(deferred: false);

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
            cmd.CommandText = "UPDATE vehicle_maintenances SET is_cancelled=1, version=version+1, updated_at=$now WHERE id=$id;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", maintenanceId);
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
        cmd.CommandText = @"
SELECT vm.vehicle_id, vm.maintenance_def_id, d.name, d.interval_value, d.interval_unit,
       vm.performed_km, vm.performed_hour, vm.performed_date,
       v.current_meter, v.meter_unit, MAX(vm.created_at)
FROM vehicle_maintenances vm
JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
JOIN vehicles v ON v.id = vm.vehicle_id
WHERE vm.company_id = $c AND vm.is_cancelled = 0 AND vm.is_deleted = 0
GROUP BY vm.vehicle_id, vm.maintenance_def_id;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);

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
       vm.next_due_km, vm.next_due_hour, vm.next_due_date, vm.is_cancelled
FROM vehicle_maintenances vm
JOIN vehicles v ON v.id = vm.vehicle_id
JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
LEFT JOIN maintenance_definitions sd ON sd.id = vm.sub_definition_id
WHERE vm.company_id=$c AND vm.is_deleted=0
  AND ($vid IS NULL OR vm.vehicle_id=$vid)
ORDER BY vm.created_at DESC LIMIT $lim;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$vid", (object?)vehicleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lim", limit);
        decimal? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? (decimal?)null : Money.Parse(r.GetString(i));
        long? L(SqliteDataReader r, int i) => r.IsDBNull(i) ? (long?)null : r.GetInt64(i);
        var list = new List<MaintenanceRow>();
        using var rr = cmd.ExecuteReader();
        while (rr.Read())
            list.Add(new MaintenanceRow(rr.GetString(0), rr.GetString(1), rr.GetString(2),
                rr.IsDBNull(3) ? null : rr.GetString(3),
                D(rr, 4), D(rr, 5), L(rr, 6), D(rr, 7), D(rr, 8), L(rr, 9), Convert.ToInt64(rr.GetValue(10)) == 1));
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
WHERE mm.maintenance_id=$mt ORDER BY m.code;";
        cmd.Parameters.AddWithValue("$mt", maintenanceId);
        var list = new List<MaintenanceMaterialRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new MaintenanceMaterialRow(r.GetString(0), r.GetString(1), Money.Parse(r.GetString(2))));
        return list;
    }

    // ================= çekirdek =================
    private sealed record DefRow(decimal IntervalValue, string IntervalUnit);

    private static DefRow? LoadDefinition(SqliteConnection conn, SqliteTransaction tx, string companyId, string defId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT interval_value, interval_unit FROM maintenance_definitions WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", defId);
        cmd.Parameters.AddWithValue("$c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DefRow(Money.Parse(r.GetString(0)), r.GetString(1)) : null;
    }

    private static void InsertMaintenance(SqliteConnection conn, SqliteTransaction tx, string companyId, string id,
        NewMaintenance dto, decimal? nextKm, decimal? nextHour, long? nextDate, string operationId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO vehicle_maintenances(id, company_id, vehicle_id, maintenance_def_id, sub_definition_id, technician_id,
    description, sub_definition_note, performed_km, performed_hour, performed_date,
    next_due_km, next_due_hour, next_due_date, operation_id, is_cancelled, created_at, updated_at, version, is_deleted)
VALUES($id,$c,$v,$d,$sd,$tech,$desc,$sdn,$pk,$ph,$pd,$nk,$nh,$nd,$op,0,$now,$now,1,0);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$v", dto.VehicleId);
        cmd.Parameters.AddWithValue("$d", dto.DefinitionId);
        cmd.Parameters.AddWithValue("$sd", (object?)dto.SubDefinitionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tech", (object?)dto.TechnicianId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", (object?)dto.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sdn", dto.SubDefinitionId is null ? DBNull.Value : (object?)dto.SubDefinitionNote ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pk", dto.PerformedKm is null ? DBNull.Value : Money.Serialize(dto.PerformedKm.Value));
        cmd.Parameters.AddWithValue("$ph", dto.PerformedHour is null ? DBNull.Value : Money.Serialize(dto.PerformedHour.Value));
        cmd.Parameters.AddWithValue("$pd", (object?)dto.PerformedDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$nk", nextKm is null ? DBNull.Value : Money.Serialize(nextKm.Value));
        cmd.Parameters.AddWithValue("$nh", nextHour is null ? DBNull.Value : Money.Serialize(nextHour.Value));
        cmd.Parameters.AddWithValue("$nd", (object?)nextDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$op", operationId);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void ApplyDelta(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId,
        decimal signedQty, long now, bool allowNegative = false)
    {
        decimal current;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=$m;";
            read.Parameters.AddWithValue("$m", materialId);
            current = Money.Parse(read.ExecuteScalar() as string);
        }
        var updated = current + signedQty;
        if (!allowNegative && updated < 0)
            throw new NegativeStockException($"Negatif stok engellendi: mevcut {current}, talep {-signedQty}.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_balances(company_id, material_id, quantity, updated_at) VALUES($c,$m,$q,$now)
ON CONFLICT(material_id) DO UPDATE SET quantity=excluded.quantity, updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$m", materialId);
        cmd.Parameters.AddWithValue("$q", Money.Serialize(updated));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertUsageMovement(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId,
        string maintenanceId, decimal qty, decimal? price, string operationId, long now, bool reverse = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, movement_type, direction, quantity,
    unit_price, currency_code, fx_rate, operation_id, note, created_at, document_id, is_reversed, reverses_movement_id)
VALUES($id,$c,$m,NULL,$type,$dir,$q,$price,'TRY',NULL,$op,$note,$now,NULL,0,NULL);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$m", materialId);
        cmd.Parameters.AddWithValue("$type", reverse ? "usage_reverse" : "usage");
        cmd.Parameters.AddWithValue("$dir", reverse ? 1 : -1);
        cmd.Parameters.AddWithValue("$q", Money.Serialize(qty));
        cmd.Parameters.AddWithValue("$price", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.Parameters.AddWithValue("$op", operationId);
        cmd.Parameters.AddWithValue("$note", maintenanceId);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertMaintenanceMaterial(SqliteConnection conn, SqliteTransaction tx, string maintenanceId,
        string materialId, decimal qty, decimal? price)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO maintenance_materials(id, maintenance_id, material_id, quantity, unit_price) VALUES($id,$mt,$m,$q,$p);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$mt", maintenanceId);
        cmd.Parameters.AddWithValue("$m", materialId);
        cmd.Parameters.AddWithValue("$q", Money.Serialize(qty));
        cmd.Parameters.AddWithValue("$p", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.ExecuteNonQuery();
    }

    private void AdvanceMeterInTx(SqliteConnection conn, SqliteTransaction tx, string companyId, string vehicleId,
        string unit, decimal? performedKm, decimal? performedHour, long now)
    {
        var incoming = unit == "hour" ? performedHour : performedKm;
        if (incoming is null) return;
        decimal current;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT current_meter FROM vehicles WHERE id=$id AND company_id=$c;";
            read.Parameters.AddWithValue("$id", vehicleId);
            read.Parameters.AddWithValue("$c", companyId);
            current = Money.Parse(read.ExecuteScalar() as string);
        }
        if (!MeterRule.ShouldAdvance(current, incoming.Value)) return;
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE vehicles SET current_meter=$m, version=version+1, updated_at=$now WHERE id=$id;";
            upd.Parameters.AddWithValue("$m", Money.Serialize(incoming.Value));
            upd.Parameters.AddWithValue("$now", now);
            upd.Parameters.AddWithValue("$id", vehicleId);
            upd.ExecuteNonQuery();
        }
        using var log = conn.CreateCommand();
        log.Transaction = tx;
        log.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES($id,$c,$v,$o,$n,'maintenance',$now);";
        log.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        log.Parameters.AddWithValue("$c", companyId);
        log.Parameters.AddWithValue("$v", vehicleId);
        log.Parameters.AddWithValue("$o", Money.Serialize(current));
        log.Parameters.AddWithValue("$n", Money.Serialize(incoming.Value));
        log.Parameters.AddWithValue("$now", now);
        log.ExecuteNonQuery();
    }

    private static decimal ReadMaterialPrice(SqliteConnection conn, SqliteTransaction tx, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT unit_price FROM materials WHERE id=$m;";
        cmd.Parameters.AddWithValue("$m", materialId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private static string? FindByOperation(SqliteConnection conn, SqliteTransaction tx, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM vehicle_maintenances WHERE operation_id=$op;";
        cmd.Parameters.AddWithValue("$op", operationId);
        return cmd.ExecuteScalar() as string;
    }

    private static bool? LoadMaintenanceStatus(SqliteConnection conn, SqliteTransaction tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT is_cancelled FROM vehicle_maintenances WHERE id=$id AND company_id=$c;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", companyId);
        var v = cmd.ExecuteScalar();
        return v is null ? null : Convert.ToInt64(v) == 1;
    }

    private static IEnumerable<(string MaterialId, decimal Qty, decimal? Price)> LoadMaintenanceMaterials(
        SqliteConnection conn, SqliteTransaction tx, string maintenanceId)
    {
        var list = new List<(string, decimal, decimal?)>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT material_id, quantity, unit_price FROM maintenance_materials WHERE maintenance_id=$mt;";
        cmd.Parameters.AddWithValue("$mt", maintenanceId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), Money.Parse(r.GetString(1)), r.IsDBNull(2) ? null : Money.Parse(r.GetString(2))));
        return list;
    }

    private static void EnsureVehicleOwned(SqliteConnection conn, SqliteTransaction tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", vehicleId);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
    }

    private static void EnsureMaterialOwned(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", materialId);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }
}
