using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ FAZ I (2026-09-04) — LİSTE SORGULARININ İNDEKSLERİ ═══
///
/// <b>Bulgu (indeks denetimi):</b> iki büyüyen tablo, en sık çalışan sorgusunu destekleyen indekse
/// sahip değildi:
///
/// <list type="bullet">
///   <item><c>stock_movements</c> — mevcut indeksler <c>(material_id, created_at)</c> ve
///   <c>(operation_id)</c>. Oysa liste/rapor sorgularının HEPSİ <c>WHERE company_id=@c</c> ile süzüp
///   <c>ORDER BY created_at DESC</c> ile sıralıyor. Hiçbiri <c>company_id</c> ile BAŞLAMADIĞI için
///   sorgu tabloyu baştan sona tarıyordu.</item>
///   <item><c>vehicle_maintenances</c> — aynı durum: <c>(vehicle_id, maintenance_def_id, created_at)</c>
///   araç bazlı okumaya yarar, ama şirket geneli liste <c>company_id</c> + <c>created_at</c> ister.</item>
/// </list>
///
/// <b>Neden şimdi:</b> LST-01 ile bu iki ekran SAYFALANDI. Sayfalama her sayfada bir <c>COUNT(*)</c>
/// daha çalıştırır — indekssiz bir tabloda bu, tarama sayısını ikiye katlar. Yani sayfalama düzeltmesi,
/// indeks olmadan performansı iyileştirmek yerine kötüleştirebilirdi.
///
/// <b>Stok defteri hiç silinmez</b> (append-only, ana kaynak) → tek yönlü büyür. Bugün küçük olması
/// yarın da küçük kalacağı anlamına gelmez; indeks tam da bunun için.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız <c>CREATE INDEX</c>. Hiçbir satır okunmaz/yazılmaz/silinmez,
/// şema sözleşmesi değişmez, uygulama kodu bu indeksleri bilmek zorunda değildir (sorgu planlayıcı
/// kendiliğinden kullanır). Geri alma: iki <c>DROP INDEX</c>.
///
/// <b>Neden <c>CONCURRENTLY</c> değil:</b> migration çalıştırıcısı TEK transaction kullanır ve
/// PostgreSQL <c>CREATE INDEX CONCURRENTLY</c>'yi transaction içinde kabul etmez (ADR-185/PK-FIN-03'te
/// aynı karar verilmişti). Tablolar küçük olduğu için kilit süresi ihmal edilebilir.
/// </summary>
public sealed class Migration091_ListIndexes : IMigration
{
    public int Version => 91;
    public string Name => "list_indexes";

    /// <summary>(indeks adı, tablo, kolonlar) — hepsi liste sorgusunun WHERE + ORDER BY sırasına uyar.</summary>
    private static readonly (string Name, string Table, string Columns)[] Targets =
    {
        ("ix_stock_movements_company", "stock_movements", "company_id, created_at"),
        ("ix_vehicle_maintenances_company", "vehicle_maintenances", "company_id, created_at"),
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var (name, table, columns) in Targets)
        {
            if (!DbIntrospect.TableExists(conn, tx, table)) continue;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // IF NOT EXISTS iki lehçede de desteklenir (Migration066 aynı deseni kullanır) → idempotent.
            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS {name} ON {table}({columns});";
            cmd.ExecuteNonQuery();
        }
    }
}
