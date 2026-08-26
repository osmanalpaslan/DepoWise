using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// G4-4 — ÖN MUHASEBE RAPORLARI (kullanıcı isteği 2026-08-12).
///
/// <b>⚠️ İKİNCİ FİNANSAL GERÇEKLİK YOKTUR.</b> Bu raporlar YALNIZ mevcut defterlerden OKUR:
/// <c>party_ledger</c> · <c>invoices</c> · <c>invoice_allocations</c> · <c>finance_transactions</c>.
/// Rapor için özet/bakiye tablosu OLUŞTURULMAZ; ekranlardaki servislerle aynı hesabı yaparlar
/// (cari bakiyesi <c>Σ(direction × amount)</c>, fatura kalanı tahsislerden, hesap bakiyesi defterden).
///
/// <b>ŞUBE KAPSAMI:</b> her sorgu <see cref="ReportScope.BranchSql"/> üzerinden geçer; o da
/// <see cref="BranchAccess"/>'e bağlıdır → <c>İZİNLİ ∩ İSTENEN</c>. Kullanıcı isteğe elle yetkisiz
/// bir <c>branchId</c> yazarsa kesişimde düşer; veri sızmaz. UI kapı DEĞİLDİR.
///
/// <b>PERFORMANS:</b> filtreler (tarih, şube, cari) SQL'e iner; tüm firma çekilip bellekte
/// süzülmez. Para toplamaları C#'ta <see cref="decimal"/> ile yapılır (tutarlar TEXT'tir;
/// SQL SUM kayan noktaya düşer — <see cref="Money"/> kuralı).
///
/// <b>ŞUBESİZ KAYITLAR:</b> <c>branch_id IS NULL</c> satırlar gizlenmez (eski/firma geneli veri
/// görünmez olmasın) — <see cref="BranchScope"/>/<see cref="ReportScope"/> ile aynı ilke.
/// </summary>
internal static class AccountingReports
{
    /// <summary>Cari filtresi (0 veya çok cari). Boş → filtre yok.</summary>
    private static string PartySql(ReportRequest req, string col)
    {
        var p = req.PartyIds;
        if (p is null || p.Count == 0) return "";
        var ps = string.Join(",", Enumerable.Range(0, p.Count).Select(i => "@pty" + i));
        return $" AND {col} IN ({ps})";
    }

    private static void BindParty(DbCommand cmd, ReportRequest req)
    {
        var p = req.PartyIds;
        if (p is null) return;
        for (int i = 0; i < p.Count; i++) cmd.AddWithValue("@pty" + i, p[i]);
    }

    /// <summary>Tarih filtresi (kolon adı raporuna göre değişir).</summary>
    private static string DateSql(ReportRequest req, string col)
        => (req.FromDate is null ? "" : $" AND {col} >= @from")
         + (req.ToDate is null ? "" : $" AND {col} <= @to");

    private static void BindDate(DbCommand cmd, ReportRequest req)
    {
        if (req.FromDate is not null) cmd.AddWithValue("@from", req.FromDate.Value);
        if (req.ToDate is not null) cmd.AddWithValue("@to", req.ToDate.Value);
    }

