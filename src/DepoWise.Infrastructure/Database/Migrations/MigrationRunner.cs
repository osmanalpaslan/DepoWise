using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Sürümlü migration çalıştırıcı. Sıfır DB ve mevcut DB üzerinde güvenli/idempotent çalışır:
/// yalnız uygulanmamış sürümler artan sırada, her biri tek transaction içinde uygulanır.
/// </summary>
public sealed class MigrationRunner
{
    private readonly IDbConnectionFactory _factory;
    private readonly IReadOnlyList<IMigration> _migrations;

    public MigrationRunner(IDbConnectionFactory factory, IEnumerable<IMigration>? migrations = null)
    {
        _factory = factory;
        _migrations = (migrations ?? MigrationCatalog.All())
            .OrderBy(m => m.Version)
            .ToList();
    }

    /// <summary>Bekleyen migration'ları uygular. Uygulanan sürüm listesini döndürür.</summary>
    public IReadOnlyList<int> Run()
    {
        using var conn = _factory.Create();
        EnsureHistoryTable(conn);
        var applied = AppliedVersions(conn);
        var justApplied = new List<int>();

        foreach (var m in _migrations)
        {
            if (applied.Contains(m.Version)) continue;
            using var tx = conn.BeginTransaction();
            m.Up(conn, tx);
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO schema_migrations(version, name, applied_at) VALUES(@v, @n, @t);";
                cmd.AddWithValue("@v", m.Version);
                cmd.AddWithValue("@n", m.Name);
                cmd.AddWithValue("@t", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            justApplied.Add(m.Version);
        }
        return justApplied;
    }

    public int CurrentVersion()
    {
        using var conn = _factory.Create();
        EnsureHistoryTable(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void EnsureHistoryTable(DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            // applied_at = Unix ms (~1.7 trilyon) → PostgreSQL'de 32-bit INTEGER'a SIĞMAZ, BIGINT gerekir.
            // (SQLite'ta BIGINT = INTEGER affinity, davranış aynı.)
            "CREATE TABLE IF NOT EXISTS schema_migrations(" +
            "version BIGINT PRIMARY KEY, name TEXT NOT NULL, applied_at BIGINT NOT NULL);";
        cmd.ExecuteNonQuery();
    }

    private static HashSet<int> AppliedVersions(DbConnection conn)
    {
        var set = new HashSet<int>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_migrations;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) set.Add(r.GetInt32(0));
        return set;
    }
}
