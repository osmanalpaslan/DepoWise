using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// PostgreSQL geçişi — Faz 2 Adım 3 (2026-07-23): SQLite ↔ PostgreSQL arasında ORTAK KARŞILIĞI OLMAYAN
/// SQL parçaları için lehçe-duyarlı ifadeler.
///
/// Çoğu fark ortak SQL'e çevrildi (IFNULL→COALESCE, INSERT OR IGNORE→ON CONFLICT DO NOTHING; ikisini de
/// her iki veritabanı destekler). Ama bazı fonksiyonların ortak karşılığı YOK — onlar burada bağlantının
/// türüne göre üretilir. Migration'lar bunları <c>conn</c> ile çağırır; SQLite'ta eski davranış BİREBİR
/// korunur (569 test kanıtı), PostgreSQL'de eşdeğeri üretilir.
/// </summary>
internal static class SqlDialect
{
    public static bool IsSqlite(DbConnection conn) => conn is SqliteConnection;

    /// <summary>"Şu an" milisaniye (Unix ms) SQL ifadesi.
    /// SQLite: strftime; PostgreSQL: extract(epoch).</summary>
    public static string NowMs(DbConnection conn)
        => IsSqlite(conn)
            ? "CAST(strftime('%s','now') AS INTEGER)*1000"
            : "(extract(epoch from now())*1000)::bigint";

    /// <summary>32 karakter rastgele hex kimlik (GUID-benzeri) üreten SQL ifadesi — <c>INSERT ... SELECT</c>
    /// içinde satır başına kimlik üretmek için. SQLite: hex(randomblob); PostgreSQL: gen_random_uuid (PG13+).</summary>
    public static string NewHexId(DbConnection conn)
        => IsSqlite(conn)
            ? "lower(hex(randomblob(16)))"
            : "replace(gen_random_uuid()::text,'-','')";

    /// <summary>
    /// Aynı <c>created_at</c> (Unix ms) değerine sahip satırlar için KARARLI ikincil sıralama anahtarı.
    ///
    /// SQLite'ta <c>rowid</c> ekleme sırasını verir ve bugünkü davranış budur → SQLite'ta AYNEN korunur.
    /// PostgreSQL'de <c>rowid</c> YOKTUR (<c>42703: column ... does not exist</c>) ve <c>ctid</c> fiziksel
    /// konumdur (VACUUM ile değişir) → kullanılamaz. Bu yüzden PG'de birincil anahtar (<c>id</c>, TEXT)
    /// kullanılır: ekleme sırasını vermez ama sıralamayı DETERMİNİSTİK yapar (aynı sorgu → aynı sıra).
    ///
    /// <paramref name="alias"/>: sorgudaki tablo takma adı (örn. "sm").
    /// </summary>
    public static string RowTieBreaker(DbConnection conn, string alias)
        => IsSqlite(conn) ? $"{alias}.rowid" : $"{alias}.id";

    /// <summary>
    /// STK-08 (H-1) — TEXT içinde tutulan miktarın SAYISAL karşılığı; <b>yalnız KARŞILAŞTIRMA/FİLTRE</b>
    /// içindir (<c>&lt;&gt; 0</c>, <c>&gt; 0</c>, <c>COUNT</c>). Değer OKUMA yolu DEĞİŞMEZ: miktar her zaman
    /// ham metin olarak çekilip <c>Money.Parse</c> ile decimal'e çevrilir (kayan nokta hatası girmesin).
    ///
    /// NEDEN GEREKLİ: <c>quantity</c> TEXT'tir ve <c>Money.Serialize</c> ölçeği korur — sıfır değeri
    /// "0", "0.00" ya da "0.000" olarak yazılabilir. Metin karşılaştırması (<c>quantity='0'</c>) bu yüzden
    /// GÜVENİLMEZDİR; sayısal karşılaştırma üç biçimi de doğru eler.
    /// </summary>
    public static string NumericValue(DbConnection conn, string colExpr)
        => IsSqlite(conn) ? $"CAST({colExpr} AS REAL)" : $"CAST({colExpr} AS numeric)";

    /// <summary>Otomatik artan BIGINT birincil anahtar kolon tanımı (sıra numarası tabloları için).
    /// SQLite: INTEGER PRIMARY KEY AUTOINCREMENT; PostgreSQL: GENERATED ALWAYS AS IDENTITY.</summary>
    public static string AutoIncPk(DbConnection conn)
        => IsSqlite(conn)
            ? "INTEGER PRIMARY KEY AUTOINCREMENT"
            : "BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY";

