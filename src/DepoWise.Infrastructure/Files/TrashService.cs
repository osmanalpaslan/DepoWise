using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Files;

public sealed record TrashItem(string Table, string Id, string Label, long UpdatedAt);

/// <summary>
/// Çöp Kutusu — master-data soft-delete kayıtlarını listeler/geri yükler. Erişim admin/özel buton +
/// YENİDEN DOĞRULAMA (reauthenticated) ister (analiz §6.17/§9). Operasyonel kayıtlar burada DEĞİL
/// (onlar iptal/ters kayıt ile düzeltilir).
/// </summary>
public sealed class TrashService
{
    // Geri yüklenebilir master-data tabloları (is_deleted + label kolonu ile).
    private static readonly Dictionary<string, string> Tables = new(StringComparer.Ordinal)
    {
        ["materials"] = "name", ["vehicles"] = "internal_code", ["personnel"] = "full_name",
        ["branches"] = "name", ["projects"] = "name", ["equipment"] = "name", ["cost_centers"] = "name", ["suppliers"] = "name", ["brands"] = "name", ["units"] = "name",
        ["material_categories"] = "name", ["vehicle_templates"] = "name",
        ["vehicle_types"] = "name", ["vehicle_categories"] = "name", ["vehicle_models"] = "name",
        ["maintenance_definitions"] = "name",
        // G6-03 (2026-08-11): kullanıcı silme SOFT'tur ama geri getirmenin YOLU YOKTU. Artık Çöp Kutusu'nda.
        // Kullanıcı, diğer kalemlerden daha hassastır → aşağıda EK kapılar var (admin zorunlu, süper admin
        // koruması, aktif ad çakışması kontrolü).
        ["users"] = "username",
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public TrashService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public IReadOnlyList<TrashItem> List(SessionContext s, bool reauthenticated)
    {
        RequireAccess(s, reauthenticated);
        var items = new List<TrashItem>();
        using var conn = _factory.Create();
        foreach (var (table, label) in Tables)
        {
            // G6-03: silinmiş KULLANICILAR yalnız Admin / Süper Admin'e listelenir (silme de admin işidir);
            // süper admin kullanıcı kayıtları ise yalnız süper admine görünür — UserService.ListUsers ile aynı kural.
            if (table == "users" && !AccessControl.IsAdmin(s)) continue;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = table == "users"
                ? "SELECT u.id, u.username, u.updated_at FROM users u WHERE u.company_id=@c AND u.is_deleted=1 " +
                  "AND (@all=1 OR NOT EXISTS (SELECT 1 FROM user_roles ur JOIN roles r ON r.id=ur.role_id " +
                  "                           WHERE ur.user_id=u.id AND r.role_key=@sa));"
                : $"SELECT id, {label}, updated_at FROM {table} WHERE company_id=@c AND is_deleted=1;";
            cmd.AddWithValue("@c", s.CompanyId);
            if (table == "users")
            {
                cmd.AddWithValue("@all", s.IsSuperAdmin ? 1 : 0);
                cmd.AddWithValue("@sa", RoleKeys.SuperAdmin);
            }
            using var r = cmd.ExecuteReader();
            while (r.Read())
                items.Add(new TrashItem(table, r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1), r.GetInt64(2)));
        }
        return items;
    }

    public void Restore(SessionContext s, string table, string id, bool reauthenticated)
    {
        RequireAccess(s, reauthenticated);
        if (!Tables.ContainsKey(table)) throw new ArgumentException($"Geri yüklenemez tablo: {table}");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        // G6-03: kullanıcı geri yükleme, kullanıcı SİLMEden daha zayıf olamaz → aynı kapılar (aynı transaction
        // içinde, geri yüklemeden ÖNCE): admin zorunlu, süper admin kaydını yalnız süper admin geri getirir,
        // kullanıcı adı bu arada başka AKTİF kullanıcıya verilmişse anlaşılır hata (koşullu UNIQUE indeksin
        // ham veritabanı hatasına düşmesi engellenir — bkz. Migration063).
        if (table == "users") EnsureUserRestorable(conn, tx, s, id);
        int affected;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"UPDATE {table} SET is_deleted=0, version=version+1, updated_at=@now WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            affected = cmd.ExecuteNonQuery();
        }
        if (affected == 0) throw new ForbiddenException("Kayıt bulunamadı veya başka firmaya ait.");
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, table, id, AuditActions.Restore, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>G6-03 — kullanıcı geri yüklemenin ön koşulları (hepsi fail-closed).</summary>
    private static void EnsureUserRestorable(DbConnection conn, DbTransaction tx, SessionContext s, string userId)
    {
        if (!AccessControl.IsAdmin(s))
            throw new ForbiddenException("Kullanıcı geri yükleme yalnız Admin / Süper Admin yetkisindedir.");

        string username;
        using (var q = conn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT username FROM users WHERE id=@u AND company_id=@c AND is_deleted=1;";
            q.AddWithValue("@u", userId);
            q.AddWithValue("@c", s.CompanyId);
            username = q.ExecuteScalar() as string
                ?? throw new ForbiddenException("Silinmiş kullanıcı bulunamadı veya başka firmaya ait.");
        }

        if (!s.IsSuperAdmin)
        {
            using var chk = conn.CreateCommand();
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(*) FROM user_roles ur JOIN roles r ON r.id=ur.role_id " +
                              "WHERE ur.user_id=@u AND r.role_key=@sa;";
            chk.AddWithValue("@u", userId);
            chk.AddWithValue("@sa", RoleKeys.SuperAdmin);
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0)
                throw new ForbiddenException("Süper admin kullanıcıyı yalnız süper admin geri yükleyebilir.");
        }

        using (var dup = conn.CreateCommand())
        {
            dup.Transaction = tx;
            dup.CommandText = "SELECT COUNT(*) FROM users WHERE company_id=@c AND username=@n AND is_deleted=0;";
            dup.AddWithValue("@c", s.CompanyId);
            dup.AddWithValue("@n", username);
            if (Convert.ToInt64(dup.ExecuteScalar()) > 0)
                throw new InvalidOperationException(
                    $"'{username}' kullanıcı adı şu anda AKTİF bir kullanıcıya ait. Geri yüklemek için önce o " +
                    "kullanıcının adını değiştirin veya bu kaydı geri yüklemek yerine yeni kullanıcı oluşturun.");
        }
    }

    private static void RequireAccess(SessionContext s, bool reauthenticated)
    {
        AccessControl.RequireButton(s, SpecialButtons.RestoreTrash); // admin bypass veya açık yetki
        if (!reauthenticated)
            throw new ForbiddenException("Çöp Kutusu için yeniden kimlik doğrulama gerekli.");
    }
}
