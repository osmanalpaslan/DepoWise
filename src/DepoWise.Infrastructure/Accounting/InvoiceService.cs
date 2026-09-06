using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using System.Data.Common;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Fatura yönü kataloğu — tek doğru kaynak (web + masaüstü aynı etiketi gösterir).</summary>
public static class InvoiceDirections
{
    /// <summary>ALIŞ faturası: stok GİRER, satıcıya BORÇLANIRIZ (cari alacaklı).</summary>
    public const string Purchase = Migration067_Invoices.DirectionPurchase;

    /// <summary>SATIŞ faturası: stok ÇIKAR, müşteri BİZE borçlanır.</summary>
    public const string Sales = Migration067_Invoices.DirectionSales;

    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Purchase, "Alış Faturası"),
        (Sales, "Satış Faturası"),
    };

    public static string Label(string? key) => All.FirstOrDefault(x => x.Key == key).Label ?? (key ?? "—");
    public static bool IsValid(string? key) => All.Any(x => x.Key == key);
}

/// <summary>Fatura durumu kataloğu. SİLİNMİŞ durumu YOKTUR — fatura fiziksel silinmez.</summary>
public static class InvoiceStatuses
{
    public const string Active = Migration067_Invoices.StatusActive;
    public const string Cancelled = Migration067_Invoices.StatusCancelled;

    public static string Label(string? key) => key == Cancelled ? "İptal" : "Yürürlükte";
}

/// <summary>Fatura satırı girdisi. Oranlar YÜZDE olarak verilir (%20 → 20).</summary>
public sealed record NewInvoiceLine(
    string? MaterialId,
    string? Description,
    string? Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountRate = 0m,
    decimal VatRate = 0m,
    decimal WithholdingRate = 0m);

/// <summary>Yeni fatura girdisi.</summary>
/// <param name="AffectsStock">false ise stok belgesi ÜRETİLMEZ (hizmet/masraf faturası). Cari etkisi yine oluşur.</param>
/// <param name="OperationId">Idempotency anahtarı — aynı değerle ikinci çağrı ikinci fatura üretmez.</param>
public sealed record NewInvoice(
    string Direction,
    string PartyId,
    IReadOnlyList<NewInvoiceLine> Lines,
    string OperationId,
    string? SeriesId = null,
    string? ExternalNo = null,
    string? BranchId = null,
    long? InvoiceDate = null,
    long? DueDate = null,
    string Currency = "TRY",
    string? Note = null,
    bool AffectsStock = true);

/// <summary>Hesaplanmış fatura toplamları — hiçbiri kullanıcıdan alınmaz, satırlardan TÜRETİLİR.</summary>
public sealed record InvoiceTotals(
    decimal Subtotal, decimal DiscountTotal, decimal VatTotal, decimal WithholdingTotal, decimal GrandTotal);

/// <summary>Fatura yazma sonucu.</summary>
/// <param name="AlreadyExisted">true ise aynı operation_id daha önce işlenmişti; YENİ kayıt oluşmadı.</param>
public sealed record InvoiceResult(
    string Id, string InvoiceNo, string? StockDocumentId, string? LedgerEntryId, bool AlreadyExisted);

/// <summary>
/// G4-2 — FATURA SERVİSİ (kullanıcı isteği 2026-08-12).
///
/// <b>⚠️ PARALEL DEFTER YOK.</b> Bu servis:
/// <list type="bullet">
/// <item>stok tablolarına DOĞRUDAN YAZMAZ — <see cref="StockService.ReceiveInTx"/> /
///       <see cref="StockService.IssueOutTx"/> çağırır;</item>
/// <item>cari defterine DOĞRUDAN YAZMAZ — <see cref="PartyLedgerService.AddFromDocumentTx"/> çağırır;</item>
/// <item>hiçbir bakiye SAKLAMAZ — ne stok ne cari.</item>
/// </list>
///
/// <b>TEK TRANSACTION:</b> fatura başlığı + satırlar + cari hareketi + stok belgesi AYNI transaction'da
/// yazılır. Herhangi biri hata verirse HİÇBİRİ yazılmaz (kısmi kayıt yok). Bunu mümkün kılan
/// "ambient transaction" desenidir: alt servisler çağıranın conn/tx'ini kullanır, kendi transaction'ını
/// açmaz ve commit etmez.
///
/// <b>IDEMPOTENCY:</b> tek bir <c>operation_id</c> üçe dağıtılır — fatura <c>op</c>, stok <c>op:stock</c>,
/// cari <c>op:ledger</c>. Aynı istek iki kez gelirse fatura=1, cari=1, stok=1 kalır.
///
/// <b>SİLME YOK, İPTAL VAR:</b> <see cref="Cancel"/> ters stok belgesi + ters cari hareketi üretir ve
/// faturayı <c>cancelled</c> işaretler. Çift iptal engellenir. Fiziksel silme yolu YOKTUR.
/// </summary>
public sealed class InvoiceService
{
    public const string Module = "invoices";

