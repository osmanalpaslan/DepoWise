using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
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

/// <summary>Malzeme listesi (kolon-bazlı filtre + sayfalama) satırı — <see cref="MaterialListColumns"/>'taki
/// HER kolonun görüntü değerini taşır; ekran hangi kolonları göstereceğine kendi tercihine göre karar verir.</summary>
public sealed record MaterialGridRow(
    string Id, string Code, string Name, string? Type, string? Category, string? Unit, string? Brand,
    string? Supplier, decimal UnitPrice, string Currency, decimal MinStock, decimal Stock, string Status,
    string? Description, string? CompatibleVehicles, string? Equivalents);

/// <summary>Her alan için kullanıcının o kolona yazdığı filtre metni (boş/null = filtre yok). Sıra
/// <see cref="MaterialListColumns.All"/> ile AYNIDIR (birden çok filtre aktifken deterministik öncelik).</summary>
public sealed record MaterialGridFilter(
    string? Code = null, string? Name = null, string? Type = null, string? Category = null, string? Unit = null,
    string? Brand = null, string? Supplier = null, string? UnitPrice = null, string? Currency = null,
    string? MinStock = null, string? Stock = null, string? Status = null, string? Description = null,
    string? CompatibleVehicles = null, string? Equivalents = null);

public sealed record MaterialDetail(
    string Id, string Code, string Name, string? Type,
    string? CategoryId, string? UnitId, string? BrandId, string? SupplierId,
    string? CategoryName, string? UnitName, string? BrandName, string? SupplierName,
    decimal MinStock, decimal UnitPrice, string Currency, string? Description, decimal Stock,
    IReadOnlyList<MaterialRefRow> Equivalents, IReadOnlyList<MaterialRefRow> CompatibleVehicles);

