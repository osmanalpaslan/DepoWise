using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Malzemeye ŞUBE (2026-07-25, kullanıcı kararı): <c>materials.branch_id</c> (nullable). Malzemeler artık
/// ŞUBE-BAZLIDIR — belirli bir şubeyle girişte yalnız o şubede oluşturulan (+ şubesiz eski) malzemeler listelenir;
/// "Tüm Şubeler" ile girişte hepsi görünür (bkz. <c>BranchScope</c>).
///
/// • Mevcut malzemeler NULL (= şubesiz) kalır — KASITLI (veri kaybı/görünmezlik olmasın): her şubede görünürler.
///   Babanın canlı verisi bu sayede gizlenmez; yalnız YENİ oluşturulanlar oturumun şubesine bağlanır.
/// • Stok zaten <c>material_id</c> bazlıdır → her malzeme kaydı kendi (dolayısıyla şubesinin) stoğunu taşır; ayrı
///   stok şeması değişikliği GEREKMEZ.
/// • Kod benzersizliği FİRMA bazlı kalır (ux_materials_code değişmedi) — canlı veride index değişimi + NULL-şube
///   kenar durumları riskli. Şube-bazlı kod ileride ayrıca ele alınabilir.
/// • Sert FK yok (soft-reference; SQLite ADD COLUMN FK + purge/copy FK-sırası ek yükü olmaz).
/// </summary>
public sealed class Migration055_MaterialBranch : IMigration
{
    public int Version => 55;
    public string Name => "material_branch";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.ColumnExists(conn, tx, "materials", "branch_id")) return;   // idempotent

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "ALTER TABLE materials ADD COLUMN branch_id TEXT NULL;";
            cmd.ExecuteNonQuery();
        }
        using (var ix = conn.CreateCommand())
        {
            ix.Transaction = tx;
            ix.CommandText = "CREATE INDEX IF NOT EXISTS ix_materials_branch ON materials(branch_id);";
            ix.ExecuteNonQuery();
        }
    }
}
