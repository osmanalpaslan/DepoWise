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

        Exec(conn, tx, "DELETE FROM user_permissions WHERE user_id=$u;", c => c.Parameters.AddWithValue("$u", userId));
        Exec(conn, tx, "DELETE FROM user_button_permissions WHERE user_id=$u;", c => c.Parameters.AddWithValue("$u", userId));

        foreach (var m in modules)
        {
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
