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

/// <summary>G1a — yetki özetinde bir modül satırı. Değerler HAM satır değil, <see cref="AccessControl.Can"/>
/// ile hesaplanmış ETKİN yetkidir (rol bypass'ı ve rol kilitleri uygulanmış hâli).</summary>
public sealed record PermissionSummaryRow(string ModuleKey, string Label,
    bool View, bool Create, bool Edit, bool Delete, bool RoleBlocked)
{
    /// <summary>Kullanıcıya gösterilecek kısa metin — teknik terim yok (CLAUDE.md §2).</summary>
    public string ActionsText
    {
        get
        {
            var a = new List<string>(4);
            if (View) a.Add("Görüntüleme");
            if (Create) a.Add("Ekleme");
            if (Edit) a.Add("Düzenleme");
            if (Delete) a.Add("Silme");
            return a.Count == 0 ? "—" : string.Join(" · ", a);
        }
    }
}

/// <summary>G1a — özetteki özel buton satırı.</summary>
public sealed record PermissionSummaryButton(string ButtonKey, string Label);

/// <summary>
/// G1a — "Bu kullanıcı gerçekte neye erişebiliyor?" sorusunun tek yanıtı. Ham izin satırları yanıltıcıdır:
/// adminin hiç satırı olmadan her şeye erişimi vardır; rolüne kapatılmış bir modüle satırı olsa da erişemez.
/// Bu yüzden özet, hedefin oturumu yeniden kurularak <see cref="AccessControl.Can"/> ile hesaplanır.
/// </summary>
public sealed record PermissionSummary(string UserId, string CompanyId, IReadOnlyList<string> RoleKeys,
    IReadOnlyList<PermissionSummaryRow> Modules, IReadOnlyList<PermissionSummaryButton> Buttons,
    int ExplicitModuleRows, int ExplicitButtonRows, int RoleBlockedCount)
{
    public bool IsSuperAdmin => RoleKeys.Contains(DepoWise.Application.Security.RoleKeys.SuperAdmin);
    public bool IsCompanyAdmin => RoleKeys.Contains(DepoWise.Application.Security.RoleKeys.CompanyAdmin);

    /// <summary>Erişilebilen modül sayısı (görüntüleme yetkisi olanlar).</summary>
    public int VisibleModuleCount => Modules.Count(m => m.View);

    /// <summary>Yetkinin nereden geldiğini açıklayan tek cümle — kullanıcı "neden her şeyi görüyor?" desin diye.</summary>
    public string SourceText => IsSuperAdmin
        ? "Süper Admin — tüm ekranlara erişir; ayrıca izin satırı gerekmez."
        : IsCompanyAdmin
            ? $"Admin — normal ekranlara rolü gereği erişir ({ExplicitModuleRows} açık izin satırı)."
            : $"Personel — yalnız açıkça verilen izinler ({ExplicitModuleRows} modül, {ExplicitButtonRows} buton).";
}

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
    /// <summary>F0 (YET-01): yetki kaydedilince o kullanıcının fotoğrafı ANINDA düşürülür — yetki kaybı
    /// TTL kadar gecikmemelidir. <c>null</c> → önbellek yok (F0 öncesi davranış).</summary>
    private readonly PermissionSnapshotCache? _snapshots;

    public PermissionService(IDbConnectionFactory factory, IClock? clock = null, PermissionSnapshotCache? snapshots = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _snapshots = snapshots;
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
        var companyId = EnsureUserOwned(conn, null, actor, userId);
        return Organization.RoleGrantService.BlockedForUser(conn, null, companyId, userId);
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
        // ⭐ B5 (kullanıcı kararı 2026-08-19): SÜPER ADMIN BU KİLİTTEN MUAFTIR.
        // "Yetki tamamen süper adminin elinde olsun" — sistemin sahibi, bir ekranı istediği role
        // verebilmelidir. Alt roller için kural AYNEN sürer (admin bunu hâlâ yapamaz).
        if (superOnlyKeys.Count > 0
            && !actor.IsSuperAdmin
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
        var roleBlocked = DepoWise.Infrastructure.Organization.RoleGrantService.BlockedForUser(conn, tx, companyId, userId);
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

        // ⭐ YETKİ DEĞİŞİKLİĞİ İZLENEBİLİRLİĞİ (kullanıcı isteği 2026-09-04) ────────────────────────
        // Denetim kaydı zaten yazılıyordu (kim, ne zaman) ama before/after BOŞTU → "neyi değiştirdim,
        // kaydoldu mu?" sorusu veriden CEVAPLANAMIYORDU. Gerçek bir olayda bu eksik acıttı: kullanıcı
        // bazı yetkileri kaldırdığını söyledi, veritabanında ise 60 modülün 60'ı da tam yetkiliydi ve
        // ne gönderildiği kanıtlanamadı. Artık ÖNCEKİ ve SONRAKİ durum kayda geçer.
        // Silmeden ÖNCE okunur; aynı transaction içinde olduğu için tutarlıdır.
        var oncesi = PermSnapshot(conn, tx, userId);

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
        // Yazma bittikten SONRA oku: kırpma (ClampModule) ve boş-satır atlama sonrası GERÇEKTE ne
        // kaydedildiğini gösterir — arayüzün ne gönderdiğini değil.
        var sonrasi = PermSnapshot(conn, tx, userId);
        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user", userId, AuditActions.Update, actor.UserId,
            BeforeJson: oncesi, AfterJson: sonrasi), _clock);
        tx.Commit();
        // F0 (YET-01): commit'ten SONRA düşür — yeni yetki bir sonraki istekte GEÇERLİ olur, TTL beklenmez.
        // (Commit'ten önce düşürmek, geri alınan bir transaction'da eski değeri yeniden yükletirdi.)
        _snapshots?.InvalidateUser(userId);
    }

    /// <summary>Aktörün başkasına VEREBİLECEĞİ üst sınır. null = sınırsız (Süper Admin, ya da hiç açık izni olmayan
    /// firma admini — geriye dönük uyum). Aksi halde aktörün KENDİ user_permissions/butonları sınır olur.</summary>
    /// <summary>
    /// G1b (2026-08-12) — DEVRETME TAVANI. Aktörün BAŞKASINA verebileceği en üst yetki.
    ///
    /// 🔴 ESKİ MODELDEKİ AÇIK: kırpma yalnız aktörün <c>user_permissions</c> SATIRLARINA bakıyordu ve
    /// satırı olmayan firma admini <b>sınırsız</b> sayılıyordu (<c>mods.Count == 0 &amp;&amp; IsCompanyAdmin</c>).
    /// Firma admini tipik olarak bypass ile çalışır ve satırı YOKTUR → kırpma pratikte hiç uygulanmıyordu.
    /// Somut sonuç: süper adminin aktörün ROLÜNE kapattığı bir modülü (aktör kendisi kullanamadığı hâlde)
    /// başkasına VEREBİLİYORDU — gerçek bir yetki yükseltme yolu.
    ///
    /// ✅ YENİ MODEL: tavan <see cref="AccessControl.GrantCeiling"/>'den gelir; o da <see cref="AccessControl.Can"/>
    /// ile AYNI kuralları uygular (rol kilidi → süper-admin-only → admin bypass → açık izin). Yani
    /// "aktörün gerçekten erişebildiği" ile "verebileceği" ARTIK AYNI ŞEYDİR. Firma admininin normal
    /// modülleri devretmesi bozulmaz (bypass ile onlara zaten tam erişimi var); yalnız rolüne kapatılmış
    /// modüller ve süper-admin-only ekranlar kapanır.
    ///
    /// Açık satırlar veritabanından AYNI transaction içinde okunur (oturum anlık görüntüsü bayat olabilir).
    /// </summary>
    private static (Dictionary<string, ModulePermission>? Mods, HashSet<string>? Btns) GrantableLimit(
        DbConnection conn, DbTransaction tx, SessionContext actor)
    {
        if (actor.IsSuperAdmin) return (null, null); // sınırsız

        // Aktörün AÇIK satırları (taze) — tavan hesabında "kendi izni" girdisi olarak kullanılır.
        var own = new Dictionary<string, ModulePermission>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT module_key, can_view, can_create, can_edit, can_delete FROM user_permissions WHERE user_id=@u;";
            cmd.AddWithValue("@u", actor.UserId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                own[r.GetString(0)] = new ModulePermission(r.GetString(0),
                    r.GetInt64(1) == 1, r.GetInt64(2) == 1, r.GetInt64(3) == 1, r.GetInt64(4) == 1);
        }

        // TAVAN: her modül için AccessControl kuralları (tek kaynak). Ağaçtaki her modül hesaplanır ki
        // "satırı yok → sınırsız" gibi bir kısayol KALMASIN.
        var mods = new Dictionary<string, ModulePermission>(StringComparer.Ordinal);
        foreach (var (key, _) in AppModules.All)
        {
            own.TryGetValue(key, out var explicitOwn);
            mods[key] = AccessControl.GrantCeiling(actor, key, explicitOwn);
        }

        // BUTONLAR: CanUseButton ile aynı kural (admin bypass dahil).
        var btns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, _) in SpecialButtons.All)
            if (AccessControl.CanGrantButtonKey(actor, key))
                btns.Add(key);

        return (mods, btns);
    }

    /// <summary>
    /// G1a (2026-08-12) — YETKİ SIFIRLAMA. Kullanıcının TÜM modül ve buton izinlerini siler
    /// (deny-by-default'a döner). Rol ataması ve kullanıcı kaydı DEĞİŞMEZ — yalnız izinler silinir.
    ///
    /// <see cref="SaveForUser"/> ile AYNI kapılardan geçer: yetki · firma sahipliği · hedef
    /// yönetilebilirlik · düzenleme kilidi · audit · anlık görüntü tazeleme. Ayrı bir "kısa yol"
    /// YOKTUR — aksi halde sıfırlama, kaydetmenin korumalarını atlayan ikinci bir yazma yolu olurdu.
    ///
    /// ⚠️ Yetki KIRPMA burada gerekmez: sıfırlama yalnız yetki KALDIRIR, hiçbir yetki VERMEZ →
    /// yükseltme riski yoktur.
    /// </summary>
    /// <returns>Silinen modül satırı + buton satırı sayısı (kullanıcıya gösterilecek özet).</returns>
    public (int Modules, int Buttons) ResetForUser(SessionContext actor, string userId, long? expectedVersion = null)
    {
        AccessControl.Require(actor, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        var companyId = EnsureUserOwned(conn, tx, actor, userId);
        EnsureManageableTarget(conn, tx, actor, userId);   // admin, başka admini/süper admini sıfırlayamaz

        // Kendi yetkisini sıfırlamak, kullanıcıyı kendi ekranından kilitler → bilinçli olarak ENGELLENİR.
        if (string.Equals(actor.UserId, userId, StringComparison.Ordinal))
            throw new InvalidOperationException("Kendi yetkilerinizi sıfırlayamazsınız.");

        // Düzenleme kilidi + sürüm artırma: SaveForUser ile aynı sıra (yazmadan hemen önce, aynı transaction).
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
                // SaveForUser ile AYNI ayrım: önce sürüm çakışması (409), sonra sahiplik (403).
                EditLockGuard.ThrowIfStale(conn, tx, "users", userId, companyId, expectedVersion);
                throw new ForbiddenException("Kullanıcı bulunamadı veya başka firmaya ait.");
            }
        }

        int mods, btns;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM user_permissions WHERE user_id=@u;";
            cmd.AddWithValue("@u", userId);
            mods = cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM user_button_permissions WHERE user_id=@u;";
            cmd.AddWithValue("@u", userId);
            btns = cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user_permissions", userId, AuditActions.Delete,
            actor.UserId, AfterJson: $"{{\"reset\":true,\"modules\":{mods},\"buttons\":{btns}}}"), _clock);

        tx.Commit();
        _snapshots?.InvalidateUser(userId);   // oturum anlık görüntüsü bayat kalmasın
        return (mods, btns);
    }

    /// <summary>
    /// G1a — YETKİ ÖZETİ (salt okuma). "Bu kullanıcı gerçekte neye erişebiliyor?" sorusunun tek yanıtı.
    /// Ham satırlar değil, <see cref="AccessControl.Can"/> ile hesaplanan ETKİN yetki döner — yani rol
    /// bypass'ı, rol kilitleri ve süper-admin-only kuralları uygulanmış hâli. Ekranlar bunu doğrudan gösterir.
    /// </summary>
    public PermissionSummary SummaryForUser(SessionContext actor, string userId)
    {
        AccessControl.Require(actor, Module, PermissionAction.View);
        var data = GetForUser(actor, userId);   // kendi kapılarını uygular (firma sahipliği vb.)

        using var conn = _factory.Create();
        var companyId = EnsureUserOwned(conn, null, actor, userId);
        var roles = RolesOf(conn, null, userId);
        var blocked = DepoWise.Infrastructure.Organization.RoleGrantService.BlockedForUser(conn, null, companyId, userId);

        // Hedefin ETKİN yetkisini, hedefin KENDİ oturumu gibi hesapla (ham satır göstermek yanıltıcıdır:
        // admin'de satır olmasa da her şeye erişir; rol kilidi varsa satır olsa da erişemez).
        var target = new SessionContext(userId, companyId, roles,
            new PermissionSet(data.Modules, data.Buttons)) { BlockedModules = blocked };

        var rows = new List<PermissionSummaryRow>();
        foreach (var (key, label) in AppModules.All)
        {
            var v = AccessControl.Can(target, key, PermissionAction.View);
            var c = AccessControl.Can(target, key, PermissionAction.Create);
            var e = AccessControl.Can(target, key, PermissionAction.Edit);
            var d = AccessControl.Can(target, key, PermissionAction.Delete);
            if (!v && !c && !e && !d) continue;                 // erişimi olmayan modül özet dışı
            rows.Add(new PermissionSummaryRow(key, label, v, c, e, d, blocked.Contains(key)));
        }
        var buttons = SpecialButtons.All
            .Where(b => AccessControl.CanUseButton(target, b.Key))
            .Select(b => new PermissionSummaryButton(b.Key, b.Label)).ToList();

        return new PermissionSummary(userId, companyId, roles, rows, buttons,
            data.Modules.Count, data.Buttons.Count, blocked.Count);
    }

    private static IReadOnlyList<string> RolesOf(DbConnection conn, DbTransaction? tx, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=@u;";
        cmd.AddWithValue("@u", userId);
        var list = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
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

    /// <summary>
    /// Kullanıcının O ANKİ yetkilerinin denetim kaydına yazılacak özeti (kullanıcı isteği 2026-09-04).
    ///
    /// Biçim bilinçli olarak KOMPAKT: <c>{"m":["daily_activity:1111","materials:1000"],"b":["btn-x"]}</c>
    /// — 60 modül için ~1 KB. Bayrak sırası: görüntüle/oluştur/düzenle/sil. Modül anahtarları sıralıdır
    /// ki iki kayıt gözle karşılaştırılabilsin.
    /// </summary>
    private static string PermSnapshot(DbConnection conn, DbTransaction tx, string userId)
    {
        var mods = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT module_key, can_view, can_create, can_edit, can_delete " +
                              "FROM user_permissions WHERE user_id=@u ORDER BY module_key;";
            cmd.AddWithValue("@u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                mods.Add($"{r.GetString(0)}:{Convert.ToInt32(r.GetValue(1))}{Convert.ToInt32(r.GetValue(2))}" +
                         $"{Convert.ToInt32(r.GetValue(3))}{Convert.ToInt32(r.GetValue(4))}");
        }

        var btns = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT button_key FROM user_button_permissions WHERE user_id=@u ORDER BY button_key;";
            cmd.AddWithValue("@u", userId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) btns.Add(r.GetString(0));
        }

        return System.Text.Json.JsonSerializer.Serialize(new { m = mods, b = btns });
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql, Action<DbCommand> bind)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        bind(cmd);
        cmd.ExecuteNonQuery();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════
    //  G4-3c — ŞUBE KAPSAMI YÖNETİMİ (GAP-7, kullanıcı isteği 2026-08-12)
    // ═══════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kullanıcının ŞUBE KAPSAMI görünümü. Bu bir <b>ikinci yetki ağacı DEĞİLDİR</b>: modül yetkileri
    /// aynı ekranda, aynı <see cref="SaveForUser"/> mimarisinde kalır; burada yalnız "hangi şubelerde"
    /// sorusunun cevabı yönetilir.
    ///
    /// <b>ETKİN ERİŞİM = MODÜL YETKİSİ ∧ ŞUBE KAPSAMI ∧ PLATFORM ∧ diğer AccessControl kuralları.</b>
    /// Kapsam vermek yetki vermek DEĞİLDİR; yetki vermek de kapsam vermek değildir.
    /// </summary>
    /// <param name="Mode">Kapsam kipi — repodaki GERÇEK modelden türetilir, yeni enum uydurulmadı:
    /// explicit = user_scopes satırları var; all = tüm şubeler (admin/CanViewAllBranches);
    /// own = açık kapsam yok, users.branch_id tek şube; none = ikisi de yok (sınırsız fallback).</param>
    public sealed record BranchScopeView(
        string Mode,
        IReadOnlyList<string> ScopeBranchIds,
        string? HomeBranchId,
        bool CanViewAllBranches,
        IReadOnlyList<BranchOption> AssignableBranches)
    {
        public string ModeText => Mode switch
        {
            "explicit" => "Seçili şubeler",
            "all" => "Tüm şubeler",
            "own" => "Yalnız kendi şubesi",
            _ => "Kapsam atanmamış",
        };
    }

    /// <summary>Atanabilir şube seçeneği (id + ad).</summary>
    public sealed record BranchOption(string Id, string Name);

    /// <summary>
    /// Hedef kullanıcının şube kapsamı + AKTÖRÜN verebileceği şubeler.
    /// <b>Atanabilir liste aktörün kendi kapsamıyla sınırlıdır</b> — kendisinde olmayan şubeyi
    /// listede bile göremez (UI yanlışlıkla sunamaz; asıl kapı yine serviste).
    /// </summary>
    public BranchScopeView GetBranchScope(SessionContext actor, string userId)
    {
        AccessControl.Require(actor, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var companyId = EnsureUserOwned(conn, null, actor, userId);

        var scope = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT branch_id FROM user_scopes WHERE user_id=@u AND company_id=@c;";
            cmd.AddWithValue("@u", userId); cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) scope.Add(r.GetString(0));
        }

        string? home = null; bool viewAll = false;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT branch_id, COALESCE(can_view_all_branches,0) FROM users WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@id", userId); cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                home = r.IsDBNull(0) ? null : r.GetString(0);
                viewAll = Convert.ToInt64(r.GetValue(1)) != 0;
            }
        }

        // Firmanın şubeleri → AKTÖRÜN kapsamıyla kırpılır (grant ceiling'in liste karşılığı).
        var all = new List<BranchOption>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM branches WHERE company_id=@c AND is_deleted=0 ORDER BY name;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) all.Add(new BranchOption(r.GetString(0), r.GetString(1)));
        }
        var mine = BranchAccess.Allowed(actor);
        var assignable = mine is null
            ? all
            : all.Where(b => mine.Contains(b.Id, StringComparer.Ordinal)).ToList();

        var mode = scope.Count > 0 ? "explicit"
                 : viewAll ? "all"
                 : home is not null ? "own"
                 : "none";
        return new BranchScopeView(mode, scope, home, viewAll, assignable);
    }

    /// <summary>
    /// Kullanıcının şube kapsamını yazar (TEK transaction: sil + ekle → kısmi kapsam oluşmaz).
    ///
    /// <b>DEVİR TAVANI:</b> aktör kendisinde OLMAYAN şubeyi veremez
    /// (<see cref="BranchAccess.RequireGrantable"/>). Sessizce kırpılmaz — hata verir, kullanıcı
    /// neyi veremediğini görür. G1'in "sahip olmadığın yetkiyi devredemezsin" kuralının şube karşılığı.
    ///
    /// <b>KENDİ KAPSAMINI DEĞİŞTİREMEZ:</b> kullanıcı kendi kapsamını yazamaz (yetki sıfırlamadaki
    /// kuralın aynısı) — kendi kapsamını genişletme yolu kapalıdır.
    ///
    /// Boş liste gönderilirse açık kapsam KALDIRILIR (kullanıcı own/all davranışına döner).
    /// </summary>
    public void SaveBranchScope(SessionContext actor, string userId, IReadOnlyList<string> branchIds)
    {
        AccessControl.Require(actor, Module, PermissionAction.Edit);

        if (string.Equals(actor.UserId, userId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Kendi şube kapsamınızı değiştiremezsiniz. Bu işlemi başka bir yetkili yapmalıdır.");

        // Aktörün kapsamı dışındaki şube HİÇBİR koşulda devredilemez (admin bypass'ı bunu kaldırmaz).
        BranchAccess.RequireGrantable(actor, branchIds);

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var companyId = EnsureUserOwned(conn, tx, actor, userId);
        EnsureManageableTarget(conn, tx, actor, userId);   // admin başka admin/süperadmini düzenleyemez

        var hedef = branchIds.Distinct(StringComparer.Ordinal).ToList();

        // Firma izolasyonu: şubelerin hepsi bu firmaya ait ve silinmemiş olmalı.
        foreach (var b in hedef)
        {
            using var chk = conn.CreateCommand();
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@b AND company_id=@c AND is_deleted=0;";
            chk.AddWithValue("@b", b); chk.AddWithValue("@c", companyId);
            if (Convert.ToInt64(chk.ExecuteScalar()) == 0)
                throw new ForbiddenException("Şube bulunamadı veya başka firmaya ait.");
        }

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM user_scopes WHERE user_id=@u AND company_id=@c;";
            del.AddWithValue("@u", userId); del.AddWithValue("@c", companyId);
            del.ExecuteNonQuery();
        }

        foreach (var b in hedef)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO user_scopes(user_id, company_id, branch_id) VALUES(@u,@c,@b);";
            ins.AddWithValue("@u", userId); ins.AddWithValue("@c", companyId); ins.AddWithValue("@b", b);
            ins.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(companyId, "user_scopes", userId, AuditActions.Update,
            actor.UserId, AfterJson: "{\"branches\":" + hedef.Count + "}"), _clock);
        tx.Commit();

        // Yetki fotoğrafı önbelleği: kapsam değiştiği için hedef kullanıcının oturumu tazelenmeli.
        _snapshots?.InvalidateUser(userId);
    }
}
