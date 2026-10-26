using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ AR-GE 2026-10-26 (eşitleme kararı nöbetçisinin bulgusu) — STOK DEĞİŞİKLİK KAYDI EŞİTLEMEYE ═══
/// Malzeme kartından "doğrudan stok değişikliği" uyarı kaydı (<c>stock_change_logs</c>, Migration057) masaüstünde YEREL yazılıyor,
/// eşitlenmiyordu: stok düzeltmesinin kendisi (stock_movements) sunucuya gidiyor ama "kim, neden, uyarıya rağmen devam etti mi"
/// kaydı yalnız o bilgisayarda kalıyordu; web'in "Stok Değişiklik Kaydı" ekranı onu hiç göstermiyordu (canlıda da böyle).
/// Migration121 ile aynı altyapı: sunucu damgası + masaüstü yerel değişiklik sayacı. Yalnız ekleme.
/// </summary>
public sealed class Migration122_SyncStockChangeLogs : IMigration
{
    public int Version => 122;
    public string Name => "sync_stock_change_logs";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!DbIntrospect.TableExists(conn, tx, "stock_change_logs")) return;
        Migration121_SyncB6Tables.SunucuDamgasiKur(conn, tx, "stock_change_logs");
        Migration100_LocalChangeSeq.YerelSayacKur(conn, tx, "stock_change_logs");
    }
}
