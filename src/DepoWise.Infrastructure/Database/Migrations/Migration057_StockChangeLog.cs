using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Doğrudan stok değişikliği UYARI LOGU (kullanıcı isteği 2026-08-06, madde 1.4/1.5): Malzeme kartından
/// Giriş/Çıkış ekranı KULLANILMADAN stok değiştirilmek istendiğinde gösterilen güçlü uyarının kaydı.
/// En az: kullanıcı, tarih/saat, yapılmak istenen işlem (eski→yeni stok), gösterilen uyarı metni ve
/// işlemin DEVAM mı ETTİ / İPTAL mi edildiği. Ayrı bir görüntüleme ekranı + yetki (module: stock_change_log).
///
/// Denormalize (kullanıcı adı + malzeme kod/ad) — değişmez audit kaydı; malzeme/kullanıcı sonradan silinse
/// bile o anki değer korunur. audit_logs GİBİ senkron edilmez (her DB kendi kaydını tutar); stok değişikliğinin
/// KENDİSİ zaten stock_movements'a (adjustment) yazılıp senkronlanır. Portable (SQLite + PostgreSQL).
/// </summary>
public sealed class Migration057_StockChangeLog : IMigration
{
    public int Version => 57;
    public string Name => "stock_change_log";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        if (DbIntrospect.TableExists(conn, tx, "stock_change_logs")) return;   // idempotent

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE stock_change_logs (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    user_id TEXT NULL,
    user_name TEXT NULL,                 -- denormalize: o anki kullanıcı adı/tam adı
    material_id TEXT NULL,
    material_code TEXT NULL,             -- denormalize
    material_name TEXT NULL,             -- denormalize
    branch_id TEXT NULL,
    old_quantity TEXT NOT NULL DEFAULT '0',  -- Money string (diğer miktarlarla aynı biçim)
    new_quantity TEXT NOT NULL DEFAULT '0',
    outcome TEXT NOT NULL,               -- 'continued' | 'cancelled'
    warning_text TEXT NULL,
    created_at BIGINT NOT NULL
);
CREATE INDEX ix_stock_change_logs_company_time ON stock_change_logs(company_id, created_at);";
        cmd.ExecuteNonQuery();
    }
}
