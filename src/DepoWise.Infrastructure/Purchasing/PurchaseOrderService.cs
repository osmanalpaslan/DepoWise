using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Infrastructure.Purchasing;

/// <summary>Sipariş başlığı liste satırı.</summary>
public sealed record PurchaseOrderRow(string Id, string OrderNo, string? SupplierId, string? SupplierName,
    string? RequestId, string? RequestNo, string? BranchId, string? BranchName,
    string? CostCenterId, string? CostCenterName, string Status, long OrderDate, string? Note,
    decimal TotalAmount, string TotalCurrency, long Version)
{
    public string StatusDisplay => PurchaseOrderService.StatusLabel(Status);
    public string SupplierDisplay => string.IsNullOrEmpty(SupplierName) ? "—" : SupplierName!;
    public string BranchDisplay => string.IsNullOrEmpty(BranchName) ? "—" : BranchName!;
    public string RequestDisplay => string.IsNullOrEmpty(RequestNo) ? "—" : RequestNo!;
    public string CostCenterDisplay => string.IsNullOrEmpty(CostCenterName) ? "—" : CostCenterName!;
    public string OrderDateDisplay => DateTimeOffset.FromUnixTimeMilliseconds(OrderDate).UtcDateTime.ToString("dd.MM.yyyy");
    public string TotalDisplay => TotalAmount == 0 ? "—" : TotalAmount.ToString("N2") + " " + TotalCurrency;
}

/// <summary>Sipariş satırı.</summary>
public sealed record PurchaseOrderLineRow(string Id, string OrderId, string MaterialId, string MaterialName,
    decimal Quantity, decimal? UnitPrice, string Currency, decimal ReceivedQty, string? Note)
{
    public decimal RemainingQty => Quantity - ReceivedQty;
    public string QuantityDisplay => Quantity.ToString("0.####");
    public string ReceivedDisplay => ReceivedQty.ToString("0.####");
    public string RemainingDisplay => RemainingQty.ToString("0.####");
    public string PriceDisplay => UnitPrice is { } p ? p.ToString("N2") + " " + Currency : "—";
}

public sealed record NewPurchaseOrderLine(string MaterialId, decimal Quantity, decimal? UnitPrice = null,
    string? Currency = null, string? Note = null);

public sealed record NewPurchaseOrder(string OrderNo, string? SupplierId = null, string? RequestId = null,
    string? BranchId = null, string? CostCenterId = null, long? OrderDate = null, string? Note = null,
    IReadOnlyList<NewPurchaseOrderLine>? Lines = null);

/// <summary>Mal kabul satırı: sipariş satırı + kabul edilen miktar.</summary>
public sealed record ReceiveLine(string LineId, decimal Quantity);

/// <summary>
/// ═══ STN-01 (ADR-169, 2026-08-28) — SATIN ALMA / SİPARİŞ ═══
///
/// TALEP → SİPARİŞ → MAL KABUL → STOK zincirinin sipariş halkası. Talep zincirinin durum makinesi ve
/// yetkileri DEĞİŞTİRİLMEDİ; sipariş, talep operasyonlarına OPSİYONEL bağla oturur.
///
/// <b>MAL KABUL:</b> MEVCUT <see cref="StockService.ReceiveInTx"/> çağrılır (ikinci stok mekanizması
/// YOK; negatif-stok/idempotency/TRH-01 kuralları aynen). Kabul + <c>received_qty</c> artışı +
/// (varsa) maliyet merkezi bağı AYNI transaction'dadır. <b>İDEMPOTENT:</b> aynı operationId ikinci kez
/// gönderilirse stok DEFTERİNDEN tespit edilir ve hiçbir şey ikinci kez uygulanmaz.
///
/// <b>YETKİ:</b> yeni <c>purchasing</c> modülü (ekran/CRUD kapısı — <c>request_ops_purchase</c>
/// DEĞİŞTİRİLMEDİ: o, talep durum-geçiş birim yetkisidir ve öyle kalır). Mal kabulde stok kapısı
/// (stock.Create) DA çalışır — satın alma stok yetkisinin yan kapısı değildir (zimmet/fatura emsali).
/// <b>KAPSAM:</b> teslim şubesi üzerinden <see cref="BranchAccess"/>. Tenant: her sorgu company_id.
/// <b>SİLME:</b> sipariş iptali = status 'cancelled' + soft delete YOK (liste 'İptal' gösterir);
/// kabul edilmiş satırların stok hareketi DEFTERDE kalır (mevcut ters-kayıt yolu geçerli).
/// </summary>
public sealed class PurchaseOrderService
{
    public const string Module = "purchasing";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly StockService _stock;
    private readonly CostCenterService _costCenters;

