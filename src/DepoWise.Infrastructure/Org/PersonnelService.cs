using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Org;

public sealed record PersonnelRecord(
    string Id, string CompanyId, string? BranchId, string FullName,
    string? Title, string? Phone, bool IsActive, long CreatedAt,
    bool IsFieldStaff = false,    // Fikir B: "Saha personeli" — işaretliyse kullanıcı-bağlı uyarısı çıkmaz
    long Version = 0);            // DÜZENLEME KİLİDİ: formun açıldığı andaki sürüm (bkz. EditLockGuard)

public sealed record NewPersonnel(string FullName, string? Title, string? Phone, string? BranchId, bool IsActive = true,
    bool IsFieldStaff = false);

/// <summary>
/// Personel CRUD — tenant + "personnel" permission + şube kapsamı fail-closed; soft delete/restore;
/// keyset listeleme yalnız kullanıcının kapsamındaki kayıtları getirir. Tüm mutasyonlar audit'lenir.
/// </summary>
public sealed class PersonnelService
{
    private const string Module = "personnel";
    private readonly IDbConnectionFactory _factory;
    private readonly ScopeResolver _scope;
    private readonly IClock _clock;

    public PersonnelService(IDbConnectionFactory factory, ScopeResolver scope, IClock? clock = null)
    {
        _factory = factory;
        _scope = scope;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>#6 — Olası aynı kişi: aynı firmada ad (normalize) VEYA telefon eşleşen personeller (silinmemiş).
    /// Farklı şubelerde aynı kişinin farkında olmadan iki kez eklenmesini önlemek için kayıt öncesi çağrılır.</summary>
    public IReadOnlyList<PersonnelRecord> FindDuplicates(SessionContext session, string fullName, string? phone, string? excludeId)
    {
        AccessControl.Require(session, Module, PermissionAction.View);
        var name = NormalizeName(fullName);
        var digits = DigitsOnly(phone);
        if (name.Length == 0 && digits.Length == 0) return Array.Empty<PersonnelRecord>();

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, branch_id, full_name, title, phone, is_active, created_at, is_field_staff, version FROM personnel " +
            "WHERE company_id=$c AND is_deleted=0 AND ($x IS NULL OR id<>$x) AND (" +
            "  ($n <> '' AND REPLACE(LOWER(full_name),' ','')=$n) OR " +
            "  ($d <> '' AND phone IS NOT NULL AND REPLACE(REPLACE(REPLACE(REPLACE(phone,' ',''),'-',''),'(',''),')','')=$d)" +
            ");";
        cmd.AddWithValue("$c", session.CompanyId);
        cmd.AddWithValue("$x", (object?)excludeId ?? DBNull.Value);
        cmd.AddWithValue("$n", name);
        cmd.AddWithValue("$d", digits);
        var list = new List<PersonnelRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PersonnelRecord(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.GetInt64(6) == 1, r.GetInt64(7), r.GetInt64(8) == 1, r.GetInt64(9)));
        return list;
    }

