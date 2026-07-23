using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// PostgreSQL geçişi — Faz 2 Adım 5 (2026-07-23): Türkçe arama/sıralama.
///
/// Masaüstü SQLite'ta Türkçe-duyarlı arama (like() fonksiyonu) ve sıralama (TRNOCASE collation) bağlantı
/// kurulurken KOD ile kaydedilir (<see cref="SqliteConnectionFactory"/>). PostgreSQL'de bunların karşılığı
/// ŞEMA NESNESİDİR (collation) → burada bir kez oluşturulur (ICU sağlayıcısı PG'de yerleşiktir):
///   • <c>dw_tr</c>    — Türkçe küçük-harf/karşılaştırma (deterministik); LikeTr'de
///                       <c>lower(... COLLATE dw_tr)</c> için (İ→i, I→ı).
///   • <c>nocase</c>   — harf-büyük/küçük duyarsız EŞİTLİK (SQLite <c>NOCASE</c> karşılığı); böylece uygulamadaki
///                       mevcut <c>= @n COLLATE NOCASE</c> SQL'i PG'de DEĞİŞMEDEN çalışır.
///   • <c>trnocase</c> — Türkçe harf-duyarsız SIRALAMA (SQLite <c>TRNOCASE</c> karşılığı); mevcut
///                       <c>ORDER BY ... COLLATE TRNOCASE</c> SQL'i PG'de DEĞİŞMEDEN çalışır (Ç, C'den sonra).
///
/// İsimler KÜÇÜK HARF: PG tırnaksız tanımlayıcıyı küçük harfe indirir → uygulamadaki <c>COLLATE NOCASE</c>
/// (tırnaksız) → <c>nocase</c> ile eşleşir. LIKE operatörü PG'de ezilemediği için yalnız LIKE, LikeTr'de
/// lehçe ayrımı yapar; COLLATE tabanlı ifadeler ortak kalır.
///
/// SQLite'ta NO-OP: collation'lar zaten çalışma zamanında factory'de kayıtlı; migration sürüm defterine
/// yazılır ama şemaya dokunmaz.
/// </summary>
public sealed class Migration053_PostgresTurkishCollations : IMigration
{
    public int Version => 53;
    public string Name => "postgres_turkish_collations";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (SqlDialect.IsSqlite(conn)) return;   // SQLite: like()/TRNOCASE zaten factory'de kayıtlı → no-op.

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE COLLATION IF NOT EXISTS dw_tr    (provider = icu, locale = 'tr-TR',             deterministic = true);
CREATE COLLATION IF NOT EXISTS nocase   (provider = icu, locale = 'und-u-ks-level2',   deterministic = false);
CREATE COLLATION IF NOT EXISTS trnocase (provider = icu, locale = 'tr-TR-u-ks-level1', deterministic = false);
";
        cmd.ExecuteNonQuery();
    }
}
