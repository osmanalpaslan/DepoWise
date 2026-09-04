using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ MUH-01b (FAZ D, 2026-09-04) — PARA DOĞURAN KAYITLARDA BELGE NUMARASI ═══
///
/// <b>Neden:</b> ön muhasebe (FAZ H) bir gideri kaynak belgesine bağlayamazsa, kullanıcı faturayı
/// elinde tutup sistemde karşılığını bulamaz. Belge alanı stok belgesinde ZATEN vardı
/// (<c>invoice_no</c> · <c>order_slip_no</c> · <c>credit_slip_no</c>, Migration017) ve yakıt depo
/// girişinde de vardı (<c>invoice_no</c>, Migration009) — ama şu üç kayıt türünde YOKTU:
/// <list type="bullet">
///   <item><c>fuel_distributions</c> — yakıt dağıtımı (irsaliye/fiş no)</item>
///   <item><c>vehicle_maintenances</c> — dış servis bakımı (fatura/servis fişi no)</item>
///   <item><c>equipment_maintenances</c> — aynısının ekipman karşılığı (7b/ADR-191)</item>
/// </list>
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız <c>ADD COLUMN</c>; hiç <c>UPDATE</c>/<c>DELETE</c>/backfill
/// yok, hiçbir <c>NOT NULL</c> kısıtı yok → mevcut kayıtların tamamı olduğu gibi kalır ve
/// <c>NULL</c> belge no ile geçerli olmayı sürdürür. Alan opsiyoneldir; hiçbir mevcut akış
/// zorunlu hâle gelmez. Geri alma: üç <c>DROP COLUMN</c> + <c>schema_migrations</c> satırı.
///
/// <b>SENKRON:</b> ek iş GEREKMEZ. <c>BusinessSyncService</c> tabloları <c>SELECT *</c> ile taşır
/// (BusinessSyncService.cs:608) → yeni sütun kendiliğinden pakete girer. Üç tablo da zaten senkron
/// listesindedir.
///
/// <b>Neden ayrı bir "belge" tablosu değil:</b> mevcut desen bu — stok belgesi ve yakıt depo girişi
/// belge numarasını KENDİ satırında tutuyor. Yeni bir belge tablosu açmak aynı bilgi için ikinci bir
/// gerçeklik üretir ve mevcut ekranların hiçbiriyle uyuşmazdı. (Maliyet merkezinde tersi doğruydu ve
/// orada bilinçli olarak dış bağ tablosu seçilmişti — ADR-168; iki karar çelişmez, çünkü maliyet
/// merkezi ÇOK tabloya bağlanan ortak bir boyut, belge no ise kaydın kendi alanı.)
/// </summary>
public sealed class Migration089_DocumentFields : IMigration
{
    public int Version => 89;
    public string Name => "document_fields";

    /// <summary>(tablo, sütun) — hepsi opsiyonel metin; belge/fatura/fiş numarası serbest formattır
    /// (Türkiye'de seri-sıra biçimi satıcıya göre değişir, sabit format DAYATILMAZ).</summary>
    private static readonly (string Table, string Column)[] Targets =
    {
        ("fuel_distributions", "invoice_no"),
        ("vehicle_maintenances", "invoice_no"),
        ("equipment_maintenances", "invoice_no"),
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var (table, column) in Targets)
        {
            // Idempotent: sütun zaten varsa sessizce geç (mevcut DbIntrospect iki lehçeyi de bilir).
            if (DbIntrospect.ColumnExists(conn, tx, table, column)) continue;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL;";
            cmd.ExecuteNonQuery();
        }
    }
}
