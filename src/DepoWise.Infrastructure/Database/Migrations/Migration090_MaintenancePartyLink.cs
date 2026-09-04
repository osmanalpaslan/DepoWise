using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ MUH-01c (FAZ D, 2026-09-04) — BAKIMDA CARİ (DIŞ SERVİS SAĞLAYICISI) ═══
///
/// <b>Neden yalnız iki tablo:</b> "para doğuran her kayda cari alanı" isteği ölçüldüğünde, kayıt
/// türlerinin çoğunda karşı tarafın <b>zaten ulaşılabilir</b> olduğu görüldü. Yeni kolon yalnız
/// gerçekten karşı tarafı olmayan yerlere eklendi:
///
/// <list type="bullet">
///   <item><b>Bakımlar (bu migration):</b> dış serviste yapılan bakımın sağlayıcısı hiçbir yerde
///   tutulmuyordu. Servis noktası malzeme "tedarikçisi" de değildir (oto servis, lastikçi, kaynakçı…)
///   → gerçek boşluk buradaydı.</item>
///   <item><b>Yakıt depo girişi ve satın alma:</b> <c>supplier_id</c> ZATEN var. Yanına ikinci bir
///   <c>party_id</c> koymak, aynı satırda <b>iki ayrı "karşı taraf" gerçekliği</b> üretirdi. Doğru yol
///   Migration066'nın bu iş için bıraktığı köprüdür: <c>parties.supplier_id</c> ("bu tedarikçi = bu
///   cari"). Köprü şemada vardı ama arayüzden kurulamıyordu; MUH-01c onu kullanılabilir yapar.</item>
///   <item><b>Stok belgesi (malzeme alışı):</b> karşı taraf <c>invoices.stock_document_id</c> +
///   <c>invoices.party_id</c> üzerinden zaten bağlı. Belgeye ayrıca kolon eklemek, faturanın
///   söylediğiyle çelişebilecek ikinci bir gerçeklik olurdu. Ayrıca stok belge zinciri 5 katmanlıdır
///   ve ADR-168 tam bu nedenle oraya kolon eklemeyi reddetmişti — o karar burada da geçerli.</item>
/// </list>
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız <c>ADD COLUMN</c>; <c>NOT NULL</c> yok, varsayılan yok,
/// backfill yok → mevcut bakım kayıtlarının tamamı <c>NULL</c> cari ile geçerli olmayı sürdürür.
/// Alan opsiyoneldir; hiçbir mevcut akış zorunlu hâle gelmez.
/// Geri alma: iki <c>DROP COLUMN</c> + <c>schema_migrations</c> satırı.
///
/// <b>FK KURULMADI (bilinçli):</b> <c>vehicle_maintenances</c> canlı ve büyük bir tablodur; SQLite'ta
/// var olan tabloya FK eklemek tablo yeniden inşası (rebuild) ister ve transaction içinde FK
/// kapatılamaz — ADR-191'de aynı gerekçeyle SEÇENEK B tercih edilmişti. Sahiplik doğrulaması
/// servis katmanında yapılır (mevcut <c>cost_center_links</c> deseniyle aynı yaklaşım).
///
/// <b>SENKRON:</b> ek iş gerekmez — <c>BusinessSyncService</c> <c>SELECT *</c> ile taşır ve
/// uygularken kolon kesişimi alır; yeni sütun kendiliğinden akar, eski istemci yok sayar.
/// </summary>
public sealed class Migration090_MaintenancePartyLink : IMigration
{
    public int Version => 90;
    public string Name => "maintenance_party_link";

    private static readonly string[] Tables = { "vehicle_maintenances", "equipment_maintenances" };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var table in Tables)
        {
            // Idempotent: sütun zaten varsa sessizce geç.
            if (DbIntrospect.ColumnExists(conn, tx, table, "party_id")) continue;

            using (var add = conn.CreateCommand())
            {
                add.Transaction = tx;
                add.CommandText = $"ALTER TABLE {table} ADD COLUMN party_id TEXT NULL;";
                add.ExecuteNonQuery();
            }

            // Cari bazlı okuma (FAZ H "cari hesap ekstresi") tarama yapmasın.
            using var ix = conn.CreateCommand();
            ix.Transaction = tx;
            ix.CommandText = $"CREATE INDEX IF NOT EXISTS ix_{table}_party ON {table}(company_id, party_id);";
            ix.ExecuteNonQuery();
        }
    }
}