    private static string Tarih(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("dd.MM.yyyy");

    // ═══════════════════════════════════════════════════════════════════════════
    //  1) CARİ EKSTRE — hareket dökümü + YÜRÜYEN BAKİYE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Seçili carinin (veya tüm carilerin) hareketleri, kronolojik + yürüyen bakiye.
    ///
    /// <b>Yürüyen bakiye SEÇİLİ ŞUBE KAPSAMINA göredir</b> — çoklu şube seçilirse o şubelerin
    /// hareketleri birleşik yürür. İptal edilen hareketler SATIR olarak görünür ama bakiyeye girmez
    /// (defterdeki iz korunur, tutar bozulmaz).
    /// </summary>
    public static TableModel Statement(IDbConnectionFactory factory, SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, PartyService.Module, PermissionAction.View);
        // ⭐ RPR-14 (denetim 2026-08-26): rapor ekranındaki "Firma (Süper Admin)" seçimi burada
        // YOK SAYILIYORDU (doğrudan s.CompanyId kullanılıyordu) → süper admin B'yi seçse de A'nın mali
        // verisi geliyordu. Diğer 15 raporla AYNI çözüm: süper admin seçebilir, diğerleri kendi firması
        // (yabancı firma istenirse 403 — davranış onlar için değişmez).
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT l.entry_date, p.code, p.title, l.doc_type, l.doc_no, l.description,
       l.direction, l.amount, l.currency_code, l.is_reversed, COALESCE(b.name,'') , l.created_at
FROM party_ledger l
LEFT JOIN parties p ON p.id = l.party_id
LEFT JOIN branches b ON b.id = l.branch_id
WHERE l.company_id=@c"
            + DateSql(req, "l.entry_date")
            + PartySql(req, "l.party_id")
            + ReportScope.BranchSql(s, req, "l.branch_id")
            + " ORDER BY l.entry_date, l.created_at;";
        cmd.AddWithValue("@c", companyId);
        BindDate(cmd, req);
        BindParty(cmd, req);
        ReportScope.BindBranch(cmd, s, req);

        var rows = new List<IReadOnlyList<object?>>();
        decimal borcT = 0, alacakT = 0, yuruyen = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var dir = Convert.ToInt64(r.GetValue(6));
                var tutar = Money.Parse(r.GetString(7));
                var iptal = Convert.ToInt64(r.GetValue(9)) != 0;
                var borc = dir > 0 ? tutar : 0m;
                var alacak = dir < 0 ? tutar : 0m;

                if (!iptal)
                {
                    borcT += borc; alacakT += alacak;
                    yuruyen += dir * tutar;      // iptal edilen bakiyeye GİRMEZ
                }

                rows.Add(new object?[]
                {
                    Tarih(Convert.ToInt64(r.GetValue(0))),
                    r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    PartyDocTypes.Label(r.GetString(3)),
                    r.IsDBNull(4) ? "" : r.GetString(4),
                    r.IsDBNull(5) ? "" : r.GetString(5),
                    r.GetString(10),
                    borc, alacak, yuruyen,
                    iptal ? "İPTAL" : "",
                });
            }

        return new TableModel(
            "Cari Ekstre",
            new[] { "TARİH", "CARİ KODU", "CARİ", "TÜR", "BELGE NO", "AÇIKLAMA", "ŞUBE", "BORÇ", "ALACAK", "BAKİYE", "DURUM" },
            rows,
            Numeric: new[] { false, false, false, false, false, false, false, true, true, true, false },
            TotalRow: new object?[] { "TOPLAM", "", "", "", "", "", "", borcT, alacakT, borcT - alacakT, "" });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  2) CARİ BAKİYE ÖZETİ — cari başına borç / alacak / bakiye
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cari başına toplam. <b>Bakiye = Borç − Alacak</b> (pozitif: cari BİZE borçlu).
    /// Toplam satırı, seçili şube kapsamındaki carilerin toplamıdır — kullanıcının erişemediği
    /// şubenin hareketi bu toplama GİRMEZ.
    /// </summary>
    public static TableModel Balances(IDbConnectionFactory factory, SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, PartyService.Module, PermissionAction.View);
        // ⭐ RPR-14 (denetim 2026-08-26): rapor ekranındaki "Firma (Süper Admin)" seçimi burada
        // YOK SAYILIYORDU (doğrudan s.CompanyId kullanılıyordu) → süper admin B'yi seçse de A'nın mali
        // verisi geliyordu. Diğer 15 raporla AYNI çözüm: süper admin seçebilir, diğerleri kendi firması
        // (yabancı firma istenirse 403 — davranış onlar için değişmez).
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();

        // Toplama C#'ta yapılır (amount TEXT). Satır sayısı cari × hareket değil, ham hareket kadar;
        // filtreler SQL'e indiği için tüm firma çekilmez.
        cmd.CommandText = @"
