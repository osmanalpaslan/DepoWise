using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ M-43 (AR-GE 2026-10-19, 05 F-087) — GÜNLÜK MAKİNE KONTROL LİSTESİ ═══
/// <c>daily_check_templates</c>: firmanın kontrol maddeleri (JSON dizi; yoksa varsayılan liste kullanılır).
/// <c>daily_checks</c>: araç/makine başına günlük kontrol kaydı — madde sonuçları dondurulmuş JSON, uygun olmayan madde sayısı,
/// açıldıysa arıza iş emri bağı. Değişmez kayıt (İSG kanıtı); tekrar gönderim işlem kimliğiyle zararsız. Yalnız CREATE TABLE.
/// </summary>
public sealed class Migration120_DailyChecks : IMigration
{
    public int Version => 120;
    public string Name => "daily_checks";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        void Calistir(string sql)
        {
            using var c = conn.CreateCommand();
            c.Transaction = tx;
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }
        if (!DbIntrospect.TableExists(conn, tx, "daily_check_templates"))
            Calistir(@"
CREATE TABLE daily_check_templates (
    company_id TEXT PRIMARY KEY,
    items_json TEXT NOT NULL,
    updated_by TEXT NULL,
    updated_at BIGINT NOT NULL
);");
        if (!DbIntrospect.TableExists(conn, tx, "daily_checks"))
            Calistir(@"
CREATE TABLE daily_checks (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    vehicle_id TEXT NOT NULL,
    check_date BIGINT NOT NULL,
    results_json TEXT NOT NULL,
    failed_count INTEGER NOT NULL DEFAULT 0,
    note TEXT NULL,
    work_order_id TEXT NULL,
    operation_id TEXT NULL,
    created_by TEXT NULL,
    created_at BIGINT NOT NULL
);
CREATE INDEX ix_daily_checks ON daily_checks(company_id, vehicle_id, check_date);
CREATE INDEX ix_daily_checks_op ON daily_checks(company_id, operation_id);");
    }
}
