using System.Data.Common;
using DepoWise.Infrastructure.Database;
using Npgsql;

namespace DepoWise.Api;

/// <summary>
/// PostgreSQL geçişi — Faz 3 (2026-07-23): SUNUCU (API) tarafı için üretim bağlantı üreticisi.
///
/// <see cref="SqliteConnectionFactory"/>'nin PostgreSQL karşılığı. Açık bir <see cref="NpgsqlConnection"/>
/// döndürür (taban <c>DbConnection</c> — çağıran kod lehçeden bağımsız çalışır; ortak olmayan parçalar
/// <see cref="SqlDialect"/> ile ayrışır). SQLite'ın PRAGMA/Cache/WAL kurulumu PG'de gereksizdir:
/// WAL motor düzeyinde varsayılan, foreign key'ler her zaman zorunludur.
///
/// 🔒 Npgsql BAĞIMLILIĞI YALNIZ BU PROJEDE (sunucu). Masaüstü (Desktop) SQLite'ta kalır, Npgsql almaz.
/// Yalnızca <c>DEPOWISE_PG_URL</c> ortam değişkeni tanımlıysa devreye girer (bkz. ServerServices);
/// yoksa sunucu eskisi gibi SQLite kullanır → babanın canlı sunucusu birebir aynı çalışır.
/// </summary>
public sealed class PostgresConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public PostgresConnectionFactory(string connectionString)
    {
        _connectionString = connectionString;
        // Görüntüleme/health için okunur bir etiket (host/db) — parola gibi sırları AÇMAZ.
        var b = new NpgsqlConnectionStringBuilder(connectionString);
        DatabasePath = $"postgres://{b.Host}/{b.Database}";
    }

    public string DatabasePath { get; }

    public DbConnection Create()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
