using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ FIN-B1 (ADR-179 tasarımı · ADR-185 kararları) — operation_id BENZERSİZLİĞİ FİRMA KAPSAMINA ALINIR ═══
///
/// FINAL simülasyonunun bulgusu: 6 eski tabloda <c>operation_id</c> TÜM FİRMALAR genelinde benzersizdi
/// (Migration005/008/009/076) ve idempotency kontrolleri de buna uygun firma süzgeçsizdi → BAŞKA firmada
/// kullanılmış bir operation_id ile gelen işlem SESSİZCE atlanabiliyordu. Yeni muhasebe tabloları
/// (Migration066-068) zaten doğru desendeydi: <c>(company_id, operation_id)</c>.
///
/// <b>⭐ ADR-185 / PK-FIN-02=B EKİ — <c>sync_inbox</c> DE KAPSAMDADIR (7. hedef):</b> FAZ 1 analizi,
/// <c>SyncServer.InboxHas</c>'in Push akışında servis katmanından ÖNCE çalıştığını ve firma-kör olduğunu
/// saptadı; senkronun kritik tipleri (<c>stock_movement</c>, <c>vehicle_maintenance</c>,
/// <c>fuel_distribution</c>) tam da bu tablolar olduğu için YALNIZ 6 tablo düzeltilseydi hata çevrimdışı
/// masaüstü senkron yolunda KAPANMAZDI. <c>sync_inbox.company_id</c> zaten <c>NOT NULL</c> (Migration001)
/// ve her kayıtta doldurulur → YENİ SÜTUN/BACKFILL GEREKMEZ, yalnız indeks kapsamı değişir.
///
/// <b>YAPILAN:</b> yalnız 7 indeks, AYNI ADLARLA, küresel tek-kolondan firma-kapsamlı iki-kolona
/// taşınır (DROP INDEX + CREATE UNIQUE INDEX). Kolon eklenmez/değiştirilmez, hiçbir satıra dokunulmaz.
/// <c>sync_outbox</c> KAPSAM DIŞI (istemci tarafı kuyruk; senkron sözleşmesi değişmez).
///
/// <b>VERİ GÜVENLİĞİ:</b> küreselden firma-kapsamlıya geçiş benzersizliği GEVŞETİR — bugünkü küresel
/// benzersizliği sağlayan her veri yeni indeksi otomatik sağlar → duplicate/backfill YAPISAL OLARAK
/// imkânsız; yine de CREATE UNIQUE başarısız olursa transaction tümüyle geri alınır (runner migration
/// başına tek transaction → şema 81'de kalır). Idempotent: schema_migrations + IF EXISTS.
///
/// <b>PK-FIN-03=C:</b> normal <c>CREATE UNIQUE INDEX</c> kullanılır; <c>CONCURRENTLY</c> KULLANILMAZ —
/// runner her migration'ı tek transaction'da çalıştırır ve <c>CONCURRENTLY</c> transaction içinde
/// çalışamaz (runner mimarisi bilinçli olarak DEĞİŞTİRİLMEDİ). Canlıya uygulama önkoşulları: pg_dump
/// yedeği + kısa ACCESS EXCLUSIVE kilidi. ⚠️ <c>sync_inbox</c> en büyük tablo olabilir → yayın öncesi
/// boyut ölçümü ayrı bir adımdır. Rollback: aynı adlarla tek-kolon indeksleri geri kur.
///
/// Not: <see cref="Materials.StockBalanceWriter.IsDocumentNumberRace"/> yalnız <c>ux_stock_documents_no</c>
/// metnine bakar — bu indekslerin adları korunduğundan yarış sınıflandırması etkilenmez. Senkron push/pull
/// satırları <c>id</c> üzerinden upsert eder; bu indeksler senkron tekilleştirmesinde KULLANILMAZ.
/// </summary>
public sealed class Migration082_OperationIdCompanyScope : IMigration
{
    public int Version => 82;
    public string Name => "operation_id_company_scope";

    /// <summary>(indeks adı, tablo) — 7 hedef; TEK doğru kaynak (test de bu listeden doğrular).</summary>
    public static readonly IReadOnlyList<(string Index, string Table)> Targets = new[]
    {
        ("ux_stock_movements_operation", "stock_movements"),
        ("ux_vehicle_maintenances_op", "vehicle_maintenances"),
        ("ux_fuel_depot_op", "fuel_depot_entries"),
        ("ux_fuel_dist_op", "fuel_distributions"),
        ("ux_daily_activities_op", "daily_activities"),
        ("ux_assign_operation", "assignment_movements"),
        ("ux_inbox_operation", "sync_inbox"),   // ⭐ ADR-185 / PK-FIN-02=B
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var (index, table) in Targets)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $@"
DROP INDEX IF EXISTS {index};
CREATE UNIQUE INDEX {index} ON {table}(company_id, operation_id);";
            cmd.ExecuteNonQuery();
        }
    }
}
