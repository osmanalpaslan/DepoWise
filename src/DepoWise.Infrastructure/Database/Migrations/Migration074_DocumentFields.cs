using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ EVR-01 (ADR-165, 2026-08-27) — EVRAK/BELGE YÖNETİMİ: file_records META ALANLARI ═══
///
/// Yol haritası FAZ 1 / SIRA 2 (MASTER_ROADMAP §1). Mevcut <c>file_records</c> altyapısı YENİDEN
/// KULLANILIR (ikinci belge tablosu AÇILMADI): belgeler aynı tabloda <c>kind='document'</c> ile durur,
/// fotoğraflar (<c>kind='photo'</c>) olduğu gibi kalır.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b>
/// <list type="bullet">
///   <item>Yalnız <c>ADD COLUMN</c> (hepsi NULL, varsayılansız) + bir indeks — mevcut satırlara
///     UPDATE/DELETE/dönüşüm YOK; mevcut fotoğraf kayıtlarının anlamı DEĞİŞMEZ (yeni kolonlar boş kalır).</item>
///   <item>ADD COLUMN sözdizimi iki lehçede ortak (Migration060 emsali).</item>
///   <item>Rollback: kolonlar boşsa hiçbir kod yolu onlara bağlı değildir; gerekirse
///     <c>DELETE FROM schema_migrations WHERE version=74;</c> + kolonlar yerinde bırakılır
///     (SQLite'ta DROP COLUMN riskli olduğundan geri alma "yok say" biçimindedir).</item>
/// </list>
/// </summary>
public sealed class Migration074_DocumentFields : IMigration
{
    public int Version => 74;
    public string Name => "document_fields";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE file_records ADD COLUMN title TEXT NULL;
ALTER TABLE file_records ADD COLUMN doc_type TEXT NULL;
ALTER TABLE file_records ADD COLUMN valid_from BIGINT NULL;
ALTER TABLE file_records ADD COLUMN valid_until BIGINT NULL;
ALTER TABLE file_records ADD COLUMN description TEXT NULL;
ALTER TABLE file_records ADD COLUMN uploaded_by TEXT NULL;
CREATE INDEX ix_file_company_kind ON file_records(company_id, kind, is_deleted);";
        cmd.ExecuteNonQuery();
    }
}
