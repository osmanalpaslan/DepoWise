using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Tek bir sürümlü şema değişikliği. Her migration tek transaction içinde çalışır;
/// idempotency runner tarafında (schema_migrations) sağlanır.
/// PostgreSQL geçişi Faz 2: taban <c>DbConnection</c>/<c>DbTransaction</c> — SQLite + Npgsql ortak tabanı.
/// </summary>
public interface IMigration
{
    int Version { get; }
    string Name { get; }
    void Up(DbConnection conn, DbTransaction tx);
}
