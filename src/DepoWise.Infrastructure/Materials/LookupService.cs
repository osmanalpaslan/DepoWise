using System.Text.RegularExpressions;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

public sealed record LookupItem(string Id, string Name, bool IsLocked = false);

/// <summary>
/// Tanımlar CRUD (kategori/marka/birim/tedarikçi) — tenant + "definitions" permission.
/// Benzersizlik DB UNIQUE index'leri ile; hatalar fail-closed.
/// </summary>
public sealed class LookupService
{
    private const string Module = "definitions";
    /// <summary>Yeni tanım / yeniden adlandırma ad üst sınırı (kullanıcı isteği 2026-07-18).</summary>
    public const int MaxNameLength = 50;
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
    public string AddVehicleModel(SessionContext s, string brandId, string name)
        => Insert(s, "vehicle_models", name, ("brand_id", brandId));

    /// <summary>Bir markanın araç modelleri.</summary>
    public IReadOnlyList<LookupItem> ListVehicleModels(SessionContext s, string brandId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM vehicle_models WHERE company_id=@c AND is_deleted=0 AND brand_id=@b ORDER BY name;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@b", brandId);
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
        cmd.CommandText = "SELECT id, full_name FROM personnel WHERE company_id=@c AND is_deleted=0 AND is_active=1 ORDER BY full_name;";
        cmd.AddWithValue("@c", s.CompanyId);
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
                "VALUES(@id,@c,@n,@t,1,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", fullName);
            cmd.AddWithValue("@t", (object?)title ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
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
        cmd.CommandText = $"SELECT id, name, is_locked FROM {table} WHERE company_id = @c AND is_deleted = 0 ORDER BY name;";
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1), r.GetInt64(2) != 0));
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
WHERE company_id=@c AND is_deleted=0
  AND ((CAST(@p AS TEXT) IS NULL AND parent_id IS NULL) OR parent_id=@p)
