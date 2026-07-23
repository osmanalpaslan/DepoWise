using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// "Tüm Şubeler" yetkisi (users.can_view_all_branches). YALNIZ Süper Admin belirler; bu yetkiye sahip
/// adminler login'de "Tüm Şubeler" seçip firmanın tüm şube verisiyle çalışabilir. Varsayılan 0 (kapalı).
/// </summary>
public sealed class Migration026_UserViewAllBranches : IMigration
{
    public int Version => 26;
    public string Name => "user_view_all_branches";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "ALTER TABLE users ADD COLUMN can_view_all_branches BIGINT NOT NULL DEFAULT 0;";
        cmd.ExecuteNonQuery();
    }
}
