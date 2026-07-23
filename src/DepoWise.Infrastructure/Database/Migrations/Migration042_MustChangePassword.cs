using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Güvenlik: yeni oluşturulan kullanıcı ilk giriş(ler)inde kendi şifresini belirlemek zorunda.
/// users.must_change_password (0/1). Mevcut kullanıcılar 0 (etkilenmez); yeni kullanıcılar CreateUser'da 1.
/// Idempotent.
/// </summary>
public sealed class Migration042_MustChangePassword : IMigration
{
    public int Version => 42;
    public string Name => "must_change_password";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (!ColumnExists(conn, tx, "users", "must_change_password"))
            Exec(conn, tx, "ALTER TABLE users ADD COLUMN must_change_password INTEGER NOT NULL DEFAULT 0;");
    }

    private static bool ColumnExists(DbConnection conn, DbTransaction tx, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
