using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Tek bir sürümlü şema değişikliği. Her migration tek transaction içinde çalışır;
/// idempotency runner tarafında (schema_migrations) sağlanır.
/// </summary>
public interface IMigration
{
    int Version { get; }
    string Name { get; }
    void Up(SqliteConnection conn, SqliteTransaction tx);
}
