using System.Text.RegularExpressions;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Vehicles;

public sealed record NewVehicleTemplate(
    string Name, string? InternalCode = null, string? VehicleTypeId = null, string? CategoryId = null,
    string? BrandId = null, string? VehicleModelId = null, int? ProductionYear = null, string DefaultMeterUnit = "km");

public sealed record VehicleTemplateRecord(
    string Id, string Name, string? InternalCode, string? VehicleTypeId, string? CategoryId,
    string? BrandId, string? VehicleModelId, int? ProductionYear, string DefaultMeterUnit);

public sealed record VehicleTemplateRow(
    string Id, string Name, string? InternalCode,
    string? TypeName, string? CategoryName, string? BrandName, string? ModelName, int? ProductionYear,
    string? VehicleTypeId, string? CategoryId, string? BrandId, string? VehicleModelId)
{
    public string TypeDisplay => TypeName ?? "—";
    public string BrandModelDisplay => string.IsNullOrEmpty(BrandName) ? "—" : $"{BrandName} {ModelName}".Trim();
    public string CodeDisplay => string.IsNullOrEmpty(InternalCode) ? "—" : InternalCode!;
    public string YearDisplay => ProductionYear is > 0 ? ProductionYear!.Value.ToString() : "—";
    public override string ToString() => Name;
}

public sealed record TemplateMaterialRow(string Id, string Code, string Name);

/// <summary>
/// Araç şablonu (Araç Genel Tanım) — CRUD + uyumlu malzeme (tam değiştir) + otomatik iç kod üretimi.
/// Tenant + "vehicles" permission fail-closed.
/// </summary>
public sealed class VehicleTemplateService
{
    private const string Module = "vehicle_templates"; // #15: ayrı yetki (eski: vehicles)
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public VehicleTemplateService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext s, NewVehicleTemplate dto, IEnumerable<string>? materialIds = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO vehicle_templates(id, company_id, name, internal_code, vehicle_type_id, category_id, brand_id,
    vehicle_model_id, production_year, default_meter_unit, created_by, is_global, created_at, updated_at, version, is_deleted)
VALUES($id,$c,$n,$ic,$vt,$cat,$br,$vm,$yr,$mu,$by,$g,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$by", s.UserId);
            cmd.Parameters.AddWithValue("$g", AccessControl.IsAdmin(s) ? 1 : 0); // admin şablonu herkese; diğeri kişisel
            cmd.Parameters.AddWithValue("$n", dto.Name);
            cmd.Parameters.AddWithValue("$ic", (object?)dto.InternalCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vt", (object?)dto.VehicleTypeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$br", (object?)dto.BrandId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vm", (object?)dto.VehicleModelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$yr", (object?)dto.ProductionYear ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mu", dto.DefaultMeterUnit);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        if (materialIds is not null) ReplaceMaterials(conn, tx, id, materialIds);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_template", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Şablonun uyumlu malzemelerini TAM değiştirir (eskiyi siler, seçilenleri yazar).</summary>
    public void SetMaterials(SessionContext s, string templateId, IEnumerable<string> materialIds)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, templateId);
        ReplaceMaterials(conn, tx, templateId, materialIds);
        tx.Commit();
    }

