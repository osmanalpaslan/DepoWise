using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ FIN-B1 (ADR-179, 2026-08-29) — operation_id BENZERSİZLİĞİ FİRMA KAPSAMINA ALINIR ═══
///
/// FINAL simülasyonunun bulgusu: 6 eski tabloda <c>operation_id</c> TÜM FİRMALAR genelinde benzersizdi
/// (Migration005/008/009/076) ve idempotency kontrolleri de buna uygun firma süzgeçsizdi → BAŞKA firmada
/// kullanılmış bir operation_id ile gelen işlem SESSİZCE atlanabiliyordu. Yeni muhasebe tabloları
/// (Migration066-068) zaten doğru desendeydi: <c>(company_id, operation_id)</c>.
///
/// <b>YAPILAN:</b> yalnız 6 indeks, AYNI ADLARLA, küresel tek-kolondan firma-kapsamlı iki-kolona
/// taşınır (DROP INDEX + CREATE UNIQUE INDEX). Kolon eklenmez/değiştirilmez, hiçbir satıra dokunulmaz.
/// <c>sync_inbox</c>/<c>sync_outbox</c> BİLİNÇLİ KAPSAM DIŞI (senkron sözleşmesi değişmez).
///
/// <b>VERİ GÜVENLİĞİ:</b> küreselden firma-kapsamlıya geçiş benzersizliği GEVŞETİR — bugünkü küresel
/// benzersizliği sağlayan her veri yeni indeksi otomatik sağlar → duplicate/backfill YAPISAL OLARAK
/// imkânsız; yine de CREATE UNIQUE başarısız olursa transaction tümüyle geri alınır (runner migration
/// başına tek transaction). Idempotent: schema_migrations + IF EXISTS.
///
/// ⚠️ <b>PRODUCTION'DA ÇALIŞTIRILMADI</b> (2026-08-29 itibarıyla canlı şema 81; yeni strateji gereği
/// yayın yok). Canlıya uygulama önkoşulları: pg_dump yedeği + kısa ACCESS EXCLUSIVE kilidi (tablolar
/// küçük). Rollback: aynı adlarla tek-kolon indeksleri geri kur.
///
/// Not: <see cref="Materials.StockBalanceWriter.IsDocumentNumberRace"/> yalnız <c>ux_stock_documents_no</c>
/// metnine bakar — bu indekslerin adları korunduğundan yarış sınıflandırması etkilenmez. Senkron push/pull
/// satırları <c>id</c> üzerinden upsert eder; bu indeksler senkron tekilleştirmesinde KULLANILMAZ.
/// </summary>
public sealed class Migration082_OperationIdCompanyScope : IMigration
{
    public int Version => 82;
    public string Name => "operation_id_company_scope";

    /// <summary>(indeks adı, tablo) — 6 hedef; TEK doğru kaynak (test de bu listeden doğrular).</summary>
    public static readonly IReadOnlyList<(string Index, string Table)> Targets = new[]
    {
        ("ux_stock_movements_operation", "stock_movements"),
        ("ux_vehicle_maintenances_op", "vehicle_maintenances"),
        ("ux_fuel_depot_op", "fuel_depot_entries"),
        ("ux_fuel_dist_op", "fuel_distributions"),
        ("ux_daily_activities_op", "daily_activities"),
        ("ux_assign_operation", "assignment_movements"),
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