public sealed record UpdateMaterial(
    string Code, string Name, string? Type = null,
    string? CategoryId = null, string? UnitId = null, string? BrandId = null, string? SupplierId = null,
    decimal MinStock = 0m, decimal UnitPrice = 0m, string? Description = null);

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
            cmd.Parameters.AddWithValue("$type", (object?)DepoWise.Application.Ui.MaterialType.Normalize(dto.Type) ?? DBNull.Value);
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

    /// <summary>
    /// İÇE AKTARIM için firmanın TÜM malzeme kodları → id (SAYFALAMA YOK).
    ///
    /// ⚠️ NEDEN AYRI METOT: <see cref="List"/> PageRequest kullanır ve <c>PageRequest.MaxLimit = 200</c>
    /// ile SINIRLIDIR — Limit=100000 verilse bile 200 satır döner. İçe aktarım mükerrer kontrolünü buna
    /// dayandırmak, 200'den fazla malzemesi olan firmada 201. kayıttan sonrasını "yok" sanıp KOPYA
    /// oluştururdu. Satır başına IsCodeUnique çağırmak da 2600 satırda 2600 sorgu demekti.
    /// </summary>
    public Dictionary<string, string> AllCodeToId(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT code, id FROM materials WHERE company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0).Trim().ToUpperInvariant()] = r.GetString(1);
        return map;
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

        string? code, name, type, catId, unitId, brandId, supId, catName, unitName, brandName, supName, desc, cur;
        decimal minStock, unitPrice, stock;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT m.code, m.name, m.type, m.category_id, m.unit_id, m.brand_id, m.supplier_id,
       mc.name, u.name, b.name, sup.name,
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
            catId = r.IsDBNull(3) ? null : r.GetString(3);
            unitId = r.IsDBNull(4) ? null : r.GetString(4);
            brandId = r.IsDBNull(5) ? null : r.GetString(5);
            supId = r.IsDBNull(6) ? null : r.GetString(6);
            catName = r.IsDBNull(7) ? null : r.GetString(7);
            unitName = r.IsDBNull(8) ? null : r.GetString(8);
            brandName = r.IsDBNull(9) ? null : r.GetString(9);
            supName = r.IsDBNull(10) ? null : r.GetString(10);
            minStock = Money.Parse(r.GetString(11));
            unitPrice = Money.Parse(r.GetString(12));
            cur = r.GetString(13);
            desc = r.IsDBNull(14) ? null : r.GetString(14);
            stock = Money.Parse(r.GetString(15));
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

        return new MaterialDetail(materialId, code!, name!, type, catId, unitId, brandId, supId,
            catName, unitName, brandName, supName,
            minStock, unitPrice, cur!, desc, stock, equivalents, vehicles);
    }

    /// <summary>Malzeme alanlarını günceller (kod benzersiz; FK alanları). Stok bu serviste değişmez.</summary>
    public void Update(SessionContext s, string materialId, UpdateMaterial dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new ArgumentException("Kod zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (CodeExists(conn, tx, s.CompanyId, dto.Code, excludeId: materialId))
            throw new InvalidOperationException($"Bu kod zaten kullanılıyor: {dto.Code}");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE materials SET code=$code, name=$name, type=$type, category_id=$cat, unit_id=$unit,
    brand_id=$brand, supplier_id=$sup, min_stock=$min, unit_price=$price, description=$desc,
    version=version+1, updated_at=$now
WHERE id=$id AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$code", dto.Code.Trim());
            cmd.Parameters.AddWithValue("$name", dto.Name);
            cmd.Parameters.AddWithValue("$type", (object?)DepoWise.Application.Ui.MaterialType.Normalize(dto.Type) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cat", (object?)dto.CategoryId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$unit", (object?)dto.UnitId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$brand", (object?)dto.BrandId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sup", (object?)dto.SupplierId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$min", Money.Serialize(dto.MinStock));
            cmd.Parameters.AddWithValue("$price", Money.Serialize(dto.UnitPrice));
            cmd.Parameters.AddWithValue("$desc", (object?)dto.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", materialId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material", materialId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Malzeme soft-delete (is_deleted=1).</summary>
    public void Delete(SessionContext s, string materialId)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE materials SET is_deleted=1, version=version+1, updated_at=$now WHERE id=$id AND company_id=$c AND is_deleted=0;";
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$id", materialId);
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material", materialId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
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

    private const string GridInnerSql = @"
SELECT m.id AS id, m.code AS code, m.name AS name, COALESCE(m.type,'') AS type,
       COALESCE(mc.name,'') AS category, COALESCE(u.name,'') AS unit, COALESCE(b.name,'') AS brand,
       COALESCE(sup.name,'') AS supplier,
       m.unit_price AS unit_price_raw, printf('%.2f', CAST(m.unit_price AS REAL)) AS unit_price_text,
       m.currency_code AS currency,
       m.min_stock AS min_stock_raw, printf('%.2f', CAST(m.min_stock AS REAL)) AS min_stock_text,
       COALESCE(sb.quantity,'0') AS stock_raw, printf('%.2f', CAST(COALESCE(sb.quantity,'0') AS REAL)) AS stock_text,
       CASE WHEN CAST(COALESCE(sb.quantity,'0') AS REAL) <= 0 THEN 'Stok Yok'
            WHEN CAST(COALESCE(sb.quantity,'0') AS REAL) <= CAST(m.min_stock AS REAL) THEN 'Düşük Stok'
            ELSE 'Yeterli' END AS status,
       COALESCE(m.description,'') AS description,
       COALESCE((SELECT GROUP_CONCAT(v.internal_code, ', ') FROM material_compatible_vehicles mcv
                 JOIN vehicles v ON v.id = mcv.vehicle_id WHERE mcv.material_id = m.id), '') AS compatible_vehicles,
       COALESCE((SELECT GROUP_CONCAT(m2.code, ', ') FROM material_equivalents me
                 JOIN materials m2 ON m2.id = me.equivalent_material_id WHERE me.material_id = m.id), '') AS equivalents
FROM materials m
LEFT JOIN material_categories mc ON mc.id = m.category_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN brands b ON b.id = m.brand_id
LEFT JOIN suppliers sup ON sup.id = m.supplier_id
LEFT JOIN stock_balances sb ON sb.material_id = m.id
WHERE m.company_id = $c AND m.is_deleted = 0";

    /// <summary>Kolon bazlı filtre + numaralı sayfalama (kullanıcı isteği 2026-07-17). Her filtre alanı
    /// "içerir" araması yapar; birden çok filtre aktifken sıralama, doldurulan alanların
    /// <see cref="MaterialListColumns.All"/>'daki SIRASINA göre "başlangıca göre" önceliklidir (GridQuery).</summary>
    public GridResult<MaterialGridRow> SearchGrid(SessionContext s, MaterialGridFilter filter, int page, int pageSize,
        string? sortColumn = null, bool sortDesc = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        // Kolon anahtarı → (alias/tür/ham). Hem filtre hem sıralama BUNDAN çözülür (madde 5 sıralama).
        var byKey = new (string Key, GridQuery.ColumnFilter Col)[]
        {
            (MaterialListColumns.Code, new GridQuery.ColumnFilter("t.code", filter.Code)),
            (MaterialListColumns.Name, new GridQuery.ColumnFilter("t.name", filter.Name)),
            (MaterialListColumns.Type, new GridQuery.ColumnFilter("t.type", filter.Type)),
            (MaterialListColumns.Category, new GridQuery.ColumnFilter("t.category", filter.Category)),
            (MaterialListColumns.Unit, new GridQuery.ColumnFilter("t.unit", filter.Unit)),
            (MaterialListColumns.Brand, new GridQuery.ColumnFilter("t.brand", filter.Brand)),
            (MaterialListColumns.Supplier, new GridQuery.ColumnFilter("t.supplier", filter.Supplier)),
            (MaterialListColumns.UnitPrice, new GridQuery.ColumnFilter("t.unit_price_text", filter.UnitPrice, GridQuery.ColumnKind.Numeric, "t.unit_price_raw")),
            (MaterialListColumns.Currency, new GridQuery.ColumnFilter("t.currency", filter.Currency)),
            (MaterialListColumns.MinStock, new GridQuery.ColumnFilter("t.min_stock_text", filter.MinStock, GridQuery.ColumnKind.Numeric, "t.min_stock_raw")),
            (MaterialListColumns.Stock, new GridQuery.ColumnFilter("t.stock_text", filter.Stock, GridQuery.ColumnKind.Numeric, "t.stock_raw")),
            (MaterialListColumns.Status, new GridQuery.ColumnFilter("t.status", filter.Status)),
            (MaterialListColumns.Description, new GridQuery.ColumnFilter("t.description", filter.Description)),
            (MaterialListColumns.CompatibleVehicles, new GridQuery.ColumnFilter("t.compatible_vehicles", filter.CompatibleVehicles)),
            (MaterialListColumns.Equivalents, new GridQuery.ColumnFilter("t.equivalents", filter.Equivalents)),
        };
        var cols = System.Array.ConvertAll(byKey, x => x.Col);
        GridQuery.ColumnFilter? sort = null;
        if (sortColumn is not null)
            foreach (var x in byKey) if (x.Key == sortColumn) { sort = x.Col; break; }
        var (whereSql, orderSql, ps) = GridQuery.Build(cols, "t.code", sort, sortDesc);

        using var conn = _factory.Create();
        int total;
        using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = $"SELECT COUNT(*) FROM ({GridInnerSql}) t {whereSql};";
            cnt.Parameters.AddWithValue("$c", s.CompanyId);
            GridQuery.AddParams(cnt, ps);
            total = Convert.ToInt32(cnt.ExecuteScalar());
        }

        var items = new List<MaterialGridRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT * FROM ({GridInnerSql}) t {whereSql}{orderSql}LIMIT $lim OFFSET $off;";
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            GridQuery.AddParams(cmd, ps);
            cmd.Parameters.AddWithValue("$lim", pageSize);
            cmd.Parameters.AddWithValue("$off", (page - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                items.Add(new MaterialGridRow(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                    Money.Parse(r.GetString(8)), r.GetString(10),
                    Money.Parse(r.GetString(11)), Money.Parse(r.GetString(13)),
                    r.GetString(15), r.GetString(16), r.GetString(17), r.GetString(18)));
        }
        return new GridResult<MaterialGridRow>(items, total, page, pageSize);
    }

    /// <summary>Filtrelenmiş/sıralanmış TÜM sonuçları (sayfalama sınırı YOK) döner — "Excel'e Aktar" butonu
    /// için (kullanıcı isteği 2026-07-19: sayfadaki değil, filtrelenmiş TÜM sonuç kümesi indirilmeli).
    /// SearchGrid'i 500'lük sayfalarla iç döngüde gezer (o metodun 500 sınırı BOZULMAZ).</summary>
    public IReadOnlyList<MaterialGridRow> SearchGridAll(SessionContext s, MaterialGridFilter filter, string? sortColumn = null, bool sortDesc = false)
    {
        var all = new List<MaterialGridRow>();
        int page = 1;
        while (true)
        {
            var res = SearchGrid(s, filter, page, 500, sortColumn, sortDesc);
            all.AddRange(res.Items);
            if (page >= res.TotalPages || res.Items.Count == 0) break;
            page++;
        }
        return all;
    }

    /// <summary>Grid satırlarını Excel tablosuna çevirir — kolon sırası <see cref="MaterialListColumns.All"/>
    /// ile AYNIDIR. Görünür kolon seçimi (kişiye özel) buraya YANSITILMAZ — dışa aktarım DAİMA tam alan seti
    /// verir (kullanıcının o an ekranda gizlediği bir kolon export'ta eksik kalmasın).</summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<MaterialGridRow> rows)
    {
        var headers = MaterialListColumns.All.Select(c => c.Label).ToList();
        var body = rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Code, r.Name, r.Type, r.Category, r.Unit, r.Brand, r.Supplier,
            r.UnitPrice, r.Currency, r.MinStock, r.Stock, r.Status, r.Description,
            r.CompatibleVehicles, r.Equivalents,
        }).ToList();
        return new Application.Reports.TableModel("Malzemeler", headers, body);
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
