using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Materials;

/// <summary>
/// Açılış stoğu — kart alanı DEĞİL, kontrollü 'opening' stok hareketi olarak yazılır (analiz §7).
/// Hareket + bakiye cache aynı transaction'da; operation_id ile idempotent (tekrar gönderim çift yazmaz).
/// Bakiye yalnız ledger üzerinden değişir; doğrudan set edilmez.
/// </summary>
public sealed class OpeningStockService
{
    private const string Module = "stock";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public OpeningStockService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Açılış stoğu kaydı. operation_id daha önce işlendiyse no-op (idempotent).
    /// Miktar NEGATİF olabilir (ADR-086): firma sistemi devralırken mevcut/eksik stoğunu olduğu gibi girer.</summary>
    public void RecordOpening(SessionContext s, string materialId, decimal quantity, string operationId,
        decimal? unitPrice = null, string currency = "TRY", string? branchId = null, decimal? fxRate = null, string? note = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (quantity == 0) throw new ArgumentException("Açılış miktarı sıfır olamaz.");
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");

        // ADR-086: açılış NEGATİF olabilir. Ledger sözleşmesi korunur → quantity DAİMA pozitif saklanır,
        // işaret DIRECTION ile taşınır. Böylece (1) stock_movements'in negatif-değer senkron kalkanı geçilir,
        // (2) RecomputeBalances (Σ yön×miktar) doğru kalır. Türetilmiş BAKİYE eksi olabilir; operasyonel
        // ÇIKIŞ'ın negatif-bakiye engeli (StockService) AYNEN korunur — bu yalnız BAŞLANGIÇ değeridir.
        var direction = quantity < 0 ? -1 : 1;
        var absQty = Math.Abs(quantity);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        EnsureOwned(conn, tx, s.CompanyId, materialId);
        if (OperationApplied(conn, tx, operationId)) { tx.Commit(); return; } // idempotent

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, movement_type, direction,
    quantity, unit_price, currency_code, fx_rate, operation_id, note, created_at)
VALUES($id,$c,$m,$b,'opening',$dir,$q,$price,$cur,$fx,$op,$note,$now);";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$c", s.CompanyId);
            cmd.Parameters.AddWithValue("$m", materialId);
            cmd.Parameters.AddWithValue("$b", (object?)branchId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$dir", direction);
            cmd.Parameters.AddWithValue("$q", Money.Serialize(absQty));
            cmd.Parameters.AddWithValue("$price", unitPrice is null ? DBNull.Value : Money.Serialize(unitPrice.Value));
            cmd.Parameters.AddWithValue("$cur", currency);
            cmd.Parameters.AddWithValue("$fx", fxRate is null ? DBNull.Value : Money.Serialize(fxRate.Value));
            cmd.Parameters.AddWithValue("$op", operationId);
            cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.ExecuteNonQuery();
        }

        UpsertBalance(conn, tx, s.CompanyId, materialId, quantity, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "stock_movement", materialId, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"type\":\"opening\",\"qty\":\"{Money.Serialize(quantity)}\"}}"), _clock);
        tx.Commit();
    }

    public decimal GetBalance(SessionContext s, string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=$m;";
        cmd.Parameters.AddWithValue("$m", materialId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private static bool OperationApplied(SqliteConnection conn, SqliteTransaction tx, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE operation_id=$op;";
        cmd.Parameters.AddWithValue("$op", operationId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static void UpsertBalance(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId, decimal delta, long now)
    {
        decimal current;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=$m;";
            read.Parameters.AddWithValue("$m", materialId);
            current = Money.Parse(read.ExecuteScalar() as string);
        }
        var updated = current + delta;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_balances(company_id, material_id, quantity, updated_at) VALUES($c,$m,$q,$now)
ON CONFLICT(material_id) DO UPDATE SET quantity=excluded.quantity, updated_at=excluded.updated_at;";
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$m", materialId);
        cmd.Parameters.AddWithValue("$q", Money.Serialize(updated));
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureOwned(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=$id AND company_id=$c;";
        cmd.Parameters.AddWithValue("$id", materialId);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }
}
