using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>Kasa/banka hesabı (okuma).</summary>
public sealed record FinanceAccountRecord(
    string Id, string Code, string Name, string AccountKind, string Currency,
    string? BranchId, string? BranchName, string? BankName, string? BankBranch,
    string? AccountNo, string? Iban, string? Note, bool IsDefault, bool IsActive, long Version)
{
    public string KindText => FinanceAccountKinds.Label(AccountKind);
    public string StatusText => IsActive ? "Aktif" : "Pasif";
    public bool IsBank => AccountKind == FinanceAccountKinds.Bank;
}

/// <summary>Hesap listesi satırı — bakiye SAKLANMAZ, defterden hesaplanır.</summary>
public sealed record FinanceAccountRow(FinanceAccountRecord Account, decimal Inflow, decimal Outflow)
{
    /// <summary>Bakiye = Σ giriş − Σ çıkış. Negatif olabilir (kasa açığı görünür kalsın).</summary>
    public decimal Balance => Inflow - Outflow;
    public string BalanceText => $"{Balance:0.00} {Account.Currency}";
}

/// <summary>Hesap hareketi (okuma) + yürüyen bakiye.</summary>
public sealed record FinanceTxnRow(
    string Id, string AccountId, string AccountName, string TxnType, int Direction, decimal Amount,
    string Currency, long TxnDate, string? PartyId, string? PartyTitle, string? Description,
    string? DocNo, string? PaymentMethod, string? ReferenceNo, string? BranchId, string? BranchName,
    bool IsReversed, string? ReversalOf, string? ReversalReason, string? TransferGroupId)
{
    public string TypeText => FinanceTxnTypes.Label(TxnType);
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(TxnDate).LocalDateTime.ToString("dd.MM.yyyy");
    public decimal In => Direction > 0 ? Amount : 0m;
    public decimal Out => Direction < 0 ? Amount : 0m;
    public bool IsTransfer => TxnType is FinanceTxnTypes.TransferIn or FinanceTxnTypes.TransferOut;
    /// <summary>Ters kayıt hareketi mi (orijinal değil, düzeltme kaydı)?</summary>
    public bool IsReversalEntry => ReversalOf is not null;
}

/// <summary>Hesap ekstresi satırı: hareket + YÜRÜYEN BAKİYE (hesaplanır, saklanmaz).</summary>
public sealed record FinanceStatementRow(FinanceTxnRow Txn, decimal RunningBalance);

