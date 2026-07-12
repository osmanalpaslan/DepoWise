using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Security;

public sealed record UserPermissionData(IReadOnlyList<ModulePermission> Modules, IReadOnlyList<string> Buttons);

/// <summary>
/// Kullanıcı yetkilerini (modül View/Create/Edit/Delete + özel "+"/buton izinleri) yükler/kaydeder.
/// Yetki: "permissions" modülü (admin bypass). Tenant: hedef kullanıcı oturumun firmasına ait olmalı
/// (Süper Admin hariç). Kaydetme tam-değiştirir (önce sil, sonra yaz) — tek transaction.
/// </summary>
public sealed class PermissionService
{
    private const string Module = "permissions";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public PermissionService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public UserPermissionData GetForUser(SessionContext actor, string userId)
    {
        AccessControl.Require(actor, Module, PermissionAction.View);
        using var conn = _factory.Create();
        EnsureUserOwned(conn, null, actor, userId);

        var mods = new List<ModulePermission>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT module_key, can_view, can_create, can_edit, can_delete FROM user_permissions WHERE user_id=$u;";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mods.Add(new ModulePermission(r.GetString(0), r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1));
        }

        var buttons = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id=$u;";
            cmd.Parameters.AddWithValue("$u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) buttons.Add(r.GetString(0));
        }
        return new UserPermissionData(mods, buttons);
    }

    public void SaveForUser(SessionContext actor, string userId, IEnumerable<ModulePermission> modules, IEnumerable<string> buttons)
    {
        AccessControl.Require(actor, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var companyId = EnsureUserOwned(conn, tx, actor, userId);
        EnsureManageableTarget(conn, tx, actor, userId); // #8: admin başka admin/süperadminin yetkisini düzenleyemez

        // #3: Kısıtlı modüller (Yönetim/Kullanıcı/Yetkiler...) alt role VERİLEMEZ. Süper admin muaf.
        // Hedef admin/süper değilse ve kısıtlı bir modül veriliyorsa reddet → web önce Admin'e yükseltir.
        if (!actor.IsSuperAdmin)
        {
            var restricted = modules.Where(m => (m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
                && (AppModules.IsAdminRestricted(m.ModuleKey)
                    || DepoWise.Infrastructure.Organization.CompanyGrantService.IsGlobalRestricted(conn, tx, m.ModuleKey)
                    || DepoWise.Infrastructure.Organization.CompanyGrantService.IsCompanyRestricted(conn, tx, companyId, m.ModuleKey))).ToList();
            if (restricted.Count > 0
                && !HasRole(conn, tx, userId, RoleKeys.CompanyAdmin)
                && !HasRole(conn, tx, userId, RoleKeys.SuperAdmin))
            {
                throw new InvalidOperationException(
                    "Bu ekranlar (Yönetim / Kullanıcı / Yetkiler vb.) yalnız Admin'e verilebilir. Önce kullanıcıyı Admin yapın.");
            }
        }

        // Yetki YÜKSELTME engeli: Süper Admin dışındaki bir aktör, KENDİ sahip olmadığı yetkiyi başkasına VEREMEZ.
        // (Firmaya ilk açılan sınırlı admin, kendi yetkisi dışındaki alanları başkasına atayamaz.)
        var (clampMods, clampBtns) = GrantableLimit(conn, tx, actor);

        Exec(conn, tx, "DELETE FROM user_permissions WHERE user_id=$u;", c => c.Parameters.AddWithValue("$u", userId));
        Exec(conn, tx, "DELETE FROM user_button_permissions WHERE user_id=$u;", c => c.Parameters.AddWithValue("$u", userId));

        foreach (var mIn in modules)
        {
            var m = ClampModule(mIn, clampMods);
            // Boş satır yazma (hiçbir bayrak yoksa atla → deny-by-default)
            if (!(m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)) continue;
            Exec(conn, tx,
                "INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version) " +
                "VALUES($id,$c,$u,$m,$v,$cr,$e,$d,$now,$now,1);",
                c =>
                {
                    c.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    c.Parameters.AddWithValue("$c", companyId);
                    c.Parameters.AddWithValue("$u", userId);
                    c.Parameters.AddWithValue("$m", m.ModuleKey);
                    c.Parameters.AddWithValue("$v", m.CanView ? 1 : 0);
                    c.Parameters.AddWithValue("$cr", m.CanCreate ? 1 : 0);
                    c.Parameters.AddWithValue("$e", m.CanEdit ? 1 : 0);
                    c.Parameters.AddWithValue("$d", m.CanDelete ? 1 : 0);
                    c.Parameters.AddWithValue("$now", now);
                });
        }
        foreach (var b in buttons.Distinct())
        {
            if (clampBtns is not null && !clampBtns.Contains(b)) continue; // kendi sahip olmadığı butonu veremez
            Exec(conn, tx,
                "INSERT INTO user_button_permissions(id, company_id, user_id, button_key, created_at) VALUES($id,$c,$u,$b,$now);",
                c =>
                {
                    c.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
                    c.Parameters.AddWithValue("$c", companyId);
                    c.Parameters.AddWithValue("$u", userId);
                    c.Parameters.AddWithValue("$b", b);
                    c.Parameters.AddWithValue("$now", now);
                });
        }
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Aktörün başkasına VEREBİLECEĞİ üst sınır. null = sınırsız (Süper Admin, ya da hiç açık izni olmayan
    /// firma admini — geriye dönük uyum). Aksi halde aktörün KENDİ user_permissions/butonları sınır olur.</summary>
    private static (Dictionary<string, ModulePermission>? Mods, HashSet<string>? Btns) GrantableLimit(
        SqliteConnection conn, SqliteTransaction tx, SessionContext actor)
    {
        if (actor.IsSuperAdmin) return (null, null); // sınırsız

        var mods = new Dictionary<string, ModulePermission>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT module_key, can_view, can_create, can_edit, can_delete FROM user_permissions WHERE user_id=$u;";
            cmd.Parameters.AddWithValue("$u", actor.UserId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mods[r.GetString(0)] = new ModulePermission(r.GetString(0),
                    r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1);
        }
        var btns = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id=$u;";
            cmd.Parameters.AddWithValue("$u", actor.UserId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) btns.Add(r.GetString(0));
        }

        // Açık hiç izni olmayan firma admini → geriye dönük uyum: sınırsız (Süper Admin ona sonradan sınır koyabilir).
        if (mods.Count == 0 && btns.Count == 0 && actor.IsCompanyAdmin) return (null, null);
        return (mods, btns);
    }

    private static ModulePermission ClampModule(ModulePermission incoming, Dictionary<string, ModulePermission>? limit)
    {
        if (limit is null) return incoming; // sınırsız
        limit.TryGetValue(incoming.ModuleKey, out var o);
        return new ModulePermission(incoming.ModuleKey,
            incoming.CanView && (o?.CanView ?? false),
            incoming.CanCreate && (o?.CanCreate ?? false),
            incoming.CanEdit && (o?.CanEdit ?? false),
            incoming.CanDelete && (o?.CanDelete ?? false));
    }

    /// <summary>#8 — Admin, başka admin/süperadminin yetkilerini düzenleyemez (kendisi + Personel'ler hariç).</summary>
    private static void EnsureManageableTarget(SqliteConnection conn, SqliteTransaction tx, SessionContext actor, string userId)
    {
        if (actor.IsSuperAdmin) return;
        if (string.Equals(userId, actor.UserId, StringComparison.Ordinal)) return;
        if (HasRole(conn, tx, userId, RoleKeys.SuperAdmin))
            throw new ForbiddenException("Süper admin kullanıcı düzenlenemez.");
        if (HasRole(conn, tx, userId, RoleKeys.RestrictedSuperAdmin))
            throw new ForbiddenException("Kısıtlı Süper Admin kullanıcıyı yalnız süper admin düzenleyebilir.");
        if (HasRole(conn, tx, userId, RoleKeys.CompanyAdmin))
            throw new ForbiddenException("Başka bir admin kullanıcıyı yalnız süper admin düzenleyebilir.");
    }

    private static bool HasRole(SqliteConnection conn, SqliteTransaction tx, string userId, string roleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=$u AND r.role_key=$k;";
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$k", roleKey);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string EnsureUserOwned(SqliteConnection conn, SqliteTransaction? tx, SessionContext actor, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT company_id FROM users WHERE id=$u AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$u", userId);
        var cid = cmd.ExecuteScalar() as string ?? throw new ForbiddenException("Kullanıcı bulunamadı.");
        if (!actor.IsSuperAdmin && cid != actor.CompanyId) throw new ForbiddenException("Kullanıcı başka firmaya ait.");
        return cid;
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, Action<SqliteCommand> bind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind(cmd);
        cmd.ExecuteNonQuery();
    }
}
