using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Makine tanımı sıfırlama isteği (ADR-085) — machine_resets.
///
/// Neden MAKİNE ADI ile anahtarlanır (firma değil): <c>sync_devices</c> (firma, makine adı) çiftiyle
/// tutulur; aynı fiziksel makine birden çok firmada satıra sahip olabilir. "Bu makinenin tanımını sıfırla"
/// isteği ise FİZİKSEL MAKİNEYE aittir — hangi firmayla giriş yapılırsa yapılsın algılanmalıdır.
///
/// ADR-084'teki (company_local_resets) ile AYNI iki-anlamlı desen:
///  - SUNUCUDA: "bu makine için EN SON istenen sıfırlama zamanı" (süper admin yazar).
///  - HER MASAÜSTÜNDE (kendi yerel kopyasında): "BU makinenin EN SON UYGULADIĞI sıfırlama zamanı".
/// Karşılaştırma sunucu &gt; yerel ise bir kerelik uygulanır → istek TAM BİR KEZ çalışır.
///
/// Idempotent.
/// </summary>
public sealed class Migration046_MachineReset : IMigration
{
    public int Version => 46;
    public string Name => "machine_reset";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS machine_resets(
    machine_name TEXT PRIMARY KEY,
    requested_at BIGINT NOT NULL,
    requested_by TEXT NOT NULL
);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_machine_resets_at ON machine_resets(requested_at);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