    /// <summary>Cari defterinde bu faturanın kaynak türü.</summary>
    public const string LedgerSourceType = "invoice";

    /// <summary>Para yuvarlama: 2 basamak, yarımlar yukarı (muhasebe alışkanlığı).</summary>
    private const int MoneyScale = 2;

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly StockService _stock;
    private readonly PartyLedgerService _ledger;

    public InvoiceService(IDbConnectionFactory factory, StockService stock, PartyLedgerService ledger, IClock? clock = null)
    {
        _factory = factory;
        _stock = stock;
        _ledger = ledger;
        _clock = clock ?? new SystemClock();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  TOPLAM HESABI — saf fonksiyon, veritabanına dokunmaz (ayrı test edilir)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Satır toplamı. Sıra ÖNEMLİDİR: iskonto MATRAHTAN düşülür, KDV iskontolu tutar üzerinden
    /// hesaplanır, tevkifat KDV ÜZERİNDEN alınır (Türkiye KDV tevkifatı böyle işler).
    /// </summary>
    public static (decimal Gross, decimal Discount, decimal Net, decimal Vat, decimal Withholding, decimal Total)
        LineAmounts(NewInvoiceLine l)
    {
        var gross = R(l.Quantity * l.UnitPrice);
        var discount = R(gross * l.DiscountRate / 100m);
        var net = R(gross - discount);
        var vat = R(net * l.VatRate / 100m);
        var wh = R(vat * l.WithholdingRate / 100m);
        var total = R(net + vat - wh);
        return (gross, discount, net, vat, wh, total);
    }

    /// <summary>Fatura toplamları — satır toplamlarının toplamı (ekranda ve serviste AYNI fonksiyon).</summary>
    public static InvoiceTotals Totals(IReadOnlyList<NewInvoiceLine> lines)
    {
        decimal sub = 0, disc = 0, vat = 0, wh = 0, grand = 0;
        foreach (var l in lines)
        {
            var a = LineAmounts(l);
            sub += a.Gross; disc += a.Discount; vat += a.Vat; wh += a.Withholding; grand += a.Total;
        }
        return new InvoiceTotals(R(sub), R(disc), R(vat), R(wh), R(grand));
    }

    private static decimal R(decimal v) => Math.Round(v, MoneyScale, MidpointRounding.AwayFromZero);

    // ═══════════════════════════════════════════════════════════════════════
    //  YAZMA
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fatura oluşturur: başlık + satırlar + cari hareketi (+ istenirse stok belgesi), TEK transaction'da.
    /// </summary>
    public InvoiceResult Create(SessionContext s, NewInvoice dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        Validate(dto);
        MalzemeFiyatiKapisi(s, dto);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var date = dto.InvoiceDate ?? now;

        // ⭐ G4-3b ŞUBE KAPISI: fatura başlığının şubesi kullanıcının kapsamında OLMALI.
        // Önceden yalnız stok tarafında kontrol vardı; fatura başlığı ve cari hareketi
        // doğrulanmadan yazılıyordu → kullanıcı API'ye başka şubenin id'sini yazabilirdi.
        // Belirtilmediyse oturumun/kullanıcının şubesine düşer (Resolve).
        var branchId = BranchAccess.Resolve(s, dto.BranchId, "fatura kesme");
        dto = dto with { BranchId = branchId };

        using var conn = _factory.Create();
        // IMMEDIATE: numara üretimi ve stok yazımı eş zamanlı çağrılarda seri hale gelsin.
        using var tx = conn.BeginImmediate();

        // ── IDEMPOTENCY: aynı operation_id daha önce işlendiyse MEVCUT faturayı döndür ──
        var existing = FindByOperation(conn, tx, s.CompanyId, dto.OperationId);
        if (existing is not null) return existing;

        EnsurePartyOwned(conn, tx, s.CompanyId, dto.PartyId);

        var totals = Totals(dto.Lines);
        if (totals.GrandTotal <= 0)
            throw new ArgumentException("Fatura genel toplamı sıfırdan büyük olmalıdır.");

        var series = ResolveSeries(conn, tx, s, dto.SeriesId, dto.Direction, now);
        var invoiceNo = NextInvoiceNo(conn, tx, s.CompanyId, series, now);

        var invoiceId = Guid.NewGuid().ToString("N");

        // ── 1) STOK: yalnız StockService üzerinden. Paralel stok yazımı YOKTUR. ──
        string? stockDocId = null;
        if (dto.AffectsStock)
        {
            var stockLines = dto.Lines
                .Where(l => !string.IsNullOrWhiteSpace(l.MaterialId))
                .Select(l => new StockLine(l.MaterialId!, l.Quantity, l.UnitPrice, dto.Currency))
                .ToList();

            if (stockLines.Count > 0)
            {
                var opStock = dto.OperationId + ":stock";
                var note = $"{InvoiceDirections.Label(dto.Direction)} {invoiceNo}";
                var doc = dto.Direction == InvoiceDirections.Purchase
                    ? _stock.ReceiveInTx(conn, tx, s, stockLines, opStock, dto.BranchId, null, note, date, invoiceNo)
                    : _stock.IssueOutTx(conn, tx, s, stockLines, opStock, dto.BranchId, null, note, date, invoiceNo);
                stockDocId = doc.DocumentId;
            }
        }

        // ── 2) CARİ: yalnız PartyLedgerService üzerinden. Bakiye SAKLANMAZ. ──
        // ALIŞ  → cariye borçlandık   → alacak (direction = -1, IsDebit = false)
        // SATIŞ → cari bize borçlandı → borç   (direction = +1, IsDebit = true)
        var ledgerId = _ledger.AddFromDocumentTx(conn, tx, s, new NewLedgerEntry(
            PartyId: dto.PartyId,
            DocType: PartyDocTypes.Invoice,
            Amount: totals.GrandTotal,
            IsDebit: dto.Direction == InvoiceDirections.Sales,
            EntryDate: date,
            DocNo: invoiceNo,
            Description: dto.Note,
            DueDate: dto.DueDate,
            Currency: dto.Currency,
            BranchId: dto.BranchId,
            SourceType: LedgerSourceType,
            SourceId: invoiceId,
            OperationId: dto.OperationId + ":ledger"));

        // ── 3) FATURA BAŞLIĞI + SATIRLAR ──
        InsertHeader(conn, tx, invoiceId, s, dto, invoiceNo, series.Id, date, totals, stockDocId, ledgerId, now);
        InsertLines(conn, tx, invoiceId, s.CompanyId, dto.Lines, now);

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "invoices", invoiceId, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"no\":\"{invoiceNo}\",\"dir\":\"{dto.Direction}\",\"total\":\"{Money.Serialize(totals.GrandTotal)}\"}}"), _clock);

