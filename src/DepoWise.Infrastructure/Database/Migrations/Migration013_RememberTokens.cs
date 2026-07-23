using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// "Beni Hatırla" token'ları. Parola DÜZ saklanmaz; yalnız token HASH'i tutulur (sızıntıda parola açığa çıkmaz).
/// Düz token cihazda DPAPI ile korunur. Süreli; logout/iptalde silinir.
/// </summary>
public sealed class Migration013_RememberTokens : IMigration
{
    public int Version => 13;
    public string Name => "remember_tokens";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE remember_tokens (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    token_hash TEXT NOT NULL,
    expires_at BIGINT NOT NULL,
    created_at BIGINT NOT NULL,
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE INDEX ix_remember_tokens ON remember_tokens(token_hash, expires_at);";
        cmd.ExecuteNonQuery();
    }
}
