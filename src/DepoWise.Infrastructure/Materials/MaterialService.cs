using System.Globalization;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Materials;

public sealed record NewMaterial(
    string Code, string Name, string? Type = null,
    string? CategoryId = null, string? UnitId = null, string? BrandId = null, string? SupplierId = null,
    decimal MinStock = 0m, decimal UnitPrice = 0m, string Currency = "TRY",
    string? Description = null, string? ExternalEquivalentNote = null);

public sealed record MaterialRecord(
    string Id, string CompanyId, string Code, string Name, string? Type,
    decimal MinStock, decimal UnitPrice, string Currency, long CreatedAt);

public sealed record MaterialStock(string MaterialId, string Code, string Name, decimal Quantity);

public sealed record MaterialRefRow(string Id, string Code, string Name);

public sealed record MaterialDetail(
    string Id, string Code, string Name, string? Type,
    string? CategoryName, string? UnitName, string? BrandName, string? SupplierName,
    decimal MinStock, decimal UnitPrice, string Currency, string? Description, decimal Stock,
    IReadOnlyList<MaterialRefRow> Equivalents, IReadOnlyList<MaterialRefRow> CompatibleVehicles);

/// <summary>
/// Malzeme kartı — kod benzersiz (tenant), muadil (çift yönlü, döngü güvenli), uyumlu araç (çoklu seçim).
/// Para decimal + currency. Stok bakiyesi BU SERVİSTE değiştirilmez (ledger ayrı servis).
/// </summary>
public sealed class MaterialService
{
    private const string Module = "materials";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MaterialService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Create(SessionContext s, NewMaterial dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new ArgumentException("Kod zorunlu.");
        if (!Money.IsSupported(dto.Currency)) throw new ArgumentException($"Desteklenmeyen para birimi: {dto.Currency}");

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (CodeExists(conn, tx, s.CompanyId, dto.Code, excludeId: null))
            throw new InvalidOperationException($"Bu kod zaten kullanılıyor: {dto.Code}");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO materials(id, company_id, code, name, type, category_id, unit_id, brand_id, supplier_id,
    min_stock, unit_price, currency_code, description, external_equivalent_note,
    created_at, updated_at, version, is_deleted)
VALUES($id,$c,$code,$name,$type,$cat,$unit,$brand,$sup,$min,$price,$cur,$desc,$eqnote,$now,$now,1,0);";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$code", dto.Code.Trim());
            cmd.Parameters.AddWithValue("$name", dto.Name);
            cmd.Parameters.AddWithValue("$type", (object?)dto.Type ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$unit", (object?)dto.UnitId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$brand", (object?)dto.BrandId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$min", Money.Serialize(dto.MinStock));
            cmd.Parameters.AddWithValue("$price", Money.Serialize(dto.UnitPrice));
            cmd.Parameters.AddWithValue("$cur", dto.Currency);
            cmd.Parameters.AddWithValue("$desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$eqnote", (object?)dto.ExternalEquivalentNote ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    public bool IsCodeUnique(SessionContext s, string code, string? excludeId = null)
    {
        using var conn = _factory.Create();
        return !CodeExists(conn, null, s.CompanyId, code, excludeId);
    }

    // ---- Muadil (çift yönlü, döngü güvenli) ----
    public void AddEquivalent(SessionContext s, string materialId, string equivalentId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (materialId == equivalentId) throw new InvalidOperationException("Malzeme kendisine muadil olamaz.");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, materialId);
        EnsureOwned(conn, tx, s.CompanyId, equivalentId);
        // Simetrik: her iki yön de yazılır (INSERT OR IGNORE → tekrar güvenli)
        InsertPair(conn, tx, materialId, equivalentId);
        InsertPair(conn, tx, equivalentId, materialId);
        tx.Commit();
    }

    public void RemoveEquivalent(SessionContext s, string materialId, string equivalentId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "DELETE FROM material_equivalents WHERE (material_id=$a AND equivalent_material_id=$b) " +
            "OR (material_id=$b AND equivalent_material_id=$a);";
        cmd.Parameters.AddWithValue("$a", materialId);
        cmd.Parameters.AddWithValue("$b", equivalentId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Muadil grubunu döngü-güvenli BFS ile çözer (transitive; visited set ile sonsuz döngü yok).</summary>
    public IReadOnlyCollection<string> GetEquivalentGroup(string materialId)
    {
        using var conn = _factory.Create();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(materialId);
        visited.Add(materialId);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var n in DirectEquivalents(conn, cur))
                if (visited.Add(n)) queue.Enqueue(n);
        }
        visited.Remove(materialId);
        return visited;
    }

    // ---- Uyumlu araç (çoklu seçim) ----
    public void SetCompatibleVehicles(SessionContext s, string materialId, IEnumerable<string> vehicleIds)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwned(conn, tx, s.CompanyId, materialId);
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM material_compatible_vehicles WHERE material_id=$m;";
            del.Parameters.AddWithValue("$m", materialId);
            del.ExecuteNonQuery();
        }
        foreach (var v in vehicleIds.Distinct())
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO material_compatible_vehicles(material_id, vehicle_id) VALUES($m,$v);";
            ins.Parameters.AddWithValue("$m", materialId);
            ins.Parameters.AddWithValue("$v", v);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Bir aracın uyumlu malzemelerini GÜNCEL STOĞUYLA döndürür (araç detayı stok gösterimi).</summary>
    public IReadOnlyList<MaterialStock> MaterialsForVehicle(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.id, m.code, m.name, COALESCE(b.quantity,'0')
FROM material_compatible_vehicles mcv
JOIN materials m ON m.id = mcv.material_id AND m.company_id = $c AND m.is_deleted = 0
LEFT JOIN stock_balances b ON b.material_id = m.id
WHERE mcv.vehicle_id = $v;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$v", vehicleId);
        var list = new List<MaterialStock>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaterialStock(r.GetString(0), r.GetString(1), r.GetString(2), Money.Parse(r.GetString(3))));
        return list;
    }

    /// <summary>Malzeme detayı (salt okuma) — tüm alanlar + lookup adları + muadiller + uyumlu araçlar + stok.</summary>
    public MaterialDetail GetDetail(SessionContext s, string materialId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        string? code, name, type, catName, unitName, brandName, supName, desc, cur;
        decimal minStock, unitPrice, stock;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT m.code, m.name, m.type, mc.name, u.name, b.name, sup.name,
       m.min_stock, m.unit_price, m.currency_code, m.description, COALESCE(sb.quantity,'0')
FROM materials m
LEFT JOIN material_categories mc ON mc.id = m.category_id
LEFT JOIN units u   ON u.id = m.unit_id
LEFT JOIN brands b  ON b.id = m.brand_id
LEFT JOIN suppliers sup ON sup.id = m.supplier_id
LEFT JOIN stock_balances sb ON sb.material_id = m.id
WHERE m.id=$id AND m.company_id=$c AND m.is_deleted=0;";
            cmd.Parameters.AddWithValue("$id", materialId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
            code = r.GetString(0); name = r.GetString(1);
            type = r.IsDBNull(2) ? null : r.GetString(2);
            catName = r.IsDBNull(3) ? null : r.GetString(3);
            unitName = r.IsDBNull(4) ? null : r.GetString(4);
            brandName = r.IsDBNull(5) ? null : r.GetString(5);
            supName = r.IsDBNull(6) ? null : r.GetString(6);
            minStock = Money.Parse(r.GetString(7));
            unitPrice = Money.Parse(r.GetString(8));
            cur = r.GetString(9);
            desc = r.IsDBNull(10) ? null : r.GetString(10);
            stock = Money.Parse(r.GetString(11));
        }

        // Muadiller (grup ids → kod/ad)
        var equivIds = GetEquivalentGroup(materialId);
        var equivalents = new List<MaterialRefRow>();
        foreach (var eid in equivIds)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, code, name FROM materials WHERE id=$id AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$id", eid);
            using var r = cmd.ExecuteReader();
            if (r.Read()) equivalents.Add(new MaterialRefRow(r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        // Uyumlu araçlar
        var vehicles = new List<MaterialRefRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT v.id, v.internal_code, COALESCE(v.plate,'')
FROM material_compatible_vehicles mcv JOIN vehicles v ON v.id = mcv.vehicle_id
WHERE mcv.material_id=$m AND v.company_id=$c AND v.is_deleted=0 ORDER BY v.internal_code;";
            cmd.Parameters.AddWithValue("$m", materialId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) vehicles.Add(new MaterialRefRow(r.GetString(0), r.GetString(1), r.GetString(2)));
        }

        return new MaterialDetail(materialId, code!, name!, type, catName, unitName, brandName, supName,
            minStock, unitPrice, cur!, desc, stock, equivalents, vehicles);
    }

    // ---- Liste (arama + keyset) ----
    public PagedResult<MaterialRecord> List(SessionContext s, PageRequest page, string? search = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var limit = page.NormalizedLimit();
        var hasCursor = Cursor.TryDecode(page.Cursor, out var cursor);
        bool hasSearch = !string.IsNullOrWhiteSpace(search);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, code, name, type, min_stock, unit_price, currency_code, created_at FROM materials " +
            "WHERE company_id = $c AND is_deleted = 0 " +
            (hasSearch ? "AND (code LIKE $q OR name LIKE $q) " : "") +
            (hasCursor ? "AND " + TenantSql.KeysetAfterPredicate + " " : "") +
            TenantSql.KeysetOrderBy + " LIMIT $limit;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$limit", limit + 1);
        if (hasSearch) cmd.Parameters.AddWithValue("$q", "%" + search!.Trim() + "%");
        if (hasCursor)
        {
            cmd.Parameters.AddWithValue("$cursorCreatedAt", cursor.CreatedAt);
            cmd.Parameters.AddWithValue("$cursorId", cursor.Id);
        }

        var items = new List<MaterialRecord>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                items.Add(new MaterialRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), Money.Parse(r.GetString(5)), Money.Parse(r.GetString(6)),
                    r.GetString(7), r.GetInt64(8)));
        }
        string? next = null;
        if (items.Count > limit)
        {
            var last = items[limit - 1];
            items.RemoveAt(items.Count - 1);
            next = new Cursor(last.CreatedAt, last.Id).Encode();
        }
        return PagedResult<MaterialRecord>.Of(items, next);
    }

    private static bool CodeExists(SqliteConnection conn, SqliteTransaction? tx, string companyId, string code, string? excludeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE company_id=$c AND code=$code" +
                          (excludeId is null ? ";" : " AND id<>$ex;");
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$code", code.Trim());
        if (excludeId is not null) cmd.Parameters.AddWithValue("$ex", excludeId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static void EnsureOwned(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=$id AND company_id=$c;";
        cmd.Parameters.AddWithValue("$id", materialId);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }

    private static void InsertPair(SqliteConnection conn, SqliteTransaction tx, string a, string b)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO material_equivalents(material_id, equivalent_material_id) VALUES($a,$b);";
        cmd.Parameters.AddWithValue("$a", a);
        cmd.Parameters.AddWithValue("$b", b);
        cmd.ExecuteNonQuery();
    }

    private static List<string> DirectEquivalents(SqliteConnection conn, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT equivalent_material_id FROM material_equivalents WHERE material_id=$m;";
        cmd.Parameters.AddWithValue("$m", materialId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
