using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-10 (AR-GE 2026-10-18, 05 F-066) — OTURUM DÖNEMİ (jeton iptali) ═══
///
/// <b>Sorun.</b> Oturum jetonu (JWT) 12 saat geçerli ve <c>/api/auth/refresh</c> her geçerli jetonu yeniliyordu: şifre değişse
/// ya da yönetici sıfırlasa bile ESKİ jeton (ör. çalınmış) çalışmaya devam ediyordu.
///
/// <b>Çözüm.</b> <c>users.token_epoch</c>: her şifre değişiminde/sıfırlamasında 1 artar. Jeton üretildiği andaki dönemi taşır;
/// sunucu her istekte karşılaştırır (OturumDonemi) → dönem eskiyse 401. Yalnız ekleme: kolon 0 ile doğar, mevcut jetonlar
/// (dönem bilgisi yok = 0) şifre değişene kadar geçerli kalır.
/// </summary>
public sealed class Migration104_TokenEpoch : IMigration
{
    public int Version => 104;
    public string Name => "token_epoch";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.ColumnExists(conn, tx, "users", "token_epoch")) return;
        using var c = conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "ALTER TABLE users ADD COLUMN token_epoch BIGINT NOT NULL DEFAULT 0;";
        c.ExecuteNonQuery();
    }
}