    public IReadOnlyList<string> GetMaterials(string templateId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT material_id FROM vehicle_template_materials WHERE template_id=$t;";
        cmd.Parameters.AddWithValue("$t", templateId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public VehicleTemplateRecord? Get(SessionContext s, string templateId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, name, internal_code, vehicle_type_id, category_id, brand_id, vehicle_model_id,
    production_year, default_meter_unit FROM vehicle_templates WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", templateId);
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new VehicleTemplateRecord(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetInt32(7), r.GetString(8));
    }

    /// <summary>Şablon listesi (salt okuma) — lookup adlarıyla; ad araması.</summary>
    public IReadOnlyList<VehicleTemplateRow> List(SessionContext s, string? search = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT t.id, t.name, t.internal_code, vt.name, vc.name, b.name, vm.name, t.production_year,
       t.vehicle_type_id, t.category_id, t.brand_id, t.vehicle_model_id
FROM vehicle_templates t
LEFT JOIN vehicle_types vt ON vt.id = t.vehicle_type_id
LEFT JOIN vehicle_categories vc ON vc.id = t.category_id
LEFT JOIN brands b ON b.id = t.brand_id
LEFT JOIN vehicle_models vm ON vm.id = t.vehicle_model_id
WHERE t.company_id=$c AND t.is_deleted=0
  AND (t.is_global=1 OR t.created_by=$me)
  AND ($s IS NULL OR t.name LIKE $like OR COALESCE(t.internal_code,'') LIKE $like)
ORDER BY t.name LIMIT $lim;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$me", s.UserId);
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.Parameters.AddWithValue("$s", (object?)term ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$like", term is null ? "%" : "%" + term + "%");
        cmd.Parameters.AddWithValue("$lim", limit);
        string? S(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        var list = new List<VehicleTemplateRow>();
        using var rr = cmd.ExecuteReader();
        while (rr.Read())
            list.Add(new VehicleTemplateRow(rr.GetString(0), rr.GetString(1), S(rr, 2),
                S(rr, 3), S(rr, 4), S(rr, 5), S(rr, 6), rr.IsDBNull(7) ? (int?)null : rr.GetInt32(7),
                S(rr, 8), S(rr, 9), S(rr, 10), S(rr, 11)));
        return list;
    }

    /// <summary>Şablon alanlarını günceller (uyumlu malzemeler SetMaterials ile ayrı).</summary>
    public void Update(SessionContext s, string templateId, NewVehicleTemplate dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE vehicle_templates SET name=$n, internal_code=$ic, vehicle_type_id=$vt, category_id=$cat,
    brand_id=$br, vehicle_model_id=$vm, production_year=$yr, default_meter_unit=$mu,
    version=version+1, updated_at=$now
WHERE id=$id AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$n", dto.Name);
            cmd.Parameters.AddWithValue("$ic", (object?)dto.InternalCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vt", (object?)dto.VehicleTypeId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$br", (object?)dto.BrandId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$vm", (object?)dto.VehicleModelId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$yr", (object?)dto.ProductionYear ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$mu", dto.DefaultMeterUnit);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", templateId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_template", templateId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Şablonun uyumlu malzemeleri (kod/ad ile) — detay gösterimi.</summary>
    public IReadOnlyList<TemplateMaterialRow> GetMaterialRows(SessionContext s, string templateId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.id, m.code, m.name FROM vehicle_template_materials tm
JOIN materials m ON m.id = tm.material_id AND m.company_id=$c AND m.is_deleted=0
WHERE tm.template_id=$t ORDER BY m.code;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$t", templateId);
        var list = new List<TemplateMaterialRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new TemplateMaterialRow(r.GetString(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    /// <summary>Şablon soft-delete.</summary>
    public void Delete(SessionContext s, string templateId)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vehicle_templates SET is_deleted=1, version=version+1, updated_at=$now WHERE id=$id AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", templateId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_template", templateId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Örnek koddan (ör. KM-001) sonraki iç kodu üretir: önek + en büyük numara + 1 (genişlik korunur).</summary>
    public string GenerateNextInternalCode(SessionContext s, string baseCode)
    {
        var m = Regex.Match(baseCode ?? "", @"^(.*?)(\d+)\s*$");
        if (!m.Success) return baseCode ?? "";
        var prefix = m.Groups[1].Value;
        var width = m.Groups[2].Value.Length;
        long max = long.Parse(m.Groups[2].Value);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT internal_code FROM vehicles WHERE company_id=$c AND internal_code LIKE $like;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$like", prefix + "%");
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var code = r.GetString(0);
                var mm = Regex.Match(code, @"^(.*?)(\d+)\s*$");
                if (mm.Success && mm.Groups[1].Value == prefix && long.TryParse(mm.Groups[2].Value, out var n))
                    max = Math.Max(max, n);
            }
        }
        var next = max + 1;
        return prefix + next.ToString().PadLeft(width, '0');
    }

    private static void ReplaceMaterials(SqliteConnection conn, SqliteTransaction tx, string templateId, IEnumerable<string> materialIds)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM vehicle_template_materials WHERE template_id=$t;";
            del.Parameters.AddWithValue("$t", templateId);
            del.ExecuteNonQuery();
        }
        foreach (var mid in materialIds.Distinct())
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO vehicle_template_materials(template_id, material_id) VALUES($t,$m);";
            ins.Parameters.AddWithValue("$t", templateId);
            ins.Parameters.AddWithValue("$m", mid);
            ins.ExecuteNonQuery();
        }
    }

    private static void EnsureOwned(SqliteConnection conn, SqliteTransaction tx, string companyId, string templateId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicle_templates WHERE id=$id AND company_id=$c;";
        cmd.Parameters.AddWithValue("$id", templateId);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Şablon bulunamadı veya başka firmaya ait.");
    }
}
