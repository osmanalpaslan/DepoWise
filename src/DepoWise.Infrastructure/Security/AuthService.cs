using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Security;

public sealed record LoginResult(
    bool Success,
    bool Locked,
    int SecondsRemaining,
    SessionContext? Session,
    string? Error = null,
    bool MustChangePassword = false);   // yeni kullanıcı / admin-reset → ilk girişte şifre belirleme zorunlu

/// <summary>Uzak (sunucu) kullanıcı paketi — masaüstü yerel DB'de kullanıcı yoksa sunucudan çekip
/// yerele yazmak için. password_hash (bcrypt) taşınır → sonraki açılışlarda offline giriş de çalışır.</summary>
public sealed record RemoteUserBundle(
    string CompanyId,
    string CompanyName,
    string UserId,
    string Username,
    string PasswordHash,
    string? FullName,
    bool IsActive,
    IReadOnlyList<string> RoleKeys,
    IReadOnlyList<ModulePermission> Permissions,
    IReadOnlyList<string> Buttons,
    bool CanViewAllBranches = false,
    string? BranchId = null,
    bool MustChangePassword = false);   // ilk giriş şifre belirleme zorunluluğu yerele taşınır

/// <summary>
/// Kimlik doğrulama + brute-force kilidi. 5 ardışık hatalı denemeden sonra 5 dk kilit.
/// Başarılı login kilidi sıfırlar. company_id login isteğinden bağımsız; oturum onunla kurulur.
/// </summary>
public sealed class AuthService
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockWindow = TimeSpan.FromMinutes(5);

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    /// <summary>F0 (YET-01): yetki fotoğrafı önbelleği. <c>null</c> → önbellek YOK, her çağrı veritabanından
    /// okur (F0 öncesi davranışın birebir aynısı). Sunucu/masaüstü host'ları paylaşılan tek örnek verir.</summary>
    private readonly PermissionSnapshotCache? _snapshots;

    public AuthService(IDbConnectionFactory factory, IClock? clock = null, PermissionSnapshotCache? snapshots = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _snapshots = snapshots;
    }

    public LoginResult Login(string companyId, string username, string password)
    {
        TenantGuard.Require(companyId);
        var now = _clock.UtcNow;
        using var conn = _factory.Create();

        // 1) Kilit kontrolü (consecutive failures since last success)
        var (failCount, lastFailMs) = ConsecutiveFailures(conn, companyId, username);
        if (failCount >= MaxFailures && lastFailMs is long lf)
        {
            var lockUntil = DateTimeOffset.FromUnixTimeMilliseconds(lf).Add(LockWindow);
            if (now < lockUntil)
                return new LoginResult(false, true, (int)Math.Ceiling((lockUntil - now).TotalSeconds), null);
        }

        // 2) Kullanıcı + parola
        var user = FindUser(conn, companyId, username);
        bool ok = user is not null && PasswordHasher.Verify(password, user.Value.PasswordHash);

        RecordAttempt(conn, companyId, username, ok);

        if (!ok)
            return new LoginResult(false, false, 0, null, "Kullanıcı adı veya parola hatalı.");

        // 3) Oturum + yetkiler
        var roles = LoadRoleKeys(conn, user!.Value.Id);
        var perms = LoadPermissions(conn, user.Value.Id);
        CreateSession(conn, user.Value.Id, companyId, now);
        var session = new SessionContext(user.Value.Id, companyId, roles, perms, LoadViewAllBranches(conn, user.Value.Id))
        {
            BlockedModules = Organization.RoleGrantService.BlockedForRoles(conn, null, roles), // Rol Yetki Kontrol
        };
        return new LoginResult(true, false, 0, session, MustChangePassword: MustChangePassword(conn, user.Value.Id));
    }

    /// <summary>Kullanıcı ilk giriş(ler)inde şifre belirlemek zorunda mı (Migration042).</summary>
    private static bool MustChangePassword(DbConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(must_change_password,0) FROM users WHERE id=@u;";
        cmd.AddWithValue("@u", userId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) == 1;
    }

    /// <summary>Firma-BAĞIMSIZ giriş: kullanıcı adı birden çok firmada olabilir. Web login companyId
    /// göndermediğinde kullanılır — kullanıcı adına sahip tüm firmalar taranır, parola tutan firma ile
    /// tam Login akışı (kilit + oturum) çalışır. Hiçbiri tutmazsa hatalı.</summary>
    public LoginResult LoginAnyCompany(string username, string password)
    {
        using var conn = _factory.Create();
        var candidates = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT company_id, password_hash FROM users WHERE username=@u AND is_active=1 AND is_deleted=0;";
            cmd.AddWithValue("@u", username);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (PasswordHasher.Verify(password, r.GetString(1)))
                    candidates.Add(r.GetString(0));
        }
        if (candidates.Count == 0)
            return new LoginResult(false, false, 0, null, "Kullanıcı adı veya parola hatalı.");
        // Parola tutan (tek) firma ile tam akış
        return Login(candidates[0], username, password);
    }

    /// <summary>Yeniden kimlik doğrulama (ör. Çöp Kutusu): oturumdaki kullanıcının parolasını tekrar doğrular.</summary>
    public bool VerifyUserPassword(string companyId, string userId, string password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM users WHERE id=@u AND company_id=@c AND is_active=1 AND is_deleted=0;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@c", companyId);
        var hash = cmd.ExecuteScalar() as string;
        return !string.IsNullOrEmpty(hash) && PasswordHasher.Verify(password, hash);
    }

    /// <summary>Parolayı YALNIZ userId ile doğrular (firma filtresi YOK). userId zaten benzersiz (PK) —
    /// kullanıcıyı tek başına tanımlar. Süper admin başka bir firma bağlamındayken ("Firma Seç" → başka firma)
    /// bile KENDİ parolasını doğrulayabilsin diye: kullanıcı kaydı EV firmasındadır, oturumun seçili firmasında
    /// değil; firma-filtreli sürüm bu durumda "Parola hatalı" veriyordu (kullanıcı bulgusu 2026-07-20).</summary>
    public bool VerifyUserPassword(string userId, string password)
    {
        if (string.IsNullOrEmpty(password)) return false;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_hash FROM users WHERE id=@u AND is_active=1 AND is_deleted=0;";
        cmd.AddWithValue("@u", userId);
        var hash = cmd.ExecuteScalar() as string;
        return !string.IsNullOrEmpty(hash) && PasswordHasher.Verify(password, hash);
    }

    /// <summary>
    /// Parola olmadan oturum kurar (yalnız "Beni Hatırla" token doğrulaması SONRASI ya da JWT'den oturum
    /// yeniden kurulurken çağrılır). Kullanıcı aktif değilse/yoksa null döner. Roller + yetkiler yüklenir.
    ///
    /// ÇOK FİRMALI SÜPER ADMİN: İstenen firma kullanıcının kendi (home) firması değilse, YALNIZ Süper Admin
    /// başka bir (var olan) firma bağlamında oturum açabilir — böylece seçtiği firmayı o firmanın admini gibi
    /// yönetir. Süper admin olmayan kullanıcı çapraz firma isteğinde null döner (tenant fail-closed).
    /// </summary>
    public SessionContext? CreateSessionForUser(string companyId, string userId)
    {
        TenantGuard.Require(companyId);
        // F0 (YET-01): fotoğraf önbellekten gelir; yoksa veritabanından okunup saklanır. Önbellek verilmemişse
        // (null) doğrudan okunur → F0 öncesi davranış. Oturum nesnesi HER ÇAĞRIDA yeniden kurulur:
        // OperatingBranchId isteğe özeldir, paylaşılamaz (bkz. PermissionSnapshot.ToSession).
        if (_snapshots is null) return LoadSnapshot(companyId, userId)?.ToSession();
        return _snapshots.GetOrLoad(companyId, userId, () => LoadSnapshot(companyId, userId))?.ToSession();
    }

    /// <summary>F0: oturum kurmak için gereken TÜM yetki verisini tek okumada toplar.
    /// İçerik ve sıra <b>bilerek değiştirilmedi</b> — F0'ın amacı davranışı taşımak, değiştirmek değil.</summary>
    private PermissionSnapshot? LoadSnapshot(string companyId, string userId)
    {
        using var conn = _factory.Create();

        // Kullanıcının kendi (home) firmasını ve aktif olup olmadığını bul.
        string? homeCompany;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT company_id FROM users WHERE id=@id AND is_active=1 AND is_deleted=0;";
            cmd.AddWithValue("@id", userId);
            homeCompany = cmd.ExecuteScalar() as string;
        }
        if (homeCompany is null) return null; // kullanıcı yok/pasif/silinmiş

        var roles = LoadRoleKeys(conn, userId);

        // İstenen firma home firma DEĞİLSE: yalnız süper admin çapraz-firma oturumu açabilir.
        if (!string.Equals(homeCompany, companyId, StringComparison.Ordinal))
        {
            if (!roles.Contains(DepoWise.Application.Security.RoleKeys.SuperAdmin)) return null; // çapraz firma yalnız süper admin

            // KİLİTLENME KORUMASI (silinmiş firma) vs FAIL-CLOSED (hiç olmayan firma) ayrımı:
            //  • Firma KAYDI HİÇ YOKSA (uydurma/geçersiz id) → null. Sahte token'a karşı fail-closed kalır.
            //  • Firma VAR ama SİLİNMİŞSE → süper admin kendi (home) firmasına düşürülür, oturum yaşar.
            //    Süper admin İÇİNDE ÇALIŞTIĞI firmayı silebiliyor; eskiden token'daki firma geçersiz olduğu için
            //    sonraki HER istek 401 dönüyordu (firma listesi hiç yüklenmiyor, tekrar silme 401 veriyordu).
            if (!CompanyExists(conn, companyId))
            {
                if (!CompanyRowExists(conn, companyId)) return null;   // hiç var olmamış firma → fail-closed
                companyId = homeCompany;                                // silinmiş firma → home'a düş (kilitlenme yok)
            }
        }

        var perms = LoadPermissions(conn, userId);
        return new PermissionSnapshot(
            CompanyId: companyId,
            UserId: userId,
            RoleKeys: roles,
            Permissions: perms,
            CanViewAllBranches: LoadViewAllBranches(conn, userId),
            BlockedModules: Organization.RoleGrantService.BlockedForRoles(conn, null, roles)); // Rol Yetki Kontrol
        // ScopeBranchIds / ScopeUnitIds / AllowedRecordTypes: F4 ve F5'e AYRILMIŞ, F0'da doldurulmaz.
    }

    /// <summary>Verilen firma id'si var (ve silinmemiş) mi?</summary>
    private static bool CompanyExists(DbConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM companies WHERE id=@c AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>Firma KAYDI hiç var mı (silinmiş olsa bile)? "Silinmiş firma" ile "hiç olmamış firma" ayrımı için.</summary>
    private static bool CompanyRowExists(DbConnection conn, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM companies WHERE id=@c;";
        cmd.AddWithValue("@c", companyId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>users.can_view_all_branches bayrağını okur (yoksa false).</summary>
    private static bool LoadViewAllBranches(DbConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(can_view_all_branches,0) FROM users WHERE id=@id;";
        cmd.AddWithValue("@id", userId);
        var v = cmd.ExecuteScalar();
        return v is not null && Convert.ToInt64(v) == 1;
    }

    /// <summary>SUNUCU tarafı: kullanıcı adı+parola doğrula ve tam kullanıcı paketini döndür (masaüstü senkron
    /// girişi için). Geçersizse null. Firma/kilit isteğe bağlı — sunucu tüm firmaların kullanıcısını doğrular.</summary>
    public RemoteUserBundle? ExportForSync(string companyId, string username, string password)
    {
        using var conn = _factory.Create();

        // Kullanıcıyı bul; companyId boşsa TÜM firmalar taranır (kullanıcı adı birden çok firmada olabilir).
        using var find = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(companyId))
            find.CommandText = "SELECT id, company_id, password_hash, full_name, branch_id, COALESCE(must_change_password,0) FROM users WHERE username=@u AND is_active=1 AND is_deleted=0;";
        else
        {
            find.CommandText = "SELECT id, company_id, password_hash, full_name, branch_id, COALESCE(must_change_password,0) FROM users WHERE company_id=@c AND username=@u AND is_active=1 AND is_deleted=0;";
            find.AddWithValue("@c", companyId);
        }
        find.AddWithValue("@u", username);
        string? userId = null, coId = null, fullName = null, hash = null, branchId = null;
        bool mustChange = false;
        using (var r = find.ExecuteReader())
        {
            while (r.Read())
            {
                if (!PasswordHasher.Verify(password, r.GetString(2))) continue;
                userId = r.GetString(0); coId = r.GetString(1); hash = r.GetString(2);
                fullName = r.IsDBNull(3) ? null : r.GetString(3);
                branchId = r.IsDBNull(4) ? null : r.GetString(4);
                mustChange = r.GetInt64(5) == 1;
                break;
            }
        }
        if (userId is null || coId is null || hash is null) return null;

        var roles = LoadRoleKeys(conn, userId);
        var perms = LoadPermissions(conn, userId);
        string coName = coId;
        using (var cn = conn.CreateCommand())
        {
            cn.CommandText = "SELECT name FROM companies WHERE id=@c;";
            cn.AddWithValue("@c", coId);
            coName = cn.ExecuteScalar() as string ?? coId;
        }
        return new RemoteUserBundle(coId, coName, userId, username, hash, fullName, true,
            roles, perms.Modules.ToList(), perms.Buttons.ToList(), LoadViewAllBranches(conn, userId), branchId, mustChange);
    }

    /// <summary>MASAÜSTÜ tarafı: sunucudan gelen kullanıcı paketini YEREL DB'ye yazar (upsert). Sonrasında
    /// normal yerel Login çalışır (offline dahil). Roller sistem-global olduğundan role_key ile eşlenir.</summary>
    public void ImportRemoteUser(RemoteUserBundle b)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // 1) Firma (FK) — yoksa oluştur
        using (var c = conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted) VALUES(@id,@n,@now,@now,1,0) ON CONFLICT DO NOTHING;";
            c.AddWithValue("@id", b.CompanyId);
            c.AddWithValue("@n", b.CompanyName);
            c.AddWithValue("@now", now);
            c.ExecuteNonQuery();
        }
        // 2) Kullanıcı (upsert, id korunur)
        using (var u = conn.CreateCommand())
        {
            u.Transaction = tx;
            u.CommandText =
                "INSERT INTO users(id, company_id, username, password_hash, full_name, can_view_all_branches, branch_id, is_active, must_change_password, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id,@c,@un,@h,@f,@va,@bid,1,@mcp,@now,@now,1,0) " +
                "ON CONFLICT(id) DO UPDATE SET company_id=@c, username=@un, password_hash=@h, full_name=@f, can_view_all_branches=@va, branch_id=@bid, is_active=1, is_deleted=0, must_change_password=@mcp, updated_at=@now;";
            u.AddWithValue("@id", b.UserId);
            u.AddWithValue("@c", b.CompanyId);
            u.AddWithValue("@un", b.Username);
            u.AddWithValue("@h", b.PasswordHash);
            u.AddWithValue("@f", (object?)b.FullName ?? DBNull.Value);
            u.AddWithValue("@va", b.CanViewAllBranches ? 1 : 0);
            u.AddWithValue("@bid", (object?)b.BranchId ?? DBNull.Value);
            u.AddWithValue("@mcp", b.MustChangePassword ? 1 : 0);
            u.AddWithValue("@now", now);
            u.ExecuteNonQuery();
        }
        // 3) Roller (tam değiştir)
        using (var d = conn.CreateCommand())
        { d.Transaction = tx; d.CommandText = "DELETE FROM user_roles WHERE user_id=@u;"; d.AddWithValue("@u", b.UserId); d.ExecuteNonQuery(); }
        foreach (var rk in b.RoleKeys.Distinct())
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
            ir.AddWithValue("@u", b.UserId);
            ir.AddWithValue("@r", roleId);
            ir.ExecuteNonQuery();
        }
        // 4) Yetkiler (tam değiştir)
        using (var d = conn.CreateCommand())
        { d.Transaction = tx; d.CommandText = "DELETE FROM user_permissions WHERE user_id=@u;"; d.AddWithValue("@u", b.UserId); d.ExecuteNonQuery(); }
        foreach (var p in b.Permissions)
        {
            using var ip = conn.CreateCommand();
            ip.Transaction = tx;
            ip.CommandText =
                "INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version) " +
                "VALUES(@id,@c,@u,@m,@v,@cr,@e,@d,@now,@now,1);";
            ip.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            ip.AddWithValue("@c", b.CompanyId);
            ip.AddWithValue("@u", b.UserId);
            ip.AddWithValue("@m", p.ModuleKey);
            ip.AddWithValue("@v", p.CanView ? 1 : 0);
            ip.AddWithValue("@cr", p.CanCreate ? 1 : 0);
            ip.AddWithValue("@e", p.CanEdit ? 1 : 0);
            ip.AddWithValue("@d", p.CanDelete ? 1 : 0);
            ip.AddWithValue("@now", now);
            ip.ExecuteNonQuery();
        }
        // 5) Özel buton izinleri (tam değiştir)
        using (var d = conn.CreateCommand())
        { d.Transaction = tx; d.CommandText = "DELETE FROM user_button_permissions WHERE user_id=@u;"; d.AddWithValue("@u", b.UserId); d.ExecuteNonQuery(); }
        foreach (var bk in b.Buttons.Distinct())
        {
            using var ib = conn.CreateCommand();
            ib.Transaction = tx;
            ib.CommandText =
                "INSERT INTO user_button_permissions(id, company_id, user_id, button_key, created_at) VALUES(@id,@c,@u,@b,@now);";
            ib.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            ib.AddWithValue("@c", b.CompanyId);
            ib.AddWithValue("@u", b.UserId);
            ib.AddWithValue("@b", bk);
            ib.AddWithValue("@now", now);
            ib.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private (int count, long? lastFailMs) ConsecutiveFailures(DbConnection conn, string companyId, string username)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT success, attempted_at FROM login_attempts " +
            "WHERE company_id = @c AND username = @u ORDER BY attempted_at DESC;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@u", username);
        using var r = cmd.ExecuteReader();
        int count = 0; long? lastFail = null;
        while (r.Read())
        {
            if (r.GetInt64(0) == 1) break; // son başarıya kadar say
            count++;
            lastFail ??= r.GetInt64(1);
        }
        return (count, lastFail);
    }

    private void RecordAttempt(DbConnection conn, string companyId, string username, bool success)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO login_attempts(id, company_id, username, success, attempted_at) " +
            "VALUES(@id, @c, @u, @s, @t);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@u", username);
        cmd.AddWithValue("@s", success ? 1 : 0);
        cmd.AddWithValue("@t", _clock.UtcNow.ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }

    private readonly record struct UserRow(string Id, string PasswordHash);

    private static UserRow? FindUser(DbConnection conn, string companyId, string username)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, password_hash FROM users " +
            "WHERE company_id = @c AND username = @u AND is_active = 1 AND is_deleted = 0;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@u", username);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new UserRow(r.GetString(0), r.GetString(1)) : null;
    }

    private static List<string> LoadRoleKeys(DbConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id = ur.role_id " +
            "WHERE ur.user_id = @u AND r.is_deleted = 0;";
        cmd.AddWithValue("@u", userId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    private static PermissionSet LoadPermissions(DbConnection conn, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT module_key, can_view, can_create, can_edit, can_delete " +
            "FROM user_permissions WHERE user_id = @u;";
        cmd.AddWithValue("@u", userId);
        var mods = new List<ModulePermission>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                mods.Add(new ModulePermission(r.GetString(0), r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1));

        // Özel buton ("+") izinleri
        var buttons = new List<string>();
        using (var bc = conn.CreateCommand())
        {
            bc.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id = @u;";
            bc.AddWithValue("@u", userId);
            using var br = bc.ExecuteReader();
            while (br.Read()) buttons.Add(br.GetString(0));
        }
        return new PermissionSet(mods, buttons);
    }

    private void CreateSession(DbConnection conn, string userId, string companyId, DateTimeOffset now)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO sessions(id, user_id, company_id, created_at, expires_at, revoked_at) " +
            "VALUES(@id, @u, @c, @now, @exp, NULL);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@now", now.ToUnixTimeMilliseconds());
        cmd.AddWithValue("@exp", now.AddHours(12).ToUnixTimeMilliseconds());
        cmd.ExecuteNonQuery();
    }
}