    /// <summary>Türkçe-duyarsız "içerir/başlar" araması için LIKE ifadesi (İ↔i, I↔ı; ç/ş/ğ/ü/ö duyarsız).
    /// SQLite: like() fonksiyonu bağlantı kurulurken Türkçe kültürle EZİLDİ (<see cref="SqliteConnectionFactory"/>)
    /// → düz <c>col LIKE param</c> zaten Türkçe-duyarsız. PostgreSQL: LIKE bir OPERATÖR (fonksiyon değil), ezilemez
    /// → her iki tarafı Türkçe collation (dw_tr, Migration053'te kurulur) ile küçük harfe indirip eşleştir.
    /// Yalnız kullanıcı-arama LIKE'larında kullanılır; kod-üretim/yapısal (ASCII) LIKE'lar düz kalır.</summary>
    public static string LikeTr(bool sqlite, string colExpr, string paramExpr)
        => sqlite
            ? $"{colExpr} LIKE {paramExpr}"
            : $"lower({colExpr} COLLATE dw_tr) LIKE lower({paramExpr} COLLATE dw_tr)";

    /// <inheritdoc cref="LikeTr(bool,string,string)"/>
    public static string LikeTr(DbConnection conn, string colExpr, string paramExpr)
        => LikeTr(IsSqlite(conn), colExpr, paramExpr);

    /// <summary>SQLite'a özel (PostgreSQL'de karşılığı olmayan) fonksiyonları bağlantının lehçesine çevirir.
    /// SQLite'ta metni AYNEN döndürür → mevcut davranış BİREBİR korunur (569 test etkilenmez); yalnız PostgreSQL'de:
    ///   • <c>printf('%.2f', CAST(&lt;expr&gt; AS REAL))</c> → <c>to_char(CAST(&lt;expr&gt; AS double precision), 'FM…0.00')</c>
    ///     (sayıyı 2 ondalıklı metne çevirir — liste/grid'de sayısal kolonun "içerir" araması için).
    ///   • <c>GROUP_CONCAT(&lt;expr&gt;, '&lt;ayraç&gt;')</c> → <c>string_agg(&lt;expr&gt;, '&lt;ayraç&gt;')</c> (aynı imza).
    /// Liste/grid sorgularının komut metnini bununla sarmalayın (SQLite'ta güvenli no-op).</summary>
    /// <summary>
    /// STK-02 — Bir malzemenin TÜM lokasyon bakiyelerini tek satıra toplayan alt sorgu.
    /// <c>stock_balances</c> artık <c>(company_id, material_id, location_id)</c> anahtarlı olduğu için
    /// doğrudan JOIN yapılırsa malzeme satırları ÇOĞALIR (liste/rapor/dashboard yanlış olur).
    /// Bu alt sorgu <c>material_id</c> başına TEK satır garanti eder.
    ///
    /// ⚠️ YALNIZ GÖRÜNTÜLEME/RAPOR TOPLAMI İÇİNDİR. Yazma yollarında (CAS, recompute) SQL toplaması
    /// KULLANILMAZ — orada toplama C#'ta <c>decimal</c> ile yapılır, çünkü <c>quantity</c> TEXT içinde
    /// decimal tutulur ve SQLite'ta sayısal toplama kayan noktaya düşer (PostgreSQL'de <c>numeric</c> tamdır).
    ///
    /// <b>Çıktı tipi METİNDİR</b> — tıpkı <c>stock_balances.quantity</c> gibi. Bu bilinçlidir: çağıran
    /// sorguların hepsi bugün <c>COALESCE(b.quantity,'0')</c> yazıp C# tarafında <c>Money.Parse</c> /
    /// <c>GetString</c> ile okuyor. Sayısal döndürseydik 8 çağrı noktasının HEPSİNDE okuma kodu da
    /// değişmek zorunda kalırdı (sessiz <c>InvalidCastException</c> riski). Metin biçimi kanoniktir:
    /// en çok 6 ondalık, sondaki sıfırlar ve gereksiz nokta kırpılır → <c>15.50</c>+<c>0.00</c> = "15.5",
    /// <c>100</c> = "100", toplam 0 = "0". İki lehçe AYNI metni üretir.
    /// (6 ondalık sınırı yalnız GÖRÜNTÜLENEN toplamı ilgilendirir; defter ve bakiye satırları tam kalır.)
    /// </summary>
    /// <param name="locationWhere">
    /// DEN-E2 (2026-08-18) — OPSİYONEL LOKASYON KAPSAMI. Boş bırakılırsa davranış eskisiyle
    /// BİREBİR aynıdır (firma geneli toplam). Doluysa <c>" AND (location_id IN (…) OR location_id='')"</c>
    /// gibi bir parça beklenir ve toplama YALNIZ o depolar üzerinden yapılır — şube kapsamı raporun
    /// içine bu yoldan girer. Parametreler çağıran tarafından bağlanır.
    /// </param>
    public static string StockTotalSubquery(DbConnection conn, string locationWhere = "")
    {
        // Kırpma güvenli çünkü her iki biçim de HER ZAMAN ondalık nokta içerir ('%.6f' / 'FM….000000').
        // Nokta olmasaydı rtrim(...,'0') tam sayıyı bozardı ("100" → "1").
        var sum = IsSqlite(conn)
            ? "printf('%.6f', SUM(CAST(quantity AS REAL)))"
            : "to_char(SUM(CAST(quantity AS numeric)), 'FM999999999999990.000000')";
        return $"(SELECT material_id, company_id, rtrim(rtrim({sum}, '0'), '.') AS quantity " +
               "FROM stock_balances" + (locationWhere.Length > 0 ? " WHERE 1=1" + locationWhere : "") +
               " GROUP BY material_id, company_id)";
    }

