using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Maintenance;

public sealed record NewMaintenanceDefinition(
    string Name, decimal IntervalValue, string IntervalUnit = "km",
    string? ParentDefId = null, string? Description = null);

/// <summary>Bakım tanımı (ana/alt + periyot) + araç kapsamı. Tenant + "maintenance" permission.</summary>
public sealed class MaintenanceDefinitionService
{
    private const string Module = "maintenance";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MaintenanceDefinitionService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext s, NewMaintenanceDefinition dto, IEnumerable<string>? vehicleIds = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.IntervalValue < 0) throw new ArgumentException("Periyot negatif olamaz.");
        if (dto.IntervalUnit is not ("km" or "hour" or "day")) throw new ArgumentException("Geçersiz periyot birimi.");

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO maintenance_definitions(id, company_id, parent_def_id, name, interval_value, interval_unit,
    description, created_at, updated_at, version, is_deleted)
VALUES($id,$c,$p,$n,$iv,$iu,$d,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$p", (object?)dto.ParentDefId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$n", dto.Name);
            cmd.Parameters.AddWithValue("$iv", Money.Serialize(dto.IntervalValue));
            cmd.Parameters.AddWithValue("$iu", dto.IntervalUnit);
            cmd.Parameters.AddWithValue("$d", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        foreach (var vid in vehicleIds?.Distinct() ?? Enumerable.Empty<string>())
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO maintenance_definition_vehicles(definition_id, vehicle_id) VALUES($d,$v);";
            ins.Parameters.AddWithValue("$d", id);
            ins.Parameters.AddWithValue("$v", vid);
            ins.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "maintenance_definition", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }
}