SELECT l.party_id, p.code, p.title, l.direction, l.amount, l.entry_date
FROM party_ledger l
LEFT JOIN parties p ON p.id = l.party_id
WHERE l.company_id=@c AND l.is_reversed=0"
            + DateSql(req, "l.entry_date")
            + PartySql(req, "l.party_id")
            + ReportScope.BranchSql(s, req, "l.branch_id") + ";";
        cmd.AddWithValue("@c", companyId);
        BindDate(cmd, req);
        BindParty(cmd, req);
        ReportScope.BindBranch(cmd, s, req);

        var map = new Dictionary<string, (string Code, string Title, decimal Debit, decimal Credit, long Last)>(StringComparer.Ordinal);
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var pid = r.GetString(0);
                var dir = Convert.ToInt64(r.GetValue(3));
                var amt = Money.Parse(r.GetString(4));
                var date = Convert.ToInt64(r.GetValue(5));
                map.TryGetValue(pid, out var cur);
                map[pid] = (
                    r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    cur.Debit + (dir > 0 ? amt : 0m),
                    cur.Credit + (dir < 0 ? amt : 0m),
                    Math.Max(cur.Last, date));
            }

        decimal bT = 0, aT = 0;
        var rows = map.Values
            .OrderBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                bT += x.Debit; aT += x.Credit;
                var bakiye = x.Debit - x.Credit;
                return (IReadOnlyList<object?>)new object?[]
                {
                    x.Code, x.Title, x.Debit, x.Credit, bakiye,
                    bakiye > 0 ? "Borçlu" : bakiye < 0 ? "Alacaklı" : "Kapalı",
                    x.Last == 0 ? "" : Tarih(x.Last),
                };
            }).ToList();

        return new TableModel(
            "Cari Bakiye Özeti",
            new[] { "CARİ KODU", "CARİ", "BORÇ", "ALACAK", "BAKİYE", "DURUM", "SON HAREKET" },
            rows,
            Numeric: new[] { false, false, true, true, true, false, false },
            TotalRow: new object?[] { "TOPLAM", "", bT, aT, bT - aT, "", "" });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  3) FATURA ÖZETİ
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Faturalar + KALAN tutar. Kalan tahsislerden hesaplanır; <c>invoices</c>'ta saklanmaz.</summary>
    public static TableModel Invoices(IDbConnectionFactory factory, SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, InvoiceService.Module, PermissionAction.View);
        // ⭐ RPR-14 (denetim 2026-08-26): rapor ekranındaki "Firma (Süper Admin)" seçimi burada
        // YOK SAYILIYORDU (doğrudan s.CompanyId kullanılıyordu) → süper admin B'yi seçse de A'nın mali
        // verisi geliyordu. Diğer 15 raporla AYNI çözüm: süper admin seçebilir, diğerleri kendi firması
        // (yabancı firma istenirse 403 — davranış onlar için değişmez).
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = factory.Create();

        var ids = new List<string>();
        var rows = new List<IReadOnlyList<object?>>();
        var ham = new List<(string Id, object?[] Row, decimal Total, bool Cancelled)>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT i.id, i.invoice_no, i.direction, COALESCE(p.code,''), COALESCE(p.title,'—'),
       i.invoice_date, i.due_date, i.grand_total, i.status, COALESCE(b.name,''), i.currency_code, i.external_no