/// <summary>
/// Kapatılmayı bekleyen fatura — tahsilat/ödeme ekranının listesi.
/// <c>Remaining</c> SAKLANMAZ: <c>grand_total − Σ(iptal edilmemiş tahsisler)</c>.
/// </summary>
public sealed record OpenInvoiceRow(
    string Id, string InvoiceNo, string Direction, string PartyId, string PartyTitle,
    long InvoiceDate, long? DueDate, string Currency, decimal GrandTotal, decimal Paid)
{
    public decimal Remaining => GrandTotal - Paid;
    public string DirectionText => InvoiceDirections.Label(Direction);
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(InvoiceDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string DueText => DueDate is null ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(DueDate.Value).LocalDateTime.ToString("dd.MM.yyyy");
    /// <summary>Tahsilatla mı ödemeyle mi kapanır? (satış → tahsilat, alış → ödeme)</summary>
    public string SettlesWith => Direction == InvoiceDirections.Sales ? FinanceTxnTypes.Receipt : FinanceTxnTypes.Payment;
}

/// <summary>
/// G4-3 — KASA / BANKA OKUMA KATMANI.
///
/// Yazma tarafı <see cref="FinanceService"/>'tedir; burası yalnız OKUMA. Yetki kapısı burada da
/// uygulanır (okuma yolu yetkiyi ATLAMAZ) ve şube kapsamı <see cref="BranchAccess"/> ile TEK
/// otoriteden geçer — ikinci bir scope sistemi kurulmadı. Tekil okumalarda da (hesap kartı,
/// ekstre) kapsam DENETLENİR: kullanıcı id'yi bilse bile kapsam dışı hesabı okuyamaz.
///
/// <b>HİÇBİR BAKİYE SAKLANMAZ:</b> hesap bakiyesi de faturanın kalanı da bu sınıfta HESAPLANIR.
/// Toplama C#'ta <see cref="decimal"/> ile yapılır (tutarlar TEXT'tir; SQL SUM kayan noktaya düşer).
/// </summary>
public sealed class FinanceQueryService
{
    private const string Module = FinanceService.Module;
    private readonly IDbConnectionFactory _factory;

    public FinanceQueryService(IDbConnectionFactory factory) => _factory = factory;

    // ═══════════════════════════════════════════════════════════════════════
    //  HESAPLAR
    // ═══════════════════════════════════════════════════════════════════════

    private const string AccountSelect = @"
SELECT a.id, a.code, a.name, a.account_kind, a.currency_code, a.branch_id, b.name,
       a.bank_name, a.bank_branch, a.account_no, a.iban, a.note, a.is_default, a.is_active, a.version
FROM finance_accounts a LEFT JOIN branches b ON b.id=a.branch_id";

    private static FinanceAccountRecord ReadAccount(DbDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10),
        r.IsDBNull(11) ? null : r.GetString(11),
        Convert.ToInt64(r.GetValue(12)) != 0, Convert.ToInt64(r.GetValue(13)) != 0,
        Convert.ToInt64(r.GetValue(14)));

    /// <summary>
    /// Hesap listesi + bakiyeler. Bakiyeler TEK sorguda toplanır (hesap başına ayrı sorgu = N+1 YOK).
    /// Şube kapsamı uygulanır: belirli şubeyle çalışan kullanıcı yalnız kendi şubesinin ve şubesiz
    /// (firma geneli) hesapları görür.
    /// </summary>
    public IReadOnlyList<FinanceAccountRow> Accounts(SessionContext s, string? kind = null,
        bool onlyActive = true, string? search = null, IReadOnlyList<string>? branchIds = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        var where = " WHERE a.company_id=@c AND a.is_deleted=0";
        if (onlyActive) where += " AND a.is_active=1";
        if (FinanceAccountKinds.IsValid(kind)) where += " AND a.account_kind=@k";
        if (!string.IsNullOrWhiteSpace(search))
            where += $" AND ({SqlDialect.LikeTr(conn, "a.code", "@q")} OR {SqlDialect.LikeTr(conn, "a.name", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "COALESCE(a.bank_name,'')", "@q")})";
        // ⭐ G4-3b: şube kapsamı BranchAccess'ten gelir (izinli ∩ istenen). Kullanıcı elle branchIds
        // göndererek kapsamını genişletemez — kesişim alınır.
        where += BranchAccess.Sql(s, "a.branch_id", branchIds);

        var records = new List<FinanceAccountRecord>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = AccountSelect + where + " ORDER BY a.account_kind, a.code;";
            cmd.AddWithValue("@c", s.CompanyId);
            if (FinanceAccountKinds.IsValid(kind)) cmd.AddWithValue("@k", kind!);
            if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
            BranchAccess.Bind(cmd, s, branchIds);
            using var r = cmd.ExecuteReader();
            while (r.Read()) records.Add(ReadAccount(r));
        }
        if (records.Count == 0) return Array.Empty<FinanceAccountRow>();

        var totals = Totals(conn, s.CompanyId, records.Select(x => x.Id).ToList());
        // ⭐ FAZ 3b: karar SORGU başına bir kez — satır döngüsünün içinde değil.
        // Bakiye = Giriş − Çıkış → biri açıkta kalırsa bakiye de açıkta kalır; ikisi birlikte gizlenir.
        var bakiyeGorunur = AccountingFieldGate.HesapBakiyesi(s);
        return records.Select(a =>
        {
            totals.TryGetValue(a.Id, out var t);
            return bakiyeGorunur ? new FinanceAccountRow(a, t.In, t.Out) : new FinanceAccountRow(a, 0m, 0m);
        }).ToList();
    }

    /// <summary>Tek hesap kartı.</summary>
    public FinanceAccountRecord Account(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = AccountSelect + " WHERE a.id=@id AND a.company_id=@c AND a.is_deleted=0;";
        cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Kasa/banka hesabı bulunamadı veya başka firmaya ait.");
        var acc = ReadAccount(r);
        // ⭐ Kapsam kapısı: id bilinse bile kapsam dışı hesap OKUNAMAZ.
        BranchAccess.Require(s, acc.BranchId, "hesap görüntüleme");
        return acc;
    }

    /// <summary>Tek hesabın bakiyesi — defterden hesaplanır.</summary>
    public decimal Balance(SessionContext s, string accountId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        Account(s, accountId);   // ⭐ kapsam + firma kapısı
        if (!AccountingFieldGate.HesapBakiyesi(s)) return 0m;   // FAZ 3b: bakiye gizli
        using var conn = _factory.Create();
        var t = Totals(conn, s.CompanyId, new[] { accountId });
        return t.TryGetValue(accountId, out var v) ? v.In - v.Out : 0m;
    }

    /// <summary>Verilen hesapların giriş/çıkış toplamları — TEK sorgu, C#'ta decimal toplama.
    /// İptal edilmiş (<c>is_reversed=1</c>) hareketler ve ters kayıtları HARİÇTİR.</summary>
    private static Dictionary<string, (decimal In, decimal Out)> Totals(
        DbConnection conn, string companyId, IReadOnlyList<string> accountIds)
    {
        var map = new Dictionary<string, (decimal, decimal)>(StringComparer.Ordinal);
        if (accountIds.Count == 0) return map;

        using var cmd = conn.CreateCommand();
        var names = new List<string>(accountIds.Count);
        for (int i = 0; i < accountIds.Count; i++) { var p = "@a" + i; names.Add(p); cmd.AddWithValue(p, accountIds[i]); }
        cmd.CommandText =
            $"SELECT account_id, direction, amount FROM finance_transactions " +
            $"WHERE company_id=@c AND is_reversed=0 AND account_id IN ({string.Join(",", names)});";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var acc = r.GetString(0);
            var dir = Convert.ToInt64(r.GetValue(1));
            var amt = Money.Parse(r.GetString(2));
            map.TryGetValue(acc, out var cur);
            map[acc] = dir > 0 ? (cur.Item1 + amt, cur.Item2) : (cur.Item1, cur.Item2 + amt);
        }
        return map;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  HAREKETLER
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hesap hareketleri + YÜRÜYEN BAKİYE. Yürüyen bakiye SUNUCUDA hesaplanır → web ve masaüstü
    /// aynı sayıyı gösterir. İptal edilmiş hareketler listede GÖRÜNÜR (iz kalır) ama bakiyeye girmez.
    /// </summary>
    public IReadOnlyList<FinanceStatementRow> Statement(SessionContext s, string accountId,
        long? from = null, long? to = null, int limit = 500)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        limit = limit < 1 ? 1 : (limit > 2000 ? 2000 : limit);
        Account(s, accountId);   // ⭐ kapsam + firma kapısı (kapsam dışı hesabın ekstresi okunamaz)

        using var conn = _factory.Create();
        var where = " WHERE t.company_id=@c AND t.account_id=@a";
        if (from is not null) where += " AND t.txn_date>=@from";
        if (to is not null) where += " AND t.txn_date<=@to";

        var rows = new List<FinanceTxnRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = TxnSelect + where + " ORDER BY t.txn_date, t.created_at LIMIT @lim;";
            cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@a", accountId);
            if (from is not null) cmd.AddWithValue("@from", from.Value);
            if (to is not null) cmd.AddWithValue("@to", to.Value);
            cmd.AddWithValue("@lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add(ReadTxn(r));
        }

        // ⭐ FAZ 3b: karar SORGU başına bir kez — satır döngüsünün içinde değil.
        var tutarGorunur = AccountingFieldGate.HareketTutari(s);
        var bakiyeGorunur = AccountingFieldGate.HesapBakiyesi(s);

        var list = new List<FinanceStatementRow>(rows.Count);
        decimal running = 0m;
        foreach (var t in rows)
        {
            if (!t.IsReversed) running += t.Direction * t.Amount;   // iptal edilenler bakiyeye GİRMEZ
            // Tutar gizliyken yürüyen bakiye de gizlenir (ardışık iki satırın farkı tutarı verirdi).
            list.Add(new FinanceStatementRow(
                tutarGorunur ? t : t with { Amount = 0m },
                tutarGorunur && bakiyeGorunur ? running : 0m));
        }
        return list;
    }

    /// <summary>Firma geneli hareket listesi — filtre + sayfalama (tüm kayıtlar RAM'e çekilmez).</summary>
    public GridResult<FinanceTxnRow> Transactions(SessionContext s, string? accountId = null,
        string? txnType = null, string? partyId = null, string? search = null,
        long? from = null, long? to = null, int page = 1, int pageSize = 50,
        IReadOnlyList<string>? branchIds = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 ? 1 : (pageSize > 500 ? 500 : pageSize);

        using var conn = _factory.Create();
        var where = " WHERE t.company_id=@c";
        if (!string.IsNullOrWhiteSpace(accountId)) where += " AND t.account_id=@a";
        if (FinanceTxnTypes.IsValid(txnType)) where += " AND t.txn_type=@type";
        if (!string.IsNullOrWhiteSpace(partyId)) where += " AND t.party_id=@p";
        if (from is not null) where += " AND t.txn_date>=@from";
        if (to is not null) where += " AND t.txn_date<=@to";
        if (!string.IsNullOrWhiteSpace(search))
            where += $" AND ({SqlDialect.LikeTr(conn, "COALESCE(t.description,'')", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "COALESCE(t.doc_no,'')", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "COALESCE(t.reference_no,'')", "@q")}" +
                     $" OR {SqlDialect.LikeTr(conn, "COALESCE(p.title,'')", "@q")})";
        where += BranchAccess.Sql(s, "t.branch_id", branchIds);

        void Bind(DbCommand cmd)
        {
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(accountId)) cmd.AddWithValue("@a", accountId!);
            if (FinanceTxnTypes.IsValid(txnType)) cmd.AddWithValue("@type", txnType!);
            if (!string.IsNullOrWhiteSpace(partyId)) cmd.AddWithValue("@p", partyId!);
            if (from is not null) cmd.AddWithValue("@from", from.Value);
            if (to is not null) cmd.AddWithValue("@to", to.Value);
            if (!string.IsNullOrWhiteSpace(search)) cmd.AddWithValue("@q", "%" + search.Trim() + "%");
            BranchAccess.Bind(cmd, s, branchIds);
        }

        int total;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*)" + TxnFrom + where + ";";
            Bind(cmd);
            total = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ⭐ FAZ 3b: karar SORGU başına bir kez — satır döngüsünün içinde değil.
        var tutarGorunur = AccountingFieldGate.HareketTutari(s);

        var rows = new List<FinanceTxnRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = TxnSelect + where + " ORDER BY t.txn_date DESC, t.created_at DESC LIMIT @lim OFFSET @off;";
            Bind(cmd);
            cmd.AddWithValue("@lim", pageSize);
            cmd.AddWithValue("@off", (page - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read()) { var t = ReadTxn(r); rows.Add(tutarGorunur ? t : t with { Amount = 0m }); }
        }
        return new GridResult<FinanceTxnRow>(rows, total, page, pageSize);
    }

    private const string TxnFrom = @"
