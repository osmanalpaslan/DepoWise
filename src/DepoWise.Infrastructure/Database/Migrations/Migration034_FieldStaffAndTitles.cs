using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Fikir B (#6 revizyon):
/// 1) personnel.is_field_staff — "Saha personeli" kutucuğu. İşaretliyse kayıtta "kullanıcı bağlanmadı"
///    uyarısı çıkmaz (kişi bilinçli olarak yalnız saha personelidir). Varsayılan 0.
/// 2) personnel_titles — Unvan SABİT TANIM listesi (firma bazlı). Personel formunda seçilir, "+" ile
///    yeni unvan eklenir. Aynı firmada aynı unvan iki kez tanımlanamaz (silinmemişler arasında).
/// personnel.title serbest metin olarak KALIR (geçmiş kayıtlar bozulmasın); yeni kayıtlarda listeden gelir.
/// </summary>
public sealed class Migration034_FieldStaffAndTitles : IMigration
{
    public int Version => 34;
    public string Name => "field_staff_and_titles";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE personnel ADD COLUMN is_field_staff INTEGER NOT NULL DEFAULT 0;

CREATE TABLE personnel_titles (
    id          TEXT    NOT NULL PRIMARY KEY,
    company_id  TEXT    NOT NULL,
    name        TEXT    NOT NULL,
    created_at  INTEGER NOT NULL,
    updated_at  INTEGER NOT NULL,
    version     INTEGER NOT NULL DEFAULT 1,
    is_deleted  INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_personnel_titles_name
    ON personnel_titles(company_id, name) WHERE is_deleted=0;
CREATE INDEX ix_personnel_titles_company ON personnel_titles(company_id);";
        cmd.ExecuteNonQuery();

        // Mevcut serbest-metin unvanları tanım listesine taşı (veri kaybı olmasın, liste dolu başlasın).
        using var seed = conn.CreateCommand();
        seed.Transaction = tx;
        seed.CommandText = @"
INSERT OR IGNORE INTO personnel_titles(id, company_id, name, created_at, updated_at, version, is_deleted)
SELECT lower(hex(randomblob(16))), company_id, TRIM(title),
       strftime('%s','now')*1000, strftime('%s','now')*1000, 1, 0
FROM personnel
WHERE is_deleted=0 AND title IS NOT NULL AND TRIM(title) <> ''
GROUP BY company_id, TRIM(title);";
        seed.ExecuteNonQuery();
    }
}
