using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database;

public interface IDbConnectionFactory
{
    /// <summary>Açık ve PRAGMA'ları ayarlanmış bir bağlantı döndürür.</summary>
    SqliteConnection Create();
    string DatabasePath { get; }
}

/// <summary>
/// Yerel SQLite bağlantı üreticisi. Kural (CLAUDE.md / analiz §3):
/// Cache=Private (SHARED YASAK — UI donması), WAL, foreign_keys=ON, busy_timeout=5000.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    public string DatabasePath { get; }

    public SqliteConnectionFactory(string databasePath)
    {
        DatabasePath = databasePath;
    }

    public static SqliteConnectionFactory ForEnvironment(string environment)
        => new(AppPaths.DatabasePath(environment));

    public SqliteConnection Create()
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();

        var conn = new SqliteConnection(connStr);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "PRAGMA journal_mode=WAL;" +
                "PRAGMA foreign_keys=ON;" +
                "PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }
}
