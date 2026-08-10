using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Security;

/// <param name="Version">
/// KLT-01c — yetki kümesinin DÜZENLEME KİLİDİ jetonu (<c>users.version</c>).
/// Ekran bu değeri okur, kaydederken geri gönderir; arada başkası kaydettiyse sürüm artmış olur
/// ve kayıt reddedilir. 0 = sürüm bilinmiyor (eski istemci) → kontrol yapılmaz.
/// </param>
public sealed record UserPermissionData(IReadOnlyList<ModulePermission> Modules, IReadOnlyList<string> Buttons,
    long Version = 0);

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
            cmd.CommandText = "SELECT module_key, can_view, can_create, can_edit, can_delete FROM user_permissions WHERE user_id=@u;";
            cmd.AddWithValue("@u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mods.Add(new ModulePermission(r.GetString(0), r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1));
        }

        var buttons = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id=@u;";
            cmd.AddWithValue("@u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) buttons.Add(r.GetString(0));
        }

        // KLT-01c: yetki kümesinin sürüm jetonu. Kaydederken geri gönderilir (bkz. SaveForUser).
        long version = 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT version FROM users WHERE id=@u AND is_deleted=0;";
            cmd.AddWithValue("@u", userId);
            var v = cmd.ExecuteScalar();
            if (v is not null and not DBNull) version = Convert.ToInt64(v);
        }
        return new UserPermissionData(mods, buttons, version);
    }

    /// <summary>Yetki ağacı için: hedef kullanıcının ROLÜNE kapatılmış modüller (Rol Yetki Kontrol).
    /// Bu modüller ağaçta hiç gösterilmez.</summary>
    public IReadOnlySet<string> BlockedModulesForUser(SessionContext actor, string userId)
    {
        AccessControl.Require(actor, Module, PermissionAction.View);
        using var conn = _factory.Create();
        EnsureUserOwned(conn, null, actor, userId);
        return Organization.RoleGrantService.BlockedForUser(conn, null, userId);
    }

    /// <summary>
    /// Yetkileri TAM DEĞİŞTİRİR (önce sil, sonra yaz) — tek transaction.
    /// </summary>
    /// <param name="expectedVersion">
    /// KLT-01c — DÜZENLEME KİLİDİ. <see cref="GetForUser"/>'ın döndürdüğü <c>Version</c> geri gönderilir.
    /// Arada başka bir yönetici kaydettiyse sürüm artmıştır → <see cref="ConcurrencyException"/> (409) atılır
    /// ve <b>hiçbir değişiklik yazılmaz</b> (transaction geri alınır).
    ///
    /// Neden bu tablo: kayıt "sil + yeniden yaz" olduğu için <c>user_permissions</c> SATIR sürümü kümeyi
    /// koruyamaz (satırlar zaten yok ediliyor). Yetki kümesinin sahibi KULLANICI kaydıdır; bu yüzden jeton
    /// <c>users.version</c>'dır. Bu kolon şemada zaten VARDI ama hiç artırılmıyordu; hiçbir okuyucusu ve
    /// senkron bağımlılığı olmadığı için (senkron upsert'i <c>version</c>'a dokunmaz) benimsenmesi
    /// <b>migration gerektirmedi</b>.
    ///
    /// <c>null</c> → kontrol yok (geriye uyumlu: sürüm taşımayan eski çağrılar ve YENİ KULLANICI oluşturma
    /// akışı bozulmaz — yeni kullanıcıda çakışacak bir önceki kayıt zaten yoktur).
    /// </param>
    public void SaveForUser(SessionContext actor, string userId, IEnumerable<ModulePermission> modules, IEnumerable<string> buttons,
        long? expectedVersion = null)
    {
        AccessControl.Require(actor, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var companyId = EnsureUserOwned(conn, tx, actor, userId);
        EnsureManageableTarget(conn, tx, actor, userId); // #8: admin başka admin/süperadminin yetkisini düzenleyemez

        // Süper Admin düzeyi ekranlar (derleme-sabit süper-admin-only VEYA firma "superadmin" düzeyi) YALNIZ
        // "Kısıtlı Süper Admin" (veya süper admin) hedefe verilebilir — süper admin dahil kimse başka role veremez.
        var superOnlyKeys = modules.Where(m => (m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
            && (AppModules.IsSuperAdminOnly(m.ModuleKey)
                || DepoWise.Infrastructure.Organization.CompanyGrantService.IsCompanySuperRestricted(conn, tx, companyId, m.ModuleKey)))
            .Select(m => m.ModuleKey).ToHashSet(StringComparer.Ordinal);
        if (superOnlyKeys.Count > 0
            && !HasRole(conn, tx, userId, RoleKeys.RestrictedSuperAdmin)
            && !HasRole(conn, tx, userId, RoleKeys.SuperAdmin))
        {
            throw new InvalidOperationException(
                "Bu ekranlar (Kota, Canlı Sunucu, Yedekler, Makine, Güncelleme, Firma Tanım veya 'Süper Admin' düzeyine alınmış ekranlar) yalnız 'Kısıtlı Süper Admin' rolüne verilebilir. Önce kullanıcıya bu rolü atayın.");
        }

        // #3: "Admin" düzeyi kısıtlı modüller (Yönetim/Kullanıcı/Yetkiler + firma-admin düzeyi) alt role VERİLEMEZ.
        // Süper admin muaf. Süper-admin düzeyi olanlar yukarıda ele alındı → burada tekrar sayılmaz.
        if (!actor.IsSuperAdmin)
        {
            var restricted = modules.Where(m => (m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)
                && !superOnlyKeys.Contains(m.ModuleKey)
                && (AppModules.IsAdminRestricted(m.ModuleKey)
                    || DepoWise.Infrastructure.Organization.CompanyGrantService.IsCompanyRestricted(conn, tx, companyId, m.ModuleKey))).ToList();
            if (restricted.Count > 0
                && !HasRole(conn, tx, userId, RoleKeys.CompanyAdmin)
                && !HasRole(conn, tx, userId, RoleKeys.SuperAdmin))
            {
                throw new InvalidOperationException(
                    "Bu ekranlar (Yönetim / Kullanıcı / Yetkiler vb.) yalnız Admin'e verilebilir. Önce kullanıcıyı Admin yapın.");
            }
        }

        // Rol Yetki Kontrol: hedefin ROLÜNE kapatılmış ekran kimse tarafından (süper admin dahil) verilemez.
        // Açmak için önce "Rol Yetki Kontrol" ekranından o rol için serbest bırakılmalıdır.
        var roleBlocked = DepoWise.Infrastructure.Organization.RoleGrantService.BlockedForUser(conn, tx, userId);
        if (roleBlocked.Count > 0)
        {
            var hits = modules
                .Where(m => (m.CanView || m.CanCreate || m.CanEdit || m.CanDelete) && roleBlocked.Contains(m.ModuleKey))
                .Select(m => ModuleLabel(m.ModuleKey)).ToList();
            if (hits.Count > 0)
                throw new InvalidOperationException(
                    "Bu ekranlar kullanıcının rolüne kapatılmıştır ve verilemez: " + string.Join(", ", hits) +
                    ". Açmak için 'Rol Yetki Kontrol' ekranından ilgili rol için serbest bırakın.");
        }

        // Yetki YÜKSELTME engeli: Süper Admin dışındaki bir aktör, KENDİ sahip olmadığı yetkiyi başkasına VEREMEZ.
        // (Firmaya ilk açılan sınırlı admin, kendi yetkisi dışındaki alanları başkasına atayamaz.)
        var (clampMods, clampBtns) = GrantableLimit(conn, tx, actor);

        // ── KLT-01c: DÜZENLEME KİLİDİ ────────────────────────────────────────────────────────────
        // Sürüm ARTIRMA + kontrol, silme/yazmadan HEMEN ÖNCE ve AYNI transaction içinde yapılır:
        // çakışma varsa hiçbir DELETE/INSERT çalışmaz, transaction geri alınır → kısmi yazma olmaz.
        // (Geç konumlandırıldı ki users satırındaki yazma kilidi mümkün olan en kısa süre tutulsun.)
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE users SET version=version+1, updated_at=@now WHERE id=@u AND company_id=@c AND is_deleted=0"
                + EditLockGuard.Clause(expectedVersion) + ";";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@u", userId);
            cmd.AddWithValue("@c", companyId);
            EditLockGuard.Bind(cmd, expectedVersion);
            if (cmd.ExecuteNonQuery() == 0)
            {
                // Kayıt duruyorsa sebep sürüm uyuşmazlığıdır → ConcurrencyException (409).
                EditLockGuard.ThrowIfStale(conn, tx, "users", userId, companyId, expectedVersion);
                // Buraya düşmek EnsureUserOwned'dan sonra beklenmez; yine de sessiz geçilmez.
                throw new ForbiddenException("Kullanıcı bulunamadı veya başka firmaya ait.");
            }
        }

        Exec(conn, tx, "DELETE FROM user_permissions WHERE user_id=@u;", c => c.AddWithValue("@u", userId));
        Exec(conn, tx, "DELETE FROM user_button_permissions WHERE user_id=@u;", c => c.AddWithValue("@u", userId));

        foreach (var mIn in modules)
        {
            var m = ClampModule(mIn, clampMods);
            // Boş satır yazma (hiçbir bayrak yoksa atla → deny-by-default)
            if (!(m.CanView || m.CanCreate || m.CanEdit || m.CanDelete)) continue;
            Exec(conn, tx,
                "INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version) " +
                "VALUES(@id,@c,@u,@m,@v,@cr,@e,@d,@now,@now,1);",
                c =>
                {
                    c.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    c.AddWithValue("@c", companyId);
                    c.AddWithValue("@u", userId);
                    c.AddWithValue("@m", m.ModuleKey);
                    c.AddWithValue("@v", m.CanView ? 1 : 0);
                    c.AddWithValue("@cr", m.CanCreate ? 1 : 0);
                    c.AddWithValue("@e", m.CanEdit ? 1 : 0);
                    c.AddWithValue("@d", m.CanDelete ? 1 : 0);
                    c.AddWithValue("@now", now);
                });
        }
        foreach (var b in buttons.Distinct())
        {
            if (clampBtns is not null && !clampBtns.Contains(b)) continue; // kendi sahip olmadığı butonu veremez
            Exec(conn, tx,
                "INSERT INTO user_button_permissions(id, company_id, user_id, button_key, created_at) VALUES(@id,@c,@u,@b,@now);",
                c =>
                {
                    c.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    c.AddWithValue("@c", companyId);
                    c.AddWithValue("@u", userId);
                    c.AddWithValue("@b", b);
                    c.AddWithValue("@now", now);
                });
        }
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Aktörün başkasına VEREBİLECEĞİ üst sınır. null = sınırsız (Süper Admin, ya da hiç açık izni olmayan
    /// firma admini — geriye dönük uyum). Aksi halde aktörün KENDİ user_permissions/butonları sınır olur.</summary>
    private static (Dictionary<string, ModulePermission>? Mods, HashSet<string>? Btns) GrantableLimit(
        DbConnection conn, DbTransaction tx, SessionContext actor)
    {
        if (actor.IsSuperAdmin) return (null, null); // sınırsız

        var mods = new Dictionary<string, ModulePermission>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT module_key, can_view, can_create, can_edit, can_delete FROM user_permissions WHERE user_id=@u;";
            cmd.AddWithValue("@u", actor.UserId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mods[r.GetString(0)] = new ModulePermission(r.GetString(0),
                    r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1);
        }
        var btns = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id=@u;";
            cmd.AddWithValue("@u", actor.UserId);
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
    private static void EnsureManageableTarget(DbConnection conn, DbTransaction tx, SessionContext actor, string userId)
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

    private static string ModuleLabel(string moduleKey)
        => AppModules.All.FirstOrDefault(m => m.Key == moduleKey).Label ?? moduleKey;

    private static bool HasRole(DbConnection conn, DbTransaction tx, string userId, string roleKey)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=@u AND r.role_key=@k;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@k", roleKey);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static string EnsureUserOwned(DbConnection conn, DbTransaction? tx, SessionContext actor, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT company_id FROM users WHERE id=@u AND is_deleted=0;";
        cmd.AddWithValue("@u", userId);
        var cid = cmd.ExecuteScalar() as string ?? throw new ForbiddenException("Kullanıcı bulunamadı.");
        if (!actor.IsSuperAdmin && cid != actor.CompanyId) throw new ForbiddenException("Kullanıcı başka firmaya ait.");
        return cid;
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql, Action<DbCommand> bind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind(cmd);
        cmd.ExecuteNonQuery();
    }
}
