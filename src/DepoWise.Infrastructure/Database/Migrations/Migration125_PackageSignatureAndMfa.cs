using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-01 (K-20, 2026-10-27) — PAKET İMZASI + SÜPER ADMİN İKİ ADIMLI GİRİŞ ═══
/// • <c>app_releases.signature</c>: yayının ECDSA imzası (base64). Eski satırlar NULL kalır (imzasız eski yayın).
/// • <c>user_mfa</c>: kullanıcı başına doğrulama uygulaması (TOTP) gizli bilgisi — sunucu anahtarıyla ŞİFRELİ; son kullanılan
///   zaman adımı (aynı kod iki kez kullanılamaz) ve kurtarma kodlarının özetleri. Firma kimliği taşımaz ve EŞİTLENMEZ:
///   gizli bilgi masaüstlerine inmez, doğrulama yalnız sunucuda yapılır.
/// Yalnız ADD COLUMN + CREATE TABLE.
/// </summary>
public sealed class Migration125_PackageSignatureAndMfa : IMigration
{
    public int Version => 125;
    public string Name => "package_signature_and_mfa";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Exec(string sql)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }
        if (!DbIntrospect.ColumnExists(conn, tx, "app_releases", "signature"))
            Exec("ALTER TABLE app_releases ADD COLUMN signature TEXT NULL;");
        if (!DbIntrospect.TableExists(conn, tx, "user_mfa"))
            Exec(@"
CREATE TABLE user_mfa (
    user_id TEXT PRIMARY KEY,
    secret_enc TEXT NOT NULL,
    enabled_at BIGINT NULL,
    last_step BIGINT NOT NULL DEFAULT 0,
    recovery_hashes TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL
);");
    }
}