    /// <summary>
    /// DEN-D2 (denetim 2026-08-18) — <b>PARA/MİKTAR TOPLAMI İÇİN KESİN SQL TOPLAMA.</b>
    ///
    /// Rapor ve ana ekran toplamları <c>SUM(CAST(x AS REAL))</c> ile hesaplanıyordu; sonuç kullanıcıya
    /// <c>1234,5600000000002</c> gibi görünebiliyor ya da kuruş sapması olabiliyordu (yakıt maliyeti =
    /// <c>SUM(litre × birim fiyat)</c> → PARA). Defter ve bakiye etkilenmiyordu (onlar doğru yoldan
    /// hesaplanıyor), etkilenen GÖSTERİLEN toplamlardı.
    ///
    /// Bu yardımcı <see cref="StockTotalSubquery"/> içindeki denemeyi yeniden kullanılabilir hâle getirir:
    /// • <b>PostgreSQL</b> (üretim): <c>numeric</c> ile toplar → <b>tam kesinlik</b>.
    /// • <b>SQLite</b> (masaüstü): kayan noktada toplar ama 6 ondalığa yuvarlar → görünen değer temiz.
    /// Dönen değer METİNDİR ve <c>Money.Parse</c> ile okunur (sondaki sıfırlar kırpılır: "15.5", "100", "0").
    /// </summary>
    public static string ExactSumText(DbConnection conn, string expr)
    {
        var sum = IsSqlite(conn)
            ? $"printf('%.6f', COALESCE(SUM(CAST({expr} AS REAL)),0))"
            : $"to_char(COALESCE(SUM(CAST({expr} AS numeric)),0), 'FM999999999999990.000000')";
        // Kırpma güvenli: iki biçim de HER ZAMAN ondalık nokta içerir (bkz. StockTotalSubquery).
        return $"rtrim(rtrim({sum}, '0'), '.')";
    }

    /// <summary>STK-02 — <c>const</c> SQL metinlerinde kullanılan yer tutucu. <see cref="PortableSql"/>
    /// bunu <see cref="StockTotalSubquery"/> ile değiştirir (const'ta bağlantı bilinemez).</summary>
    public const string StockTotalsToken = "{STOCK_TOTALS}";

    public static string PortableSql(DbConnection conn, string sql)
    {
        // Lehçeden BAĞIMSIZ ilk adım: stok toplamı yer tutucusu her iki veritabanında da açılır.
        if (sql.Contains(StockTotalsToken, StringComparison.Ordinal))
            sql = sql.Replace(StockTotalsToken, StockTotalSubquery(conn), StringComparison.Ordinal);

        if (IsSqlite(conn)) return sql;
        sql = System.Text.RegularExpressions.Regex.Replace(
            sql,
            @"printf\('%\.2f',\s*CAST\((.*?) AS REAL\)\)",
            "to_char(CAST($1 AS double precision), 'FM999999999990.00')");
        sql = sql.Replace("GROUP_CONCAT(", "string_agg(");
        return sql;
    }
}
