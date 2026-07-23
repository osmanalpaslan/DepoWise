using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Kalıcı Silme (Firma Verisi Silme) altyapısı — ADR-083.
///
/// 1) users.special_code_hash — "özel kod". YALNIZ süper adminden istenir; Kalıcı Silme ekranını açar.
///    Şifre gibi hash'lenir (asla düz metin). NULL = henüz oluşturulmamış → ilk web girişinde sorulur.
///    Unutulursa süper admin ŞİFRESİYLE yeniden belirlenebilir (kullanıcı kararı 2026-07-16).
///
/// 2) company_purges — silme KÜNYESİ (tombstone). Firma kalıcı silinince buraya bir satır kalır.
///    Masaüstü eşitleme adımı bunu görüp YEREL veriyi siler ve login'e döner. Bu tablo purge sırasında
///    ASLA temizlenmez; aksi halde çevrimdışı makineler silmeyi hiç öğrenemez ve veri geri dirilir.
///    company_id burada FK DEĞİLDİR (firma satırı artık yok).
///
/// Idempotent.
/// </summary>
public sealed class Migration044_SpecialCodeAndPurge : IMigration
{
    public int Version => 44;
    public string Name => "special_code_and_company_purge";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!ColumnExists(conn, tx, "users", "special_code_hash"))
            Exec(conn, tx, "ALTER TABLE users ADD COLUMN special_code_hash TEXT;");

        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS company_purges(
    company_id   TEXT PRIMARY KEY,
    company_name TEXT NOT NULL,
    purged_at    INTEGER NOT NULL,
    purged_by    TEXT NOT NULL
);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_company_purges_at ON company_purges(purged_at);");
    }

    private static bool ColumnExists(DbConnection conn, DbTransaction tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
