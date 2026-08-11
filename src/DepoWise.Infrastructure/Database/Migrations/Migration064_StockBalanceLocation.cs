using DepoWise.Application.Common;
using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// STK-01 (FAZ C, 2026-08-11) — DEPO/LOKASYON BAZLI STOK BAKİYESİ. Karar: <b>KARAR-7 = A</b>
/// (malzeme kartı FİRMA GENELİ, stok LOKASYON BAZLI).
///
/// SORUN: <c>stock_balances</c> birincil anahtarı <c>(material_id)</c> idi → firma başına TEK bakiye.
/// Oysa <c>stock_movements</c> baştan beri lokasyon taşıyor (<c>branch_id</c> + transferde
/// <c>branch_from_id</c>) ve <c>StockService.Transfer</c> kaynak çıkış + hedef giriş olmak üzere iki
/// hareket yazıyor. Yani defter doğruydu; bakiye tek havuzda topladığı için transfer bakiyede
/// GÖRÜNMÜYORDU ("net bakiye değişmez" — StockService yorumu).
///
/// ÇÖZÜM: birincil anahtar <c>(company_id, material_id, location_id)</c> olur.
///  • <c>location_id</c> = <c>branches.id</c> (stok lokasyonu; ayrı depo tablosu YOK — bkz. tasarım belgesi).
///  • <c>location_id = ''</c> → <b>ATANMAMIŞ</b>. PostgreSQL'de birincil anahtar kolonu NULL olamaz;
///    boş metin açık, sorgulanabilir ve ekranda gösterilebilir bir kovadır.
///
/// VERİ KAYNAĞI — TAHMİN YOK: yeni bakiyeler <b>hareket defterinden yeniden hesaplanır</b>
/// (<c>GROUP BY material_id, COALESCE(branch_id,'')</c>). Eski tek-satır bakiyeler lokasyonlara
/// DAĞITILMAZ/TAHMİN EDİLMEZ. Lokasyonu bilinmeyen geçmiş hareket ATANMAMIŞ kovasında kalır;
/// kullanıcı bunu sonradan transferle gerçek depoya taşır (KARAR-8, STK-08).
///
/// TOPLAM HASSASİYETİ: miktarlar TEXT içinde decimal tutuluyor. SQL'de <c>CAST(... AS REAL)</c> ile
/// toplamak SQLite'ta kayan nokta hatası üretir → toplama <b>C#'ta decimal ile</b> yapılır
/// (<see cref="Money"/>). Bu, <c>StockService</c>'in mevcut yeniden hesaplama deseniyle aynıdır.
///
/// GÜVENLİK — DOĞRULAMA + ROLLBACK: yazmadan önce her malzeme için
/// <c>SUM(yeni lokasyon bakiyeleri) == eski tek bakiye</c> karşılaştırılır. Tek bir malzemede bile
/// fark varsa migration <b>istisna fırlatır</b> ve <see cref="MigrationRunner"/> transaction'ı geri alır
/// (hiçbir şey yazılmaz). Üretim kopyasında önceden kanıtlandı: 664 → 665 satır, <b>uyuşmayan 0</b>.
///
/// MEVCUT NEGATİFLER KORUNUR: üretimde 66 malzemede negatif bakiye var (ADR-086 — devralınan eksik
/// stok). Migration bunları "düzeltmez"; defterin söylediğini yazar.
///
/// LEHÇE: son durum İKİSİNDE DE AYNI. SQLite birincil anahtarı ALTER ile değiştiremediği için tablo
/// standart yöntemle yeniden kurulur (yeni tablo → doldur → eskiyi bırak → adlandır). Aynı yol
/// PostgreSQL'de de çalışır; tek kod yolu kullanılır.
///
/// GERİ ALMA (gerekirse, iki veritabanında da):
///     CREATE TABLE stock_balances_old AS SELECT company_id, material_id,
///            CAST(SUM(CAST(quantity AS numeric)) AS TEXT) AS quantity, MAX(updated_at) AS updated_at
///     FROM stock_balances GROUP BY company_id, material_id;   -- lokasyonlar toplanır
///     DROP TABLE stock_balances; ALTER TABLE stock_balances_old RENAME TO stock_balances;
///     -- ardından PRIMARY KEY(material_id) ile yeniden kurulmalıdır
///     DELETE FROM schema_migrations WHERE version = 64;
/// </summary>
/// <remarks>
/// ✅ <b>ETKİN</b> — <see cref="MigrationCatalog"/>'a <c>STK-02</c> ile AYNI iş biriminde eklendi (2026-08-11).
///
/// Tek başına etkinleştirilmesi YASAKTI: şema değişince <c>stock_balances</c> malzeme başına ÇOK SATIR
/// döndürür ve eski tek-satır varsayımına dayanan 16 üretim çağrı noktası stok değerlerini <b>sessizce
/// yanlış</b> gösterirdi. STK-02'de hepsi lokasyon farkında hale getirildi:
///   • skaler okumalar → <see cref="Materials.StockBalanceWriter.ReadTotal"/> (C#'ta decimal toplama)
///   • 8 × <c>LEFT JOIN stock_balances</c> → <c>SqlDialect.StockTotalSubquery</c> (malzeme başına tek satır)
///   • CAS yazma → <c>ON CONFLICT(company_id, material_id, location_id)</c>
///
/// PROVA (2026-08-11): üretim yedeğinin izole PostgreSQL kopyasında 667 hareket / 664 bakiye → 665 bakiye,
/// uyuşmayan <b>0</b>, toplam korundu; SQLite'ta dolu v63→v64 yükseltmesi de kayıpsız. Doğrulama kapısının
/// gerçekten durdurduğu ve geri aldığı ayrıca kanıtlandı.
/// Kayıt: <c>docs/project-control/FAZ_C_DEPO_BAZLI_STOK_TASARIM.md</c>.
/// </remarks>
public sealed class Migration064_StockBalanceLocation : IMigration
{
    public int Version => 64;
    public string Name => "stock_balance_location";

