using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Şubeye KOD + ŞİFRE (hash). Login'de firma+şube seçimi + şube şifresi doğrulaması için. Şifre BCrypt ile
/// saklanır (düz metin tutulmaz). code firma içinde benzersiz olmalı (uygulama katmanı kontrol eder).
/// </summary>
public sealed class Migration024_BranchCodePassword : IMigration
{
    public int Version => 24;
    public string Name => "branch_code_password";

    public void Up(SqliteConnection conn, SqliteTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
ALTER TABLE branches ADD COLUMN code TEXT;
ALTER TABLE branches ADD COLUMN password_hash TEXT;";
        cmd.ExecuteNonQuery();
    }
}
