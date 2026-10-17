using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-15 (AR-GE 2026-10-17, B1 adım 4) — GÖNDERİLEMEYEN KAYITLAR LİSTESİ (yalnız masaüstü SQLite) ═══
///
/// <b>Sorun (05 F-174, F-065).</b> Sunucu bir kaydı reddettiğinde (ebeveyni yok, kod çakışması, yetki, doğrulama…) masaüstü
/// yalnız sayıyı ve en fazla 20 satırlık metni görüyordu; HANGİ kaydın gitmediği hiçbir yerde tutulmuyordu. 5 denemeden sonra
/// imleç ilerletildiği için kayıt bir daha denenmiyor, kullanıcı "kaydettim" sanıyordu.
///
/// <b>Çözüm.</b> Yerel <c>sync_rejections</c>: her reddedilen satır için tablo, satır anahtarı, neden, kalıcı mı, ilk/son
/// deneme, deneme sayısı, durum (<c>open</c> / <c>ignored</c>). Sonraki gönderimde aynı satır kabul edilirse kayıt silinir.
/// Eşitlenmez (yalnız bu bilgisayarın durumu). PostgreSQL'de bu migration hiçbir şey yapmaz.
/// </summary>
public sealed class Migration101_SyncRejections : IMigration
{
    public int Version => 101;
    public string Name => "sync_rejections";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!SqlDialect.IsSqlite(conn)) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = """
            CREATE TABLE IF NOT EXISTS sync_rejections (
                anahtar     TEXT PRIMARY KEY,
                company_id  TEXT NOT NULL,
                table_name  TEXT NOT NULL,
                row_key     TEXT NOT NULL,
                reason      TEXT NOT NULL,
                permanent   INTEGER NOT NULL DEFAULT 0,
                first_at    INTEGER NOT NULL,
                last_at     INTEGER NOT NULL,
                attempts    INTEGER NOT NULL DEFAULT 1,
                status      TEXT NOT NULL DEFAULT 'open',
                kind        TEXT NOT NULL DEFAULT 'red'     -- red = gönderilemedi · kod = K-22 kod değiştirildi (bilgi)
            );
            CREATE INDEX IF NOT EXISTS ix_sync_rejections_company ON sync_rejections(company_id, status);
            """;
        c.ExecuteNonQuery();
    }
}
