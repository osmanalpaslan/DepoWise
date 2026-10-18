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

/// <summary>⭐ M-32: siparişin bir mal kabulü (stok giriş belgesi).</summary>
public sealed record PurchaseReceiptRow(string DocumentId, string DocNo, long DocDate, string? IrsaliyeNo, string Status,
    decimal ToplamMiktar, int KalemSayisi)
{
    public bool IptalMi => Status == "cancelled";
    public string TarihDisplay => DateTimeOffset.FromUnixTimeMilliseconds(DocDate).ToLocalTime().ToString("dd.MM.yyyy");
    public string Ozet => $"{DocNo} · {TarihDisplay}" + (string.IsNullOrWhiteSpace(IrsaliyeNo) ? "" : $" · İrsaliye {IrsaliyeNo}") +
                          $" · {KalemSayisi} kalem, {ToplamMiktar:0.####}" + (IptalMi ? " · GERİ ALINDI" : "");
}

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

    /// <summary>
    /// ⭐ ARA İŞ 5 / ALT FAZ 2 (ADR-187 / ADR-188 §4): OPSİYONEL onay zinciri motoru.
    /// <b>null olabilir</b> — bağlanmazsa bu servis BUGÜNKÜ davranışını birebir sürdürür.
    /// Bağlı olsa bile siparişi oluşturanın hiyerarşide üstü yoksa zincir OLUŞMAZ (İK-3 opsiyonellik).
    /// </summary>
    public DepoWise.Infrastructure.Approvals.ApprovalService? Approvals { get; set; }

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
        // ⭐ FAZ 3c-2: toplam da birim fiyattan TÜRER (miktar biliniyorken fiyat geri hesaplanabilir),
        // bu yüzden fiyat gizliyse toplam HİÇ hesaplanmaz → 0 kalır, ekranda "—" görünür.
        var toplamlar = new Dictionary<string, (decimal Amt, string Cur)>(StringComparer.Ordinal);
        if (MaterialService.FiyatGorunur(s))
        {
            using var cmd = conn.CreateCommand();
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
        // ⭐ FAZ 3c-2 — ALAN KAPISI (okuma). Sipariş satırı fiyatı, malzeme birim fiyatının bir
        // taşıyıcısıdır: malzeme kartında gizlenip burada açık kalsaydı koruma delinirdi.
        // Karar SORGU BAŞINA BİR KEZ (satır döngüsünün dışında) — ADR-223 performans sözleşmesi.
        var fiyatGorunur = MaterialService.FiyatGorunur(s);
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
                D(r.GetString(4)), !fiyatGorunur || r.IsDBNull(5) ? null : D(r.GetString(5)), r.GetString(6),
                D(r.GetString(7)), N(r, 8)));
        return list;
    }

    // ══════════════ OLUŞTUR / DÜZENLE / İPTAL ══════════════

    public string Create(SessionContext s, NewPurchaseOrder dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.Lines is not { Count: > 0 }) throw new ArgumentException("En az bir sipariş satırı girin.");
        foreach (var l in dto.Lines)
            if (l.Quantity <= 0) throw new ArgumentException("Satır miktarı pozitif olmalı.");
        // ⭐ FAZ 3c-2 — YAZMA KAPISI (kanonik kural): fiyatı GÖREMEYEN kullanıcının gönderdiği fiyat
        // yazılmaz. Sipariş satırı YENİ kayıttır → korunacak eski değer yoktur; kolon NULL kabul eder
        // ("fiyat belirtilmedi" zaten geçerli bir durumdur), bu yüzden 403 değil NULL yazılır.
        var fiyatYazilabilir = MaterialService.FiyatGorunur(s);
        var isGunu = DateEntryPolicy.Uygula(s, dto.OrderDate) ?? _clock.UtcNow.ToUnixTimeMilliseconds();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        // Numara üretilecekse IMMEDIATE: eşzamanlı iki kayıt aynı sıradaki numarayı almasın.
        using var tx = string.IsNullOrWhiteSpace(dto.OrderNo) ? conn.BeginImmediate() : conn.BeginTransaction();
        // ⭐ M-29 (AR-GE 2026-10-18, F-073): sipariş no boşsa otomatik — SIP-2026-A3-0042 (bilgisayar kodlu, çevrimdışı
        // çakışmaz; masaüstü dışında SIP-2026-0042). Talep/fatura numaralarıyla aynı yardımcı (BelgeNumarasi).
        if (string.IsNullOrWhiteSpace(dto.OrderNo))
            dto = dto with { OrderNo = DepoWise.Infrastructure.Database.BelgeNumarasi.Sonraki(conn, tx, "purchase_orders", "order_no",
                s.CompanyId, $"SIP-{DateTimeOffset.FromUnixTimeMilliseconds(isGunu).Year}-", s.MachineCode) };
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
            cmd.AddWithValue("@p", fiyatYazilabilir && l.UnitPrice is { } p ? S(p) : (object)DBNull.Value);
            cmd.AddWithValue("@cur", Nv(l.Currency));
            cmd.AddWithValue("@n", Nv(l.Note));
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "purchase_order", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"orderNo\":\"{dto.OrderNo.Trim()}\",\"lines\":{dto.Lines.Count}}}"), _clock);
        // ⭐ ARA İŞ 5 / ALT FAZ 2: onay zinciri SUNUCU tarafında, siparişi oluşturanın hiyerarşisinden
        // kurulur (ADR-188 §3: istemciden onaycı ALINMAZ). Üstü yoksa zincir oluşmaz → bugünkü akış.
        // `purchase_orders.status` DEĞİŞMEZ (ADR-188 §2) — onay durumu approval_instance'tadır.
        if (Approvals is not null && DbIntrospect.TableExists(conn, tx, "approval_instance"))
            Approvals.Start(conn, tx, s, DepoWise.Application.Approvals.ApprovalEntityTypes.PurchaseOrder,
                id, s.UserId, now);
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
        string operationId, long? docDate = null, string? note = null, string? irsaliyeNo = null)
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
        EnsureApprovedForReceive(conn, tx, s, orderId);   // ⭐ ADR-188 §1: onaysız mal kabul YOK
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
        // ⭐ FAZ 3c-2: fiyat SİPARİŞ SATIRINDAN okundu (kullanıcı girdisi değil) → alan kapısı
        // uygulanmaz; aksi hâlde fiyatı göremeyen depo görevlisinin mal kabulü, siparişte yazılı
        // fiyatı hareketten silerdi (sessiz veri kaybı).
        var doc = _stock.ReceiveInTx(conn, tx, s, stockLines, stockOp, branchId,
            note: string.IsNullOrWhiteSpace(note) ? "Mal kabul" : note, docDate: docDate,
            invoiceNo: string.IsNullOrWhiteSpace(irsaliyeNo) ? null : irsaliyeNo.Trim(),   // ⭐ M-32: belgenin "Fatura/İrsaliye No" alanı
            fiyatSunucuKaynakli: true);
        // ⭐ M-32: belge siparişe bağlanır (geri alma ve "bu kabul hangi siparişten" sorusu için; Migration105).
        if (DbIntrospect.ColumnExists(conn, tx, "stock_documents", "purchase_order_id"))
            using (var bag = conn.CreateCommand())
            {
                bag.Transaction = tx;
                bag.CommandText = "UPDATE stock_documents SET purchase_order_id=@o WHERE id=@d AND company_id=@c;";
                bag.AddWithValue("@o", orderId);
                bag.AddWithValue("@d", doc.DocumentId);
                bag.AddWithValue("@c", s.CompanyId);
                bag.ExecuteNonQuery();
            }

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

    /// <summary>⭐ M-32 (AR-GE 2026-10-18): siparişin mal kabulleri (en yeni önce), iptal edilenler dahil.</summary>
    public IReadOnlyList<PurchaseReceiptRow> Receipts(SessionContext s, string orderId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        EnsureOrderVisible(s, conn, null, orderId);
        if (!DbIntrospect.ColumnExists(conn, null, "stock_documents", "purchase_order_id")) return Array.Empty<PurchaseReceiptRow>();
        var belgeler = new List<(string Id, string No, long Tarih, string? Irs, string Durum)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, doc_no, doc_date, invoice_no, COALESCE(status,'active') FROM stock_documents " +
                              "WHERE company_id=@c AND purchase_order_id=@o AND doc_type='in' ORDER BY doc_date DESC, created_at DESC;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@o", orderId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) belgeler.Add((r.GetString(0), r.GetString(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4)));
        }
        var sonuc = new List<PurchaseReceiptRow>();
        foreach (var b in belgeler)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT quantity FROM stock_movements WHERE document_id=@d AND company_id=@c AND movement_type='in';";
            cmd.AddWithValue("@d", b.Id);
            cmd.AddWithValue("@c", s.CompanyId);
            decimal toplam = 0; int adet = 0;
            using (var r = cmd.ExecuteReader()) while (r.Read()) { toplam += D(r.GetString(0)); adet++; }
            sonuc.Add(new PurchaseReceiptRow(b.Id, b.No, b.Tarih, b.Irs, b.Durum, toplam, adet));
        }
        return sonuc;
    }

    /// <summary>
    /// ⭐ M-32 (AR-GE 2026-10-18, F-113) — MAL KABULÜNÜ GERİ AL (düzeltme izi). TEK işlemde: stok belgesi ters kayıtla iptal
    /// (defter silinmez; eksi stok kalkanı geçerli — mal kullanıldıysa geri alınamaz), siparişin "teslim alınan" miktarı aynı
    /// kadar düşer, tamamlanmış sipariş yeniden açılır, gerekçe işlem geçmişine yazılır. Belge bu siparişe bağlı değilse reddedilir.
    /// Tekrar çağrı zararsız (zaten iptalse hiçbir şey yapmaz). Yetki: sipariş Edit + stok iptal (stok ekranındaki iptalle aynı).
    /// </summary>
    public void ReverseReceipt(SessionContext s, string orderId, string documentId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Geri alma gerekçesi zorunlu.");
        DepoWise.Infrastructure.Materials.StockBalanceWriter.Run(() => ReverseReceiptOnce(s, orderId, documentId, reason.Trim()),
            $"po-reverse:{documentId}");
    }

    private void ReverseReceiptOnce(SessionContext s, string orderId, string documentId, string reason)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var (durum, _) = EnsureOrderVisible(s, conn, tx, orderId);
        if (StockService.SiparisBagi(conn, tx, s.CompanyId, documentId) != orderId)
            throw new ForbiddenException("Bu stok belgesi bu siparişin mal kabulü değil.");

        // Geri alınacak miktarlar (iptal edilmemiş giriş hareketleri) — ters kayıttan ÖNCE okunur.
        var miktarlar = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT material_id, quantity FROM stock_movements WHERE document_id=@d AND company_id=@c " +
                              "AND movement_type='in' AND is_reversed=0;";
            cmd.AddWithValue("@d", documentId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) miktarlar[r.GetString(0)] = miktarlar.GetValueOrDefault(r.GetString(0)) + D(r.GetString(1));
        }
        if (!_stock.ReverseDocumentInTx(conn, tx, s, documentId, reason)) { tx.Commit(); return; }   // zaten geri alınmış

        // Teslim alınan miktar düşer: aynı malzemeli satırlarda, önce en çok teslim alınandan.
        foreach (var (malzeme, kalanIade) in miktarlar)
        {
            var iade = kalanIade;
            var satirlar = new List<(string Id, decimal Received)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT id, received_qty FROM purchase_order_lines WHERE order_id=@o AND company_id=@c AND material_id=@m AND is_deleted=0;";
                cmd.AddWithValue("@o", orderId);
                cmd.AddWithValue("@c", s.CompanyId);
                cmd.AddWithValue("@m", malzeme);
                using var r = cmd.ExecuteReader();
                while (r.Read()) satirlar.Add((r.GetString(0), D(r.GetString(1))));
            }
            foreach (var (id, received) in satirlar.OrderByDescending(x => x.Received))
            {
                if (iade <= 0) break;
                var dus = Math.Min(iade, received);
                if (dus <= 0) continue;
                using var u = conn.CreateCommand();
                u.Transaction = tx;
                u.CommandText = "UPDATE purchase_order_lines SET received_qty=@q, updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
                u.AddWithValue("@q", S(received - dus));
                u.AddWithValue("@now", now);
                u.AddWithValue("@id", id);
                u.AddWithValue("@c", s.CompanyId);
                u.ExecuteNonQuery();
                iade -= dus;
            }
            if (iade > 0) throw new InvalidOperationException("Sipariş satırlarının teslim alınan miktarı bu kabulden az; geri alma yapılamadı.");
        }
        if (durum == "closed")
            using (var u = conn.CreateCommand())
            {
                u.Transaction = tx;
                u.CommandText = "UPDATE purchase_orders SET status='open', updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
                u.AddWithValue("@now", now);
                u.AddWithValue("@id", orderId);
                u.AddWithValue("@c", s.CompanyId);
                u.ExecuteNonQuery();
            }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "purchase_order", orderId, AuditActions.Reverse, s.UserId,
            AfterJson: $"{{\"receiveReversed\":\"{documentId}\",\"reason\":\"{reason.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}"), _clock);
        tx.Commit();
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
    /// <summary>
    /// ═══ ADR-188 §1 — ONAYSIZ MAL KABUL ENGELİ ═══
    ///
    /// Sipariş için bir onay zinciri BAŞLATILMIŞSA, süreç <c>approved</c> olmadan mal kabul yapılamaz.
    /// Zincir hiç başlatılmamışsa (hiyerarşi tanımsız → İK-3 opsiyonellik) bugünkü akış BİREBİR sürer.
    ///
    /// <b>Kapı neden BURADA:</b> UI'da butonu gizlemek yetersizdir (ADR-188 §1). Bu kontrol
    /// <see cref="Receive"/>'ın <c>BeginImmediate</c> transaction'ı İÇİNDE, stok hareketinden ÖNCE
    /// çalışır → onay ile mal kabul arasında yarış oluşamaz. Eski istemcinin sunucuya gönderdiği
    /// mal kabul isteği de aynı sunucu servisinden geçtiği için bu kapıyı BYPASS EDEMEZ.
    ///
    /// <b>Şema toleransı:</b> Migration085 uygulanmamış bir veritabanında tablo yoktur → kapı sessizce
    /// geçer; eski şema bozulmaz.
    /// </summary>
    private static void EnsureApprovedForReceive(DbConnection conn, DbTransaction tx, SessionContext s, string orderId)
    {
        if (!DbIntrospect.TableExists(conn, tx, "approval_instance")) return;
        var durum = DepoWise.Infrastructure.Approvals.ApprovalService.LatestStatus(
            conn, tx, s.CompanyId, DepoWise.Application.Approvals.ApprovalEntityTypes.PurchaseOrder, orderId);
        if (durum is null) return;                       // zincir YOK → mevcut davranış (İK-3)
        if (durum == DepoWise.Application.Approvals.ApprovalStatus.Approved) return;
        throw new ArgumentException(durum == DepoWise.Application.Approvals.ApprovalStatus.Rejected
            ? "Bu siparişin onayı REDDEDİLDİ; mal kabul yapılamaz."
            : "Bu sipariş onay bekliyor; onay tamamlanmadan mal kabul yapılamaz.");
    }

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
