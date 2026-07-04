using DepoWise.Application.Security;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// 2-rol modeli (Batch C / #9,#10): sistem rolleri Personel + Admin + Süper Admin.
/// - "Personel" (role-staff) sistem rolü eklenir (yoksa).
/// - Eski roller (Yönetici/Depo/Operasyon/Salt Okunur) → kullanıcıları Personel'e taşınır.
///   İzinler user_permissions'ta durduğundan davranış AYNI kalır (rol yalnız etiket).
/// - Bir kullanıcının hem legacy hem Personel/başka rol kaydı oluşmasın diye: Admin'i olan Admin kalır,
///   olmayan tek Personel olur (tek-rol modeli). Eski roller soft-delete edilir (FK kırılmaz).
/// - Firma Admini rolünün görünen adı "Admin" olarak güncellenir.
/// </summary>
public sealed class Migration029_TwoRoleModel : IMigration
{
    public int Version => 29;
    public string Name => "two_role_model";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1) Personel (role-staff) sistem rolü — yoksa ekle.
        var staffId = ScalarOrNull(conn, tx, "SELECT id FROM roles WHERE role_key=$k AND is_deleted=0;", ("$k", RoleKeys.Staff));
        if (staffId is null)
        {
            staffId = Guid.NewGuid().ToString("N");
            Exec(conn, tx, @"INSERT INTO roles(id, company_id, role_key, name, is_system, created_at, updated_at, version, is_deleted)
VALUES($id, NULL, $k, 'Personel', 1, $now, $now, 1, 0);",
                ("$id", staffId), ("$k", RoleKeys.Staff), ("$now", now));
        }

        // 2) Firma Admini görünen adını "Admin" yap.
        Exec(conn, tx, "UPDATE roles SET name='Admin', updated_at=$now WHERE role_key=$k;",
            ("$k", RoleKeys.CompanyAdmin), ("$now", now));

        // 3) Legacy rol id'leri.
        var legacyIds = new List<string>();
        foreach (var key in RoleKeys.Legacy)
        {
            var id = ScalarOrNull(conn, tx, "SELECT id FROM roles WHERE role_key=$k;", ("$k", key));
            if (id is not null) legacyIds.Add(id);
        }

        if (legacyIds.Count > 0)
        {
            var inClause = string.Join(",", legacyIds.Select((_, i) => "$l" + i));

            // 3a) Legacy rolü olan kullanıcılar: Admin değilse Personel rolü ver (yoksa).
            //     Admin'i olanlar Admin kalır (Personel eklenmez).
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $@"
INSERT INTO user_roles(user_id, role_id)
SELECT DISTINCT ur.user_id, $staff
FROM user_roles ur
WHERE ur.role_id IN ({inClause})
  AND NOT EXISTS (SELECT 1 FROM user_roles a JOIN roles r ON r.id=a.role_id
                  WHERE a.user_id=ur.user_id AND r.role_key IN ($adm,$sa,$stf));";
                cmd.Parameters.AddWithValue("$staff", staffId);
                cmd.Parameters.AddWithValue("$adm", RoleKeys.CompanyAdmin);
                cmd.Parameters.AddWithValue("$sa", RoleKeys.SuperAdmin);
                cmd.Parameters.AddWithValue("$stf", RoleKeys.Staff);
                for (int i = 0; i < legacyIds.Count; i++) cmd.Parameters.AddWithValue("$l" + i, legacyIds[i]);
                cmd.ExecuteNonQuery();
            }

            // 3b) Legacy user_roles kayıtlarını sil (kullanıcılar artık Personel/Admin).
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM user_roles WHERE role_id IN ({inClause});";
                for (int i = 0; i < legacyIds.Count; i++) cmd.Parameters.AddWithValue("$l" + i, legacyIds[i]);
                cmd.ExecuteNonQuery();
            }

            // 3c) Legacy rolleri soft-delete (görünmez ama FK/geçmiş kırılmaz).
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"UPDATE roles SET is_deleted=1, updated_at=$now WHERE id IN ({inClause});";
                cmd.Parameters.AddWithValue("$now", now);
                for (int i = 0; i < legacyIds.Count; i++) cmd.Parameters.AddWithValue("$l" + i, legacyIds[i]);
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static string? ScalarOrNull(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar() as string;
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, params (string, object)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }
}
