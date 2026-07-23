using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Kullanıcı yetki şablonları (Süper Admin) — isimli şablon; yeni kullanıcı oluştururken seçilir ve
/// yetkiler bu şablona göre yazılır. Modül izinleri + özel butonlar JSON olarak saklanır.
/// </summary>
public sealed class Migration019_PermissionTemplates : IMigration
{
    public int Version => 19;
    public string Name => "permission_templates";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE permission_templates (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    permissions_json TEXT NOT NULL DEFAULT '[]',
    buttons_json TEXT NOT NULL DEFAULT '[]',
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_permission_templates_name ON permission_templates(company_id, name) WHERE is_deleted=0;";
        cmd.ExecuteNonQuery();
    }
}
