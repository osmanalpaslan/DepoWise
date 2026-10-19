using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Purchasing;
using System.Data.Common;

namespace DepoWise.Infrastructure.Requests;

/// <summary>⭐ M-26: talep kaleminin karşılanma durumu (malzeme başına).</summary>
public sealed record TalepKalemDurumu(string MaterialId, string MaterialCode, string MaterialName,
    decimal Istenen, decimal Gonderilen, decimal Sipariste, decimal KaynakStok, decimal ToplamStok)
{
    /// <summary>Henüz ne gönderilen ne siparişe bağlanan miktar.</summary>
    public decimal Kalan => Math.Max(0m, Istenen - Gonderilen - Sipariste);
    /// <summary>Stoktan hemen gönderilebilecek miktar (kalan ile kaynak depodaki stoğun küçüğü).</summary>
    public decimal Gonderilebilir => Math.Max(0m, Math.Min(Kalan, KaynakStok));
    public string Ozet => $"{MaterialCode} — {MaterialName}: istenen {Istenen:0.####} · gönderilen {Gonderilen:0.####} · " +
                          $"siparişte {Sipariste:0.####} · kalan {Kalan:0.####} · kaynak depoda {KaynakStok:0.####} (toplam {ToplamStok:0.####})";
}

/// <summary>⭐ M-26: talebin karşılanma tablosu.</summary>
public sealed record TalepKarsilama(string RequestId, string DocNo, string? FromBranchId, string? ToBranchId,
    IReadOnlyList<TalepKalemDurumu> Kalemler)
{
    public decimal Yuzde => Kalemler.Sum(k => k.Istenen) is var t && t > 0
        ? Math.Round(100m * Kalemler.Sum(k => Math.Min(k.Istenen, k.Gonderilen)) / t, 0) : 0m;
    public bool TamamiGonderildi => Kalemler.Count > 0 && Kalemler.All(k => k.Gonderilen >= k.Istenen);
}

/// <summary>⭐ M-26: "Stoktan Gönder" / "Sipariş Aç" satırı.</summary>
public sealed record TalepKarsilamaSatiri(string MaterialId, decimal Quantity, decimal? UnitPrice = null);

