using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-31 (AR-GE 2026-10-26, F-114) — DEPO BAZINDA ASGARİ STOK ═══
/// <c>material_location_mins</c>: malzeme × depo/şantiye asgari miktarı. Öneri listesi (otomatik sipariş DEĞİL) bunu kullanır; depoya
/// özel değer yoksa malzeme kartındaki genel asgari stok geçerlidir. Kayıt kimliği <c>{malzeme}:{depo}</c> — iki bilgisayar aynı çift
/// için çevrimdışı değer girerse eşitlemede KOPYA oluşmaz (aynı satır, son yazan kazanır). Eşitlemede taşınır (sunucu damgası + yerel
/// sayaç burada kurulur). Boş doğar; yalnız CREATE TABLE.
/// </summary>
public sealed class Migration123_MaterialLocationMins : IMigration
{
    public int Version => 123;
    public string Name => "material_location_mins";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!DbIntrospect.TableExists(conn, tx, "material_location_mins"))
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = @"
CREATE TABLE material_location_mins (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    location_id TEXT NOT NULL,
    min_qty TEXT NOT NULL DEFAULT '0',
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE INDEX ix_material_location_mins ON material_location_mins(company_id, location_id, is_deleted);";
            c.ExecuteNonQuery();
        }
        Migration121_SyncB6Tables.SunucuDamgasiKur(conn, tx, "material_location_mins");
        Migration100_LocalChangeSeq.YerelSayacKur(conn, tx, "material_location_mins");
    }
}
