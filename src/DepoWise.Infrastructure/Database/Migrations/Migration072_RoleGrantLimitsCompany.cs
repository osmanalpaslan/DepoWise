using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ A1 (ADR-116, 2026-08-19) — ROL YETKİ TAVANI ARTIK FİRMA BAZLI ═══
///
/// <b>SORUN:</b> <c>role_grant_limits</c> tablosunda firma kolonu YOKTU; tablo <b>platform geneliydi</b>.
/// Süper adminin "Rol Yetki Kontrol" ekranında yaptığı tek bir değişiklik <b>bütün firmaları aynı anda</b>
/// etkiliyordu (kaydetme <c>DELETE FROM role_grant_limits;</c> ile tabloyu komple siliyordu). Firma sayısı
/// arttıkça bu, bir firmanın ayarının diğerini bozması demektir — çok firmalı bir üründe kabul edilemez.
///
/// <b>ÇÖZÜM:</b> tabloya <c>company_id</c> eklenir ve benzersizlik <c>(company_id, role_key, module_key)</c>
/// olur. Böylece rol tavanı, kardeşi <c>company_grant_limits</c> ile aynı eksende yönetilir ve iki ayrı
/// ekran tek ekranda birleşebilir.
///
/// <b>VERİ KAYBI YOK — KOPYALANARAK TAŞINIR (kullanıcı kararı):</b> mevcut ortak satırların HER BİRİ
/// <b>her firmaya</b> yazılır. Yani migration öncesi ve sonrası her firmanın gördüğü kısıt AYNIDIR;
/// yalnız bundan sonra firmalar birbirinden bağımsız yönetilir.
///
/// <b>DOĞRULAMA + ROLLBACK:</b> yazmadan önce <c>yeniSatır == eskiSatır × firmaSayısı</c> karşılaştırılır.
/// Tutmazsa migration <b>istisna fırlatır</b>, <see cref="MigrationRunner"/> transaction'ı geri alır ve
/// hiçbir şey yazılmaz (şema sürümü 71'de kalır).
///
/// <b>LEHÇE:</b> eski tablodaki benzersizlik bir TABLO KISITIDIR (<c>UNIQUE(role_key, module_key)</c>);
/// SQLite bunu <c>ALTER</c> ile kaldıramaz. Bu yüzden <see cref="Migration064_StockBalanceLocation"/>
/// ile aynı kanıtlanmış yol izlenir: <b>yeni tablo → doldur → doğrula → eskiyi bırak → adlandır</b>.
/// Tek kod yolu iki lehçede de çalışır.
///
/// <b>GERİ ALMA (gerekirse):</b>
/// <code>
///     CREATE TABLE role_grant_limits_old AS
///         SELECT MIN(id) AS id, role_key, module_key, MIN(created_at) AS created_at
///         FROM role_grant_limits GROUP BY role_key, module_key;   -- firmalar birleştirilir
///     DROP TABLE role_grant_limits;
///     ALTER TABLE role_grant_limits_old RENAME TO role_grant_limits;
///     -- ⚠️ ADIM 4 ZORUNLU: CREATE TABLE ... AS SELECT kısıtları TAŞIMAZ. v71 sözleşmesi
///     --    (PRIMARY KEY + UNIQUE(role_key, module_key)) yeniden kurulmalıdır, aksi hâlde
///     --    v71 kodundaki ON CONFLICT eşleşecek kısıt bulamaz.
///     DELETE FROM schema_migrations WHERE version = 72;
/// </code>
/// </summary>
public sealed class Migration072_RoleGrantLimitsCompany : IMigration
{
    public int Version => 72;
    public string Name => "role_grant_limits_company";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // Tablo hiç kurulmamışsa (çok eski/kısmi şema) yapacak iş yok — Migration041 zaten kuracak.
        if (!DbIntrospect.TableExists(conn, tx, "role_grant_limits")) return;
        // Idempotent: kolon zaten varsa migration daha önce uygulanmış.
        if (DbIntrospect.ColumnExists(conn, tx, "role_grant_limits", "company_id")) return;

        // ── 1) Eski (firma-üstü) satırlar ──────────────────────────────────────────────────────
        var eski = new List<(string Role, string Module, long CreatedAt)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT role_key, module_key, created_at FROM role_grant_limits;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) eski.Add((r.GetString(0), r.GetString(1), r.GetInt64(2)));
        }

        // ── 2) Firmalar — SİLİNMİŞ OLANLAR DA DAHİL ───────────────────────────────────────────
        // Pasife alınmış bir firma sonradan geri açılabilir; onun kısıtını burada düşürmek sessiz
        // bir yetki genişlemesi olurdu (deny-by-default'a aykırı).
        var firmalar = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM companies;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) firmalar.Add(r.GetString(0));
        }

        // ── 3) Yeni tablo ─────────────────────────────────────────────────────────────────────
        Exec(conn, tx, @"
CREATE TABLE role_grant_limits_new (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    role_key    TEXT NOT NULL,
    module_key  TEXT NOT NULL,
    created_at  BIGINT NOT NULL
);");
        Exec(conn, tx, "CREATE UNIQUE INDEX ux_role_grant_limits ON role_grant_limits_new(company_id, role_key, module_key);");

        // ── 4) Kopyala: her firma × her eski satır ────────────────────────────────────────────
        foreach (var firma in firmalar)
            foreach (var (role, module, createdAt) in eski)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO role_grant_limits_new(id, company_id, role_key, module_key, created_at) " +
                                  "VALUES(@id,@c,@r,@m,@t);";
                ins.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                ins.AddWithValue("@c", firma);
                ins.AddWithValue("@r", role);
                ins.AddWithValue("@m", module);
                ins.AddWithValue("@t", createdAt);
                ins.ExecuteNonQuery();
            }

        // ── 5) DOĞRULAMA KAPISI — tutmazsa hiçbir şey yazılmaz ────────────────────────────────
        long yeni;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM role_grant_limits_new;";
            yeni = Convert.ToInt64(cmd.ExecuteScalar());
        }
        var beklenen = (long)eski.Count * firmalar.Count;
        if (yeni != beklenen)
            throw new InvalidOperationException(
                $"Migration 072 doğrulaması başarısız: beklenen {beklenen} satır ({eski.Count} kısıt × " +
                $"{firmalar.Count} firma), oluşan {yeni}. Hiçbir şey yazılmadı.");

        // ── 6) Takas ──────────────────────────────────────────────────────────────────────────
        Exec(conn, tx, "DROP TABLE role_grant_limits;");
        Exec(conn, tx, "ALTER TABLE role_grant_limits_new RENAME TO role_grant_limits;");
        // Okuma yolu daima (company_id, role_key) ile süzer.
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_role_grant_limits_company_role ON role_grant_limits(company_id, role_key);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