FROM invoices i
LEFT JOIN parties p ON p.id = i.party_id
LEFT JOIN branches b ON b.id = i.branch_id
WHERE i.company_id=@c"
                + DateSql(req, "i.invoice_date")
                + PartySql(req, "i.party_id")
                + ReportScope.BranchSql(s, req, "i.branch_id")
                + " ORDER BY i.invoice_date, i.invoice_no;";
            cmd.AddWithValue("@c", companyId);
            BindDate(cmd, req);
            BindParty(cmd, req);
            ReportScope.BindBranch(cmd, s, req);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                var total = Money.Parse(r.GetString(7));
                var iptal = r.GetString(8) == InvoiceStatuses.Cancelled;
                ids.Add(id);
                ham.Add((id, new object?[]
                {
                    Tarih(Convert.ToInt64(r.GetValue(5))),
                    r.GetString(1),
                    InvoiceDirections.Label(r.GetString(2)),
                    r.GetString(3), r.GetString(4),
                    r.GetString(9),
                    r.IsDBNull(11) ? "" : r.GetString(11),
                    r.IsDBNull(6) ? "" : Tarih(Convert.ToInt64(r.GetValue(6))),
                    total, 0m, 0m,
                    iptal ? "İptal" : "Yürürlükte",
                }, total, iptal));
            }
        }

        // Ödenen tutarlar TEK sorguda (fatura başına ayrı sorgu = N+1 YOK).
        var paid = FinanceQueryService.PaidTotals(conn, companyId, ids);

        decimal tT = 0, oT = 0, kT = 0;
        foreach (var (id, row, total, iptal) in ham)
        {
            var odenen = paid.TryGetValue(id, out var v) ? v : 0m;
            var kalan = iptal ? 0m : total - odenen;
            row[9] = odenen; row[10] = kalan;
            if (!iptal) { tT += total; oT += odenen; kT += kalan; }
            rows.Add(row);
        }

        return new TableModel(
            "Fatura Özeti",
            new[] { "TARİH", "FATURA NO", "TÜR", "CARİ KODU", "CARİ", "ŞUBE", "BELGE NO", "VADE", "TUTAR", "ÖDENEN", "KALAN", "DURUM" },
            rows,
            Numeric: new[] { false, false, false, false, false, false, false, false, true, true, true, false },
            TotalRow: new object?[] { "TOPLAM", "", "", "", "", "", "", "", tT, oT, kT, "" });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  4) AÇIK FATURALAR / VADE
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Kapanmamış (kalanı > 0) yürürlükteki faturalar + gecikme günü.</summary>
    public static TableModel OpenInvoices(IDbConnectionFactory factory, SessionContext s, ReportRequest req, IClock clock)
    {
        AccessControl.Require(s, InvoiceService.Module, PermissionAction.View);
        // ⭐ RPR-14 (denetim 2026-08-26): rapor ekranındaki "Firma (Süper Admin)" seçimi burada
        // YOK SAYILIYORDU (doğrudan s.CompanyId kullanılıyordu) → süper admin B'yi seçse de A'nın mali
        // verisi geliyordu. Diğer 15 raporla AYNI çözüm: süper admin seçebilir, diğerleri kendi firması
        // (yabancı firma istenirse 403 — davranış onlar için değişmez).
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = factory.Create();

        var ids = new List<string>();
        var ham = new List<(string Id, object?[] Row, decimal Total, long? Due)>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT i.id, i.invoice_no, i.direction, COALESCE(p.code,''), COALESCE(p.title,'—'),
       i.invoice_date, i.due_date, i.grand_total, COALESCE(b.name,'')
FROM invoices i
LEFT JOIN parties p ON p.id = i.party_id
LEFT JOIN branches b ON b.id = i.branch_id
WHERE i.company_id=@c AND i.status=@st"
                + PartySql(req, "i.party_id")
                + ReportScope.BranchSql(s, req, "i.branch_id")
                + " ORDER BY i.due_date, i.invoice_date;";
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@st", InvoiceStatuses.Active);
            BindParty(cmd, req);
            ReportScope.BindBranch(cmd, s, req);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                ids.Add(id);
                ham.Add((id, new object?[]
                {
                    r.GetString(1),
                    InvoiceDirections.Label(r.GetString(2)),
                    r.GetString(3), r.GetString(4), r.GetString(8),
                    Tarih(Convert.ToInt64(r.GetValue(5))),
                    r.IsDBNull(6) ? "" : Tarih(Convert.ToInt64(r.GetValue(6))),
                    Money.Parse(r.GetString(7)), 0m, 0m, "",
                }, Money.Parse(r.GetString(7)), r.IsDBNull(6) ? null : Convert.ToInt64(r.GetValue(6))));
            }
        }

        var paid = FinanceQueryService.PaidTotals(conn, companyId, ids);
        var now = clock.UtcNow.ToUnixTimeMilliseconds();

        var rows = new List<IReadOnlyList<object?>>();
        decimal tT = 0, oT = 0, kT = 0;
        foreach (var (id, row, total, due) in ham)
        {
            var odenen = paid.TryGetValue(id, out var v) ? v : 0m;
            var kalan = total - odenen;
            if (kalan <= 0) continue;              // kapanmış fatura AÇIK listede yer almaz
            row[8] = odenen; row[9] = kalan;
            row[10] = due is not null && due < now
                ? $"{(now - due.Value) / 86_400_000L} gün"
                : "";
            tT += total; oT += odenen; kT += kalan;
            rows.Add(row);
        }

        return new TableModel(
            "Açık Faturalar / Vade",
            new[] { "FATURA NO", "TÜR", "CARİ KODU", "CARİ", "ŞUBE", "TARİH", "VADE", "TUTAR", "ÖDENEN", "KALAN", "GECİKME" },
            rows,
            Numeric: new[] { false, false, false, false, false, false, false, true, true, true, false },
            TotalRow: new object?[] { "TOPLAM", "", "", "", "", "", "", tT, oT, kT, "" });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  5) TAHSİLAT / ÖDEME ÖZETİ
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Yalnız CARİ ETKİLEYEN hareketler (tahsilat/ödeme). Transfer/açılış Kasa raporundadır.</summary>
    public static TableModel Payments(IDbConnectionFactory factory, SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, FinanceService.Module, PermissionAction.View);
        // ⭐ RPR-14 (denetim 2026-08-26): rapor ekranındaki "Firma (Süper Admin)" seçimi burada
        // YOK SAYILIYORDU (doğrudan s.CompanyId kullanılıyordu) → süper admin B'yi seçse de A'nın mali
        // verisi geliyordu. Diğer 15 raporla AYNI çözüm: süper admin seçebilir, diğerleri kendi firması
        // (yabancı firma istenirse 403 — davranış onlar için değişmez).
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = factory.Create();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
SELECT t.txn_date, t.txn_type, COALESCE(p.code,''), COALESCE(p.title,'—'), COALESCE(a.name,'—'),
       t.amount, t.currency_code, t.payment_method, t.doc_no, t.reference_no,
       t.is_reversed, COALESCE(b.name,''), t.description
