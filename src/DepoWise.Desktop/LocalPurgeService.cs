using System;
using System.Collections.Generic;
using System.Data.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop;

/// <summary>
/// ADR-083 — Firma sunucuda KALICI silindiğinde bu makinenin YEREL verisini temizler.
///
/// Akış: giriş → eşitleme adımı sunucuya "firmam silindi mi?" diye sorar → evetse burası çalışır →
/// login ekranına dönülür. Yerel kullanıcılar da silindiği için o firmadan bir daha giriş yapılamaz
/// ("firma silindikten sonra firma kullanıcıları verilere erişememeli" — kullanıcı şartı).
///
/// ⚠️ Sıra kritik: bu temizlik, çevrimdışı kuyruk (sync_outbox) sunucuya İŞLENMEDEN ÖNCE çalışmalıdır.
/// Aksi halde makine, silinmiş firmanın kayıtlarını sunucuya geri push edip veriyi diriltir.
///
/// Yalnız İLGİLİ firmanın satırları silinir — bu makinede başka firma verisi varsa (süper admin) korunur.
/// schema_migrations / sqlite_sequence / sistem rolleri (company_id NULL) korunur.
/// </summary>
public static class LocalPurgeService
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "schema_migrations", "sqlite_sequence",
    };

    /// <summary>Verilen firmanın TÜM yerel kayıtlarını siler. Silinen satır sayısını döndürür.</summary>
    public static int PurgeLocalCompany(string companyId)
    {
        if (string.IsNullOrWhiteSpace(companyId)) return 0;
        using var conn = DesktopServices.Factory.Create();
        var tables = new List<string>();
        using (var q = conn.CreateCommand())
        {
            q.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            using var r = q.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        using (var off = conn.CreateCommand()) { off.CommandText = "PRAGMA foreign_keys=OFF;"; off.ExecuteNonQuery(); }
        int rows = 0;
        try
        {
            using var tx = conn.BeginTransaction();

            // user_roles'ta company_id YOK → firmanın kullanıcıları üzerinden; users'tan ÖNCE.
            rows += Exec(conn, tx, "DELETE FROM user_roles WHERE user_id IN (SELECT id FROM users WHERE company_id=@c);", companyId);

            foreach (var t in tables)
            {
                if (Protected.Contains(t)) continue;
                if (string.Equals(t, "companies", StringComparison.OrdinalIgnoreCase)) continue; // en sonda
                if (!HasColumn(conn, t, "company_id")) continue;
                rows += Exec(conn, tx, $"DELETE FROM \"{t}\" WHERE company_id=@c;", companyId);
            }
            rows += Exec(conn, tx, "DELETE FROM companies WHERE id=@c;", companyId);

            tx.Commit();
        }
        finally
        {
            using var on = conn.CreateCommand(); on.CommandText = "PRAGMA foreign_keys=ON;"; on.ExecuteNonQuery();
        }
        return rows;
    }

    /// <summary>YALNIZ iş verisi tablolarını (BusinessSyncService.Tables — malzeme/araç/stok/bakım/yakıt/…)
    /// bu firma için HARD siler; firma/kullanıcı/şube/tanım-dışı sistem verisi KORUNUR (oturum bozulmaz).
    /// Kullanım (kullanıcı isteği 2026-07-19): "yerelimi temizle, sunucudan tam çek" — sıfır PC simülasyonu.
    /// HARD delete olduğu için silme olarak YAYILMAZ (is_deleted=1 üretmez); sonrasında TAM pull ile geri gelir.</summary>
    public static int PurgeBusinessData(string companyId)
    {
        if (string.IsNullOrWhiteSpace(companyId)) return 0;
        using var conn = DesktopServices.Factory.Create();
        using (var off = conn.CreateCommand()) { off.CommandText = "PRAGMA foreign_keys=OFF;"; off.ExecuteNonQuery(); }
        int rows = 0;
        try
        {
            using var tx = conn.BeginTransaction();
            foreach (var t in DepoWise.Infrastructure.Sync.BusinessSyncService.Tables)
            {
                if (!TableExistsIn(conn, t) || !HasColumn(conn, t, "company_id")) continue;
                rows += Exec(conn, tx, $"DELETE FROM \"{t}\" WHERE company_id=@c;", companyId);
            }
            tx.Commit();
        }
        finally
        {
            using var on = conn.CreateCommand(); on.CommandText = "PRAGMA foreign_keys=ON;"; on.ExecuteNonQuery();
        }
        return rows;
    }

    private static bool TableExistsIn(DbConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1;";
        cmd.AddWithValue("@n", table);
        return cmd.ExecuteScalar() is not null;
    }

    private static bool HasColumn(DbConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static int Exec(DbConnection conn, DbTransaction tx, string sql, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", companyId);
        return cmd.ExecuteNonQuery();
    }
}
