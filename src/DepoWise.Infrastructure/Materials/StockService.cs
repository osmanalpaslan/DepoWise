using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

public sealed class NegativeStockException : Exception
{
    public NegativeStockException(string message) : base(message) { }
}

public sealed record StockLine(string MaterialId, decimal Quantity, decimal? UnitPrice = null, string Currency = "TRY");
public sealed record CountLine(string MaterialId, decimal CountedQuantity);

public sealed record StockDocResult(string DocumentId, string DocNo);

/// <summary>A3 (Aurora): malzeme kartı "Son Hareketler" satırı. Quantity İŞARETLİ (+giriş/−çıkış).</summary>
public sealed record MaterialMovementRow(long Date, string Kind, decimal Quantity, string Label, string? Reference);

public sealed record StockMovementRow(long CreatedAt, string MovementType, string Code, string Name, string Unit,
    int Direction, decimal Quantity, decimal? UnitPrice, string? Note,
    string? InvoiceNo = null, string? OrderSlipNo = null, string? CreditSlipNo = null,
    string? DocumentId = null, bool IsReversed = false)
{
    public string InvoiceText => string.IsNullOrWhiteSpace(InvoiceNo) ? "—" : InvoiceNo!;
    public bool CanReverse => !IsReversed && DocumentId != null && MovementType != "opening";
    public string StatusText => IsReversed ? "İptal edildi" : "";
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
        branchId = EnforceOwnBranch(s, branchId, "çıkış");   // şubeye bağlı kullanıcı yalnız kendi şubesinden
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
        // Şubeye bağlı kullanıcı yalnız KENDİ şubesinden transfer başlatabilir (kaynak şube = kendi şubesi).
        EnforceOwnBranch(s, fromBranchId, "transfer");
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

    // Şube-yetki (kullanıcı isteği 2026-08-05): şubeye bağlı kullanıcı (BranchScope.Active != null) yalnız
    // KENDİ şubesinden çıkış/transfer başlatabilir; "Tüm Şubeler"/admin (null) her şubeden. Yalnız interaktif
    // create yolunda çağrılır — sync ve idari ters kayıt bu kontrole girmez (offline/onarım bozulmaz).
    private static string? EnforceOwnBranch(SessionContext s, string? branchId, string op)
    {
        var scope = BranchScope.Active(s);
        if (scope is null) return branchId;                   // Tüm Şubeler / admin → serbest
        if (string.IsNullOrEmpty(branchId)) return scope;     // belirtilmemişse kendi şubesine ata
        if (branchId != scope)
            throw new ForbiddenException($"Yalnız kendi şubenizden {op} yapabilirsiniz.");
        return branchId;
    }

    // Şube bazlı stok bakiyesi (madde 8b) — o (malzeme, şube) için hareket defteri toplamı (giriş +, çıkış −).
    // Money.Parse ile C#'ta toplanır (SQL CAST hassasiyeti bozmasın). Aynı tx içindeki eklenmiş hareketleri de görür.
    private static decimal BranchBalance(DbConnection conn, DbTransaction tx, string companyId, string materialId, string branchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT direction, quantity FROM stock_movements WHERE company_id=@c AND material_id=@m AND branch_id=@b;";
        cmd.AddWithValue("@c", companyId); cmd.AddWithValue("@m", materialId); cmd.AddWithValue("@b", branchId);
        decimal total = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long dir = r.GetInt64(0);
            var qty = Money.Parse(r.IsDBNull(1) ? null : r.GetString(1));
            total += dir * qty;
        }
        return total;
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
        using var tx = conn.BeginImmediate();

        var doc = LoadDocument(conn, tx, s.CompanyId, documentId)
            ?? throw new ForbiddenException("Belge bulunamadı veya başka firmaya ait.");
        if (doc.Status == "cancelled") { tx.Commit(); return; } // idempotent

        foreach (var mv in ActiveMovements(conn, tx, documentId))
        {
            // Ters yön uygula (negatif guard ters kayıtta da geçerli)
            ApplyDelta(conn, tx, s.CompanyId, mv.MaterialId, -mv.Direction * mv.Quantity, now, allowNegative: false);
            var revId = InsertMovement(conn, tx, s.CompanyId, mv.MaterialId, documentId, "reverse",
                -mv.Direction, mv.Quantity, null, null, null, $"{mv.OperationId}:rev", reason, now, mv.BranchId, mv.BranchFromId, mv.GroupId, reversesId: mv.Id, opBranchId: s.OperatingBranchId);
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
        => SearchMovements(s, null, null, null, limit);

    /// <summary>Stok Hareketleri ekranı (kullanıcı isteği 2026-08-05): tarih aralığı (fromMs/toMs, Unix ms) +
    /// metin araması (malzeme kodu/adı, not, belge/fatura no). Şube kapsamı ve yetki RecentMovements ile aynı.</summary>
    public IReadOnlyList<StockMovementRow> SearchMovements(SessionContext s, long? fromMs, long? toMs, string? search, int limit = 500)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (limit < 1) limit = 1; if (limit > 5000) limit = 5000;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        var sb = new System.Text.StringBuilder(@"
SELECT sm.created_at, sm.movement_type, m.code, m.name, COALESCE(u.name,''),
       sm.direction, sm.quantity, sm.unit_price, sm.note,
       d.invoice_no, d.order_slip_no, d.credit_slip_no, sm.document_id, sm.is_reversed
FROM stock_movements sm
JOIN materials m ON m.id = sm.material_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN stock_documents d ON d.id = sm.document_id
WHERE sm.company_id = @c");
        sb.Append(DepoWise.Application.Security.BranchScope.Sql(s, "sm.branch_id"));
        if (fromMs is not null) sb.Append(" AND sm.created_at >= @from");
        if (toMs is not null) sb.Append(" AND sm.created_at <= @to");
        if (!string.IsNullOrWhiteSpace(search))
            sb.Append(" AND (m.code LIKE @q OR m.name LIKE @q OR sm.note LIKE @q OR d.invoice_no LIKE @q OR d.doc_no LIKE @q)");
        sb.Append(" ORDER BY sm.created_at DESC, sm.rowid DESC LIMIT @lim;");
        cmd.CommandText = sb.ToString();
        cmd.AddWithValue("@c", s.CompanyId);
        if (DepoWise.Application.Security.BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        if (fromMs is not null) cmd.AddWithValue("@from", fromMs.Value);
        if (toMs is not null) cmd.AddWithValue("@to", toMs.Value);
        if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
        cmd.AddWithValue("@lim", limit);
        string? S(DbDataReader rr, int i) => rr.IsDBNull(i) ? null : rr.GetString(i);
        var list = new List<StockMovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StockMovementRow(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetInt32(5), Money.Parse(r.GetString(6)),
                r.IsDBNull(7) ? (decimal?)null : Money.Parse(r.GetString(7)),
                S(r, 8), S(r, 9), S(r, 10), S(r, 11), S(r, 12), r.GetInt32(13) == 1));
        return list;
    }

    /// <summary>A3 (Aurora): TEK malzemenin son N hareketi (giriş/çıkış/transfer/sayım). Malzeme kartı sağ
    /// sütunundaki "Son Hareketler" paneli için. Yetki: malzeme OKUMA + firma kapsamı (malzeme detay ucuyla aynı).
    /// Tarihe göre azalan. Miktar İŞARETLİ (giriş +, çıkış −). Boşsa panel gösterilmez.</summary>
    public IReadOnlyList<MaterialMovementRow> RecentForMaterial(SessionContext s, string materialId, int take = 10)
    {
        AccessControl.Require(s, "materials", PermissionAction.View);   // malzeme detay ucuyla aynı kontrol
        if (take < 1) take = 1; if (take > 100) take = 100;
        using var conn = _factory.Create();
        // Firma kapsamı: yalnız oturumun firmasına ait + bu malzemenin hareketleri (çapraz-tenant sızıntısı yok).
        EnsureMaterialOwned(conn, null, s.CompanyId, materialId);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT sm.created_at, sm.movement_type, sm.direction, sm.quantity,
       COALESCE(br.name,''), COALESCE(d.doc_no,'')
FROM stock_movements sm
LEFT JOIN stock_documents d ON d.id = sm.document_id
LEFT JOIN branches br ON br.id = sm.branch_id
WHERE sm.company_id=@c AND sm.material_id=@m
ORDER BY sm.created_at DESC, sm.rowid DESC
LIMIT @take;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@take", take);
        var list = new List<MaterialMovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var type = r.GetString(1);
            int dir = r.GetInt32(2);
            var qty = Money.Parse(r.GetString(3));
            var branch = r.GetString(4);
            var doc = r.GetString(5);
            var typeText = type switch
            {
                "in" => "Giriş", "out" => "Çıkış", "transfer" => "Transfer",
                "adjustment" => "Sayım Düzeltme", "opening" => "Açılış", "reverse" => "İptal", _ => type
            };
            var kind = type switch
            {
                "in" or "opening" => "in", "out" => "out", "transfer" => "transfer",
                "adjustment" or "count" => "adjust", "reverse" => "adjust", _ => type
            };
            var label = string.IsNullOrEmpty(branch) ? typeText : $"{typeText} · {branch}";
            list.Add(new MaterialMovementRow(r.GetInt64(0), kind, dir * qty, label,
                string.IsNullOrEmpty(doc) ? null : doc));
        }
        return list;
    }

    // ================= çekirdek =================

    private StockDocResult RunDocument(SessionContext s, string docType, string operationId,
        string? toBranch, string? fromBranch, string? primaryBranch, string? personnelId, string? vehicleId,
        string? note, long? docDate, Action<DbConnection, DbTransaction, string> body, string? groupId = null,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var date = docDate ?? now;

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate(); // IMMEDIATE → eş zamanlı çıkış serialize

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

    private void ApplyLine(DbConnection conn, DbTransaction tx, SessionContext s, string docId,
        StockLine line, int direction, string operationId, string movementType, string? branchId, string? branchFromId, string? groupId = null)
    {
        if (line.Quantity <= 0) throw new ArgumentException("Miktar pozitif olmalı.");
        EnsureMaterialOwned(conn, tx, s.CompanyId, line.MaterialId);

        // Per-branch stok (madde 8b, kullanıcı isteği 2026-08-05): çıkış/transfer-çıkışta O ŞUBENİN defter
        // bakiyesi yeterli mi? Firma-geneli kalkan (ApplyDelta) zaten var; bu ondan KATI — şubede stok yoksa
        // çıkışı/transferi engeller. Sayım/ters kayıt HARİÇ (gerçeği düzeltir). NULL şube (Tüm Şubeler/idari) →
        // firma-geneli kalkanla yetinilir. Şema DEĞİŞMEZ; şube bakiyesi hareket defterinden anlık hesaplanır.
        if (direction < 0 && !string.IsNullOrEmpty(branchId) && (movementType == "out" || movementType == "transfer"))
        {
            var branchBal = BranchBalance(conn, tx, s.CompanyId, line.MaterialId, branchId!);
            if (branchBal < line.Quantity)
                throw new NegativeStockException($"Bu şubede yeterli stok yok. Mevcut: {branchBal:0.##}, çıkış istenen: {line.Quantity:0.##}.");
        }

        ApplyDelta(conn, tx, s.CompanyId, line.MaterialId, direction * line.Quantity, _clock.UtcNow.ToUnixTimeMilliseconds(), allowNegative: false);
        InsertMovement(conn, tx, s.CompanyId, line.MaterialId, docId, movementType, direction, line.Quantity,
            line.UnitPrice, line.Currency, null, operationId, null, _clock.UtcNow.ToUnixTimeMilliseconds(), branchId, branchFromId, groupId, null, s.OperatingBranchId);
    }

    /// <summary>Bakiyeye işaretli miktarı uygular; düşüşte negatif olursa fail-closed.</summary>
    private static void ApplyDelta(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        decimal signedQty, long now, bool allowNegative)
    {
        var current = ReadBalance(conn, tx, materialId);
        var updated = current + signedQty;
        if (!allowNegative && updated < 0)
            throw new NegativeStockException($"Negatif stok engellendi: mevcut {current}, talep {-signedQty}.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_balances(company_id, material_id, quantity, updated_at) VALUES(@c,@m,@q,@now)
ON CONFLICT(material_id) DO UPDATE SET quantity=excluded.quantity, updated_at=excluded.updated_at;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@q", Money.Serialize(updated));
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static decimal ReadBalance(DbConnection conn, DbTransaction? tx, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    /// <summary>
    /// SUNUCU-OTORİTELİ bakiye (Senkron 2b): firmanın TÜM stok bakiyelerini hareket defterinden yeniden hesaplar.
    /// balance(malzeme) = Σ(direction × quantity) tüm hareketler (ters hareket ayrı satır olarak toplama girer).
    /// Money ile decimal-kesin (quantity TEXT). Çok makineli senkronda push sonrası çağrılır → makinelerin
    /// birleşik hareketlerinden DOĞRU tek bakiye üretir (istemci snapshot'ı birbirini ezmez).
    /// </summary>
    public void RecomputeBalances(string companyId)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT material_id, direction, quantity FROM stock_movements WHERE company_id=@c;";
            read.AddWithValue("@c", companyId);
            using var r = read.ExecuteReader();
            while (r.Read())
            {
                var mat = r.GetString(0);
                long dir = r.GetInt64(1);
                var qty = Money.Parse(r.IsDBNull(2) ? null : r.GetString(2));
                totals.TryGetValue(mat, out var cur);
                totals[mat] = cur + dir * qty;
            }
        }

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        foreach (var (mat, total) in totals)
        {
            using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = "INSERT INTO stock_balances(company_id, material_id, quantity, updated_at) VALUES(@c,@m,@q,@now) " +
                "ON CONFLICT(material_id) DO UPDATE SET quantity=excluded.quantity, updated_at=excluded.updated_at;";
            up.AddWithValue("@c", companyId);
            up.AddWithValue("@m", mat);
            up.AddWithValue("@q", Money.Serialize(total));
            up.AddWithValue("@now", now);
            up.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static string InsertMovement(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        string documentId, string movementType, int direction, decimal quantity, decimal? unitPrice, string? currency,
        decimal? fxRate, string operationId, string? note, long now, string? branchId, string? branchFromId, string? groupId, string? reversesId,
        string? opBranchId = null)
    {
        var id = Guid.NewGuid().ToString("N");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, branch_from_id, movement_type, direction,
    quantity, unit_price, currency_code, fx_rate, operation_id, note, created_at, document_id, is_reversed, reverses_movement_id, op_branch_id)
VALUES(@id,@c,@m,@b,@bf,@type,@dir,@q,@price,@cur,@fx,@op,@note,@now,@doc,0,@rev,@opb);";
        cmd.AddWithValue("@opb", (object?)opBranchId ?? DBNull.Value);
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.AddWithValue("@bf", (object?)branchFromId ?? DBNull.Value);
        cmd.AddWithValue("@type", movementType);
        cmd.AddWithValue("@dir", direction);
        cmd.AddWithValue("@q", Money.Serialize(quantity));
        cmd.AddWithValue("@price", unitPrice is null ? DBNull.Value : Money.Serialize(unitPrice.Value));
        cmd.AddWithValue("@cur", (object?)currency ?? DBNull.Value);
        cmd.AddWithValue("@fx", fxRate is null ? DBNull.Value : Money.Serialize(fxRate.Value));
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@doc", documentId);
        cmd.AddWithValue("@rev", (object?)reversesId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
        return id;
    }

    private static void InsertDocument(DbConnection conn, DbTransaction tx, string id, string companyId,
        string docType, string docNo, long docDate, string? fromBranch, string? toBranch, string? personnelId,
        string? vehicleId, string? note, string? groupId, long now,
        string? invoiceNo = null, string? orderSlipNo = null, string? creditSlipNo = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_documents(id, company_id, doc_type, doc_no, doc_date, from_branch_id, to_branch_id,
    personnel_id, vehicle_id, note, status, group_id, invoice_no, order_slip_no, credit_slip_no, created_at, version, is_deleted)
VALUES(@id,@c,@type,@no,@date,@from,@to,@pers,@veh,@note,'active',@grp,@inv,@ord,@crd,@now,1,0);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@type", docType);
        cmd.AddWithValue("@no", docNo);
        cmd.AddWithValue("@date", docDate);
        cmd.AddWithValue("@from", (object?)fromBranch ?? DBNull.Value);
        cmd.AddWithValue("@to", (object?)toBranch ?? DBNull.Value);
        cmd.AddWithValue("@pers", (object?)personnelId ?? DBNull.Value);
        cmd.AddWithValue("@veh", (object?)vehicleId ?? DBNull.Value);
        cmd.AddWithValue("@note", (object?)note ?? DBNull.Value);
        cmd.AddWithValue("@grp", (object?)groupId ?? DBNull.Value);
        cmd.AddWithValue("@inv", (object?)invoiceNo ?? DBNull.Value);
        cmd.AddWithValue("@ord", (object?)orderSlipNo ?? DBNull.Value);
        cmd.AddWithValue("@crd", (object?)creditSlipNo ?? DBNull.Value);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertCountLine(DbConnection conn, DbTransaction tx, string docId, string materialId,
        decimal system, decimal counted, decimal diff, string reason)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_count_lines(id, document_id, material_id, system_qty, counted_qty, diff_qty, reason)
VALUES(@id,@doc,@m,@s,@c,@d,@r);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@doc", docId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@s", Money.Serialize(system));
        cmd.AddWithValue("@c", Money.Serialize(counted));
        cmd.AddWithValue("@d", Money.Serialize(diff));
        cmd.AddWithValue("@r", reason);
        cmd.ExecuteNonQuery();
    }

    private static string NextDocNo(DbConnection conn, DbTransaction tx, string companyId, string docType, long docDateMs)
    {
        var year = DateTimeOffset.FromUnixTimeMilliseconds(docDateMs).Year;
        var prefix = docType switch { "in" => "GIR", "out" => "CIK", "transfer" => "TRF", "count" => "SAY", _ => "DOC" };
        var like = $"{prefix}-{year}-%";
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COALESCE(MAX(CAST(substr(doc_no, length(@p)+1) AS INTEGER)),0) FROM stock_documents " +
            "WHERE company_id=@c AND doc_type=@t AND doc_no LIKE @like;";
        cmd.AddWithValue("@p", $"{prefix}-{year}-");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@t", docType);
        cmd.AddWithValue("@like", like);
        var next = Convert.ToInt64(cmd.ExecuteScalar()) + 1;
        return $"{prefix}-{year}-{next:0000}";
    }

    private static StockDocResult? FindDocumentByOperation(DbConnection conn, DbTransaction tx, string baseOperationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT d.id, d.doc_no FROM stock_movements mv JOIN stock_documents d ON d.id = mv.document_id " +
            "WHERE mv.operation_id LIKE @op LIMIT 1;";
        cmd.AddWithValue("@op", baseOperationId + ":%");
        using var r = cmd.ExecuteReader();
        return r.Read() ? new StockDocResult(r.GetString(0), r.GetString(1)) : null;
    }

    private sealed record MovementRow(string Id, string MaterialId, int Direction, decimal Quantity,
        string OperationId, string? BranchId, string? BranchFromId, string? GroupId);

    private static IReadOnlyList<MovementRow> ActiveMovements(DbConnection conn, DbTransaction tx, string documentId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, material_id, direction, quantity, operation_id, branch_id, branch_from_id, " +
            "(SELECT group_id FROM stock_documents d WHERE d.id=@doc) FROM stock_movements " +
            "WHERE document_id=@doc AND is_reversed=0;";
        cmd.AddWithValue("@doc", documentId);
        var list = new List<MovementRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MovementRow(r.GetString(0), r.GetString(1), r.GetInt32(2), Money.Parse(r.GetString(3)),
                r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        return list;
    }

    private sealed record DocRow(string Id, string Status);

    private static DocRow? LoadDocument(DbConnection conn, DbTransaction tx, string companyId, string documentId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, status FROM stock_documents WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", documentId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DocRow(r.GetString(0), r.GetString(1)) : null;
    }

    private static void MarkReversed(DbConnection conn, DbTransaction tx, string movementId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE stock_movements SET is_reversed=1 WHERE id=@id;";
        cmd.AddWithValue("@id", movementId);
        cmd.ExecuteNonQuery();
    }

    private static void SetDocumentStatus(DbConnection conn, DbTransaction tx, string documentId, string status, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE stock_documents SET status=@s, version=version+1 WHERE id=@id;";
        cmd.AddWithValue("@s", status);
        cmd.AddWithValue("@id", documentId);
        cmd.ExecuteNonQuery();
    }

    private static void EnsureMaterialOwned(DbConnection conn, DbTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", materialId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }
}
