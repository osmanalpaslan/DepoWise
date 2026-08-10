using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Rol Yetki Kontrol satırı: bir ekranın her rol için durumu.
/// <paramref name="Blocked"/> = o role verilemez. <paramref name="Hard"/> = yapısal kilit (değiştirilemez):
/// süper-admin-only ekran Admin/Personel'e; admin-kısıtlı ekran Personel'e zaten verilemez.</summary>
public sealed record RoleGrantCell(string RoleKey, bool Blocked, bool Hard);

public sealed record RoleGrantRow(string ModuleKey, string Label, IReadOnlyList<RoleGrantCell> Cells);

/// <summary>
/// Rol Yetki Kontrol (YALNIZ Süper Admin). Firma Yetki Kontrol'ün ROL eksenli karşılığı: bir ekranın
/// belirli bir role verilmesini platform genelinde yasaklar. Yasaklı modül:
///  1) yetki AĞACINDA görünmez (o roldeki kullanıcıya yetki verilirken listelenmez),
///  2) grant sırasında reddedilir (PermissionService),
///  3) oturum yüklenirken izin satırı DÜŞÜRÜLÜR → admin bypass'ı dahil API/UI erişimi kapanır.
/// Süper Admin bu kısıtlardan muaftır (aksi halde platform sahibi kendini kilitler).
/// </summary>
public sealed class RoleGrantService
{
    /// <summary>Matriste yönetilebilen roller (Süper Admin hariç — o daima tam yetkilidir).</summary>
    public static readonly IReadOnlyList<(string Key, string Name)> ManagedRoles = new[]
    {
        (RoleKeys.RestrictedSuperAdmin, "Kısıtlı Süper Admin"),
        (RoleKeys.CompanyAdmin, "Admin"),
        (RoleKeys.Staff, "Personel"),
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    /// <summary>F0 (YET-01): rol kısıtı ROL seviyesindedir → değişince o role sahip HERKES etkilenir.
    /// Kimlerin etkilendiğini ayrıca sorgulamak yerine tüm fotoğraflar düşürülür (güvenli taraf).
    /// <c>null</c> → önbellek yok (F0 öncesi davranış).</summary>
    private readonly PermissionSnapshotCache? _snapshots;

    public RoleGrantService(IDbConnectionFactory factory, IClock? clock = null, PermissionSnapshotCache? snapshots = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _snapshots = snapshots;
    }

    /// <summary>Yapısal (değiştirilemez) kilit: mevcut kurallar gereği bu modül bu role zaten verilemez.</summary>
    public static bool IsHardBlocked(string moduleKey, string roleKey)
    {
        if (roleKey == RoleKeys.SuperAdmin) return false;
        if (AppModules.IsSuperAdminOnly(moduleKey)) return roleKey != RoleKeys.RestrictedSuperAdmin;
        if (AppModules.IsAdminRestricted(moduleKey)) return roleKey == RoleKeys.Staff;
        return false;
    }

    public IReadOnlyList<RoleGrantRow> GetControl(SessionContext s)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Rol Yetki Kontrol yalnız süper admine açıktır.");
        var blocked = LoadAll();
        var rows = new List<RoleGrantRow>();
        foreach (var (key, label) in AppModules.All)
        {
            if (AppModules.IsPublic(key)) continue; // Ana Ekran/Tema/Hakkında — kapatılamaz
            var cells = ManagedRoles.Select(r => new RoleGrantCell(
                r.Key,
                IsHardBlocked(key, r.Key) || (blocked.TryGetValue(r.Key, out var set) && set.Contains(key)),
                IsHardBlocked(key, r.Key))).ToList();
            rows.Add(new RoleGrantRow(key, label, cells));
        }
        return rows;
    }

    /// <summary>Matrisi TAM DEĞİŞTİRİR (rol → kapalı modül anahtarları). Yalnız süper admin.
    /// Yapısal kilitler ve public modüller yok sayılır; Süper Admin rolü hiç yazılmaz.</summary>
    public void SetMatrix(SessionContext s, IReadOnlyDictionary<string, IReadOnlyList<string>> blockedByRole)
    {
        if (!s.IsSuperAdmin) throw new ForbiddenException("Rol Yetki Kontrol yalnız süper admine açıktır.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM role_grant_limits;";
            del.ExecuteNonQuery();
        }
        foreach (var (roleKey, modules) in blockedByRole)
        {
            if (!ManagedRoles.Any(r => r.Key == roleKey)) continue; // Süper Admin / bilinmeyen rol yazılmaz
            foreach (var moduleKey in modules.Distinct(StringComparer.Ordinal))
            {
                if (!AppModules.All.Any(m => m.Key == moduleKey)) continue;
                if (AppModules.IsPublic(moduleKey)) continue;
                if (IsHardBlocked(moduleKey, roleKey)) continue; // zaten yapısal kilitli — satır tutmaya gerek yok
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO role_grant_limits(id, role_key, module_key, created_at) VALUES(@id,@r,@m,@now) ON CONFLICT DO NOTHING;";
                ins.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                ins.AddWithValue("@r", roleKey);
                ins.AddWithValue("@m", moduleKey);
                ins.AddWithValue("@now", now);
                ins.ExecuteNonQuery();
            }
        }
        tx.Commit();
        _snapshots?.InvalidateAll();   // F0: rol kısıtı herkesi etkileyebilir → tüm fotoğraflar düşürülür
    }

    /// <summary>Bir kullanıcının rollerine göre KAPALI modüller (yetki ağacı + grant kontrolü için).
    /// Süper admin rolü taşıyan kullanıcıda daima boş küme.</summary>
    public IReadOnlySet<string> BlockedForUser(string userId)
    {
        using var conn = _factory.Create();
        return BlockedForUser(conn, null, userId);
    }

    /// <summary>Rol anahtarlarına göre KAPALI modüller (herhangi bir rolde kapalıysa kapalı — deny-by-default).</summary>
    public static IReadOnlySet<string> BlockedForRoles(DbConnection conn, DbTransaction? tx, IEnumerable<string> roleKeys)
    {
        var roles = roleKeys.ToList();
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (roles.Contains(RoleKeys.SuperAdmin, StringComparer.Ordinal)) return result; // süper admin muaf
        if (roles.Count == 0) return result;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var names = roles.Select((_, i) => "@r" + i).ToList();
        cmd.CommandText = $"SELECT module_key FROM role_grant_limits WHERE role_key IN ({string.Join(",", names)});";
        for (var i = 0; i < roles.Count; i++) cmd.AddWithValue(names[i], roles[i]);
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    /// <summary>Kullanıcının rollerini okuyup kapalı modülleri döndürür (yapısal kilitler hariç — onlar
    /// AccessControl'de zaten uygulanır).</summary>
    public static IReadOnlySet<string> BlockedForUser(DbConnection conn, DbTransaction? tx, string userId)
    {
        var roles = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id=ur.role_id WHERE ur.user_id=@u AND r.is_deleted=0;";
            cmd.AddWithValue("@u", userId);
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) roles.Add(rd.GetString(0));
        }
        return BlockedForRoles(conn, tx, roles);
    }

    private Dictionary<string, HashSet<string>> LoadAll()
    {
        var d = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT role_key, module_key FROM role_grant_limits;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var role = r.GetString(0);
            if (!d.TryGetValue(role, out var set)) d[role] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(r.GetString(1));
        }
        return d;
    }
}
