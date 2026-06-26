using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

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

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO vehicles(id, company_id, internal_code, plate, production_year, current_meter, meter_unit,
    branch_id, driver_personnel_id, chassis_no, engine_no, status, status_note,
    vehicle_type_id, category_id, brand_id, vehicle_model_id, template_id,
    created_at, updated_at, version, is_deleted)
VALUES($id,$c,$ic,$plate,$yr,$meter,$mu,$br,$drv,$ch,$en,$st,$note,$vt,$cat,$brand,$vm,$tpl,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$ic", applied.InternalCode.Trim());
            cmd.Parameters.AddWithValue("$plate", (object?)applied.Plate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$yr", (object?)applied.ProductionYear ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$meter", Money.Serialize(applied.CurrentMeter));
            cmd.Parameters.AddWithValue("$mu", applied.MeterUnit);
            cmd.Parameters.AddWithValue("$br", (object?)applied.BranchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$drv", (object?)applied.DriverPersonnelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ch", (object?)applied.ChassisNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$en", (object?)applied.EngineNo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$st", applied.Status);
            // Durum açıklaması yalnız 'maintenance' durumunda saklanır
            cmd.Parameters.AddWithValue("$note", applied.Status == "maintenance" ? (object?)applied.StatusNote ?? DBNull.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("$vt", (object?)applied.VehicleTypeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)applied.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$brand", (object?)applied.BrandId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vm", (object?)applied.VehicleModelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$tpl", (object?)applied.TemplateId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
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
        using var tx = conn.BeginTransaction(deferred: false);
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
        using var tx = conn.BeginTransaction(deferred: false);
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

    public IReadOnlyList<(decimal Old, decimal New, string Source)> MeterHistory(string vehicleId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT old_value, new_value, source FROM vehicle_meter_logs WHERE vehicle_id=$v ORDER BY created_at;";
        cmd.Parameters.AddWithValue("$v", vehicleId);
        var list = new List<(decimal, decimal, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((Money.Parse(r.GetString(0)), Money.Parse(r.GetString(1)), r.GetString(2)));
        return list;
    }

    // ---- yardımcılar ----
    private NewVehicle ApplyTemplate(SqliteConnection conn, SqliteTransaction tx, string companyId, NewVehicle dto)
    {
        if (dto.TemplateId is null) return dto;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT vehicle_type_id, category_id, brand_id, vehicle_model_id, production_year, default_meter_unit
FROM vehicle_templates WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", dto.TemplateId);
        cmd.Parameters.AddWithValue("$c", companyId);
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

    private static void CopyTemplateMaterials(SqliteConnection conn, SqliteTransaction tx, string templateId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT OR IGNORE INTO material_compatible_vehicles(material_id, vehicle_id) " +
            "SELECT material_id, $v FROM vehicle_template_materials WHERE template_id=$t;";
        cmd.Parameters.AddWithValue("$v", vehicleId);
        cmd.Parameters.AddWithValue("$t", templateId);
        cmd.ExecuteNonQuery();
    }

    private static decimal ReadMeter(SqliteConnection conn, SqliteTransaction? tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT current_meter FROM vehicles WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", vehicleId);
        cmd.Parameters.AddWithValue("$c", companyId);
        var v = cmd.ExecuteScalar();
        if (v is null) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
        return Money.Parse(v as string);
    }

    private static void UpdateMeter(SqliteConnection conn, SqliteTransaction tx, string vehicleId, decimal value, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE vehicles SET current_meter=$m, version=version+1, updated_at=$now WHERE id=$id;";
        cmd.Parameters.AddWithValue("$m", Money.Serialize(value));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$id", vehicleId);
        cmd.ExecuteNonQuery();
    }

    private static void WriteMeterLog(SqliteConnection conn, SqliteTransaction tx, string companyId, string vehicleId,
        decimal oldVal, decimal newVal, string source, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES($id,$c,$v,$o,$n,$src,$now);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$v", vehicleId);
        cmd.Parameters.AddWithValue("$o", Money.Serialize(oldVal));
        cmd.Parameters.AddWithValue("$n", Money.Serialize(newVal));
        cmd.Parameters.AddWithValue("$src", source);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static bool CodeExists(SqliteConnection conn, SqliteTransaction tx, string companyId, string code)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE company_id=$c AND internal_code=$ic;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$ic", code.Trim());
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
}