    /// <summary>
    /// İÇE AKTARIM için firmanın TÜM personeli: normalize ad → id (SAYFALAMA YOK).
    ///
    /// ⚠️ NEDEN AYRI METOT: <see cref="List"/> PageRequest kullanır ve <c>PageRequest.MaxLimit = 200</c>
    /// ile SINIRLIDIR → 200'den fazla personeli olan firmada mükerrer kontrolü 201. kişiden sonrasını
    /// "yok" sanıp KOPYA oluştururdu. Satır başına <see cref="FindDuplicates"/> çağırmak da 2600 satırda
    /// 2600 sorgu demekti. Ad normalizasyonu FindDuplicates ile AYNIDIR (tutarlı mükerrer tanımı).
    /// </summary>
    public Dictionary<string, string> AllNameToId(SessionContext session)
    {
        AccessControl.Require(session, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT full_name, id FROM personnel WHERE company_id=$c AND is_deleted=0;";
        cmd.AddWithValue("$c", session.CompanyId);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[NormalizeName(r.GetString(0))] = r.GetString(1);
        return map;
    }

    /// <summary>İçe aktarımın mükerrer anahtarı — <see cref="AllNameToId"/> ile aynı normalizasyon.</summary>
    public static string ImportKey(string? fullName) => NormalizeName(fullName);

    private static string NormalizeName(string? s) => (s ?? "").Trim().ToLowerInvariant().Replace(" ", "");
    private static string DigitsOnly(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());

    public string Create(SessionContext session, NewPersonnel dto)
    {
        AccessControl.Require(session, Module, PermissionAction.Create);
        _scope.EnsureBranchAllowed(session, dto.BranchId);

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "INSERT INTO personnel(id, company_id, branch_id, full_name, title, phone, is_active, is_field_staff, created_at, updated_at, version, is_deleted) " +
                "VALUES($id,$c,$b,$n,$t,$p,$a,$fs,$now,$now,1,0);";
            Bind(cmd, id, session.CompanyId, dto, now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(session.CompanyId, "personnel", id, AuditActions.Create, session.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <param name="expectedVersion">DÜZENLEME KİLİDİ: formun açıldığı andaki <c>version</c>. Verilirse ve kayıt
    /// o andan beri değiştiyse <see cref="ConcurrencyException"/> atılır. null = kontrol yok (geriye uyumlu).</param>
    public void Update(SessionContext session, string id, NewPersonnel dto, long? expectedVersion = null)
    {
        AccessControl.Require(session, Module, PermissionAction.Edit);
        _scope.EnsureBranchAllowed(session, dto.BranchId);
        var existing = GetOwned(session, id) ?? throw new ForbiddenException("Personel bulunamadı veya kapsam dışı.");
        // Mevcut kaydın şubesi de kapsamda olmalı (başka şubeye taşımayı engelle değil; erişim kontrolü)
        _scope.EnsureBranchAllowed(session, existing.BranchId);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE personnel SET branch_id=$b, full_name=$n, title=$t, phone=$p, is_active=$a, is_field_staff=$fs, " +
                "version=version+1, updated_at=$now WHERE id=$id AND company_id=$c" + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.AddWithValue("$b", (object?)dto.BranchId ?? DBNull.Value);
            cmd.AddWithValue("$n", dto.FullName);
            cmd.AddWithValue("$t", (object?)dto.Title ?? DBNull.Value);
            cmd.AddWithValue("$p", (object?)dto.Phone ?? DBNull.Value);
            cmd.AddWithValue("$a", dto.IsActive ? 1 : 0);
            cmd.AddWithValue("$fs", dto.IsFieldStaff ? 1 : 0);
            cmd.AddWithValue("$now", now);
            cmd.AddWithValue("$id", id);
            cmd.AddWithValue("$c", session.CompanyId);
            // 0 satır + sürüm verilmişse → kayıt biz düzenlerken değişmiş (düzenleme kilidi).
            // Sürüm verilmemişse ThrowIfStale sessizce döner → eski davranış aynen korunur.
            if (cmd.ExecuteNonQuery() == 0)
                EditLockGuard.ThrowIfStale(conn, tx, "personnel", id, session.CompanyId, expectedVersion);
        }
        AuditWriter.Write(conn, tx, new AuditEntry(session.CompanyId, "personnel", id, AuditActions.Update, session.UserId), _clock);
        tx.Commit();
    }

    public void SoftDelete(SessionContext session, string id) => SetDeleted(session, id, true, AuditActions.Delete, PermissionAction.Delete);
    public void Restore(SessionContext session, string id) => SetDeleted(session, id, false, AuditActions.Restore, PermissionAction.Edit);

    /// <summary>Tenant + kapsam filtreli keyset sayfası.</summary>
    public PagedResult<PersonnelRecord> List(SessionContext session, PageRequest page, bool includeDeleted = false)
    {
        AccessControl.Require(session, Module, PermissionAction.View);
        var allowedBranches = _scope.AllowedBranchIds(session);
        bool isAdmin = AccessControl.IsAdmin(session);
        var limit = page.NormalizedLimit();
        var hasCursor = Cursor.TryDecode(page.Cursor, out var cursor);

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, branch_id, full_name, title, phone, is_active, created_at, is_field_staff, version FROM personnel " +
            "WHERE company_id = $c " + (includeDeleted ? "" : "AND is_deleted = 0 ") +
            (hasCursor ? "AND " + TenantSql.KeysetAfterPredicate + " " : "") +
            TenantSql.KeysetOrderBy + " LIMIT $limit;";
        cmd.AddWithValue("$c", session.CompanyId);
        cmd.AddWithValue("$limit", limit + 1);
        if (hasCursor)
        {
            cmd.AddWithValue("$cursorCreatedAt", cursor.CreatedAt);
            cmd.AddWithValue("$cursorId", cursor.Id);
        }

        var items = new List<PersonnelRecord>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var branchId = r.IsDBNull(2) ? null : r.GetString(2);
                // Kapsam dışı şubedeki personel admin-olmayana gösterilmez (şubesiz kayıt herkese görünür).
                if (!isAdmin && branchId is not null && !allowedBranches.Contains(branchId)) continue;
                items.Add(new PersonnelRecord(r.GetString(0), r.GetString(1), branchId,
                    r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetInt64(6) == 1, r.GetInt64(7), r.GetInt64(8) == 1, r.GetInt64(9)));
            }
        }

        string? next = null;
        if (items.Count > limit)
        {
            var last = items[limit - 1];
            items.RemoveAt(items.Count - 1);
            next = new Cursor(last.CreatedAt, last.Id).Encode();
        }
        return PagedResult<PersonnelRecord>.Of(items, next);
    }

    private void SetDeleted(SessionContext session, string id, bool deleted, string action, PermissionAction perm)
    {
        AccessControl.Require(session, Module, perm);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        int affected;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE personnel SET is_deleted=$d, version=version+1, updated_at=$now WHERE id=$id AND company_id=$c;";
            cmd.AddWithValue("$d", deleted ? 1 : 0);
            cmd.AddWithValue("$now", now);
            cmd.AddWithValue("$id", id);
            cmd.AddWithValue("$c", session.CompanyId);
            affected = cmd.ExecuteNonQuery();
        }
        if (affected > 0)
            AuditWriter.Write(conn, tx, new AuditEntry(session.CompanyId, "personnel", id, action, session.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Tek personel kaydı (tenant korumalı). Hesap açma vb. için.</summary>
    public PersonnelRecord? Get(SessionContext session, string id)
    {
        AccessControl.Require(session, Module, PermissionAction.View);
        return GetOwned(session, id);
    }

    private PersonnelRecord? GetOwned(SessionContext session, string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, branch_id, full_name, title, phone, is_active, created_at, is_field_staff, version FROM personnel " +
            "WHERE id = $id AND company_id = $c;";
        cmd.AddWithValue("$id", id);
        cmd.AddWithValue("$c", session.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PersonnelRecord(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
            r.GetInt64(6) == 1, r.GetInt64(7), r.GetInt64(8) == 1, r.GetInt64(9));
    }

    private static void Bind(DbCommand cmd, string id, string companyId, NewPersonnel dto, long now)
    {
        cmd.AddWithValue("$id", id);
        cmd.AddWithValue("$c", companyId);
        cmd.AddWithValue("$b", (object?)dto.BranchId ?? DBNull.Value);
        cmd.AddWithValue("$n", dto.FullName);
        cmd.AddWithValue("$t", (object?)dto.Title ?? DBNull.Value);
        cmd.AddWithValue("$p", (object?)dto.Phone ?? DBNull.Value);
        cmd.AddWithValue("$a", dto.IsActive ? 1 : 0);
        cmd.AddWithValue("$fs", dto.IsFieldStaff ? 1 : 0);
        cmd.AddWithValue("$now", now);
    }
}