    /// <summary>Lokasyonu bilinmeyen (geçmiş) stok için kova. Boş metin — NULL DEĞİL (PK kolonu).</summary>
    public const string Unassigned = "";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // 1) ESKİ bakiyeler (doğrulama referansı) — malzeme başına tek satır.
        var oldBalances = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT material_id, quantity FROM stock_balances;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                oldBalances[r.GetString(0)] = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
        }

        // 2) YENİ bakiyeler — DEFTERDEN, C#'ta decimal toplama (SQL CAST hassasiyeti bozmasın).
        //    Anahtar: (company_id, material_id, location_id)
        var fresh = new Dictionary<(string Company, string Material, string Location), decimal>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT company_id, material_id, branch_id, direction, quantity FROM stock_movements;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = (r.GetString(0), r.GetString(1), r.IsDBNull(2) ? Unassigned : r.GetString(2));
                var qty = Money.Parse(r.IsDBNull(4) ? null : r.GetString(4));
                fresh.TryGetValue(key, out var cur);
                fresh[key] = cur + r.GetInt64(3) * qty;
            }
        }

        // 3) DOĞRULAMA — malzeme bazında toplamlar eski bakiyeyle BİREBİR eşleşmeli.
        //    Eşleşmezse istisna → MigrationRunner transaction'ı geri alır, hiçbir şey yazılmaz.
        var newTotals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var ((_, material, _), qty) in fresh)
        {
            newTotals.TryGetValue(material, out var cur);
            newTotals[material] = cur + qty;
        }
        foreach (var (material, oldQty) in oldBalances)
        {
            newTotals.TryGetValue(material, out var newQty);
            if (newQty != oldQty)
                throw new InvalidOperationException(
                    $"Migration064 DURDURULDU: '{material}' malzemesinde bakiye uyuşmuyor " +
                    $"(eski={oldQty}, defterden={newQty}). Hiçbir değişiklik yazılmadı.");
        }

        // 4) Tabloyu yeniden kur (SQLite PK'yi ALTER ile değiştiremez; aynı yol PG'de de geçerli).
        Exec(conn, tx, @"
CREATE TABLE stock_balances_new (
    company_id  TEXT NOT NULL,
    material_id TEXT NOT NULL,
    location_id TEXT NOT NULL,               -- branches.id  |  '' = ATANMAMIŞ
    quantity    TEXT NOT NULL DEFAULT '0',   -- decimal (invariant) — float KULLANILMAZ
    updated_at  BIGINT NOT NULL,
    PRIMARY KEY (company_id, material_id, location_id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);");

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var ((company, material, location), qty) in fresh)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO stock_balances_new(company_id, material_id, location_id, quantity, updated_at) " +
                "VALUES(@c,@m,@l,@q,@now);";
            ins.AddWithValue("@c", company);
            ins.AddWithValue("@m", material);
            ins.AddWithValue("@l", location);
            ins.AddWithValue("@q", Money.Serialize(qty));
            ins.AddWithValue("@now", now);
            ins.ExecuteNonQuery();
        }

        Exec(conn, tx, "DROP TABLE stock_balances;");
        Exec(conn, tx, "ALTER TABLE stock_balances_new RENAME TO stock_balances;");

        // 5) İndeks — lokasyon bazlı listeleme/rapor sorgusu (WHERE company_id AND location_id).
        //    Malzeme bazlı erişim zaten birincil anahtarın ön ekinden yararlanır.
        Exec(conn, tx, "CREATE INDEX ix_stock_balances_location ON stock_balances(company_id, location_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