/// <summary>
/// ═══ M-26 (AR-GE 2026-10-18, 05 F-071) — TALEPTEN TEK TIKLA KARŞILAMA ═══
///
/// Onaylı talep, Talep Operasyonları ekranından iki yolla karşılanır:
/// <list type="bullet">
///   <item><b>Stoktan Gönder:</b> kaynak depodan hedef şantiyeye GERÇEK transfer belgesi (stok defteri, eksi stok kalkanı,
///   idempotency — <see cref="StockService.TransferTx"/>); belge talebe bağlanır (<c>stock_documents.request_id</c>).</item>
///   <item><b>Sipariş Aç:</b> kalan miktarlarla talebe bağlı satın alma siparişi (<see cref="PurchaseOrderService.Create"/>).</item>
/// </list>
/// Talep kalemi üçüncü kez yazılmaz; merkezde olan mal yeniden satın alınmaz (kaynak stok görünür). Gönderilen + siparişteki
/// miktar istenenden fazla olamaz. Talep operasyon durumu kullanıcıda kalır (bu servis durum makinesine dokunmaz).
/// Yetki: Talep Operasyonları düzenleme + stok/satın alma oluşturma (her biri kendi servisinde de denetlenir).
/// </summary>
public sealed class TalepKarsilamaService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly StockService _stock;
    private readonly PurchaseOrderService _po;

    public TalepKarsilamaService(IDbConnectionFactory factory, StockService stock, PurchaseOrderService po, IClock? clock = null)
    {
        _factory = factory;
        _stock = stock;
        _po = po;
        _clock = clock ?? new SystemClock();
    }

    private sealed record TalepBasligi(string DocNo, string Status, string? Ops, string? From, string? To);

    private static TalepBasligi Baslik(DbConnection conn, DbTransaction? tx, string companyId, string requestId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT doc_no, status, operation_status, ops_from_branch_id, ops_to_branch_id FROM material_requests " +
                          "WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", requestId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Talep bulunamadı veya başka firmaya ait.");
        return new TalepBasligi(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4));
    }

    private static Dictionary<string, decimal> Topla(DbConnection conn, DbTransaction? tx, string sql, string companyId, string requestId)
    {
        var d = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@r", requestId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) d[r.GetString(0)] = d.GetValueOrDefault(r.GetString(0)) + Money.Parse(r.GetString(1));
        return d;
    }

    private List<TalepKalemDurumu> Kalemler(DbConnection conn, DbTransaction? tx, string companyId, string requestId, string? kaynak)
    {
        var istenen = new List<(string Id, string Kod, string Ad)>();
        var miktar = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT i.material_id, m.code, m.name, i.quantity FROM material_request_items i " +
                              "JOIN materials m ON m.id=i.material_id AND m.company_id=@c WHERE i.request_id=@r ORDER BY m.code;";
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@r", requestId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!miktar.ContainsKey(r.GetString(0))) istenen.Add((r.GetString(0), r.GetString(1), r.GetString(2)));
                miktar[r.GetString(0)] = miktar.GetValueOrDefault(r.GetString(0)) + Money.Parse(r.GetString(3));
            }
        }
        var gonderilen = DbIntrospect.ColumnExists(conn, tx, "stock_documents", "request_id")
            ? Topla(conn, tx, "SELECT sm.material_id, sm.quantity FROM stock_movements sm JOIN stock_documents d ON d.id=sm.document_id " +
                              "WHERE d.company_id=@c AND d.request_id=@r AND COALESCE(d.status,'active')<>'cancelled' " +
                              "AND sm.movement_type='transfer' AND sm.direction=1 AND sm.is_reversed=0;", companyId, requestId)
            : new Dictionary<string, decimal>();
        var sipariste = Topla(conn, tx, "SELECT l.material_id, l.quantity FROM purchase_order_lines l JOIN purchase_orders o ON o.id=l.order_id " +
                                        "WHERE o.company_id=@c AND o.request_id=@r AND o.status<>'cancelled' AND o.is_deleted=0 AND l.is_deleted=0;",
            companyId, requestId);
        return istenen.Select(k => new TalepKalemDurumu(k.Id, k.Kod, k.Ad, miktar[k.Id], gonderilen.GetValueOrDefault(k.Id),
            sipariste.GetValueOrDefault(k.Id),
            string.IsNullOrWhiteSpace(kaynak) ? 0m : StockBalanceWriter.ReadBalance(conn, tx, companyId, k.Id, kaynak!),
            StockBalanceWriter.ReadTotal(conn, tx, companyId, k.Id))).ToList();
    }

    /// <summary>Talebin karşılanma tablosu (istenen / gönderilen / siparişte / kalan / kaynak depo stoğu).
    /// <paramref name="kaynakSube"/> verilmezse talebin operasyon "gönderen şube"si kullanılır.</summary>
    public TalepKarsilama Durum(SessionContext s, string requestId, string? kaynakSube = null)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.View);
        using var conn = _factory.Create();
        var b = Baslik(conn, null, s.CompanyId, requestId);
        var kaynak = string.IsNullOrWhiteSpace(kaynakSube) ? b.From : kaynakSube;
        return new TalepKarsilama(requestId, b.DocNo, kaynak, b.To, Kalemler(conn, null, s.CompanyId, requestId, kaynak));
    }

    private static void KarsilanabilirMi(TalepBasligi b)
    {
        if (b.Status != "approved") throw new InvalidOperationException("Yalnız ONAYLANMIŞ talep karşılanabilir.");
        if (b.Ops == RequestOperationStatusInfo.ToDb(RequestOperationStatus.CancelledOps))
            throw new InvalidOperationException("Talebin operasyonu iptal edilmiş; karşılama yapılamaz.");
    }

    private static void FazlaMi(IReadOnlyList<TalepKarsilamaSatiri> satirlar, List<TalepKalemDurumu> kalemler, string islem)
    {
        if (satirlar.Count == 0) throw new ArgumentException("En az bir kalem için miktar girin.");
        foreach (var g in satirlar.GroupBy(x => x.MaterialId))
        {
            var k = kalemler.FirstOrDefault(x => x.MaterialId == g.Key)
                ?? throw new ArgumentException("Talepte olmayan malzeme " + islem + " yapılamaz.");
            var q = g.Sum(x => x.Quantity);
            if (q <= 0) throw new ArgumentException("Miktar pozitif olmalı.");
            if (q > k.Kalan)
                throw new ArgumentException($"{k.MaterialName}: {islem} miktarı ({q:0.####}) talebin kalanından ({k.Kalan:0.####}) fazla.");
        }
    }

    /// <summary>
    /// STOKTAN GÖNDER — kaynak depodan hedefe transfer, TEK işlemde belge talebe bağlanır. Tekrar gönderim (aynı
    /// <paramref name="operationId"/>) aynı belgeyi döndürür. Eksi stok kalkanı geçerli (kaynakta yoksa gönderilmez).
    /// </summary>
    public StockDocResult StoktanGonder(SessionContext s, string requestId, string fromBranchId, string toBranchId,
        IReadOnlyList<TalepKarsilamaSatiri> satirlar, string operationId)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        return StockBalanceWriter.Run(() => GonderOnce(s, requestId, fromBranchId, toBranchId, satirlar, operationId),
            $"request-ship:{requestId} op={operationId}");
    }

    private StockDocResult GonderOnce(SessionContext s, string requestId, string fromBranchId, string toBranchId,
        IReadOnlyList<TalepKarsilamaSatiri> satirlar, string operationId)
    {
        using var conn = _factory.Create();
        if (!DbIntrospect.ColumnExists(conn, null, "stock_documents", "request_id"))
            throw new InvalidOperationException("Veritabanı talep ↔ stok bağını henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        var b = Baslik(conn, tx, s.CompanyId, requestId);
        // Tekrar gönderim: belge zaten varsa (talep durumu sonradan değişmiş olsa bile) aynı sonucu döndür.
        if (StockService.BelgeIslemden(conn, tx, s.CompanyId, operationId) is { } onceki) { tx.Commit(); return onceki; }
        KarsilanabilirMi(b);
        FazlaMi(satirlar, Kalemler(conn, tx, s.CompanyId, requestId, fromBranchId), "gönderim");
        var doc = _stock.TransferTx(conn, tx, s, satirlar.Select(x => new StockLine(x.MaterialId, x.Quantity)).ToList(),
            fromBranchId, toBranchId, operationId, $"Talep {b.DocNo}")!;
        using (var u = conn.CreateCommand())
        {
            u.Transaction = tx;
            u.CommandText = "UPDATE stock_documents SET request_id=@r WHERE id=@d AND company_id=@c;";
            u.AddWithValue("@r", requestId);
            u.AddWithValue("@d", doc.DocumentId);
            u.AddWithValue("@c", s.CompanyId);
            u.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_request", requestId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"stoktanGonderildi\":\"{doc.DocumentId}\"}}"), _clock);
        tx.Commit();
        return doc;
    }

    /// <summary>
    /// SİPARİŞ AÇ — kalan miktarlarla talebe bağlı satın alma siparişi (numara otomatik). Teslim deposu talebin hedef
    /// şubesi. Siparişteki miktar talebin kalanını aşamaz (aynı mal ikinci kez sipariş edilmez).
    /// </summary>
    public string SiparisAc(SessionContext s, string requestId, string? supplierId, IReadOnlyList<TalepKarsilamaSatiri> satirlar,
        string? teslimSubesi = null)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.Edit);
        TalepBasligi b;
        using (var conn = _factory.Create())
        {
            b = Baslik(conn, null, s.CompanyId, requestId);
            KarsilanabilirMi(b);
            FazlaMi(satirlar, Kalemler(conn, null, s.CompanyId, requestId, b.From), "sipariş");
        }
        return _po.Create(s, new NewPurchaseOrder("", SupplierId: string.IsNullOrWhiteSpace(supplierId) ? null : supplierId,
            RequestId: requestId, BranchId: string.IsNullOrWhiteSpace(teslimSubesi) ? b.To : teslimSubesi,
            Note: $"Talep {b.DocNo}",
            Lines: satirlar.Select(x => new NewPurchaseOrderLine(x.MaterialId, x.Quantity, x.UnitPrice)).ToList()));
    }
}
