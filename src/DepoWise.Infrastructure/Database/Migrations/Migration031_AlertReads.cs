using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// #18 — Uyarı "okundu": kullanıcının ana ekranda gizlediği uyarılar. signature ile birlikte saklanır;
/// uyarının hali değişirse (kötüleşme) imza uyuşmaz → uyarı ana ekranda yeniden görünür.
/// İlgili modül ekranı (bakım/muayene/stok) okundu'dan bağımsız gösterir.
/// </summary>
public sealed class Migration031_AlertReads : IMigration
{
    public int Version => 31;
    public string Name => "alert_reads";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE alert_reads (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    alert_key TEXT NOT NULL,
    signature TEXT NOT NULL,
    created_at BIGINT NOT NULL
);
CREATE UNIQUE INDEX ux_alert_reads_user_key ON alert_reads(user_id, alert_key);";
        cmd.ExecuteNonQuery();
    }
}
