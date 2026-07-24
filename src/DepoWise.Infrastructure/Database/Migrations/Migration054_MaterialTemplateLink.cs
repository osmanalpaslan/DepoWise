using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Malzemeye ŞABLON BAĞI (2026-07-24): <c>materials.template_id</c> (nullable). Yeni kayıt formunda bir malzeme
/// şablonu SEÇİLİRSE bu bağ yazılır → "şablonlu / şablon-dışı" yönetici raporları ayrımı için.
///
/// • Şablon SEÇME herkese açık kalır; yalnız şablon OLUŞTURMA yetkiye bağlıdır (material_templates modül izni, ayrı).
/// • Mevcut malzemeler NULL (= şablon-dışı) kalır — KASITLI (kullanıcı kararı): temizlik/inceleme listesinde görünürler.
/// • Sert FK eklenmedi (soft-reference): şablon soft-delete edilse bile bağ korunur; ayrıca SQLite ADD COLUMN FK
///   kenar durumları + purge/copy FK-sırası ek yükü olmaz. Rapor yalnız IS NULL / IS NOT NULL ile sorgular.
/// </summary>
public sealed class Migration054_MaterialTemplateLink : IMigration
{
    public int Version => 54;
    public string Name => "material_template_link";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.ColumnExists(conn, tx, "materials", "template_id")) return;   // idempotent

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "ALTER TABLE materials ADD COLUMN template_id TEXT NULL;";
            cmd.ExecuteNonQuery();
        }
        using (var ix = conn.CreateCommand())
        {
            ix.Transaction = tx;
            ix.CommandText = "CREATE INDEX IF NOT EXISTS ix_materials_template ON materials(template_id);";
            ix.ExecuteNonQuery();
        }
    }
}
