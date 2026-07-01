using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Materials;

public sealed record LookupItem(string Id, string Name);

/// <summary>
/// Tanımlar CRUD (kategori/marka/birim/tedarikçi) — tenant + "definitions" permission.
/// Benzersizlik DB UNIQUE index'leri ile; hatalar fail-closed.
/// </summary>
public sealed class LookupService
{
    private const string Module = "definitions";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public LookupService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string AddCategory(SessionContext s, string name, string? parentId = null)
        => Insert(s, "material_categories", name, ("parent_id", (object?)parentId ?? DBNull.Value));

    public string AddBrand(SessionContext s, string name, string brandType = "material")
        => Insert(s, "brands", name, ("brand_type", brandType));

    public string AddUnit(SessionContext s, string name) => Insert(s, "units", name);

    public string AddSupplier(SessionContext s, string name) => Insert(s, "suppliers", name);

    // ── Araç tanımları ──
    public string AddVehicleType(SessionContext s, string name) => Insert(s, "vehicle_types", name);
    public string AddVehicleCategory(SessionContext s, string name) => Insert(s, "vehicle_categories", name);
    public string AddVehicleBrand(SessionContext s, string name) => Insert(s, "brands", name, ("brand_type", "vehicle"));
    public string AddBranch(SessionContext s, string name) => Insert(s, "branches", name, ("kind", "site"));
    public string AddVehicleModel(SessionContext s, string brandId, string name)
        => Insert(s, "vehicle_models", name, ("brand_id", brandId));

    /// <summary>Bir markanın araç modelleri.</summary>
    public IReadOnlyList<LookupItem> ListVehicleModels(SessionContext s, string brandId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM vehicle_models WHERE company_id=$c AND is_deleted=0 AND brand_id=$b ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$b", brandId);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1)));
        return list;
    }

    // ── Personel (full_name kolonu → özel sorgu) ──
    public IReadOnlyList<LookupItem> ListPersonnel(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, full_name FROM personnel WHERE company_id=$c AND is_deleted=0 AND is_active=1 ORDER BY full_name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1)));
        return list;
    }

    public string AddPersonnel(SessionContext s, string fullName, string? title = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO personnel(id, company_id, full_name, title, is_active, created_at, updated_at, version, is_deleted) " +
                "VALUES($id,$c,$n,$t,1,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$n", fullName);
            cmd.Parameters.AddWithValue("$t", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "personnel", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    public IReadOnlyList<LookupItem> List(SessionContext s, string table)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        EnsureKnownTable(table);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id, name FROM {table} WHERE company_id = $c AND is_deleted = 0 ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>Malzeme kategorileri — parentId null ise üst seviye, doluysa o kategorinin alt kategorileri.</summary>
    public IReadOnlyList<LookupItem> ListCategories(SessionContext s, string? parentId = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name FROM material_categories
WHERE company_id=$c AND is_deleted=0
  AND (($p IS NULL AND parent_id IS NULL) OR parent_id=$p)
ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$p", (object?)parentId ?? DBNull.Value);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>Markalar — tür filtreli (material/vehicle); tür belirtilmemiş eski kayıtlar da gelir.</summary>
    public IReadOnlyList<LookupItem> ListBrands(SessionContext s, string brandType = "material")
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name FROM brands
WHERE company_id=$c AND is_deleted=0 AND (brand_type=$t OR brand_type IS NULL)
ORDER BY name;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$t", brandType);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1)));
        return list;
    }

    private string Insert(SessionContext s, string table, string name, params (string Col, object Val)[] extra)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        EnsureKnownTable(table);
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        var cols = "id, company_id, name, created_at, updated_at, version, is_deleted";
        var vals = "$id, $c, $n, $now, $now, 1, 0";
        foreach (var (col, _) in extra) { cols += $", {col}"; vals += $", ${col}"; }

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO {table}({cols}) VALUES({vals});";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$n", name);
            cmd.Parameters.AddWithValue("$now", now);
            foreach (var (col, val) in extra) cmd.Parameters.AddWithValue($"${col}", val);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Tanımı yeniden adlandır (tenant + "definitions"/Edit).</summary>
    public void Rename(SessionContext s, string table, string id, string newName)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        EnsureKnownTable(table);
        if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("Ad boş olamaz.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET name=$n, updated_at=$now, version=version+1 " +
                              "WHERE id=$id AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$n", newName.Trim());
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Tanımı soft-delete et (tenant + "definitions"/Delete). Referanslar id ile korunur.</summary>
    public void Delete(SessionContext s, string table, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        EnsureKnownTable(table);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET is_deleted=1, updated_at=$now, version=version+1 " +
                              "WHERE id=$id AND company_id=$c;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    private static void EnsureKnownTable(string table)
    {
        if (table is not ("material_categories" or "brands" or "units" or "suppliers"
            or "vehicle_types" or "vehicle_categories" or "vehicle_models" or "branches"))
            throw new ArgumentException($"Bilinmeyen tanım tablosu: {table}");
    }
}
