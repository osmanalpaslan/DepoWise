using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Maintenance;

public sealed record NewMaintenanceDefinition(
    string Name, decimal IntervalValue, string IntervalUnit = "km",
    string? ParentDefId = null, string? Description = null);

public sealed record MaintenanceDefinitionRow(
    string Id, string Name, decimal IntervalValue, string IntervalUnit, string? Description, string? ParentDefId)
{
    public string UnitDisplay => IntervalUnit switch { "hour" => "saat", "day" => "gün", _ => "km" };
    public string IntervalDisplay => $"{IntervalValue:0.##} {UnitDisplay}";
    public override string ToString() => Name;
}

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

    /// <summary>Tanım listesi — parentDefId null ise ANA tanımlar, doluysa o tanımın ALT bakımları.</summary>
    public IReadOnlyList<MaintenanceDefinitionRow> List(SessionContext s, string? parentDefId = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, interval_value, interval_unit, description, parent_def_id
FROM maintenance_definitions
WHERE company_id=$c AND is_deleted=0
  AND (($p IS NULL AND parent_def_id IS NULL) OR parent_def_id=$p)
ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$p", (object?)parentDefId ?? DBNull.Value);
        var list = new List<MaintenanceDefinitionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaintenanceDefinitionRow(r.GetString(0), r.GetString(1), Money.Parse(r.GetString(2)),
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    /// <param name="expectedVersion">DÜZENLEME KİLİDİ: formun açıldığı andaki <c>version</c>. Verilirse ve kayıt
    /// o andan beri değiştiyse <see cref="ConcurrencyException"/> atılır. null = kontrol yok (geriye uyumlu).</param>
    public void Update(SessionContext s, string id, NewMaintenanceDefinition dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (dto.IntervalValue < 0) throw new ArgumentException("Periyot negatif olamaz.");
        if (dto.IntervalUnit is not ("km" or "hour" or "day")) throw new ArgumentException("Geçersiz periyot birimi.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE maintenance_definitions SET name=$n, interval_value=$iv, interval_unit=$iu, description=$d,
    version=version+1, updated_at=$now WHERE id=$id AND company_id=$c AND is_deleted=0"
                + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.Parameters.AddWithValue("$n", dto.Name);
            cmd.Parameters.AddWithValue("$iv", Money.Serialize(dto.IntervalValue));
            cmd.Parameters.AddWithValue("$iu", dto.IntervalUnit);
            cmd.Parameters.AddWithValue("$d", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "maintenance_definitions", id, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Tanım bulunamadı veya başka firmaya ait.");
            }
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "maintenance_definition", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE maintenance_definitions SET is_deleted=1, version=version+1, updated_at=$now WHERE id=$id AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Tanım bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "maintenance_definition", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Tanıma bağlı (periyodik takip edilen) araç id'leri.</summary>
    public IReadOnlyList<string> GetVehicleIds(SessionContext s, string defId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT vehicle_id FROM maintenance_definition_vehicles WHERE definition_id=$d;";
        cmd.Parameters.AddWithValue("$d", defId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Tanımın araç kapsamını TAM değiştirir.</summary>
    public void SetVehicles(SessionContext s, string defId, IEnumerable<string> vehicleIds)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM maintenance_definition_vehicles WHERE definition_id=$d;";
            del.Parameters.AddWithValue("$d", defId);
            del.ExecuteNonQuery();
        }
        foreach (var vid in vehicleIds.Distinct())
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO maintenance_definition_vehicles(definition_id, vehicle_id) VALUES($d,$v);";
            ins.Parameters.AddWithValue("$d", defId);
            ins.Parameters.AddWithValue("$v", vid);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