ORDER BY name;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@p", (object?)parentId ?? DBNull.Value);
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
WHERE company_id=@c AND is_deleted=0 AND (brand_type=@t OR brand_type IS NULL)
ORDER BY name;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@t", brandType);
        var list = new List<LookupItem>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new LookupItem(r.GetString(0), r.GetString(1)));
        return list;
    }

    /// <summary>Baş/son boşluk kırpılır + İÇERDEKİ ardışık 2+ boşluk TEK boşluğa indirilir (kullanıcı isteği
    /// 2026-07-19: "2 adet boşluktan fazla olan boşlukları 1 adet boşluk varmış gibi güncelle" — kopyala-yapıştır
    /// kaynaklı fazladan boşluk satırları gereksiz uzatıyordu). Sekme/satır sonu da boşluk sayılır.</summary>
    internal static string NormalizeSpaces(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    private string Insert(SessionContext s, string table, string name, params (string Col, object Val)[] extra)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        EnsureWritableTable(table);
        name = NormalizeSpaces(name ?? "");
        if (string.IsNullOrEmpty(name)) throw new ArgumentException("Ad boş olamaz.");
        if (name.Length > MaxNameLength) throw new ArgumentException($"Tanım adı en fazla {MaxNameLength} karakter olabilir.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // TEKİLLEŞTİRME (madde 9+11): aynı ad + aynı ayırt edici (parent_id/brand_type/brand_id) zaten varsa
        // YENİ satır AÇMA — mevcut Tanım ID'yi döndür. Böylece aynı isimli birden çok tanım oluşmaz (tek Tanım ID).
        var existing = FindByName(conn, tx, table, s.CompanyId, name, extra);
        if (existing is not null) { tx.Commit(); return existing; }

        var id = Guid.NewGuid().ToString("N");
        var cols = "id, company_id, name, created_at, updated_at, version, is_deleted";
        var vals = "@id, @c, @n, @now, @now, 1, 0";
        foreach (var (col, _) in extra) { cols += $", {col}"; vals += $", @{col}"; }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"INSERT INTO {table}({cols}) VALUES({vals});";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", name);
            cmd.AddWithValue("@now", now);
            foreach (var (col, val) in extra) cmd.AddWithValue($"@{col}", val);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Aynı ad (harf duyarsız) + aynı ayırt edici sütunlarla AKTİF kayıt varsa id'sini döndürür.
    /// Tekilleştirme (dedup) — aynı isimli birden çok tanım oluşmasını engeller.</summary>
    private static string? FindByName(DbConnection conn, DbTransaction tx, string table, string companyId,
        string name, (string Col, object Val)[] extra)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var sql = $"SELECT id FROM {table} WHERE company_id=@c AND is_deleted=0 AND name=@n COLLATE NOCASE";
        foreach (var (col, val) in extra)
            sql += (val is System.DBNull) ? $" AND {col} IS NULL" : $" AND {col}=@{col}";
        sql += " LIMIT 1;";
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@n", name);
        foreach (var (col, val) in extra) if (val is not System.DBNull) cmd.AddWithValue($"@{col}", val);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Tanımı yeniden adlandır (tenant + "definitions"/Edit). Kilitli ("sabit") tanım düzenlenemez.</summary>
    public void Rename(SessionContext s, string table, string id, string newName)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        EnsureWritableTable(table);
        RequireNotLocked(s, table, id, "Sabit tanım düzenlenemez.");
        newName = NormalizeSpaces(newName ?? "");
        if (string.IsNullOrEmpty(newName)) throw new ArgumentException("Ad boş olamaz.");
        if (newName.Length > MaxNameLength) throw new ArgumentException($"Tanım adı en fazla {MaxNameLength} karakter olabilir.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET name=@n, updated_at=@now, version=version+1 " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@n", newName.Trim());
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Tanımı soft-delete et (tenant + "definitions"/Delete). Referanslar id ile korunur.
    /// Kilitli ("sabit") tanım silinemez.</summary>
    public void Delete(SessionContext s, string table, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        EnsureWritableTable(table);
        RequireNotLocked(s, table, id, "Sabit tanım silinemez.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET is_deleted=1, updated_at=@now, version=version+1 " +
                              "WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Tanımı kilitle/kilit aç ("sabit tanım" — kullanıcı isteği 2026-07-19). Yalnız firma/süper
    /// admin. Kilitli tanım yeniden adlandırılamaz/silinemez ama diğer YENİ tanımların eklenmesini etkilemez.</summary>
    public void SetLocked(SessionContext s, string table, string id, bool locked)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Tanım kilidini yalnız yönetici değiştirebilir.");
        EnsureKnownTable(table);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET is_locked=@l, updated_at=@now, version=version+1 " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@l", locked ? 1 : 0);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    private void RequireNotLocked(SessionContext s, string table, string id, string message)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT is_locked FROM {table} WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", s.CompanyId);
        var v = cmd.ExecuteScalar();
        if (v is not null && System.Convert.ToInt64(v) != 0) throw new ArgumentException(message);
    }

    /// <summary>OKUMA için tanınan tablolar. <c>branches</c> burada KALIR (seçim listeleri okur).</summary>
    private static void EnsureKnownTable(string table)
    {
        if (table is not ("material_categories" or "brands" or "units" or "suppliers"
            or "vehicle_types" or "vehicle_categories" or "vehicle_models" or "branches"))
            throw new ArgumentException($"Bilinmeyen tanım tablosu: {table}");
    }

    /// <summary>
    /// YAZMA (ekle/yeniden adlandır/sil) için tanınan tablolar — <c>branches</c> BİLEREK YOKTUR.
    ///
    /// GEREKÇE (2026-08-09 denetimi): Şube/Şantiye tanımları <c>branches</c> modülüne aittir ve o modül
    /// <see cref="DepoWise.Application.Security.AppModules.IsAdminRestricted"/> ile admin-kısıtlıdır.
    /// Bu sınıf ise <c>definitions</c> ("Tanımlar") modülüyle çalışır ve normal rollere verilebilir.
    /// Bu yüzden buradan şube yazımına izin vermek, admin-kısıtlı modülün ATLATILMASI anlamına geliyordu
    /// (ekleme, yeniden adlandırma ve silme dahil).
    ///
    /// Kilit BİLEREK servis katmanındadır: arayüzdeki buton gizlense, istemci değiştirilse ya da uç nokta
    /// doğrudan çağrılsa bile şube yazımı buradan geçemez. Meşru yol: Şube/Şantiye Tanımları ekranı →
    /// <c>Organization.BranchService</c> (yetki: <c>branches</c> Create/Edit/Delete).
    /// </summary>
    private static void EnsureWritableTable(string table)
    {
        EnsureKnownTable(table);
        if (table == "branches")
            throw new ForbiddenException(
                "Şube / Şantiye tanımları buradan oluşturulamaz, değiştirilemez veya silinemez. " +
                "Bu işlem yalnızca Şube / Şantiye Tanımları ekranından yapılabilir.");
    }
}
