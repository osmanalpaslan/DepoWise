using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Fatura listesi satırı (ekran ve rapor aynı satırı gösterir).</summary>
public sealed record InvoiceListRow(
    string Id, string Direction, string InvoiceNo, string? ExternalNo, string PartyId, string PartyTitle,
    long InvoiceDate, long? DueDate, string Currency, decimal GrandTotal, string Status, bool AffectsStock)
{
    public string DirectionText => InvoiceDirections.Label(Direction);
    public string StatusText => InvoiceStatuses.Label(Status);
    public bool IsCancelled => Status == InvoiceStatuses.Cancelled;
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(InvoiceDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string DueText => DueDate is null ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(DueDate.Value).LocalDateTime.ToString("dd.MM.yyyy");
}

/// <summary>Fatura satırı (okuma) — tutarlar YAZILDIĞI GİBİ okunur, yeniden hesaplanmaz.</summary>
public sealed record InvoiceLineRecord(
    string Id, int LineNo, string? MaterialId, string? MaterialCode, string? MaterialName,
    string? Description, string? Unit, decimal Quantity, decimal UnitPrice,
    decimal DiscountRate, decimal DiscountAmount, decimal VatRate, decimal VatAmount,
    decimal WithholdingRate, decimal WithholdingAmount, decimal NetTotal, decimal LineTotal)
{
    public string ItemText => MaterialCode is null ? (Description ?? "—")
        : $"{MaterialCode} — {MaterialName}";
}

/// <summary>Fatura başlığı (okuma) + satırları.</summary>
public sealed record InvoiceRecord(
    string Id, string Direction, string InvoiceNo, string? ExternalNo, string? SeriesId,
    string PartyId, string PartyTitle, string? BranchId, string? BranchName,
    long InvoiceDate, long? DueDate, string Currency,
    decimal Subtotal, decimal DiscountTotal, decimal VatTotal, decimal WithholdingTotal, decimal GrandTotal,
    string? Note, string Status, bool AffectsStock,
    string? StockDocumentId, string? LedgerEntryId,
    string? CancelReason, long? CancelledAt, long Version,
    IReadOnlyList<InvoiceLineRecord> Lines)
{
    public string DirectionText => InvoiceDirections.Label(Direction);
    public string StatusText => InvoiceStatuses.Label(Status);
    public bool IsCancelled => Status == InvoiceStatuses.Cancelled;
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(InvoiceDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string DueText => DueDate is null ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(DueDate.Value).LocalDateTime.ToString("dd.MM.yyyy");
}

/// <summary>Belge serisi (okuma / yönetim).</summary>
public sealed record InvoiceSeriesRow(string Id, string Code, string? Name, string Direction,
    string? Prefix, long NextNumber, int Padding, bool IsDefault, bool IsActive);

/// <summary>KDV oranı kataloğu satırı — oran KODDA SABİT DEĞİL, bu tablodan gelir.</summary>
public sealed record VatRateRow(string Id, decimal Rate, string? Label, bool IsDefault, bool IsActive);

/// <summary>
/// G4-2 — FATURA OKUMA ve KATALOG YÖNETİMİ.
///
/// Yazma tarafı <see cref="InvoiceService"/>'tedir; bu dosya yalnız OKUMA ve yapılandırma
/// (belge serisi / KDV oranı) içindir. Yetki kapısı burada da uygulanır — okuma yolu
/// yetkiyi ATLAMAZ (deny-by-default).
/// </summary>
public sealed partial class InvoiceQueryService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public InvoiceQueryService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LİSTE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fatura listesi — arama + yön/durum/cari/tarih filtresi + SAYFALAMA.
    /// Tüm kayıtlar RAM'e ÇEKİLMEZ; filtreleme ve sınır SUNUCUDA (SQL) uygulanır.
    /// </summary>
    public GridResult<InvoiceListRow> List(SessionContext s, string? search = null, string? direction = null,
        string? status = null, string? partyId = null, long? from = null, long? to = null,
        int page = 1, int pageSize = 50)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        using var conn = _factory.Create();
        var where = " WHERE i.company_id=@c";
        if (!string.IsNullOrWhiteSpace(search))
            where += $" AND ({SqlDialect.LikeTr(conn, "i.invoice_no", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "COALESCE(i.external_no,'')", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "p.title", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "p.code", "@q")})";
        if (InvoiceDirections.IsValid(direction)) where += " AND i.direction=@dir";
        if (status is InvoiceStatuses.Active or InvoiceStatuses.Cancelled) where += " AND i.status=@st";
        if (!string.IsNullOrWhiteSpace(partyId)) where += " AND i.party_id=@pid";
        if (from is not null) where += " AND i.invoice_date>=@from";
        if (to is not null) where += " AND i.invoice_date<=@to";

        void Bind(DbCommand cmd)
        {
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
            if (InvoiceDirections.IsValid(direction)) cmd.AddWithValue("@dir", direction!);
            if (status is InvoiceStatuses.Active or InvoiceStatuses.Cancelled) cmd.AddWithValue("@st", status!);
            if (!string.IsNullOrWhiteSpace(partyId)) cmd.AddWithValue("@pid", partyId!);
            if (from is not null) cmd.AddWithValue("@from", from.Value);
            if (to is not null) cmd.AddWithValue("@to", to.Value);
        }

        const string Join = " FROM invoices i LEFT JOIN parties p ON p.id=i.party_id";

        int total;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*)" + Join + where + ";";
            Bind(cmd);
            total = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var rows = new List<InvoiceListRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT i.id, i.direction, i.invoice_no, i.external_no, i.party_id, COALESCE(p.title,'—'), " +
                "i.invoice_date, i.due_date, i.currency_code, i.grand_total, i.status, i.affects_stock" +
                Join + where + " ORDER BY i.invoice_date DESC, i.invoice_no DESC LIMIT @lim OFFSET @off;";
            Bind(cmd);
            cmd.AddWithValue("@lim", pageSize);
            cmd.AddWithValue("@off", (page - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add(new InvoiceListRow(r.GetString(0), r.GetString(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5),
                    Convert.ToInt64(r.GetValue(6)), r.IsDBNull(7) ? null : Convert.ToInt64(r.GetValue(7)),
                    r.GetString(8), Money.Parse(r.GetString(9)), r.GetString(10),
                    Convert.ToInt64(r.GetValue(11)) != 0));
        }
        return new GridResult<InvoiceListRow>(rows, total, page, pageSize);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  DETAY
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Fatura detayı + satırları. Başka firmanın faturası OKUNAMAZ.</summary>
    public InvoiceRecord Get(SessionContext s, string invoiceId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        InvoiceRecord head;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT i.id, i.direction, i.invoice_no, i.external_no, i.series_id, i.party_id, COALESCE(p.title,'—'),
       i.branch_id, b.name, i.invoice_date, i.due_date, i.currency_code,
       i.subtotal, i.discount_total, i.vat_total, i.withholding_total, i.grand_total,
       i.note, i.status, i.affects_stock, i.stock_document_id, i.ledger_entry_id,
       i.cancel_reason, i.cancelled_at, i.version
FROM invoices i
LEFT JOIN parties p ON p.id=i.party_id
LEFT JOIN branches b ON b.id=i.branch_id
WHERE i.id=@id AND i.company_id=@c;";
            cmd.AddWithValue("@id", invoiceId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Fatura bulunamadı veya başka firmaya ait.");
            head = new InvoiceRecord(
                r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                Convert.ToInt64(r.GetValue(9)), r.IsDBNull(10) ? null : Convert.ToInt64(r.GetValue(10)),
                r.GetString(11),
                Money.Parse(r.GetString(12)), Money.Parse(r.GetString(13)), Money.Parse(r.GetString(14)),
                Money.Parse(r.GetString(15)), Money.Parse(r.GetString(16)),
                r.IsDBNull(17) ? null : r.GetString(17), r.GetString(18), Convert.ToInt64(r.GetValue(19)) != 0,
                r.IsDBNull(20) ? null : r.GetString(20), r.IsDBNull(21) ? null : r.GetString(21),
                r.IsDBNull(22) ? null : r.GetString(22), r.IsDBNull(23) ? null : Convert.ToInt64(r.GetValue(23)),
                Convert.ToInt64(r.GetValue(24)), Array.Empty<InvoiceLineRecord>());
        }

        var lines = new List<InvoiceLineRecord>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT l.id, l.line_no, l.material_id, m.code, m.name, l.description, l.unit, l.quantity, l.unit_price,
       l.discount_rate, l.discount_amount, l.vat_rate, l.vat_amount,
       l.withholding_rate, l.withholding_amount, l.net_total, l.line_total
FROM invoice_lines l LEFT JOIN materials m ON m.id=l.material_id
WHERE l.company_id=@c AND l.invoice_id=@id ORDER BY l.line_no;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@id", invoiceId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lines.Add(new InvoiceLineRecord(
                    r.GetString(0), Convert.ToInt32(r.GetValue(1)),
                    r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                    r.IsDBNull(6) ? null : r.GetString(6),
                    Money.Parse(r.GetString(7)), Money.Parse(r.GetString(8)),
                    Money.Parse(r.GetString(9)), Money.Parse(r.GetString(10)),
                    Money.Parse(r.GetString(11)), Money.Parse(r.GetString(12)),
                    Money.Parse(r.GetString(13)), Money.Parse(r.GetString(14)),
                    Money.Parse(r.GetString(15)), Money.Parse(r.GetString(16))));
        }

        return head with { Lines = lines };
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  KATALOGLAR — belge serisi ve KDV oranı (Türkiye kuralları VERİDİR)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Belge serileri. <paramref name="direction"/> verilirse yalnız o yöndekiler.</summary>
    public IReadOnlyList<InvoiceSeriesRow> Series(SessionContext s, string? direction = null, bool onlyActive = true)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var list = new List<InvoiceSeriesRow>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, code, name, direction, prefix, next_number, number_padding, is_default, is_active " +
                          "FROM invoice_series WHERE company_id=@c AND is_deleted=0" +
                          (InvoiceDirections.IsValid(direction) ? " AND direction=@d" : "") +
                          (onlyActive ? " AND is_active=1" : "") +
                          " ORDER BY direction, is_default DESC, code;";
        cmd.AddWithValue("@c", s.CompanyId);
        if (InvoiceDirections.IsValid(direction)) cmd.AddWithValue("@d", direction!);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new InvoiceSeriesRow(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), Convert.ToInt64(r.GetValue(5)),
                Convert.ToInt32(r.GetValue(6)), Convert.ToInt64(r.GetValue(7)) != 0, Convert.ToInt64(r.GetValue(8)) != 0));
        return list;
    }

    /// <summary>Belge serisi oluşturur/günceller. Seri KODU firma + yön içinde benzersizdir.</summary>
    public string SaveSeries(SessionContext s, string? id, string code, string? name, string direction,
        string? prefix, long? nextNumber, int padding, bool isDefault, bool isActive)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Seri kodu zorunlu.");
        if (!InvoiceDirections.IsValid(direction)) throw new ArgumentException("Fatura türü geçersiz.");
        if (padding is < 1 or > 18) throw new ArgumentException("Numara uzunluğu 1–18 arasında olmalıdır.");
        if (nextNumber is not null && nextNumber < 1) throw new ArgumentException("Sıradaki numara 1'den küçük olamaz.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Varsayılan TEK olur: yeni varsayılan seçilirse diğerleri düşer.
        if (isDefault)
        {
            using var clr = conn.CreateCommand();
            clr.Transaction = tx;
            clr.CommandText = "UPDATE invoice_series SET is_default=0, updated_at=@n WHERE company_id=@c AND direction=@d;";
            clr.AddWithValue("@n", now); clr.AddWithValue("@c", s.CompanyId); clr.AddWithValue("@d", direction);
            clr.ExecuteNonQuery();
        }

        var newId = id ?? Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = id is null
                ? @"INSERT INTO invoice_series(id, company_id, code, name, direction, prefix, next_number,
                        number_padding, is_default, is_active, created_at, updated_at, version, is_deleted)
                    VALUES(@id,@c,@code,@name,@d,@pre,@next,@pad,@def,@act,@n,@n,1,0);"
                : @"UPDATE invoice_series SET code=@code, name=@name, prefix=@pre,
                        next_number=COALESCE(@next, next_number), number_padding=@pad, is_default=@def,
                        is_active=@act, updated_at=@n, version=version+1
                    WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", newId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@code", code.Trim());
            cmd.AddWithValue("@name", (object?)name ?? DBNull.Value);
            cmd.AddWithValue("@d", direction);
            cmd.AddWithValue("@pre", (object?)prefix ?? DBNull.Value);
            cmd.AddWithValue("@next", (object?)nextNumber ?? (id is null ? 1L : DBNull.Value));
            cmd.AddWithValue("@pad", (long)padding);
            cmd.AddWithValue("@def", isDefault ? 1L : 0L);
            cmd.AddWithValue("@act", isActive ? 1L : 0L);
            cmd.AddWithValue("@n", now);
            if (cmd.ExecuteNonQuery() == 0 && id is not null)
                throw new ForbiddenException("Belge serisi bulunamadı veya başka firmaya ait.");
        }

        tx.Commit();
        return newId;
    }

    /// <summary>KDV oranı kataloğu.</summary>
    public IReadOnlyList<VatRateRow> VatRates(SessionContext s, bool onlyActive = true)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var list = new List<VatRateRow>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, rate, label, is_default, is_active FROM vat_rates " +
                          "WHERE company_id=@c AND is_deleted=0" + (onlyActive ? " AND is_active=1" : "") +
                          " ORDER BY sort_order, rate;";
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new VatRateRow(r.GetString(0), Money.Parse(r.GetString(1)),
                r.IsDBNull(2) ? null : r.GetString(2), Convert.ToInt64(r.GetValue(3)) != 0,
                Convert.ToInt64(r.GetValue(4)) != 0));
        return list;
    }

    /// <summary>
    /// KDV oranı ekler/günceller. Oran KODA GÖMÜLMEZ — Türkiye'de oranlar değişir; değiştiğinde
    /// migration değil, bu kayıt güncellenir.
    /// </summary>
    public string SaveVatRate(SessionContext s, string? id, decimal rate, string? label, bool isDefault, bool isActive, int sortOrder = 0)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (rate < 0 || rate > 100) throw new ArgumentException("KDV oranı 0–100 arasında olmalıdır.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (isDefault)
        {
            using var clr = conn.CreateCommand();
            clr.Transaction = tx;
            clr.CommandText = "UPDATE vat_rates SET is_default=0, updated_at=@n WHERE company_id=@c;";
            clr.AddWithValue("@n", now); clr.AddWithValue("@c", s.CompanyId);
            clr.ExecuteNonQuery();
        }

        var newId = id ?? Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = id is null
                ? @"INSERT INTO vat_rates(id, company_id, rate, label, is_default, is_active, sort_order,
                        created_at, updated_at, version, is_deleted)
                    VALUES(@id,@c,@rate,@lbl,@def,@act,@srt,@n,@n,1,0);"
                : @"UPDATE vat_rates SET rate=@rate, label=@lbl, is_default=@def, is_active=@act,
                        sort_order=@srt, updated_at=@n, version=version+1
                    WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", newId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@rate", Money.Serialize(rate));
            cmd.AddWithValue("@lbl", (object?)label ?? DBNull.Value);
            cmd.AddWithValue("@def", isDefault ? 1L : 0L);
            cmd.AddWithValue("@act", isActive ? 1L : 0L);
            cmd.AddWithValue("@srt", (long)sortOrder);
            cmd.AddWithValue("@n", now);
            if (cmd.ExecuteNonQuery() == 0 && id is not null)
                throw new ForbiddenException("KDV oranı bulunamadı veya başka firmaya ait.");
        }

        tx.Commit();
        return newId;
    }

    private const string Module = InvoiceService.Module;
}