        tx.Commit();
        return new InvoiceResult(invoiceId, invoiceNo, stockDocId, ledgerId, AlreadyExisted: false);
    }

    /// <summary>
    /// İPTAL — faturayı SİLMEZ. Ters stok belgesi + ters cari hareketi üretir, başlığı
    /// <c>cancelled</c> işaretler. İkinci kez çağrılırsa hata verir (çift iptal yok).
    /// </summary>
    public void Cancel(SessionContext s, string invoiceId, string reason)
    {
        // Silme değil DÜZELTME işlemidir → Edit yetkisi (Delete aksiyonu bilinçli olarak kullanılmaz).
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var head = ReadHeadForCancel(conn, tx, s.CompanyId, invoiceId);
        // ⭐ Kapsam kapısı: kapsam dışı şubenin faturası İPTAL EDİLEMEZ.
        BranchAccess.Require(s, head.BranchId, "fatura iptali");
        if (head.Status == InvoiceStatuses.Cancelled)
            throw new ArgumentException("Fatura zaten iptal edilmiş; ikinci kez iptal edilemez.");

        // ── Ters stok belgesi: alışın tersi çıkış, satışın tersi giriş ──
        string? cancelStockDoc = null;
        if (!string.IsNullOrWhiteSpace(head.StockDocumentId))
        {
            var lines = ReadStockLines(conn, tx, s.CompanyId, invoiceId);
            if (lines.Count > 0)
            {
                var op = $"invoice:{invoiceId}:cancel:stock";
                var note = $"İPTAL — {head.InvoiceNo}: {reason}";
                var doc = head.Direction == InvoiceDirections.Purchase
                    ? _stock.IssueOutTx(conn, tx, s, lines, op, head.BranchId, null, note, now, head.InvoiceNo)
                    : _stock.ReceiveInTx(conn, tx, s, lines, op, head.BranchId, null, note, now, head.InvoiceNo);
                cancelStockDoc = doc.DocumentId;
            }
        }

        // ── Ters cari hareketi: yön TERSİNE çevrilir ──
        var cancelLedger = _ledger.AddFromDocumentTx(conn, tx, s, new NewLedgerEntry(
            PartyId: head.PartyId,
            DocType: PartyDocTypes.Invoice,
            Amount: head.GrandTotal,
            IsDebit: head.Direction != InvoiceDirections.Sales,   // ters yön
            EntryDate: now,
            DocNo: head.InvoiceNo,
            Description: $"İPTAL: {reason}",
            DueDate: null,
            Currency: head.Currency,
            BranchId: head.BranchId,
            SourceType: LedgerSourceType,
            SourceId: invoiceId,
            OperationId: $"invoice:{invoiceId}:cancel:ledger"));

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE invoices SET status=@st, cancel_reason=@r, cancelled_at=@n, cancelled_by=@by,
       cancel_stock_document_id=@csd, cancel_ledger_entry_id=@cle, updated_at=@n, version=version+1
WHERE id=@id AND company_id=@c AND status=@active;";
            cmd.AddWithValue("@st", InvoiceStatuses.Cancelled);
            cmd.AddWithValue("@r", reason);
            cmd.AddWithValue("@n", now);
            cmd.AddWithValue("@by", s.UserId);
            cmd.AddWithValue("@csd", (object?)cancelStockDoc ?? DBNull.Value);
            cmd.AddWithValue("@cle", cancelLedger);
            cmd.AddWithValue("@id", invoiceId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@active", InvoiceStatuses.Active);
            // 0 satır = başka bir işlem araya girip iptal etmiş → yarış durumu, transaction geri alınır.
            if (cmd.ExecuteNonQuery() == 0)
                throw new ConcurrencyException(0, 0);
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "invoices", invoiceId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"status\":\"cancelled\"}}"), _clock);

        tx.Commit();
    }

    /// <summary>
    /// Fatura BİLGİ alanlarını günceller (not, karşı taraf belge no, vade).
    ///
    /// <b>⚠️ TUTAR/SATIR DEĞİŞTİRİLEMEZ.</b> Tutar veya satır değişmesi, çoktan yazılmış stok
    /// hareketini ve cari borcunu geçersiz kılardı; "düzeltilmiş" fatura ile defter arasında sessiz
    /// bir fark oluşurdu. Doğru yol: faturayı İPTAL et, yenisini kes (izlenebilir kalır).
    /// </summary>
    public void UpdateInfo(SessionContext s, string invoiceId, string? externalNo, long? dueDate, string? note, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        var head = ReadHeadForCancel(conn, tx, s.CompanyId, invoiceId);
        BranchAccess.Require(s, head.BranchId, "fatura düzenleme");   // ⭐ kapsam kapısı
        if (head.Status == InvoiceStatuses.Cancelled)
            throw new ArgumentException("İptal edilmiş fatura düzenlenemez.");
        if (expectedVersion.HasValue && expectedVersion.Value != head.Version)
            throw new ConcurrencyException(head.Version, expectedVersion.Value);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE invoices SET external_no=@ext, due_date=@due, note=@note, updated_at=@n, version=version+1
WHERE id=@id AND company_id=@c AND status=@active;";
            cmd.AddWithValue("@ext", (object?)externalNo ?? DBNull.Value);
            cmd.AddWithValue("@due", (object?)dueDate ?? DBNull.Value);
            cmd.AddWithValue("@note", (object?)note ?? DBNull.Value);
            cmd.AddWithValue("@n", now);
            cmd.AddWithValue("@id", invoiceId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@active", InvoiceStatuses.Active);
            cmd.ExecuteNonQuery();
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "invoices", invoiceId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ FAZ 3c-2 — YAZMA KAPISI: malzeme birim fiyatını GÖREMEYEN kullanıcı, malzeme satırlı fatura
    /// yazamaz. Burada kanonik "gönderileni yok say" davranışı UYGULANAMAZ: fatura satır tutarı,
    /// KDV'si ve genel toplamı birim fiyattan hesaplanır — fiyatı sessizce 0 yazmak YANLIŞ bir mali
    /// belge üretirdi (sessiz veri kaybı yasağı). Bu yüzden açık ret verilir; hizmet/serbest metin
    /// satırlı faturalar etkilenmez. Alan korumalı değilse (varsayılan) bu kapı hiçbir şey yapmaz.
    /// </summary>
    private static void MalzemeFiyatiKapisi(SessionContext s, NewInvoice dto)
    {
        if (AccountingFieldGate.MalzemeBirimFiyati(s)) return;
        if (dto.Lines is null) return;
        foreach (var l in dto.Lines)
            if (!string.IsNullOrWhiteSpace(l.MaterialId))
                throw new ForbiddenException(
                    "Malzeme birim fiyatlarını görme yetkiniz olmadığı için malzeme satırlı fatura kaydedemezsiniz.");
    }

    private static void Validate(NewInvoice dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OperationId)) throw new ArgumentException("operation_id zorunlu.");
        if (!InvoiceDirections.IsValid(dto.Direction)) throw new ArgumentException("Fatura türü geçersiz.");
        if (string.IsNullOrWhiteSpace(dto.PartyId)) throw new ArgumentException("Cari seçilmelidir.");
        if (!Money.IsSupported(dto.Currency)) throw new ArgumentException("Para birimi geçersiz.");
        if (dto.Lines is null || dto.Lines.Count == 0) throw new ArgumentException("Faturada en az bir satır olmalıdır.");
        if (dto.DueDate is not null && dto.InvoiceDate is not null && dto.DueDate < dto.InvoiceDate)
            throw new ArgumentException("Vade tarihi fatura tarihinden önce olamaz.");

        for (int i = 0; i < dto.Lines.Count; i++)
        {
            var l = dto.Lines[i];
            var no = i + 1;
            if (l.Quantity <= 0) throw new ArgumentException($"{no}. satır: miktar sıfırdan büyük olmalıdır.");
            if (l.UnitPrice < 0) throw new ArgumentException($"{no}. satır: birim fiyat negatif olamaz.");
            if (l.DiscountRate < 0 || l.DiscountRate > 100) throw new ArgumentException($"{no}. satır: iskonto oranı 0–100 arasında olmalıdır.");
            if (l.VatRate < 0 || l.VatRate > 100) throw new ArgumentException($"{no}. satır: KDV oranı 0–100 arasında olmalıdır.");
            if (l.WithholdingRate < 0 || l.WithholdingRate > 100) throw new ArgumentException($"{no}. satır: tevkifat oranı 0–100 arasında olmalıdır.");
            if (string.IsNullOrWhiteSpace(l.MaterialId) && string.IsNullOrWhiteSpace(l.Description))
                throw new ArgumentException($"{no}. satır: malzeme seçin veya açıklama yazın.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SERİ / NUMARA
    // ═══════════════════════════════════════════════════════════════════════

    private sealed record SeriesRow(string Id, string Code, string? Prefix, long NextNumber, int Padding);

    /// <summary>
    /// Seriyi çözer. Seri verilmediyse yönün varsayılan serisini kullanır; hiç seri yoksa "A"
    /// serisini OLUŞTURUR (kural kodda sabit değil, VERİ olarak kaydedilir; kullanıcı sonradan
    /// önek/dolgu/sıra numarasını değiştirebilir).
    /// </summary>
    private SeriesRow ResolveSeries(DbConnection conn, DbTransaction tx, SessionContext s, string? seriesId, string direction, long now)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = seriesId is null
                ? "SELECT id, code, prefix, next_number, number_padding FROM invoice_series " +
                  "WHERE company_id=@c AND direction=@d AND is_deleted=0 AND is_active=1 " +
                  "ORDER BY is_default DESC, code ASC;"
                : "SELECT id, code, prefix, next_number, number_padding FROM invoice_series " +
                  "WHERE company_id=@c AND direction=@d AND id=@id AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@d", direction);
            if (seriesId is not null) cmd.AddWithValue("@id", seriesId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
                return new SeriesRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                    Convert.ToInt64(r.GetValue(3)), Convert.ToInt32(r.GetValue(4)));
        }

        if (seriesId is not null)
            throw new ForbiddenException("Belge serisi bulunamadı veya başka firmaya ait.");

        // Varsayılan seriyi oluştur — ilk fatura seri tanımı yüzünden bloke olmasın.
        var id = Guid.NewGuid().ToString("N");
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO invoice_series(id, company_id, code, name, direction, prefix, next_number, number_padding,
                           is_default, is_active, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,'A','Varsayılan',@d,'A',1,8,1,1,@n,@n,1,0);";
            ins.AddWithValue("@id", id);
            ins.AddWithValue("@c", s.CompanyId);
            ins.AddWithValue("@d", direction);
            ins.AddWithValue("@n", now);
            ins.ExecuteNonQuery();
        }
        return new SeriesRow(id, "A", "A", 1, 8);
    }

    /// <summary>
    /// Sıradaki fatura numarasını üretir ve seriyi İLERLETİR — aynı transaction içinde.
    /// "En büyük no + 1" taraması YOKTUR: eş zamanlı iki fatura aynı numarayı alamaz
    /// (IMMEDIATE transaction + UPDATE aynı satırı kilitler).
    /// </summary>
    private static string NextInvoiceNo(DbConnection conn, DbTransaction tx, string companyId, SeriesRow series, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE invoice_series SET next_number=next_number+1, updated_at=@n, version=version+1 " +
                          "WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@id", series.Id);
        cmd.AddWithValue("@c", companyId);
        cmd.ExecuteNonQuery();

        var padding = series.Padding <= 0 ? 1 : Math.Min(series.Padding, 18);
        return (series.Prefix ?? series.Code) + series.NextNumber.ToString(new string('0', padding));
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  YAZMA YARDIMCILARI
    // ═══════════════════════════════════════════════════════════════════════

    private static void InsertHeader(DbConnection conn, DbTransaction tx, string invoiceId, SessionContext s,
        NewInvoice dto, string invoiceNo, string seriesId, long date, InvoiceTotals t,
        string? stockDocId, string? ledgerId, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO invoices(id, company_id, direction, series_id, invoice_no, external_no, party_id, branch_id,
                     invoice_date, due_date, currency_code, fx_rate, subtotal, discount_total, vat_total,
                     withholding_total, grand_total, note, status, affects_stock, stock_document_id,
                     ledger_entry_id, operation_id, created_at, created_by, updated_at, version)
VALUES(@id,@c,@dir,@ser,@no,@ext,@p,@br,@date,@due,@cur,NULL,@sub,@disc,@vat,@wh,@grand,@note,@st,@afs,
       @sdoc,@led,@op,@n,@by,@n,1);";
        cmd.AddWithValue("@id", invoiceId);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@dir", dto.Direction);
        cmd.AddWithValue("@ser", seriesId);
        cmd.AddWithValue("@no", invoiceNo);
        cmd.AddWithValue("@ext", (object?)dto.ExternalNo ?? DBNull.Value);
        cmd.AddWithValue("@p", dto.PartyId);
        cmd.AddWithValue("@br", (object?)dto.BranchId ?? DBNull.Value);
        cmd.AddWithValue("@date", date);
        cmd.AddWithValue("@due", (object?)dto.DueDate ?? DBNull.Value);
        cmd.AddWithValue("@cur", dto.Currency);
        cmd.AddWithValue("@sub", Money.Serialize(t.Subtotal));
        cmd.AddWithValue("@disc", Money.Serialize(t.DiscountTotal));
        cmd.AddWithValue("@vat", Money.Serialize(t.VatTotal));
        cmd.AddWithValue("@wh", Money.Serialize(t.WithholdingTotal));
        cmd.AddWithValue("@grand", Money.Serialize(t.GrandTotal));
        cmd.AddWithValue("@note", (object?)dto.Note ?? DBNull.Value);
        cmd.AddWithValue("@st", InvoiceStatuses.Active);
        cmd.AddWithValue("@afs", dto.AffectsStock ? 1L : 0L);
        cmd.AddWithValue("@sdoc", (object?)stockDocId ?? DBNull.Value);
        cmd.AddWithValue("@led", (object?)ledgerId ?? DBNull.Value);
        cmd.AddWithValue("@op", dto.OperationId);
        cmd.AddWithValue("@n", now);
        cmd.AddWithValue("@by", s.UserId);
        cmd.ExecuteNonQuery();
    }

    private static void InsertLines(DbConnection conn, DbTransaction tx, string invoiceId, string companyId,
        IReadOnlyList<NewInvoiceLine> lines, long now)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var a = LineAmounts(l);
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO invoice_lines(id, company_id, invoice_id, line_no, material_id, description, unit, quantity,
                          unit_price, discount_rate, discount_amount, vat_rate, vat_amount,
                          withholding_rate, withholding_amount, net_total, line_total, created_at, updated_at)