    public PurchaseOrderService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _stock = new StockService(factory, _clock);
        _costCenters = new CostCenterService(factory, _clock);
    }

    public static string StatusLabel(string status) => status switch
    {
        "closed" => "Tamamlandı",
        "cancelled" => "İptal",
        _ => "Açık",
    };

    // ══════════════ LİSTE ══════════════

    public IReadOnlyList<PurchaseOrderRow> List(SessionContext s, string? search = null, string? status = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var list = new List<PurchaseOrderRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT o.id, o.order_no, o.supplier_id, sup.name, o.request_id, req.doc_no, o.branch_id, b.name,
       o.cost_center_id, cc.name, o.status, o.order_date, o.note, o.version
FROM purchase_orders o
LEFT JOIN suppliers sup ON sup.id = o.supplier_id
LEFT JOIN material_requests req ON req.id = o.request_id
LEFT JOIN branches b ON b.id = o.branch_id
LEFT JOIN cost_centers cc ON cc.id = o.cost_center_id
WHERE o.company_id=@c AND o.is_deleted=0" +
                (string.IsNullOrWhiteSpace(status) ? "" : " AND o.status=@st") +
                " ORDER BY o.order_date DESC, o.order_no DESC;";
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(status)) cmd.AddWithValue("@st", status);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new PurchaseOrderRow(r.GetString(0), r.GetString(1),
                    N(r, 2), N(r, 3), N(r, 4), N(r, 5), N(r, 6), N(r, 7), N(r, 8), N(r, 9),
                    r.GetString(10), r.GetInt64(11), N(r, 12), 0m, "TRY", r.GetInt64(13)));
        }

        // Toplam tutar: satırlardan C# decimal (Money kuralı — SQL SUM yok). Tek sorgu, bellek içi eşleme.
        var toplamlar = new Dictionary<string, (decimal Amt, string Cur)>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT order_id, quantity, unit_price, COALESCE(currency_code,'TRY') " +
                              "FROM purchase_order_lines WHERE company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(2)) continue;
                var amt = D(r.GetString(1)) * D(r.GetString(2));
                var cur = r.GetString(3);
                var key = r.GetString(0);
                toplamlar[key] = toplamlar.TryGetValue(key, out var v)
                    ? (v.Cur == cur ? (v.Amt + amt, cur) : (v.Amt, v.Cur))   // farklı birim karıştırılmaz; ilk birim gösterilir
                    : (amt, cur);
            }
        }
        list = list.Select(o => toplamlar.TryGetValue(o.Id, out var t) ? o with { TotalAmount = t.Amt, TotalCurrency = t.Cur } : o).ToList();

        // ŞUBE KAPSAMI: teslim şubesi kapsam dışıysa sipariş görünmez; şubesiz sipariş gizlenmez.
        var izinli = BranchAccess.Allowed(s);
        if (izinli is not null)
        {
            var set = izinli.ToHashSet(StringComparer.Ordinal);
            list = list.Where(o => o.BranchId is null || set.Contains(o.BranchId)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(o =>
                o.OrderNo.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (o.SupplierName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (o.RequestNo?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (o.BranchName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (o.Note?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return list;
    }

    public IReadOnlyList<PurchaseOrderLineRow> Lines(SessionContext s, string orderId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        EnsureOrderVisible(s, conn, null, orderId);
        var list = new List<PurchaseOrderLineRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT l.id, l.order_id, l.material_id, m.name, l.quantity, l.unit_price, " +
                          "COALESCE(l.currency_code,'TRY'), l.received_qty, l.note " +
                          "FROM purchase_order_lines l JOIN materials m ON m.id = l.material_id " +
                          "WHERE l.company_id=@c AND l.order_id=@o AND l.is_deleted=0 ORDER BY m.name;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@o", orderId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PurchaseOrderLineRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                D(r.GetString(4)), r.IsDBNull(5) ? null : D(r.GetString(5)), r.GetString(6),
                D(r.GetString(7)), N(r, 8)));
        return list;
    }

    // ══════════════ OLUŞTUR / DÜZENLE / İPTAL ══════════════

    public string Create(SessionContext s, NewPurchaseOrder dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.OrderNo)) throw new ArgumentException("Sipariş no zorunlu.");
        if (dto.Lines is not { Count: > 0 }) throw new ArgumentException("En az bir sipariş satırı girin.");
        foreach (var l in dto.Lines)
            if (l.Quantity <= 0) throw new ArgumentException("Satır miktarı pozitif olmalı.");
        var isGunu = DateEntryPolicy.Uygula(s, dto.OrderDate) ?? _clock.UtcNow.ToUnixTimeMilliseconds();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureRefs(s, conn, tx, dto);
        EnsureOrderNoFree(conn, tx, s.CompanyId, dto.OrderNo, excludeId: null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO purchase_orders(id, company_id, order_no, supplier_id, request_id, branch_id, cost_center_id,
    status, order_date, note, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@no,@sup,@req,@b,@cc,'open',@dt,@n,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@no", dto.OrderNo.Trim());
            cmd.AddWithValue("@sup", Nv(dto.SupplierId));
            cmd.AddWithValue("@req", Nv(dto.RequestId));
            cmd.AddWithValue("@b", Nv(dto.BranchId));
            cmd.AddWithValue("@cc", Nv(dto.CostCenterId));
            cmd.AddWithValue("@dt", isGunu);
            cmd.AddWithValue("@n", Nv(dto.Note));
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        foreach (var l in dto.Lines)
        {
            EnsureMaterialOwned(conn, tx, s.CompanyId, l.MaterialId);
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO purchase_order_lines(id, order_id, company_id, material_id, quantity, unit_price, currency_code,
    received_qty, note, created_at, updated_at, version, is_deleted)
VALUES(@id,@o,@c,@m,@q,@p,@cur,'0',@n,@now,@now,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@o", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@m", l.MaterialId);
            cmd.AddWithValue("@q", S(l.Quantity));
            cmd.AddWithValue("@p", l.UnitPrice is { } p ? S(p) : DBNull.Value);
            cmd.AddWithValue("@cur", Nv(l.Currency));
            cmd.AddWithValue("@n", Nv(l.Note));
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "purchase_order", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"orderNo\":\"{dto.OrderNo.Trim()}\",\"lines\":{dto.Lines.Count}}}"), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Başlık META düzenleme (no/tedarikçi/talep/depo/merkez/tarih/not). Satır düzenleme İLK
    /// sürümde YOK — yanlış satır için sipariş iptal edilip yeniden açılır (kabul başladıysa satırlar
    /// artık stok defterine bağlıdır; sessiz değişim izlenebilirliği bozardı).</summary>
    public void UpdateMeta(SessionContext s, string id, NewPurchaseOrder dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(dto.OrderNo)) throw new ArgumentException("Sipariş no zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var (durum, _) = EnsureOrderVisible(s, conn, tx, id, expectedVersion);
        if (durum == "cancelled") throw new ArgumentException("İptal edilmiş sipariş düzenlenemez.");
        EnsureRefs(s, conn, tx, dto);
        EnsureOrderNoFree(conn, tx, s.CompanyId, dto.OrderNo, excludeId: id);
        var isGunu = DateEntryPolicy.Uygula(s, dto.OrderDate);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE purchase_orders SET order_no=@no, supplier_id=@sup, request_id=@req, " +
                "branch_id=@b, cost_center_id=@cc" + (isGunu is null ? "" : ", order_date=@dt") + ", note=@n, " +
                "updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@no", dto.OrderNo.Trim());
            cmd.AddWithValue("@sup", Nv(dto.SupplierId));
            cmd.AddWithValue("@req", Nv(dto.RequestId));
            cmd.AddWithValue("@b", Nv(dto.BranchId));
            cmd.AddWithValue("@cc", Nv(dto.CostCenterId));
            if (isGunu is not null) cmd.AddWithValue("@dt", isGunu.Value);
            cmd.AddWithValue("@n", Nv(dto.Note));
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "purchase_order", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Sipariş iptali: status='cancelled' (satır/kabul geçmişi DEFTERDE aynen kalır).</summary>
    public void Cancel(SessionContext s, string id, string? reason = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var (durum, _) = EnsureOrderVisible(s, conn, tx, id);
        if (durum == "cancelled") { tx.Commit(); return; }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE purchase_orders SET status='cancelled', updated_at=@now, version=version+1 " +
                              "WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "purchase_order", id, AuditActions.Reverse, s.UserId,
            AfterJson: string.IsNullOrWhiteSpace(reason) ? null : $"{{\"reason\":\"{reason!.Trim()}\"}}"), _clock);
        tx.Commit();
    }

    // ══════════════ MAL KABUL ══════════════

    /// <summary>
    /// MAL KABUL — kısmi/tam. TEK transaction'da: MEVCUT stok girişi (ReceiveInTx; yetki+idempotency+
    /// TRH-01 aynen) + satır received_qty artışı + (siparişte merkez seçiliyse) stok belgesine D dış-bağı +
    /// tüm satırlar tamamlandıysa sipariş 'closed'.
    /// <b>İDEMPOTENT:</b> aynı operationId ikinci kez gelirse stok DEFTERİNDEN tespit edilir ve
    /// İKİNCİ stok hareketi de İKİNCİ received_qty artışı da OLMAZ.
    /// </summary>
    public string Receive(SessionContext s, string orderId, IReadOnlyList<ReceiveLine> lines,
        string operationId, long? docDate = null, string? note = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (lines is not { Count: > 0 }) throw new ArgumentException("Kabul edilecek satır seçin.");
        foreach (var l in lines)
            if (l.Quantity <= 0) throw new ArgumentException("Kabul miktarı pozitif olmalı.");
        var stockOp = "po:" + operationId;
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var (durum, branchId) = EnsureOrderVisible(s, conn, tx, orderId);
        if (durum == "cancelled") throw new ArgumentException("İptal edilmiş siparişe mal kabul yapılamaz.");
        if (string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Mal kabul için siparişte teslim deposu (şube) seçili olmalı.");

        // İDEMPOTENCY — stok defterinden (kalıcı gerçek): bu operationId daha önce uygulandıysa hiçbir şey yapma.
        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            // ⭐ FIN-B1 (ADR-185, Migration082 ile birlikte): FİRMA KAPSAMLI — başka firmanın aynı
            // operation_id'si bu firmanın mal kabulünü sessizce atlatamaz. Aynı-firma retry aynen.
            chk.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id=@c AND operation_id LIKE @op;";
            chk.AddWithValue("@c", s.CompanyId);
            chk.AddWithValue("@op", stockOp + "%");
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0) { tx.Commit(); return stockOp; }
        }

        // Satırları doğrula (siparişe ait + kalan miktar aşılmıyor) ve stok satırlarını hazırla.
        // received_qty yeni değerleri C# DECIMAL ile hesaplanır (REAL/float YOK — miktar hassasiyeti korunur).
        var stockLines = new List<StockLine>();
        var yeniReceived = new Dictionary<string, decimal>(StringComparer.Ordinal);
        string? costCenterId = null;
        using (var cc = conn.CreateCommand())
        {
            cc.Transaction = tx;
            cc.CommandText = "SELECT cost_center_id FROM purchase_orders WHERE id=@id;";
            cc.AddWithValue("@id", orderId);
            costCenterId = cc.ExecuteScalar() as string;
        }
        foreach (var l in lines)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT material_id, quantity, received_qty, unit_price FROM purchase_order_lines " +
                              "WHERE id=@id AND order_id=@o AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", l.LineId);
            cmd.AddWithValue("@o", orderId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ArgumentException("Sipariş satırı bulunamadı.");
            var mevcutReceived = D(r.GetString(2));
            var kalan = D(r.GetString(1)) - mevcutReceived;
            if (l.Quantity > kalan)
                throw new ArgumentException($"Kabul miktarı kalan miktarı aşıyor (kalan: {kalan:0.####}).");
            yeniReceived[l.LineId] = mevcutReceived + l.Quantity;
            stockLines.Add(new StockLine(r.GetString(0), l.Quantity, r.IsDBNull(3) ? null : D(r.GetString(3))));
        }

        // 1) MEVCUT stok girişi (aynı transaction; stok yetki kapısı + TRH-01 burada çalışır).
        var doc = _stock.ReceiveInTx(conn, tx, s, stockLines, stockOp, branchId,
            note: string.IsNullOrWhiteSpace(note) ? "Mal kabul" : note, docDate: docDate);

        // 2) received_qty artışı (aynı tx — defterle birlikte).
        foreach (var l in lines)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE purchase_order_lines SET " +
                "received_qty = @q, updated_at=@now, version=version+1 " +
                "WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@q", S(yeniReceived[l.LineId]));
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", l.LineId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }

        // 3) Tüm satırlar tamamlandıysa sipariş kapanır (otomatik — teknik sonuç, onay katmanı değil).
        // Kapanış kontrolü C# DECIMAL ile (REAL karşılaştırması yok).
        bool tamam = true;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT quantity, received_qty FROM purchase_order_lines WHERE order_id=@o AND is_deleted=0;";
            cmd.AddWithValue("@o", orderId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (D(r.GetString(1)) < D(r.GetString(0))) { tamam = false; break; }
        }
        if (tamam)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE purchase_orders SET status='closed', updated_at=@now, version=version+1 WHERE id=@id;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", orderId);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "purchase_order", orderId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"receive\":\"{stockOp}\",\"doc\":\"{doc.DocumentId}\"}}"), _clock);
        tx.Commit();

        // 4) Maliyet merkezi bağı — kabul BAŞARILI olduktan sonra, D'nin mevcut mekanizmasıyla
        //    (kendi transaction'ı; bilgilendirici — başarısızlığı kabulü geri almaz, MLY-01 kuralı).
        if (!string.IsNullOrWhiteSpace(costCenterId))
        {
            try { _costCenters.Link(s, "stock_document", doc.DocumentId, costCenterId); }
            catch { /* bağ sonradan Maliyet Merkezleri üzerinden kurulabilir */ }
        }
        return doc.DocumentId;
    }

    /// <summary>Excel (liste kuralı 2): filtrelenmiş TÜM sipariş listesi.</summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<PurchaseOrderRow> rows)
        => new("Satın Alma Siparişleri",
            new[] { "Sipariş No", "Tarih", "Tedarikçi", "Talep", "Depo", "Maliyet Merkezi", "Durum", "Toplam" },
            rows.Select(o => (IReadOnlyList<object?>)new object?[]
                { o.OrderNo, o.OrderDateDisplay, o.SupplierDisplay, o.RequestDisplay, o.BranchDisplay,
                  o.CostCenterDisplay, o.StatusDisplay, o.TotalDisplay }).ToList());

    // ══════════════ yardımcılar ══════════════

    /// <summary>Tenant + kapsam + (verilmişse) düzenleme kilidi. Dönüş: (status, branch_id).</summary>
    private static (string Status, string? BranchId) EnsureOrderVisible(SessionContext s, DbConnection conn,
        DbTransaction? tx, string id, long? expectedVersion = null)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT status, branch_id, version FROM purchase_orders WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ArgumentException("Sipariş bulunamadı.");
        var branchId = r.IsDBNull(1) ? null : r.GetString(1);
        if (branchId is not null) BranchAccess.Require(s, branchId, "satın alma");
        if (expectedVersion is { } ev && r.GetInt64(2) != ev) throw new ConcurrencyException(ev, r.GetInt64(2));
        return (r.GetString(0), branchId);
    }

    /// <summary>Başlık referansları bu firmanın olmalı; teslim şubesi kullanıcının kapsamında olmalı.</summary>
    private static void EnsureRefs(SessionContext s, DbConnection conn, DbTransaction tx, NewPurchaseOrder dto)
    {
        void Var(string? id, string tablo, string ad)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT COUNT(*) FROM {tablo} WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", id!);
            cmd.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                throw new ArgumentException($"{ad} bulunamadı veya bu firmaya ait değil.");
        }
        Var(dto.SupplierId, "suppliers", "Tedarikçi");
        Var(dto.RequestId, "material_requests", "Talep");
        Var(dto.BranchId, "branches", "Şube");
        Var(dto.CostCenterId, "cost_centers", "Maliyet merkezi");
        if (!string.IsNullOrWhiteSpace(dto.BranchId)) BranchAccess.Require(s, dto.BranchId, "satın alma");
    }

    private static void EnsureMaterialOwned(DbConnection conn, DbTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", materialId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ArgumentException("Malzeme bulunamadı veya bu firmaya ait değil.");
    }

    private static void EnsureOrderNoFree(DbConnection conn, DbTransaction tx, string companyId, string orderNo, string? excludeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM purchase_orders WHERE company_id=@c AND order_no=@no AND is_deleted=0" +
                          (excludeId is null ? ";" : " AND id<>@id;");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@no", orderNo.Trim());
        if (excludeId is not null) cmd.AddWithValue("@id", excludeId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
            throw new ArgumentException($"'{orderNo.Trim()}' sipariş numarası zaten kullanılıyor.");
    }

    private static string? N(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static object Nv(string? v) => string.IsNullOrWhiteSpace(v) ? DBNull.Value : v!.Trim();
    private static decimal D(string v) => decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
    private static string S(decimal v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
