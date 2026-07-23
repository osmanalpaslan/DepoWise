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
}
