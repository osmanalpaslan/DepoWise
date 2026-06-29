using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Maintenance;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Operations;

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
    public string TypeText => ActivityType == "maintenance" ? "Bakım"
        : MovementKind == "transfer" ? "Transfer" : "Hareket";
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
    private readonly IClock _clock;

    public DailyActivityService(IDbConnectionFactory factory, MaintenanceService maintenance, IClock? clock = null)
    {
        _factory = factory;
        _maintenance = maintenance;
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
            maintenance.Description, maintenanceId, stockProcessed: true, maintenance.PerformedDate ?? now, operationId, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "daily_activity", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
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
        InsertActivity(conn, tx, id, s.CompanyId, "movement", dto.MovementKind, dto.VehicleId, dto.FromLocationId,
            dto.ToLocationId, dto.OperatorId, dto.DurationDays, dto.Description, null, stockProcessed: false,
            dto.ActivityDate ?? now, operationId, now);

        if (dto.MovementKind == "transfer" && dto.VehicleId is not null)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE vehicles SET status='passive', version=version+1, updated_at=$now " +
                "WHERE id=$v AND company_id=$c AND status<>'passive';";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$v", dto.VehicleId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
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
            "FROM daily_activities WHERE company_id=$c AND vehicle_id=$v AND is_deleted=0 " +
            (activityType is null ? "" : "AND activity_type=$t ") +
            "ORDER BY activity_date DESC;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$v", vehicleId);
        if (activityType is not null) cmd.Parameters.AddWithValue("$t", activityType);
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
LEFT JOIN vehicles v ON v.id = da.vehicle_id
LEFT JOIN branches fb ON fb.id = da.from_location_id
LEFT JOIN branches tb ON tb.id = da.to_location_id
LEFT JOIN personnel p ON p.id = da.operator_id
WHERE da.company_id = $c AND da.is_deleted = 0
  AND ($t IS NULL OR da.activity_type = $t)
ORDER BY da.activity_date DESC, da.created_at DESC LIMIT $lim;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$t", (object?)activityType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$lim", limit);
        string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        var list = new List<DailyActivityListRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new DailyActivityListRow(r.GetString(0), r.GetString(1), S(r, 2),
                S(r, 3), S(r, 4), S(r, 5), S(r, 6), S(r, 7),
                r.IsDBNull(8) ? (int?)null : r.GetInt32(8), S(r, 9), r.GetInt64(10), S(r, 11)));
        return list;
    }

    /// <summary>Günlük faaliyeti soft-delete eder. Bakım tipinde bağlı bakım kaydı Bakım ekranında kalır (orada iptal edilir).</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE daily_activities SET is_deleted=1, updated_at=$now WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Faaliyet bulunamadı veya başka firmaya ait.");
    }

    private string? FindActivity(string operationId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id FROM daily_activities WHERE operation_id=$op;";
        cmd.Parameters.AddWithValue("$op", operationId);
        return cmd.ExecuteScalar() as string;
    }

    private static void InsertActivity(SqliteConnection conn, SqliteTransaction tx, string id, string companyId,
        string activityType, string? movementKind, string? vehicleId, string? fromLoc, string? toLoc, string? operatorId,
        int? durationDays, string? description, string? maintenanceId, bool stockProcessed, long activityDate,
        string operationId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO daily_activities(id, company_id, activity_type, movement_kind, vehicle_id, from_location_id, to_location_id,
    operator_id, duration_days, description, maintenance_id, source_module, stock_processed, activity_date, operation_id,
    created_at, updated_at, version, is_deleted)
VALUES($id,$c,$at,$mk,$v,$from,$to,$op2,$dur,$desc,$mid,'daily_activity',$sp,$ad,$op,$now,$now,1,0);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$at", activityType);
        cmd.Parameters.AddWithValue("$mk", (object?)movementKind ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$v", (object?)vehicleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$from", (object?)fromLoc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to", (object?)toLoc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$op2", (object?)operatorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dur", (object?)durationDays ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$desc", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$mid", (object?)maintenanceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$sp", stockProcessed ? 1 : 0);
        cmd.Parameters.AddWithValue("$ad", activityDate);
        cmd.Parameters.AddWithValue("$op", operationId);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }
}