FROM finance_transactions t
LEFT JOIN parties p ON p.id = t.party_id
LEFT JOIN finance_accounts a ON a.id = t.account_id
LEFT JOIN branches b ON b.id = t.branch_id
WHERE t.company_id=@c AND t.txn_type IN (@tr,@tp)"
            + DateSql(req, "t.txn_date")
            + PartySql(req, "t.party_id")
            + ReportScope.BranchSql(s, req, "t.branch_id")
            + " ORDER BY t.txn_date, t.created_at;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@tr", FinanceTxnTypes.Receipt);
        cmd.AddWithValue("@tp", FinanceTxnTypes.Payment);
        BindDate(cmd, req);
        BindParty(cmd, req);
        ReportScope.BindBranch(cmd, s, req);

        var rows = new List<IReadOnlyList<object?>>();
        decimal tahsilatT = 0, odemeT = 0;
        using (var r = cmd.ExecuteReader())
            while (r.Read())
            {
                var tur = r.GetString(1);
                var tutar = Money.Parse(r.GetString(5));
                var iptal = Convert.ToInt64(r.GetValue(10)) != 0;
                var tahsilat = tur == FinanceTxnTypes.Receipt ? tutar : 0m;
                var odeme = tur == FinanceTxnTypes.Payment ? tutar : 0m;
                if (!iptal) { tahsilatT += tahsilat; odemeT += odeme; }

                rows.Add(new object?[]
                {
                    Tarih(Convert.ToInt64(r.GetValue(0))),
                    FinanceTxnTypes.Label(tur),
                    r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(11),
                    r.IsDBNull(7) ? "" : r.GetString(7),
                    r.IsDBNull(8) ? "" : r.GetString(8),
                    r.IsDBNull(9) ? "" : r.GetString(9),
                    tahsilat, odeme,
                    iptal ? "İPTAL" : "",
                });
            }

        return new TableModel(
            "Tahsilat / Ödeme Özeti",
            new[] { "TARİH", "TÜR", "CARİ KODU", "CARİ", "HESAP", "ŞUBE", "YÖNTEM", "BELGE NO", "REFERANS", "TAHSİLAT", "ÖDEME", "DURUM" },
            rows,
            Numeric: new[] { false, false, false, false, false, false, false, false, false, true, true, false },
            TotalRow: new object?[] { "TOPLAM", "", "", "", "", "", "", "", "", tahsilatT, odemeT, "" });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  6) KASA / BANKA ÖZETİ
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hesap başına DÖNEM giriş/çıkış + GÜNCEL bakiye.
    /// Dönem hareketi tarih filtresine tabidir; bakiye TÜM hareketlerden hesaplanır — ikisi bilinçli
    /// olarak ayrıdır (kullanıcı "bu ay ne girdi" ile "şu an kasada ne var"ı birlikte görür).
    /// </summary>
    public static TableModel Cash(IDbConnectionFactory factory, SessionContext s, ReportRequest req)
    {
        AccessControl.Require(s, FinanceService.Module, PermissionAction.View);
        // ⭐ RPR-14 (denetim 2026-08-26): rapor ekranındaki "Firma (Süper Admin)" seçimi burada
        // YOK SAYILIYORDU (doğrudan s.CompanyId kullanılıyordu) → süper admin B'yi seçse de A'nın mali
        // verisi geliyordu. Diğer 15 raporla AYNI çözüm: süper admin seçebilir, diğerleri kendi firması
        // (yabancı firma istenirse 403 — davranış onlar için değişmez).
        var companyId = ReportGate.ResolveCompany(s, req.CompanyId);
        using var conn = factory.Create();

        var hesap = new Dictionary<string, (string Code, string Name, string Kind, string Branch, string Cur)>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT a.id, a.code, a.name, a.account_kind, COALESCE(b.name,''), a.currency_code
FROM finance_accounts a LEFT JOIN branches b ON b.id = a.branch_id
WHERE a.company_id=@c AND a.is_deleted=0"
                + ReportScope.BranchSql(s, req, "a.branch_id") + " ORDER BY a.account_kind, a.code;";
            cmd.AddWithValue("@c", companyId);
            ReportScope.BindBranch(cmd, s, req);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                hesap[r.GetString(0)] = (r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5));
        }
        if (hesap.Count == 0)
            return new TableModel("Kasa / Banka Özeti",
                new[] { "KOD", "HESAP", "TÜR", "ŞUBE", "DÖNEM GİRİŞ", "DÖNEM ÇIKIŞ", "BAKİYE" },
                Array.Empty<IReadOnlyList<object?>>(),
                Numeric: new[] { false, false, false, false, true, true, true });

        // Hareketler TEK sorguda (hesap başına ayrı sorgu = N+1 YOK). Dönem/bakiye ayrımı C#'ta.
        var donem = new Dictionary<string, (decimal In, decimal Out)>(StringComparer.Ordinal);
        var bakiye = new Dictionary<string, decimal>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            var names = hesap.Keys.ToList();
            var ps = string.Join(",", Enumerable.Range(0, names.Count).Select(i => "@a" + i));
            for (int i = 0; i < names.Count; i++) cmd.AddWithValue("@a" + i, names[i]);
            cmd.CommandText =
                $"SELECT account_id, direction, amount, txn_date FROM finance_transactions " +
                $"WHERE company_id=@c AND is_reversed=0 AND account_id IN ({ps});";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var acc = r.GetString(0);
                var dir = Convert.ToInt64(r.GetValue(1));
                var amt = Money.Parse(r.GetString(2));
                var date = Convert.ToInt64(r.GetValue(3));

                bakiye.TryGetValue(acc, out var b);
                bakiye[acc] = b + dir * amt;

                if ((req.FromDate is null || date >= req.FromDate) && (req.ToDate is null || date <= req.ToDate))
                {
                    donem.TryGetValue(acc, out var d);
                    donem[acc] = dir > 0 ? (d.In + amt, d.Out) : (d.In, d.Out + amt);
                }
            }
        }

        decimal gT = 0, cT = 0, bT = 0;
        var rows = hesap
            .OrderBy(x => x.Value.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Value.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                donem.TryGetValue(x.Key, out var d);
                bakiye.TryGetValue(x.Key, out var b);
                gT += d.In; cT += d.Out; bT += b;
                return (IReadOnlyList<object?>)new object?[]
                {
                    x.Value.Code, x.Value.Name, FinanceAccountKinds.Label(x.Value.Kind),
                    x.Value.Branch, d.In, d.Out, b,
                };
            }).ToList();

        return new TableModel(
            "Kasa / Banka Özeti",
            new[] { "KOD", "HESAP", "TÜR", "ŞUBE", "DÖNEM GİRİŞ", "DÖNEM ÇIKIŞ", "BAKİYE" },
            rows,
            Numeric: new[] { false, false, false, false, true, true, true },
            TotalRow: new object?[] { "TOPLAM", "", "", "", gT, cT, bT });
    }
}
