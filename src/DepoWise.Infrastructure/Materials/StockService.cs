using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace DepoWise.Infrastructure.Materials;

public sealed class NegativeStockException : Exception
{
    public NegativeStockException(string message) : base(message) { }
}

public sealed record StockLine(string MaterialId, decimal Quantity, decimal? UnitPrice = null, string Currency = "TRY");
public sealed record CountLine(string MaterialId, decimal CountedQuantity);

public sealed record StockDocResult(string DocumentId, string DocNo);

public sealed record StockMovementRow(long CreatedAt, string MovementType, string Code, string Name, string Unit,
    int Direction, decimal Quantity, decimal? UnitPrice, string? Note,
    string? InvoiceNo = null, string? OrderSlipNo = null, string? CreditSlipNo = null)
{
    public string InvoiceText => string.IsNullOrWhiteSpace(InvoiceNo) ? "—" : InvoiceNo!;
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string DirectionText => Direction > 0 ? "Giriş" : "Çıkış";
    public string TypeText => MovementType switch
    {
        "opening" => "Açılış", "in" => "Giriş", "out" => "Çıkış",
        "transfer" => "Transfer", "adjustment" => "Düzeltme", _ => MovementType
    };
    public string QtyText => $"{Quantity:0.##} {Unit}".Trim();
    public string PriceText => UnitPrice is null ? "—" : $"{UnitPrice:0.##}";
    public string NoteText => string.IsNullOrWhiteSpace(Note) ? "—" : Note!;
}