FROM finance_transactions t
LEFT JOIN finance_accounts a ON a.id=t.account_id
LEFT JOIN parties p ON p.id=t.party_id
LEFT JOIN branches b ON b.id=t.branch_id";

    private const string TxnSelect = @"
SELECT t.id, t.account_id, COALESCE(a.name,'—'), t.txn_type, t.direction, t.amount, t.currency_code,
       t.txn_date, t.party_id, p.title, t.description, t.doc_no, t.payment_method, t.reference_no,
       t.branch_id, b.name, t.is_reversed, t.reversal_of, t.reversal_reason, t.transfer_group_id" + TxnFrom;

    private static FinanceTxnRow ReadTxn(DbDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), Convert.ToInt32(r.GetValue(4)),
        Money.Parse(r.GetString(5)), r.GetString(6), Convert.ToInt64(r.GetValue(7)),
        r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
        r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
        r.IsDBNull(12) ? null : r.GetString(12), r.IsDBNull(13) ? null : r.GetString(13),
        r.IsDBNull(14) ? null : r.GetString(14), r.IsDBNull(15) ? null : r.GetString(15),
        Convert.ToInt64(r.GetValue(16)) != 0, r.IsDBNull(17) ? null : r.GetString(17),
        r.IsDBNull(18) ? null : r.GetString(18), r.IsDBNull(19) ? null : r.GetString(19));

    // ═══════════════════════════════════════════════════════════════════════
    //  AÇIK FATURALAR (tahsilat / ödeme ekranı)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kapatılmayı bekleyen faturalar. İptal edilmiş faturalar ve tamamı kapanmış olanlar
    /// listelenmez. Ödenen tutar TEK sorguda toplanır (fatura başına ayrı sorgu = N+1 YOK).
    /// </summary>
    public IReadOnlyList<OpenInvoiceRow> OpenInvoices(SessionContext s, string partyId, string? direction = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        limit = limit < 1 ? 1 : (limit > 500 ? 500 : limit);

        // ⭐ FAZ 3b — FAIL-CLOSED. Bu akış BAŞTAN SONA tutarla ilgilidir; tutarı maskelemek
        // listeyi sessizce boşaltır (kalan = 0 → hiçbir fatura görünmez) ve kullanıcı nedenini
        // anlamaz. Bu yüzden değer gizlenmez, EKRAN AÇILMAZ: açık ve doğru hata verilir.
        if (!AccountingFieldGate.FaturaTutari(s))
            throw new ForbiddenException("Fatura tutarlarını görme yetkiniz olmadığı için tahsilat/ödeme ekranını kullanamazsınız.");

        var heads = new List<OpenInvoiceRow>();
        using var conn = _factory.Create();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT i.id, i.invoice_no, i.direction, i.party_id, COALESCE(p.title,'—'), i.invoice_date, " +
                "i.due_date, i.currency_code, i.grand_total " +
                "FROM invoices i LEFT JOIN parties p ON p.id=i.party_id " +
                "WHERE i.company_id=@c AND i.party_id=@p AND i.status=@st" +
                (InvoiceDirections.IsValid(direction) ? " AND i.direction=@d" : "") +
                " ORDER BY i.invoice_date, i.invoice_no LIMIT @lim;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@p", partyId);
            cmd.AddWithValue("@st", InvoiceStatuses.Active);
            if (InvoiceDirections.IsValid(direction)) cmd.AddWithValue("@d", direction!);
            cmd.AddWithValue("@lim", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                heads.Add(new OpenInvoiceRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetString(4), Convert.ToInt64(r.GetValue(5)),
                    r.IsDBNull(6) ? null : Convert.ToInt64(r.GetValue(6)),
                    r.GetString(7), Money.Parse(r.GetString(8)), 0m));
        }
        if (heads.Count == 0) return Array.Empty<OpenInvoiceRow>();

        var paid = PaidTotals(conn, s.CompanyId, heads.Select(x => x.Id).ToList());
        return heads
            .Select(h => h with { Paid = paid.TryGetValue(h.Id, out var v) ? v : 0m })
            .Where(h => h.Remaining > 0)          // tamamı kapanmış fatura listelenmez
            .ToList();
    }

    /// <summary>Faturanın kapanan tutarı — tekil sorgu (fatura detay ekranı için).</summary>
    public decimal PaidOf(SessionContext s, string invoiceId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (!AccountingFieldGate.FaturaTutari(s)) return 0m;   // FAZ 3b: fatura tutarı gizli
        using var conn = _factory.Create();
        var map = PaidTotals(conn, s.CompanyId, new[] { invoiceId });
        return map.TryGetValue(invoiceId, out var v) ? v : 0m;
    }

    /// <summary>Verilen faturaların kapanan tutarları — TEK sorgu, C#'ta decimal toplama.
    /// İptal edilmiş tahsisler (<c>is_reversed=1</c>) SAYILMAZ → tahsilat iptali kalanı geri artırır.</summary>
    internal static Dictionary<string, decimal> PaidTotals(DbConnection conn, string companyId, IReadOnlyList<string> invoiceIds)
    {
        var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (invoiceIds.Count == 0) return map;

        using var cmd = conn.CreateCommand();
        var names = new List<string>(invoiceIds.Count);
        for (int i = 0; i < invoiceIds.Count; i++) { var p = "@i" + i; names.Add(p); cmd.AddWithValue(p, invoiceIds[i]); }
        cmd.CommandText =
            $"SELECT invoice_id, amount FROM invoice_allocations " +
            $"WHERE company_id=@c AND is_reversed=0 AND invoice_id IN ({string.Join(",", names)});";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var inv = r.GetString(0);
            map.TryGetValue(inv, out var cur);
            map[inv] = cur + Money.Parse(r.GetString(1));
        }
        return map;
    }
}
