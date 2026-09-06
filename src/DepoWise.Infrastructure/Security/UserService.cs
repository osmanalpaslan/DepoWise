using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Security;

public sealed record NewUser(
    string Username,
    string Password,
    string? FullName,
    IReadOnlyList<string> RoleKeys,
    string? CompanyId = null,
    IReadOnlyList<ModulePermission>? Permissions = null,
    string? BranchId = null,
    bool CanViewAllBranches = false,
    string? PersonnelId = null);   // #6: hesabın bağlı olduğu personel (opsiyonel; bir personele tek kullanıcı)

public sealed record UserRow(string Id, string Username, string? FullName, bool IsActive, string Roles, string? BranchId, string? BranchName,
    bool CanViewAllBranches = false, bool IsAdmin = false,
    string? PersonnelId = null, string? PersonnelName = null)   // Fikir B: hesabın bağlı olduğu personel
{
    public string BranchDisplay => CanViewAllBranches ? "Tüm Şubeler" : (string.IsNullOrEmpty(BranchName) ? "—" : BranchName!);
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    /// <summary>Kullanıcı listesinde "Personel" sütunu — bağlı değilse "—".</summary>
    public string PersonnelDisplay => string.IsNullOrEmpty(PersonnelName) ? "—" : PersonnelName!;
}

/// <summary>#6 — Bir personele bağlı kullanıcı hesabının özeti (çalışan listesi rozeti için).</summary>
public sealed record PersonnelAccount(string UserId, string Username, bool IsActive, bool IsAdmin);

/// <summary>Bir personele bağlanabilir (henüz bağsız) kullanıcı — personel ekranı "kullanıcı bağla" listesi.</summary>
public sealed record LinkableUser(string Id, string Username, string? FullName, bool IsActive, string? BranchName = null)
{
    /// <summary>Personel ekranı gösterimi: YALNIZ Ad Soyad + şube (kullanıcı adı gizli — kullanıcı isteği).</summary>
    public string Display
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FullName) ? Username : FullName!;
            var text = string.IsNullOrWhiteSpace(BranchName) ? name : $"{name} — {BranchName}";
            return IsActive ? text : text + " (pasif)";
        }
    }
}

/// <summary>Kota izleme satırı: NORMAL (personel) ve ADMIN kullanımı AYRI kotalar (max_users / max_admins).</summary>
public sealed record QuotaMonitorRow(string CompanyId, string CompanyName, int MaxUsers, int NormalCount, int MaxAdmins, int AdminCount, int ActiveCount = 0)
{
    public string UserText => MaxUsers > 0 ? $"{NormalCount} / {MaxUsers}" : $"{NormalCount} / ∞";
    public string AdminText => MaxAdmins > 0 ? $"{AdminCount} / {MaxAdmins}" : $"{AdminCount} / ∞";
    /// <summary>Aktif kullanıcı sayısı (pasifler hariç) — kotaya sayılan toplamdan ayrı gösterilir.</summary>
    public string ActiveText => $"{ActiveCount} aktif";
    public bool UserFull => MaxUsers > 0 && NormalCount >= MaxUsers;
    public bool AdminFull => MaxAdmins > 0 && AdminCount >= MaxAdmins;
}

