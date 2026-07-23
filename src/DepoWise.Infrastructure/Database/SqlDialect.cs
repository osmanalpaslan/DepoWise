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
    public static string PortableSql(DbConnection conn, string sql)
    {
        if (IsSqlite(conn)) return sql;
        sql = System.Text.RegularExpressions.Regex.Replace(
            sql,
            @"printf\('%\.2f',\s*CAST\((.*?) AS REAL\)\)",
            "to_char(CAST($1 AS double precision), 'FM999999999990.00')");
        sql = sql.Replace("GROUP_CONCAT(", "string_agg(");
        return sql;
    }
}