/// <summary>
/// Stok giriş/çıkış/transfer/sayım — hareket defteri ANA KAYNAK; bakiye yalnız hareketle değişir.
/// Negatif stok engeli + idempotency (operation_id) + IMMEDIATE transaction (eş zamanlı çıkış güvenli).
/// İptal = ters hareket (fiziksel silme yok). Tüm akışlar tek transaction; hata → rollback.
/// </summary>
public sealed class StockService
{
    private const string Module = "stock";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public StockService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    // ---- Giriş ----
    public StockDocResult ReceiveIn(SessionContext s, IReadOnlyList<StockLine> lines, string operationId,
        string? branchId = null, string? personnelId = null, string? vehicleId = null, string? note = null, long? docDate = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        return RunDocument(s, "in", operationId, branchId, null, branchId, personnelId, vehicleId, note, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                    ApplyLine(conn, tx, s, docId, lines[i], +1, $"{operationId}:{i}", "in", branchId, null);
            }, invoiceNo: invoiceNo, orderSlipNo: orderSlipNo, creditSlipNo: creditSlipNo);
    }

    // ---- Çıkış (negatif stok engeli) ----
    public StockDocResult IssueOut(SessionContext s, IReadOnlyList<StockLine> lines, string operationId,
        string? branchId = null, string? personnelId = null, string? vehicleId = null, string? note = null, long? docDate = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        return RunDocument(s, "out", operationId, branchId, branchId, null, personnelId, vehicleId, note, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                    ApplyLine(conn, tx, s, docId, lines[i], -1, $"{operationId}:{i}", "out", branchId, branchId);
            }, invoiceNo: invoiceNo, orderSlipNo: orderSlipNo, creditSlipNo: creditSlipNo);
    }

    // ---- Transfer (kaynak çıkış + hedef giriş atomik, aynı grup) ----
    public StockDocResult Transfer(SessionContext s, string materialId, decimal quantity,
        string fromBranchId, string toBranchId, string operationId, string? note = null, long? docDate = null,
        string? personnelId = null, string? vehicleId = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (quantity <= 0) throw new ArgumentException("Transfer miktarı pozitif olmalı.");
        if (fromBranchId == toBranchId) throw new ArgumentException("Kaynak ve hedef şube aynı olamaz.");
        var groupId = Guid.NewGuid().ToString("N");
        return RunDocument(s, "transfer", operationId, toBranchId, fromBranchId, toBranchId, personnelId, vehicleId, note, docDate,
            (conn, tx, docId) =>
            {
                var line = new StockLine(materialId, quantity);
                // Kaynak çıkış (negatif guard) + hedef giriş — net bakiye değişmez ama hareketler kayıtlı
                ApplyLine(conn, tx, s, docId, line, -1, $"{operationId}:out", "transfer", fromBranchId, fromBranchId, groupId);
                ApplyLine(conn, tx, s, docId, line, +1, $"{operationId}:in", "transfer", toBranchId, fromBranchId, groupId);
            }, groupId, invoiceNo, orderSlipNo, creditSlipNo);
    }

    // ---- Sayım (gerekçeli fark hareketi) ----
    public StockDocResult Count(SessionContext s, IReadOnlyList<CountLine> lines, string reason, string operationId,
        string? branchId = null, long? docDate = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Sayım fark gerekçesi zorunlu.");
        return RunDocument(s, "count", operationId, branchId, branchId, branchId, null, null, reason, docDate,
            (conn, tx, docId) =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = lines[i];
                    var system = ReadBalance(conn, tx, ln.MaterialId);
                    var diff = ln.CountedQuantity - system;
                    InsertCountLine(conn, tx, docId, ln.MaterialId, system, ln.CountedQuantity, diff, reason);
                    if (diff != 0)
                    {
                        var dir = diff > 0 ? +1 : -1;
                        ApplyLine(conn, tx, s, docId, new StockLine(ln.MaterialId, Math.Abs(diff)),
                            dir, $"{operationId}:{i}", "adjustment", branchId, branchId);
                    }
                }
            });
    }

    // ---- İptal = ters hareket ----
    public void ReverseDocument(SessionContext s, string documentId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        AccessControl.RequireButton(s, SpecialButtons.Reverse);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction(deferred: false);

        var doc = LoadDocument(conn, tx, s.CompanyId, documentId)
            ?? throw new ForbiddenException("Belge bulunamadı veya başka firmaya ait.");
        if (doc.Status == "cancelled") { tx.Commit(); return; } // idempotent

        foreach (var mv in ActiveMovements(conn, tx, documentId))
        {
            // Ters yön uygula (negatif guard ters kayıtta da geçerli)
            ApplyDelta(conn, tx, s.CompanyId, mv.MaterialId, -mv.Direction * mv.Quantity, now, allowNegative: false);
            var revId = InsertMovement(conn, tx, s.CompanyId, mv.MaterialId, documentId, "reverse",
                -mv.Direction, mv.Quantity, null, null, null, $"{mv.OperationId}:rev", reason, now, mv.BranchId, mv.BranchFromId, mv.GroupId, reversesId: mv.Id);
            MarkReversed(conn, tx, mv.Id);
        }
        SetDocumentStatus(conn, tx, documentId, "cancelled", now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "stock_document", documentId, AuditActions.Reverse, s.UserId,
            AfterJson: $"{{\"reason\":\"{reason}\"}}"), _clock);
        tx.Commit();
    }

    public decimal GetBalance(string materialId)
    {
        using var conn = _factory.Create();
        return ReadBalance(conn, null, materialId);
    }

    /// <summary>Son stok hareketleri (salt okuma) — malzeme kod/ad + tür/yön/miktar/fiyat/not.</summary>
    public IReadOnlyList<StockMovementRow> RecentMovements(SessionContext s, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT sm.created_at, sm.movement_type, m.code, m.name, COALESCE(u.name,''),
       sm.direction, sm.quantity, sm.unit_price, sm.note,
       d.invoice_no, d.order_slip_no, d.credit_slip_no
FROM stock_movements sm
JOIN materials m ON m.id = sm.material_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN stock_documents d ON d.id = sm.document_id
WHERE sm.company_id = $c
ORDER BY sm.created_at DESC, sm.rowid DESC LIMIT $lim;";
        cmd.Parameters.AddWithValue("$c", s.CompanyId);
        cmd.Parameters.AddWithValue("$lim", limit);
        string? S(SqliteDataReader rr, int i) => rr.IsDBNull(i) ? null : rr.GetString(i);
        var list = new List<StockMovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StockMovementRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetInt32(5), Money.Parse(r.GetString(6)),
                r.IsDBNull(7) ? (decimal?)null : Money.Parse(r.GetString(7)),
                S(r, 8), S(r, 9), S(r, 10), S(r, 11)));
        return list;
    }

    // ================= çekirdek =================

    private StockDocResult RunDocument(SessionContext s, string docType, string operationId,
        string? toBranch, string? fromBranch, string? primaryBranch, string? personnelId, string? vehicleId,
        string? note, long? docDate, Action<SqliteConnection, SqliteTransaction, string> body, string? groupId = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var date = docDate ?? now;

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction(deferred: false); // IMMEDIATE → eş zamanlı çıkış serialize

        // Idempotency: bu operationId daha önce işlendiyse mevcut belgeyi döndür (çift yazma yok)
        var existing = FindDocumentByOperation(conn, tx, operationId);
        if (existing is not null) { tx.Commit(); return existing; }

        var docId = Guid.NewGuid().ToString("N");
        var docNo = NextDocNo(conn, tx, s.CompanyId, docType, date);
        InsertDocument(conn, tx, docId, s.CompanyId, docType, docNo, date, fromBranch, toBranch, personnelId, vehicleId, note, groupId, now,
            invoiceNo, orderSlipNo, creditSlipNo);

        body(conn, tx, docId);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "stock_document", docId, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"type\":\"{docType}\",\"no\":\"{docNo}\"}}"), _clock);
        tx.Commit();
        return new StockDocResult(docId, docNo);
    }

    private void ApplyLine(SqliteConnection conn, SqliteTransaction tx, SessionContext s, string docId,
        StockLine line, int direction, string operationId, string movementType, string? branchId, string? branchFromId, string? groupId = null)
    {
        if (line.Quantity <= 0) throw new ArgumentException("Miktar pozitif olmalı.");
        EnsureMaterialOwned(conn, tx, s.CompanyId, line.MaterialId);
        ApplyDelta(conn, tx, s.CompanyId, line.MaterialId, direction * line.Quantity, _clock.UtcNow.ToUnixTimeMilliseconds(), allowNegative: false);
        InsertMovement(conn, tx, s.CompanyId, line.MaterialId, docId, movementType, direction, line.Quantity,
            line.UnitPrice, line.Currency, null, operationId, null, _clock.UtcNow.ToUnixTimeMilliseconds(), branchId, branchFromId, groupId, null);
    }

    /// <summary>Bakiyeye işaretli miktarı uygular; düşüşte negatif olursa fail-closed.</summary>
    private static void ApplyDelta(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId,
        decimal signedQty, long now, bool allowNegative)
    {
        var current = ReadBalance(conn, tx, materialId);
        var updated = current + signedQty;
        if (!allowNegative && updated < 0)
            throw new NegativeStockException($"Negatif stok engellendi: mevcut {current}, talep {-signedQty}.");
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

    private static decimal ReadBalance(SqliteConnection conn, SqliteTransaction? tx, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=$m;";
        cmd.Parameters.AddWithValue("$m", materialId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private static string InsertMovement(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId,
        string documentId, string movementType, int direction, decimal quantity, decimal? unitPrice, string? currency,
        decimal? fxRate, string operationId, string? note, long now, string? branchId, string? branchFromId, string? groupId, string? reversesId)
    {
        var id = Guid.NewGuid().ToString("N");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, branch_from_id, movement_type, direction,
    quantity, unit_price, currency_code, fx_rate, operation_id, note, created_at, document_id, is_reversed, reverses_movement_id)
VALUES($id,$c,$m,$b,$bf,$type,$dir,$q,$price,$cur,$fx,$op,$note,$now,$doc,0,$rev);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$m", materialId);
        cmd.Parameters.AddWithValue("$b", (object?)branchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bf", (object?)branchFromId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$type", movementType);
        cmd.Parameters.AddWithValue("$dir", direction);
        cmd.Parameters.AddWithValue("$q", Money.Serialize(quantity));
        cmd.Parameters.AddWithValue("$price", unitPrice is null ? DBNull.Value : Money.Serialize(unitPrice.Value));
        cmd.Parameters.AddWithValue("$cur", (object?)currency ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fx", fxRate is null ? DBNull.Value : Money.Serialize(fxRate.Value));
        cmd.Parameters.AddWithValue("$op", operationId);
        cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.Parameters.AddWithValue("$doc", documentId);
        cmd.Parameters.AddWithValue("$rev", (object?)reversesId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void InsertDocument(SqliteConnection conn, SqliteTransaction tx, string id, string companyId,
        string docType, string docNo, long docDate, string? fromBranch, string? toBranch, string? personnelId,
        string? vehicleId, string? note, string? groupId, long now,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_documents(id, company_id, doc_type, doc_no, doc_date, from_branch_id, to_branch_id,
    personnel_id, vehicle_id, note, status, group_id, invoice_no, order_slip_no, credit_slip_no, created_at, version, is_deleted)
VALUES($id,$c,$type,$no,$date,$from,$to,$pers,$veh,$note,'active',$grp,$inv,$ord,$crd,$now,1,0);";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$type", docType);
        cmd.Parameters.AddWithValue("$no", docNo);
        cmd.Parameters.AddWithValue("$date", docDate);
        cmd.Parameters.AddWithValue("$from", (object?)fromBranch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$to", (object?)toBranch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pers", (object?)personnelId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$veh", (object?)vehicleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$note", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$grp", (object?)groupId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$inv", (object?)invoiceNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ord", (object?)orderSlipNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$crd", (object?)creditSlipNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertCountLine(SqliteConnection conn, SqliteTransaction tx, string docId, string materialId,
        decimal system, decimal counted, decimal diff, string reason)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_count_lines(id, document_id, material_id, system_qty, counted_qty, diff_qty, reason)
VALUES($id,$doc,$m,$s,$c,$d,$r);";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("$doc", docId);
        cmd.Parameters.AddWithValue("$m", materialId);
        cmd.Parameters.AddWithValue("$s", Money.Serialize(system));
        cmd.Parameters.AddWithValue("$c", Money.Serialize(counted));
        cmd.Parameters.AddWithValue("$d", Money.Serialize(diff));
        cmd.Parameters.AddWithValue("$r", reason);
        cmd.ExecuteNonQuery();
    }

    private static string NextDocNo(SqliteConnection conn, SqliteTransaction tx, string companyId, string docType, long docDateMs)
    {
        var year = DateTimeOffset.FromUnixTimeMilliseconds(docDateMs).Year;
        var prefix = docType switch { "in" => "GIR", "out" => "CIK", "transfer" => "TRF", "count" => "SAY", _ => "DOC" };
        var like = $"{prefix}-{year}-%";
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COALESCE(MAX(CAST(substr(doc_no, length($p)+1) AS INTEGER)),0) FROM stock_documents " +
            "WHERE company_id=$c AND doc_type=$t AND doc_no LIKE $like;";
        cmd.Parameters.AddWithValue("$p", $"{prefix}-{year}-");
        cmd.Parameters.AddWithValue("$c", companyId);
        cmd.Parameters.AddWithValue("$t", docType);
        cmd.Parameters.AddWithValue("$like", like);
        var next = Convert.ToInt64(cmd.ExecuteScalar()) + 1;
        return $"{prefix}-{year}-{next:0000}";
    }

    private static StockDocResult? FindDocumentByOperation(SqliteConnection conn, SqliteTransaction tx, string baseOperationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT d.id, d.doc_no FROM stock_movements mv JOIN stock_documents d ON d.id = mv.document_id " +
            "WHERE mv.operation_id LIKE $op LIMIT 1;";
        cmd.Parameters.AddWithValue("$op", baseOperationId + ":%");
        using var r = cmd.ExecuteReader();
        return r.Read() ? new StockDocResult(r.GetString(0), r.GetString(1)) : null;
    }

    private sealed record MovementRow(string Id, string MaterialId, int Direction, decimal Quantity,
        string OperationId, string? BranchId, string? BranchFromId, string? GroupId);

    private static IReadOnlyList<MovementRow> ActiveMovements(SqliteConnection conn, SqliteTransaction tx, string documentId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, material_id, direction, quantity, operation_id, branch_id, branch_from_id, " +
            "(SELECT group_id FROM stock_documents d WHERE d.id=$doc) FROM stock_movements " +
            "WHERE document_id=$doc AND is_reversed=0;";
        cmd.Parameters.AddWithValue("$doc", documentId);
        var list = new List<MovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MovementRow(r.GetString(0), r.GetString(1), r.GetInt32(2), Money.Parse(r.GetString(3)),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        return list;
    }

    private sealed record DocRow(string Id, string Status);

    private static DocRow? LoadDocument(SqliteConnection conn, SqliteTransaction tx, string companyId, string documentId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, status FROM stock_documents WHERE id=$id AND company_id=$c;";
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.Parameters.AddWithValue("$c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DocRow(r.GetString(0), r.GetString(1)) : null;
    }

    private static void MarkReversed(SqliteConnection conn, SqliteTransaction tx, string movementId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE stock_movements SET is_reversed=1 WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", movementId);
        cmd.ExecuteNonQuery();
    }

    private static void SetDocumentStatus(SqliteConnection conn, SqliteTransaction tx, string documentId, string status, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE stock_documents SET status=$s, version=version+1 WHERE id=$id;";
        cmd.Parameters.AddWithValue("$s", status);
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureMaterialOwned(SqliteConnection conn, SqliteTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=$id AND company_id=$c AND is_deleted=0;";
        cmd.Parameters.AddWithValue("$id", materialId);
        cmd.Parameters.AddWithValue("$c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }
}