/// <summary>
/// Kullanıcı oluşturma — yetki yükseltme + tenant kuralları fail-closed (analiz §4/§9).
/// Tek transaction: user + user_roles + user_permissions + audit.
/// </summary>
public sealed class UserService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    /// <summary>F0 (YET-01): rol/aktiflik değişince o kullanıcının yetki fotoğrafı ANINDA düşürülür.
    /// <c>null</c> → önbellek yok (F0 öncesi davranış).</summary>
    private readonly PermissionSnapshotCache? _snapshots;

    public UserService(IDbConnectionFactory factory, IClock? clock = null, PermissionSnapshotCache? snapshots = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _snapshots = snapshots;
    }

    /// <summary>Kullanıcı listesi (tenant: Süper Admin tümünü, diğerleri kendi firmasını görür).</summary>
    public IReadOnlyList<UserRow> ListUsers(SessionContext actor)
    {
        // Kullanıcı listesi TÜM oturum sahiplerine açıktır (aynı firma). Admin OLMAYAN aktör SINIRLI görür:
        // rol alanı gizlenir (yalnız kullanıcı adı/ad-soyad/şube/durum). Düzenleme/şifre sıfırlama yalnız admin
        // (ilgili servis metotları IsAdmin ister). Süper Admin kullanıcıları yine yalnız Süper Admin'e görünür.
        bool full = AccessControl.IsAdmin(actor);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlDialect.PortableSql(conn, @"
SELECT u.id, u.username, u.full_name, u.is_active,
  (SELECT GROUP_CONCAT(r.name, ', ') FROM user_roles ur JOIN roles r ON r.id = ur.role_id WHERE ur.user_id = u.id),
  u.branch_id, b.name, COALESCE(u.can_view_all_branches,0),
  (SELECT COUNT(*) FROM user_roles ur2 JOIN roles r2 ON r2.id = ur2.role_id
     WHERE ur2.user_id = u.id AND r2.role_key IN (@ca,@sa)),
  u.personnel_id, p.full_name          -- Fikir B: bağlı personel (varsa)
FROM users u
LEFT JOIN branches b ON b.id = u.branch_id
LEFT JOIN personnel p ON p.id = u.personnel_id AND p.is_deleted = 0
WHERE u.is_deleted = 0 AND (@all = 1 OR u.company_id = @c)
  -- Süper Admin kullanıcı kayıtları yalnız Süper Admin'e görünür (diğer roller göremez)
  AND (@all = 1 OR NOT EXISTS (
        SELECT 1 FROM user_roles ur JOIN roles r ON r.id = ur.role_id
        WHERE ur.user_id = u.id AND r.role_key = @sa))
ORDER BY u.username;");
        cmd.AddWithValue("@all", actor.IsSuperAdmin ? 1 : 0);
        cmd.AddWithValue("@c", actor.CompanyId);
        cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
        cmd.AddWithValue("@ca", RoleKeys.CompanyAdmin);
        var list = new List<UserRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UserRow(r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt64(3) == 1,
                full ? (r.IsDBNull(4) ? "" : r.GetString(4)) : "",   // admin olmayana rol gizli (sınırlı liste)
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.GetInt64(7) == 1,
                r.GetInt64(8) > 0,
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10)));
        return list;
    }

    /// <summary>Kullanıcıyı soft-delete eder. YALNIZ Admin / Süper Admin. Kendi hesabını silemez.</summary>
    public void DeleteUser(SessionContext actor, string userId)
    {
        if (!AccessControl.IsAdmin(actor)) throw new ForbiddenException("Kullanıcı silme yalnız Admin / Süper Admin yetkisindedir.");
        if (string.Equals(userId, actor.UserId, StringComparison.Ordinal))
            throw new InvalidOperationException("Kendi hesabınızı silemezsiniz.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var companyId = AffectUser(conn, tx, actor, userId,
            "UPDATE users SET is_deleted=1, updated_at=@now WHERE id=@u AND is_deleted=0 AND (@all=1 OR company_id=@c);", now);
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Delete, actor.UserId), _clock);
        tx.Commit();
        _snapshots?.InvalidateUser(userId);   // F0: silinen kullanıcının oturumu beklemeden geçersizleşsin
    }

    /// <summary>Kullanıcının şifresini değiştirir. YALNIZ Admin / Süper Admin. Min 4 karakter.</summary>
    public void ChangePassword(SessionContext actor, string userId, string newPassword)
    {
        if (!AccessControl.IsAdmin(actor)) throw new ForbiddenException("Şifre değiştirme yalnız Admin / Süper Admin yetkisindedir.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw new ArgumentException("Şifre en az 4 karakter olmalı.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        // Admin başkasının şifresini sıfırladığında, o kullanıcı bir sonraki girişte kendi şifresini
        // yeniden belirlemek zorunda (must_change_password=1). Kendi şifresini değiştiriyorsa zorunluluk aranmaz.
        bool self = string.Equals(userId, actor.UserId, StringComparison.Ordinal);
        var companyId = AffectUser(conn, tx, actor, userId,
            "UPDATE users SET password_hash=@h, must_change_password=@mcp, updated_at=@now WHERE id=@u AND is_deleted=0 AND (@all=1 OR company_id=@c);",
            now, cmd => { cmd.AddWithValue("@h", PasswordHasher.Hash(newPassword)); cmd.AddWithValue("@mcp", self ? 0 : 1); });
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
    }

    /// <summary>ŞİFRE SIFIRLAMA (2026-07-25): admin, kullanıcının şifresini KULLANICI ADINA sıfırlar +
    /// must_change_password=1. Kullanıcı bir sonraki girişte kullanıcı adıyla girer ve ilk giriş gibi KENDİ
    /// şifresini belirler. Admin belirli bir şifre YAZMAZ (şifre kullanıcı tanımından değiştirilmez).
    /// Yalnız Admin / Süper Admin; admin başka admin/süper-admini sıfırlayamaz (EnsureManageableTarget).
    /// Geçici şifreyi (=kullanıcı adı) döndürür (UI bilgilendirir).</summary>
    public string ResetPassword(SessionContext actor, string userId)
    {
        if (!AccessControl.IsAdmin(actor)) throw new ForbiddenException("Şifre sıfırlama yalnız Admin / Süper Admin yetkisindedir.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        string username;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT username FROM users WHERE id=@u AND is_deleted=0;";
            q.AddWithValue("@u", userId);
            username = q.ExecuteScalar() as string ?? throw new ForbiddenException("Kullanıcı bulunamadı.");
        }
        var companyId = AffectUser(conn, tx, actor, userId,
            "UPDATE users SET password_hash=@h, must_change_password=1, updated_at=@now WHERE id=@u AND is_deleted=0 AND (@all=1 OR company_id=@c);",
            now, cmd => cmd.AddWithValue("@h", PasswordHasher.Hash(username)));
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
        return username;
    }

    /// <summary>SUNUCUDA oluşturulan kullanıcıyı YEREL DB'ye SUNUCU id'siyle işler (masaüstü çevrimiçi create
    /// sonrası yerel görünürlük + kalıcılık için — kullanıcı sunucu-otoriteli; yereldeki kopya sunucununkiyle
    /// AYNI id'yi taşımalı ki çift kayıt olmasın). Upsert (id korunur) + roller (role_key ile). Şifre yerelde
    /// hash'lenir (o kullanıcının ilk girişinde sunucu hash'iyle güncellenir). Yetki kontrolü YOK — sunucu
    /// zaten doğruladı; bu yalnız yerel yansıtmadır.</summary>
    public void ImportServerUser(string id, string companyId, string username, string plaintextPassword,
        string? fullName, string? branchId, bool canViewAllBranches, bool mustChangePassword, IEnumerable<string> roleKeys)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var u = conn.CreateCommand())
        {
            u.Transaction = tx;
            u.CommandText =
                "INSERT INTO users(id, company_id, username, password_hash, full_name, can_view_all_branches, branch_id, is_active, must_change_password, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id,@c,@un,@h,@f,@va,@bid,1,@mcp,@now,@now,1,0) " +
                "ON CONFLICT(id) DO UPDATE SET company_id=@c, username=@un, password_hash=@h, full_name=@f, can_view_all_branches=@va, branch_id=@bid, is_active=1, is_deleted=0, must_change_password=@mcp, updated_at=@now;";
            u.AddWithValue("@id", id);
            u.AddWithValue("@c", companyId);
            u.AddWithValue("@un", username);
            u.AddWithValue("@h", PasswordHasher.Hash(plaintextPassword));
            u.AddWithValue("@f", (object?)fullName ?? DBNull.Value);
            u.AddWithValue("@va", canViewAllBranches ? 1 : 0);
            u.AddWithValue("@bid", (object?)branchId ?? DBNull.Value);
            u.AddWithValue("@mcp", mustChangePassword ? 1 : 0);
            u.AddWithValue("@now", now);
            u.ExecuteNonQuery();
        }
        using (var d = conn.CreateCommand())
        { d.Transaction = tx; d.CommandText = "DELETE FROM user_roles WHERE user_id=@u;"; d.AddWithValue("@u", id); d.ExecuteNonQuery(); }
        foreach (var rk in roleKeys.Distinct())
        {
            string? roleId;
            using (var rq = conn.CreateCommand())
            {
                rq.Transaction = tx;
                rq.CommandText = "SELECT id FROM roles WHERE role_key=@k AND is_deleted=0 ORDER BY (company_id IS NULL) DESC LIMIT 1;";
                rq.AddWithValue("@k", rk);
                roleId = rq.ExecuteScalar() as string;
            }
            if (roleId is null) continue;
            using var ir = conn.CreateCommand();
            ir.Transaction = tx;
            ir.CommandText = "INSERT INTO user_roles(user_id, role_id) VALUES(@u,@r) ON CONFLICT DO NOTHING;";
            ir.AddWithValue("@u", id); ir.AddWithValue("@r", roleId);
            ir.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>İLK GİRİŞ şifre belirleme: kullanıcı KENDİ şifresini değiştirir + must_change_password sıfırlanır.
    /// Oturum sahibinden başkasına dokunmaz (self-service). Min 4 karakter.</summary>
    public void ChangeOwnPassword(SessionContext actor, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            throw new ArgumentException("Şifre en az 4 karakter olmalı.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE users SET password_hash=@h, must_change_password=0, updated_at=@now WHERE id=@u AND is_deleted=0;";
            cmd.AddWithValue("@h", PasswordHasher.Hash(newPassword));
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@u", actor.UserId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(actor.CompanyId, "user", actor.UserId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Kullanıcıyı aktif/pasif yapar. Süper admin kullanıcıyı YALNIZ süper admin aktif/pasif edebilir
    /// (pasife alınan süper admin'i diğer roller yeniden aktif edemez).</summary>
    public void SetActive(SessionContext actor, string userId, bool active)
    {
        if (!AccessControl.IsAdmin(actor)) throw new ForbiddenException("Kullanıcı durumu değişimi yalnız Admin / Süper Admin yetkisindedir.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        if (IsSuperAdminUser(conn, tx, userId) && !actor.IsSuperAdmin)
            throw new ForbiddenException("Süper admin kullanıcının durumunu yalnız süper admin değiştirebilir.");
        var companyId = AffectUser(conn, tx, actor, userId,
            "UPDATE users SET is_active=@a, updated_at=@now WHERE id=@u AND is_deleted=0 AND (@all=1 OR company_id=@c);",
            now, cmd => cmd.AddWithValue("@a", active ? 1 : 0));
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
        _snapshots?.InvalidateUser(userId);   // F0: pasife alma ANINDA etkili olmalı (yetki kaybı gecikmez)
    }

    /// <summary>
    /// #8 — Admin, başka bir admini veya süper admini YÖNETEMEZ (düzenle/şifre/sil/rol/şube).
    /// Süper admin herkesi; kullanıcı kendini yönetebilir. Firma admini yalnız Personel'leri (+kendini) yönetir.
    /// </summary>
    private static void EnsureManageableTarget(DbConnection conn, DbTransaction tx, SessionContext actor, string userId)
    {
        if (actor.IsSuperAdmin) return;
        if (string.Equals(userId, actor.UserId, StringComparison.Ordinal)) return; // kendini yönetebilir
        if (IsSuperAdminUser(conn, tx, userId))
            throw new ForbiddenException("Süper admin kullanıcı düzenlenemez.");
        if (HasRoleKey(conn, tx, userId, RoleKeys.RestrictedSuperAdmin))
            throw new ForbiddenException("Kısıtlı Süper Admin kullanıcıyı yalnız süper admin düzenleyebilir.");
        if (IsCompanyAdminUser(conn, tx, userId))
            throw new ForbiddenException("Başka bir admin kullanıcıyı yalnız süper admin düzenleyebilir.");
    }

    private static bool HasRoleKey(DbConnection conn, DbTransaction tx, string userId, string roleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=@u AND r.role_key=@k;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@k", roleKey);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static bool IsSuperAdminUser(DbConnection conn, DbTransaction tx, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=@u AND r.role_key=@sa;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string AffectUser(DbConnection conn, DbTransaction tx, SessionContext actor, string userId, string sql, long now, Action<DbCommand>? extra = null)
    {
        // Tenant: hedef kullanıcının firmasını al ve doğrula
        string companyId;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT company_id FROM users WHERE id=@u AND is_deleted=0;";
            q.AddWithValue("@u", userId);
            companyId = q.ExecuteScalar() as string ?? throw new ForbiddenException("Kullanıcı bulunamadı.");
        }
        if (!actor.IsSuperAdmin && companyId != actor.CompanyId) throw new ForbiddenException("Kullanıcı başka firmaya ait.");
        EnsureManageableTarget(conn, tx, actor, userId);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@all", actor.IsSuperAdmin ? 1 : 0);
        cmd.AddWithValue("@c", actor.CompanyId);
        extra?.Invoke(cmd);
        if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("İşlem uygulanamadı.");
        return companyId;
    }

    /// <summary>Bir kullanıcının rol anahtarları (rol düzenleme ekranı için).</summary>
    /// <summary>T-6 (2026-08-09): <c>actor</c> daha önce alınıp HİÇ KULLANILMIYORDU — ne yetki ne firma
    /// kontrolü vardı; başka firmanın kullanıcı id'siyle rolleri okunabiliyordu. Artık <c>SetRoles</c> ile
    /// aynı yetki kontrolü + kullanıcı sahipliği doğrulanıyor.</summary>
    public IReadOnlyList<string> GetRoleKeys(SessionContext actor, string userId)
    {
        AccessControl.Require(actor, "users", PermissionAction.View);
        using var conn = _factory.Create();
        EnsureUserOwned(conn, actor, userId);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=@u;";
        cmd.AddWithValue("@u", userId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Mevcut kullanıcının rollerini tam değiştirir. Yetki yükseltme koruması (EnsureCanAssign) +
    /// süper admin kullanıcının rolünü yalnız süper admin değiştirebilir.</summary>
    public void SetRoles(SessionContext actor, string userId, IReadOnlyList<string> roleKeys)
    {
        AccessControl.Require(actor, "users", PermissionAction.Edit);
        RoleAssignmentGuard.EnsureCanAssign(actor, roleKeys); // admin/süper-admin rolü atama koruması
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Hedef firmasını al + tenant doğrula
        string companyId;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT company_id FROM users WHERE id=@u AND is_deleted=0;";
            q.AddWithValue("@u", userId);
            companyId = q.ExecuteScalar() as string ?? throw new ForbiddenException("Kullanıcı bulunamadı.");
        }
        if (!actor.IsSuperAdmin && companyId != actor.CompanyId) throw new ForbiddenException("Kullanıcı başka firmaya ait.");
        EnsureManageableTarget(conn, tx, actor, userId); // #8: admin başka admini/süperadmini düzenleyemez

        // Admin kotası: kullanıcıya admin rolü EKLENİYORSA (daha önce admin değilse) max_admins kontrol edilir.
        bool willBeAdmin = roleKeys.Any(k => string.Equals(k, RoleKeys.CompanyAdmin, StringComparison.Ordinal));
        bool wasAdmin = IsCompanyAdminUser(conn, tx, userId);
        if (willBeAdmin && !wasAdmin)
        {
            var (_, maxAdmins) = CompanyQuotas(conn, tx, companyId);
            if (maxAdmins > 0 && CountCompanyAdmins(conn, tx, companyId) >= maxAdmins)
                throw new InvalidOperationException($"Admin kotası dolu (maks {maxAdmins}). Admin rolü atanamaz.");
        }

        Insert(conn, tx, "DELETE FROM user_roles WHERE user_id=@u;", cmd => cmd.AddWithValue("@u", userId));
        foreach (var roleKey in roleKeys.Distinct())
        {
            var roleId = ResolveRoleId(conn, tx, companyId, roleKey)
                ?? throw new InvalidOperationException($"Rol bulunamadı: {roleKey}");
            Insert(conn, tx, "INSERT INTO user_roles(user_id, role_id) VALUES(@u,@r) ON CONFLICT DO NOTHING;",
                cmd => { cmd.AddWithValue("@u", userId); cmd.AddWithValue("@r", roleId); });
        }
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
        _snapshots?.InvalidateUser(userId);   // F0: rol değişimi admin bypass'ını etkiler → ANINDA düşür
    }

    public string CreateUser(SessionContext actor, NewUser dto)
    {
        // 1) Yetki: users modülünde 'create' (admin bypass)
        AccessControl.Require(actor, "users", PermissionAction.Create);
        // 2) Tenant: Firma Admini kendi firmasına kilitli; Süper Admin firma seçebilir
        var companyId = RoleAssignmentGuard.ResolveTargetCompany(actor, dto.CompanyId);
        // 3) Yetki yükseltme: admin/süper-admin rolü atama koruması
        RoleAssignmentGuard.EnsureCanAssign(actor, dto.RoleKeys);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var userId = Guid.NewGuid().ToString("N");

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Kota: admin ve NORMAL (personel) kullanıcı AYRI kotalanır (max_admins / max_users; 0 = sınırsız).
        bool assigningAdmin = dto.RoleKeys.Any(k => string.Equals(k, RoleKeys.CompanyAdmin, StringComparison.Ordinal));
        bool assigningElevated = dto.RoleKeys.Any(k =>
            string.Equals(k, RoleKeys.SuperAdmin, StringComparison.Ordinal)
            || string.Equals(k, RoleKeys.RestrictedSuperAdmin, StringComparison.Ordinal));
        var (maxUsers, maxAdmins) = CompanyQuotas(conn, tx, companyId);
        if (assigningAdmin)
        {
            if (maxAdmins > 0 && CountCompanyAdmins(conn, tx, companyId) >= maxAdmins)
                throw new InvalidOperationException($"Admin kotası dolu (maks {maxAdmins}). Yeni admin eklenemez.");
        }
        else if (!assigningElevated) // normal (personel) kotası — süper/kısıtlı-süper admin kotaya sayılmaz
        {
            if (maxUsers > 0 && CountCompanyNormalUsers(conn, tx, companyId) >= maxUsers)
                throw new InvalidOperationException($"Firma kullanıcı kotası dolu (maks {maxUsers} personel). Yeni kullanıcı eklenemez.");
        }

        // G6-03: kullanıcı adı ÇAKIŞMASI anlaşılır hata versin. Benzersizlik indeksi artık YALNIZ aktif
        // kullanıcıları kapsıyor (Migration063) → silinmiş bir adın yeniden kullanılması SERBEST. Aktif bir
        // kullanıcı aynı adı taşıyorsa istek burada reddedilir; aksi halde ham UNIQUE ihlali jenerik 500'e
        // düşüyordu ve kullanıcı sebebi göremiyordu.
        using (var dup = conn.CreateCommand())
        {
            dup.Transaction = tx;
            dup.CommandText = "SELECT COUNT(*) FROM users WHERE company_id=@c AND username=@u AND is_deleted=0;";
            dup.AddWithValue("@c", companyId);
            dup.AddWithValue("@u", dto.Username);
            if (Convert.ToInt64(dup.ExecuteScalar()) > 0)
                throw new InvalidOperationException($"'{dto.Username}' kullanıcı adı bu firmada zaten kullanılıyor. Başka bir ad seçin.");
        }

        // "Tüm Şubeler" yetkisi YALNIZ Süper Admin tarafından verilebilir (fail-closed).
        int viewAll = (dto.CanViewAllBranches && actor.IsSuperAdmin) ? 1 : 0;

        // #6: personele bağlanıyorsa — personel bu firmada olmalı ve zaten bir hesabı olmamalı (tek kullanıcı).
        var personnelId = string.IsNullOrWhiteSpace(dto.PersonnelId) ? null : dto.PersonnelId;
        if (personnelId is not null) EnsurePersonnelLinkable(conn, tx, companyId, personnelId, null);

        Insert(conn, tx,
            // Güvenlik: yeni kullanıcı ilk giriş(ler)inde kendi şifresini belirlemek zorunda (must_change_password=1).
            "INSERT INTO users(id, company_id, username, password_hash, full_name, branch_id, can_view_all_branches, personnel_id, is_active, must_change_password, created_at, updated_at, version, is_deleted) " +
            "VALUES(@id,@c,@u,@h,@f,@b,@va,@pid,1,1,@now,@now,1,0);",
            cmd =>
            {
                cmd.AddWithValue("@id", userId);
                cmd.AddWithValue("@c", companyId);
                cmd.AddWithValue("@u", dto.Username);
                cmd.AddWithValue("@h", PasswordHasher.Hash(dto.Password));
                cmd.AddWithValue("@f", (object?)dto.FullName ?? DBNull.Value);
                cmd.AddWithValue("@b", (object?)dto.BranchId ?? DBNull.Value);
                cmd.AddWithValue("@va", viewAll);
                cmd.AddWithValue("@pid", (object?)personnelId ?? DBNull.Value);
                cmd.AddWithValue("@now", now);
            });

        foreach (var roleKey in dto.RoleKeys.Distinct())
        {
            var roleId = ResolveRoleId(conn, tx, companyId, roleKey)
                ?? throw new InvalidOperationException($"Rol bulunamadı: {roleKey}");
            Insert(conn, tx,
                "INSERT INTO user_roles(user_id, role_id) VALUES(@u,@r) ON CONFLICT DO NOTHING;",
                cmd => { cmd.AddWithValue("@u", userId); cmd.AddWithValue("@r", roleId); });
        }

        foreach (var p in dto.Permissions ?? Array.Empty<ModulePermission>())
        {
            Insert(conn, tx,
                "INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version) " +
                "VALUES(@id,@c,@u,@m,@v,@cr,@e,@d,@now,@now,1);",
                cmd =>
                {
                    cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    cmd.AddWithValue("@c", companyId);
                    cmd.AddWithValue("@u", userId);
                    cmd.AddWithValue("@m", p.ModuleKey);
                    cmd.AddWithValue("@v", p.CanView ? 1 : 0);
                    cmd.AddWithValue("@cr", p.CanCreate ? 1 : 0);
                    cmd.AddWithValue("@e", p.CanEdit ? 1 : 0);
                    cmd.AddWithValue("@d", p.CanDelete ? 1 : 0);
                    cmd.AddWithValue("@now", now);
                });
        }

        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Create, actor.UserId), _clock);
        tx.Commit();
        return userId;
    }

    /// <summary>#6 — Personel bir kullanıcı hesabına bağlanabilir mi? Firma eşleşmeli, personel silinmemiş olmalı
    /// ve o personele başka bir aktif kullanıcı bağlı OLMAMALI (bir personele tek kullanıcı).</summary>
    private static void EnsurePersonnelLinkable(DbConnection conn, DbTransaction tx, string companyId, string personnelId, string? excludeUserId)
    {
        using (var p = conn.CreateCommand())
        {
            p.Transaction = tx;
            p.CommandText = "SELECT COUNT(*) FROM personnel WHERE id=@p AND company_id=@c AND is_deleted=0;";
            p.AddWithValue("@p", personnelId);
            p.AddWithValue("@c", companyId);
            if (Convert.ToInt64(p.ExecuteScalar()) == 0)
                throw new InvalidOperationException("Bağlanacak personel bulunamadı (firma dışı veya silinmiş).");
        }
        using var q = conn.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "SELECT COUNT(*) FROM users WHERE personnel_id=@p AND is_deleted=0 AND (CAST(@x AS TEXT) IS NULL OR id<>@x);";
        q.AddWithValue("@p", personnelId);
        q.AddWithValue("@x", (object?)excludeUserId ?? DBNull.Value);
        if (Convert.ToInt64(q.ExecuteScalar()) > 0)
            throw new InvalidOperationException("Bu personele zaten bir kullanıcı hesabı bağlı. Bir personele yalnız bir hesap bağlanabilir.");
    }

    /// <summary>
    /// ⭐ FAZ 4.16 — PERSONELE KULLANICI BAĞLAMA KAPISI.
    ///
    /// Eskiden sabit <c>IsAdmin</c> idi. Artık: <b>admin bypass'ı VEYA açıkça verilmiş
    /// "Personele Kullanıcı Bağlama" yetkisi</b>. Deny-by-default korunur; yeni yetki motoru yok
    /// (mevcut özel buton mekanizması) → migration gerekmez.
    ///
    /// ⚠️ Hata mesajı BİLEREK açık yazılır: eski davranışta yetkisiz kullanıcı sessizce BOŞ liste
    /// görüyordu ve ekran "bağlanabilir kullanıcı yok, önce hesap açın" diyordu — yani kullanıcı
    /// yetki sorununu VERİ sorunu sanıyordu (kullanıcının bildirdiği kafa karışıklığının kaynağı).
    /// </summary>
    private static void RequireLinkPermission(SessionContext actor, string islem)
    {
        if (AccessControl.IsAdmin(actor)) return;
        if (AccessControl.CanUseButton(actor, SpecialButtons.LinkUser)) return;
        throw new ForbiddenException(
            $"{islem} için «Personele Kullanıcı Bağlama» yetkisi gerekir. Yöneticiniz bu yetkiyi Yetkiler ekranından verebilir.");
    }

    /// <summary>Bir personele BAĞLANABİLİR kullanıcılar: firmanın henüz hiçbir personele bağlı OLMAYAN
    /// (personnel_id NULL), silinmemiş kullanıcıları (süper admin kullanıcıları hariç). Personel ekranındaki
    /// "mevcut kullanıcıyı bağla" açılır listesi bunu kullanır. YALNIZ Admin / Süper Admin.</summary>
    public IReadOnlyList<LinkableUser> ListLinkableUsers(SessionContext actor)
    {
        RequireLinkPermission(actor, "Bağlanabilir kullanıcı listesini görmek");
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT u.id, u.username, u.full_name, u.is_active, b.name
FROM users u
LEFT JOIN branches b ON b.id = u.branch_id AND b.is_deleted=0
WHERE u.is_deleted=0 AND u.personnel_id IS NULL
  AND (@all=1 OR u.company_id=@c)
  AND NOT EXISTS (SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id
                  WHERE ur.user_id=u.id AND r.role_key=@sa)
ORDER BY u.full_name, u.username;";
        cmd.AddWithValue("@all", actor.IsSuperAdmin ? 1 : 0);
        cmd.AddWithValue("@c", actor.CompanyId);
        cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
        var list = new List<LinkableUser>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new LinkableUser(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt64(3) == 1,
                r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    /// <summary>Var olan bir kullanıcıyı bir personele bağlar (yanlış bağı düzeltme). YALNIZ Admin / Süper Admin.</summary>
    public void LinkPersonnel(SessionContext actor, string userId, string? personnelId)
    {
        RequireLinkPermission(actor, "Personel bağını değiştirmek");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureManageableTarget(conn, tx, actor, userId);
        string companyId;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT company_id FROM users WHERE id=@u AND is_deleted=0;";
            q.AddWithValue("@u", userId);
            companyId = q.ExecuteScalar() as string ?? throw new InvalidOperationException("Kullanıcı bulunamadı.");
        }
        if (!actor.IsSuperAdmin && !string.Equals(companyId, actor.CompanyId, StringComparison.Ordinal))
            throw new ForbiddenException("Başka firmanın kullanıcısı düzenlenemez.");
        var pid = string.IsNullOrWhiteSpace(personnelId) ? null : personnelId;
        if (pid is not null) EnsurePersonnelLinkable(conn, tx, companyId, pid, userId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE users SET personnel_id=@p, updated_at=@now WHERE id=@u AND is_deleted=0;";
            cmd.AddWithValue("@p", (object?)pid ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Bir personele bağlı kullanıcı hesabının özeti (yoksa null). Çalışan listesinde rozet için.</summary>
    public IReadOnlyDictionary<string, PersonnelAccount> AccountsByPersonnel(string companyId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT u.personnel_id, u.id, u.username, u.is_active,
  (SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id
     WHERE ur.user_id=u.id AND r.role_key IN (@ca,@sa))
FROM users u
WHERE u.is_deleted=0 AND u.company_id=@c AND u.personnel_id IS NOT NULL;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@ca", RoleKeys.CompanyAdmin);
        cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
        var map = new Dictionary<string, PersonnelAccount>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[r.GetString(0)] = new PersonnelAccount(r.GetString(1), r.GetString(2), r.GetInt64(3) == 1, r.GetInt64(4) > 0);
        return map;
    }

    /// <summary>Firma kotaları: (maxUsers = normal/personel, maxAdmins = admin). 0 = sınırsız.</summary>
    private static (int MaxUsers, int MaxAdmins) CompanyQuotas(DbConnection conn, DbTransaction tx, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(max_users,0), COALESCE(max_admins,0) FROM companies WHERE id=@c;";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt32(0), r.GetInt32(1)) : (0, 0);
    }

    /// <summary>Normal (personel) kullanıcı sayısı — admin/süper-admin/kısıtlı-süper-admin ROLÜ OLMAYANLAR.</summary>
    private static int CountCompanyNormalUsers(DbConnection conn, DbTransaction tx, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(*) FROM users u WHERE u.company_id=@c AND u.is_deleted=0 " +
            "AND NOT EXISTS (SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id " +
            "                WHERE ur.user_id=u.id AND r.role_key IN (@rk,@sa,@rsa));";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@rk", RoleKeys.CompanyAdmin);
        cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
        cmd.AddWithValue("@rsa", RoleKeys.RestrictedSuperAdmin);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Kota izleme (F): firma başına kullanıcı + admin kullanımı. Süper Admin tümünü, diğerleri kendi firmasını görür.</summary>
    public IReadOnlyList<QuotaMonitorRow> GetQuotaMonitor(SessionContext actor)
    {
        AccessControl.Require(actor, "quota_monitor", PermissionAction.View); // #15: ayrı yetki (eski: users)
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // Normal = admin/süper-admin/kısıtlı-süper-admin ROLÜ OLMAYAN kullanıcı (personel).
        cmd.CommandText = @"
SELECT c.id, c.name, COALESCE(c.max_users,0), COALESCE(c.max_admins,0),
  (SELECT COUNT(DISTINCT u.id) FROM users u
     JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id
     WHERE u.company_id=c.id AND u.is_deleted=0 AND r.role_key=@rk),
  (SELECT COUNT(*) FROM users u WHERE u.company_id=c.id AND u.is_deleted=0
     AND NOT EXISTS (SELECT 1 FROM user_roles ur2 JOIN roles r2 ON r2.id=ur2.role_id
                     WHERE ur2.user_id=u.id AND r2.role_key IN (@rk,@sa,@rsa))),
  (SELECT COUNT(*) FROM users u WHERE u.company_id=c.id AND u.is_deleted=0 AND u.is_active=1)
FROM companies c
WHERE c.is_deleted=0 AND (@all=1 OR c.id=@c)
ORDER BY c.name;";
        cmd.AddWithValue("@rk", RoleKeys.CompanyAdmin);
        cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
        cmd.AddWithValue("@rsa", RoleKeys.RestrictedSuperAdmin);
        cmd.AddWithValue("@all", actor.IsSuperAdmin ? 1 : 0);
        cmd.AddWithValue("@c", actor.CompanyId);
        var list = new List<QuotaMonitorRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            // 0:id 1:name 2:max_users 3:max_admins 4:adminCount 5:normalCount 6:activeCount
            list.Add(new QuotaMonitorRow(r.GetString(0), r.GetString(1), r.GetInt32(2),
                r.GetInt32(5), r.GetInt32(3), r.GetInt32(4), r.GetInt32(6)));
        return list;
    }

    /// <summary>Adım 6: Kullanıcı oluşturma AKIŞINDA (web API + masaüstü) çağrılır — Admin dahil TÜM firma
    /// kullanıcıları için şube/şantiye ZORUNLU. Muaf YALNIZ platform rolleri: Süper Admin, Kısıtlı Süper Admin.
    /// Admin için firmanın HERHANGİ bir şubesi geçerlidir. Şube yoksa "önce şube oluştur" mesajı; varsa
    /// "şube seçin"; seçilen şube firmaya ait ve geçerli olmalı. Servis çekirdeği (CreateUser) bunu ZORLAMAZ;
    /// sınır katmanı (API + masaüstü VM) çağırır.</summary>
    public void ValidateBranchForNewUser(SessionContext actor, string? requestedCompanyId, IReadOnlyList<string> roleKeys, string? branchId)
    {
        bool exempt = roleKeys.Any(k =>
            string.Equals(k, RoleKeys.SuperAdmin, StringComparison.Ordinal)
            || string.Equals(k, RoleKeys.RestrictedSuperAdmin, StringComparison.Ordinal));
        if (exempt) return;
        var companyId = RoleAssignmentGuard.ResolveTargetCompany(actor, requestedCompanyId);
        using var conn = _factory.Create();
        if (string.IsNullOrWhiteSpace(branchId))
            throw new InvalidOperationException(CompanyHasBranch(conn, companyId)
                ? "Şube/şantiye zorunlu. Kullanıcıya bir şube seçin."
                : "Bu firmada henüz şube/şantiye yok. Önce Şube/Şantiye ekranından bir şube oluşturun, sonra kullanıcı ekleyin.");
        EnsureBranchInCompany(conn, companyId, branchId!);
    }

    /// <summary>Firmada aktif (silinmemiş) en az bir şube/şantiye var mı?</summary>
    private static bool CompanyHasBranch(DbConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Seçilen şube bu firmaya ait ve geçerli (silinmemiş) mi? Değilse hata (tenant + geçerlilik).</summary>
    private static void EnsureBranchInCompany(DbConnection conn, string companyId, string branchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@b AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@b", branchId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new InvalidOperationException("Seçilen şube geçersiz veya bu firmaya ait değil.");
    }

    private static int CountCompanyAdmins(DbConnection conn, DbTransaction tx, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(DISTINCT u.id) FROM users u " +
            "JOIN user_roles ur ON ur.user_id = u.id JOIN roles r ON r.id = ur.role_id " +
            "WHERE u.company_id=@c AND u.is_deleted=0 AND r.role_key=@rk;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@rk", RoleKeys.CompanyAdmin);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static bool IsCompanyAdminUser(DbConnection conn, DbTransaction tx, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id = ur.role_id " +
            "WHERE ur.user_id=@u AND r.role_key=@rk;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@rk", RoleKeys.CompanyAdmin);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>"Tüm Şubeler" yetkisini ayarlar — YALNIZ Süper Admin.</summary>
    public void SetViewAllBranches(SessionContext actor, string userId, bool value)
    {
        if (!actor.IsSuperAdmin)
            throw new ForbiddenException("Tüm Şubeler yetkisini yalnız Süper Admin belirleyebilir.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET can_view_all_branches=@v, updated_at=@now WHERE id=@u AND is_deleted=0;";
        cmd.AddWithValue("@v", value ? 1 : 0);
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@u", userId);
        cmd.ExecuteNonQuery();

        // DEN-B1 (denetim 2026-08-18): bu YETKİ DEĞİŞİKLİĞİ iz bırakmıyordu ve yetki fotoğrafını
        // düşürmüyordu. Kardeş metotlar (DeleteUser / SetActive / SetRoles) üçü de düşürüyor.
        // ⚠️ Asıl risk GERİ ALMA yönünde: yetki kaldırıldıktan sonra kullanıcı 90 sn (fotoğraf TTL'i)
        // daha TÜM ŞUBELERİN verisini görmeye devam ediyordu. "Tüm Şubeler" firma genelinde veri açar.
        AuditWriter.Write(conn, null, new AuditEntry(actor.CompanyId, "user_view_all_branches", userId,
            AuditActions.Update, actor.UserId, AfterJson: $"{{\"value\":{(value ? "true" : "false")}}}"), _clock);
        _snapshots?.InvalidateUser(userId);
    }

    /// <summary>İlk kurulum: sistem düzeyinde Süper Admin/Firma Admini oluşturur (actor yok).</summary>
    /// <param name="mustChangePassword">
    /// GUV-01 (2026-08-10): <c>true</c> ise hesap ilk girişte parolasını DEĞİŞTİRMEK ZORUNDA kalır
    /// (<c>users.must_change_password=1</c>; kolon Migration042'de mevcut — yeni migration GEREKMEZ).
    ///
    /// Neden gerekli: sunucu tohumlaması (<c>ServerServices.EnsureSeedAdmins</c>) ortam değişkeni verilmezse
    /// RASTGELE bir geçici parola üretip konsola yazıyor. Bu parola değiştirilmezse kurulumda kalıcı hâle
    /// geliyordu — mekanizma projede zaten vardı (<see cref="ResetPassword"/>) ama tohuma uygulanmamıştı.
    ///
    /// Varsayılan <c>false</c>: bu metot 90'dan fazla testte doğrudan çağrılıyor; varsayılanı değiştirmek
    /// test semantiğini topluca değiştirirdi. Zorunluluk YALNIZ üretim tohumlamasında açılır.
    ///
    /// Not: bayrak API uçlarını KİLİTLEMEZ; giriş yanıtındaki <c>mustChangePassword</c> ile iki arayüz de
    /// ilk giriş şifre ekranını gösterir (Login.razor · LoginViewModel). Projenin mevcut deseni budur.
    /// </param>
    public string EnsureInitialAdmin(string companyId, string username, string password, string roleKey,
        bool mustChangePassword = false)
    {
        TenantGuard.Require(companyId);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var userId = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Firma kaydı yoksa oluştur
        Insert(conn, tx,
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted) VALUES(@id,@n,@now,@now,1,0) ON CONFLICT DO NOTHING;",
            cmd => { cmd.AddWithValue("@id", companyId); cmd.AddWithValue("@n", companyId); cmd.AddWithValue("@now", now); });

        Insert(conn, tx,
            "INSERT INTO users(id, company_id, username, password_hash, full_name, is_active, must_change_password, created_at, updated_at, version, is_deleted) " +
            "VALUES(@id,@c,@u,@h,@f,1,@mcp,@now,@now,1,0);",
            cmd =>
            {
                cmd.AddWithValue("@id", userId);
                cmd.AddWithValue("@c", companyId);
                cmd.AddWithValue("@u", username);
                cmd.AddWithValue("@h", PasswordHasher.Hash(password));
                cmd.AddWithValue("@f", DBNull.Value);
                cmd.AddWithValue("@mcp", mustChangePassword ? 1 : 0);   // GUV-01
                cmd.AddWithValue("@now", now);
            });

        var roleId = ResolveRoleId(conn, tx, companyId, roleKey)
            ?? throw new InvalidOperationException($"Rol bulunamadı: {roleKey}");
        Insert(conn, tx,
            "INSERT INTO user_roles(user_id, role_id) VALUES(@u,@r) ON CONFLICT DO NOTHING;",
            cmd => { cmd.AddWithValue("@u", userId); cmd.AddWithValue("@r", roleId); });

        tx.Commit();
        return userId;
    }

    private static string? ResolveRoleId(DbConnection conn, DbTransaction tx, string companyId, string roleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Önce firmaya özel, yoksa sistem rolü (company_id IS NULL)
        cmd.CommandText =
            "SELECT id FROM roles WHERE role_key = @k AND (company_id = @c OR company_id IS NULL) AND is_deleted = 0 " +
            "ORDER BY company_id IS NULL LIMIT 1;";
        cmd.AddWithValue("@k", roleKey);
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteScalar() as string;
    }

    private static void Insert(DbConnection conn, DbTransaction tx, string sql, Action<DbCommand> bind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind(cmd);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Hedef kullanıcı oturumun firmasına mı ait? (T-6, 2026-08-09 — PermissionService'teki
    /// aynı desen.) Süper admin tüm firmalara yetkilidir.</summary>
    private static void EnsureUserOwned(DbConnection conn, SessionContext actor, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT company_id FROM users WHERE id=@u AND is_deleted=0;";
        cmd.AddWithValue("@u", userId);
        var cid = cmd.ExecuteScalar() as string ?? throw new ForbiddenException("Kullanıcı bulunamadı.");
        if (!actor.IsSuperAdmin && cid != actor.CompanyId)
            throw new ForbiddenException("Kullanıcı başka firmaya ait.");
    }
}
