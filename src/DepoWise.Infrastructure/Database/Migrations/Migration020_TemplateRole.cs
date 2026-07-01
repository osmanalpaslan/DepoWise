using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Yetki şablonuna opsiyonel rol: şablon seçilince yeni kullanıcıya bu rol de atanır (yetkilerle birlikte).
/// Additive — mevcut şablonlar NULL (rol yok).
/// </summary>
public sealed class Migration020_TemplateRole : IMigration
{
    public int Version => 20;
    public string Name => "permission_templates.role_key";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE permission_templates ADD COLUMN role_key TEXT NULL;";
        cmd.ExecuteNonQuery();
    }
}