VALUES(@id,@c,@inv,@no,@mat,@desc,@unit,@qty,@price,@dr,@da,@vr,@va,@wr,@wa,@net,@total,@n,@n);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@inv", invoiceId);
            cmd.AddWithValue("@no", (long)(i + 1));
            cmd.AddWithValue("@mat", (object?)l.MaterialId ?? DBNull.Value);
            cmd.AddWithValue("@desc", (object?)l.Description ?? DBNull.Value);
            cmd.AddWithValue("@unit", (object?)l.Unit ?? DBNull.Value);
            cmd.AddWithValue("@qty", Money.Serialize(l.Quantity));
            cmd.AddWithValue("@price", Money.Serialize(l.UnitPrice));
            cmd.AddWithValue("@dr", Money.Serialize(l.DiscountRate));
            cmd.AddWithValue("@da", Money.Serialize(a.Discount));
            cmd.AddWithValue("@vr", Money.Serialize(l.VatRate));
            cmd.AddWithValue("@va", Money.Serialize(a.Vat));
            cmd.AddWithValue("@wr", Money.Serialize(l.WithholdingRate));
            cmd.AddWithValue("@wa", Money.Serialize(a.Withholding));
            cmd.AddWithValue("@net", Money.Serialize(a.Net));
            cmd.AddWithValue("@total", Money.Serialize(a.Total));
            cmd.AddWithValue("@n", now);
            cmd.ExecuteNonQuery();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OKUMA YARDIMCILARI
    // ═══════════════════════════════════════════════════════════════════════

    private static InvoiceResult? FindByOperation(DbConnection conn, DbTransaction tx, string companyId, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, invoice_no, stock_document_id, ledger_entry_id FROM invoices " +
                          "WHERE company_id=@c AND operation_id=@op;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@op", operationId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new InvoiceResult(r.GetString(0), r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), AlreadyExisted: true);
    }

    private sealed record CancelHead(string InvoiceNo, string Direction, string PartyId, string? BranchId,
        string Currency, decimal GrandTotal, string Status, string? StockDocumentId, long Version);

    private static CancelHead ReadHeadForCancel(DbConnection conn, DbTransaction tx, string companyId, string invoiceId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT invoice_no, direction, party_id, branch_id, currency_code, grand_total, " +
                          "status, stock_document_id, version FROM invoices WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", invoiceId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Fatura bulunamadı veya başka firmaya ait.");
        return new CancelHead(r.GetString(0), r.GetString(1), r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), Money.Parse(r.GetString(5)),
            r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), Convert.ToInt64(r.GetValue(8)));
    }

    private static List<StockLine> ReadStockLines(DbConnection conn, DbTransaction tx, string companyId, string invoiceId)
    {
        var list = new List<StockLine>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT material_id, quantity, unit_price FROM invoice_lines " +
                          "WHERE company_id=@c AND invoice_id=@inv AND material_id IS NOT NULL ORDER BY line_no;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@inv", invoiceId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new StockLine(r.GetString(0), Money.Parse(r.GetString(1)), Money.Parse(r.GetString(2))));
        return list;
    }

    private static void EnsurePartyOwned(DbConnection conn, DbTransaction tx, string companyId, string partyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM parties WHERE id=@p AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@p", partyId); cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Cari bulunamadı veya başka firmaya ait.");
    }
}
