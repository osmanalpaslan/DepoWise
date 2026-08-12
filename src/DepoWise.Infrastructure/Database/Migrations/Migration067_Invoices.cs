using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// G4-2 — FATURA ALTYAPISI (kullanıcı isteği 2026-08-12).
///
/// Dört tablo:
/// <list type="bullet">
/// <item><c>invoice_series</c> — belge serisi ve sıra numarası (A, B, ... + sıradaki no).</item>
/// <item><c>vat_rates</c> — KDV oranı kataloğu.</item>
/// <item><c>invoices</c> — fatura başlığı.</item>
/// <item><c>invoice_lines</c> — fatura satırı.</item>
/// </list>
///
/// <b>⚠️ PARALEL STOK YOK:</b> fatura stok tablolarını DOĞRUDAN YAZMAZ. Stok etkisi yalnız
/// <c>StockService.ReceiveInTx / IssueOutTx</c> üzerinden oluşur; oluşan belgenin kimliği burada
/// <c>invoices.stock_document_id</c> olarak SAKLANIR (kopyalanmaz, referans verilir). Stok defterinin
/// tek yazıcısı <c>StockService</c> olmaya devam eder.
///
/// <b>⚠️ PARALEL CARİ YOK:</b> para etkisi yalnız <c>PartyLedgerService.AddFromDocumentTx</c> ile
/// yazılır; sonucu <c>invoices.ledger_entry_id</c> olarak referanslanır. Fatura üzerinde SAKLANAN
/// BİR CARİ BAKİYE YOKTUR — bakiye her zaman <c>Σ(direction × amount)</c>'tan hesaplanır.
///
/// <b>TÜRKİYE'YE ÖZGÜ KURALLAR SABİTLENMEZ (kullanıcı kuralı):</b> KDV oranı <c>vat_rates</c>
/// tablosundan, belge serisi <c>invoice_series</c> tablosundan gelir; tevkifat oranı fatura
/// SATIRINDA VERİ olarak durur. Kodda sabit oran/seri/tevkifat yoktur — oranlar değişirse
/// migration değil, kayıt güncellenir.
///
/// <b>SİLME YOK:</b> fatura fiziksel silinmez (CLAUDE.md §4). İptal, <c>status='cancelled'</c> +
/// TERS kayıtlarla yürür: <c>cancel_stock_document_id</c> ve <c>cancel_ledger_entry_id</c> ters
/// belgeleri işaret eder. Çift iptal, <c>status</c> kontrolüyle engellenir.
///
/// <b>IDEMPOTENCY:</b> <c>invoices.operation_id</c> üzerinde kısmi tekil indeks vardır — aynı
/// operasyon iki kez gönderilse bile ikinci fatura (dolayısıyla ikinci cari borcu ve ikinci stok
/// hareketi) OLUŞMAZ. Stok ve cari tarafında da aynı <c>operation_id</c> türetilerek kullanılır.
///
/// Idempotent; boş veritabanında ve mevcut veritabanında güvenle çalışır.
/// </summary>
public sealed class Migration067_Invoices : IMigration
{
    public int Version => 67;
    public string Name => "invoices";

    /// <summary>Fatura yönü — ALIŞ: stok GİRER, cariye BORÇLANIRIZ (satıcı alacaklı).</summary>
    public const string DirectionPurchase = "purchase";

    /// <summary>Fatura yönü — SATIŞ: stok ÇIKAR, cari BİZE borçlanır.</summary>
    public const string DirectionSales = "sales";

    /// <summary>Yürürlükteki fatura.</summary>
    public const string StatusActive = "active";

    /// <summary>İptal edilmiş fatura — kaydı durur, etkisi ters kayıtlarla sıfırlanmıştır.</summary>
    public const string StatusCancelled = "cancelled";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // ─────────── BELGE SERİSİ ───────────
        // Numara üretimi transaction içinde next_number ARTIRILARAK yapılır; kod tarafında
        // "en büyük no + 1" taraması YOKTUR (eş zamanlı iki fatura aynı numarayı alamaz).
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS invoice_series (
    id             TEXT PRIMARY KEY,
    company_id     TEXT NOT NULL,
    code           TEXT NOT NULL,
    name           TEXT NULL,
    direction      TEXT NOT NULL,
    prefix         TEXT NULL,
    next_number    BIGINT NOT NULL DEFAULT 1,
    number_padding BIGINT NOT NULL DEFAULT 8,
    is_default     BIGINT NOT NULL DEFAULT 0,
    is_active      BIGINT NOT NULL DEFAULT 1,
    created_at     BIGINT NOT NULL,
    updated_at     BIGINT NOT NULL,
    version        BIGINT NOT NULL DEFAULT 1,
    is_deleted     BIGINT NOT NULL DEFAULT 0
);");
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_invoice_series_code ON invoice_series(company_id, direction, code) WHERE is_deleted = 0;");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_invoice_series_company ON invoice_series(company_id, is_deleted);");

        // ─────────── KDV ORANLARI ───────────
        // Oran TEXT decimal (Money ile serileştirilir) — %20 → "20". Yüzde olarak saklanır, kesir değil.
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS vat_rates (
    id          TEXT PRIMARY KEY,
    company_id  TEXT NOT NULL,
    rate        TEXT NOT NULL,
    label       TEXT NULL,
    is_default  BIGINT NOT NULL DEFAULT 0,
    is_active   BIGINT NOT NULL DEFAULT 1,
    sort_order  BIGINT NOT NULL DEFAULT 0,
    created_at  BIGINT NOT NULL,
    updated_at  BIGINT NOT NULL,
    version     BIGINT NOT NULL DEFAULT 1,
    is_deleted  BIGINT NOT NULL DEFAULT 0
);");
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_vat_rates_rate ON vat_rates(company_id, rate) WHERE is_deleted = 0;");

        // ─────────── FATURA BAŞLIĞI ───────────
        // TUTARLAR TEXT decimal'dir (Money) — SQL SUM ile toplanmaz, yazma yolunda C# decimal ile
        // hesaplanır (para kuralı, CLAUDE.md §4).
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS invoices (
    id                       TEXT PRIMARY KEY,
    company_id               TEXT NOT NULL,
    direction                TEXT NOT NULL,
    series_id                TEXT NULL,
    invoice_no               TEXT NOT NULL,
    external_no              TEXT NULL,
    party_id                 TEXT NOT NULL,
    branch_id                TEXT NULL,
    invoice_date             BIGINT NOT NULL,
    due_date                 BIGINT NULL,
    currency_code            TEXT NOT NULL DEFAULT 'TRY',
    fx_rate                  TEXT NULL,
    subtotal                 TEXT NOT NULL,
    discount_total           TEXT NOT NULL,
    vat_total                TEXT NOT NULL,
    withholding_total        TEXT NOT NULL,
    grand_total              TEXT NOT NULL,
    note                     TEXT NULL,
    status                   TEXT NOT NULL DEFAULT 'active',
    affects_stock            BIGINT NOT NULL DEFAULT 1,
    stock_document_id        TEXT NULL,
    ledger_entry_id          TEXT NULL,
    cancel_stock_document_id TEXT NULL,
    cancel_ledger_entry_id   TEXT NULL,
    cancel_reason            TEXT NULL,
    cancelled_at             BIGINT NULL,
    cancelled_by             TEXT NULL,
    operation_id             TEXT NULL,
    created_at               BIGINT NOT NULL,
    created_by               TEXT NULL,
    updated_at               BIGINT NOT NULL,
    version                  BIGINT NOT NULL DEFAULT 1
);");
        // Fatura numarası firma + yön içinde BENZERSİZ. İptal edilen numara da benzersizliği korur
        // (kayıt durduğu için numara yeniden kullanılamaz — muhasebe izlenebilirliği).
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_invoices_no ON invoices(company_id, direction, invoice_no);");
        // IDEMPOTENCY: aynı operation_id ile ikinci fatura YAZILAMAZ.
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_invoices_op ON invoices(company_id, operation_id) WHERE operation_id IS NOT NULL;");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_invoices_party ON invoices(company_id, party_id, invoice_date);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_invoices_date ON invoices(company_id, invoice_date);");

        // ─────────── FATURA SATIRI ───────────
        // quantity de TEXT decimal — stok defteriyle aynı gösterim (ondalık kaybı olmaz).
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS invoice_lines (
    id                 TEXT PRIMARY KEY,
    company_id         TEXT NOT NULL,
    invoice_id         TEXT NOT NULL,
    line_no            BIGINT NOT NULL,
    material_id        TEXT NULL,
    description        TEXT NULL,
    unit               TEXT NULL,
    quantity           TEXT NOT NULL,
    unit_price         TEXT NOT NULL,
    discount_rate      TEXT NOT NULL DEFAULT '0',
    discount_amount    TEXT NOT NULL DEFAULT '0',
    vat_rate           TEXT NOT NULL DEFAULT '0',
    vat_amount         TEXT NOT NULL DEFAULT '0',
    withholding_rate   TEXT NOT NULL DEFAULT '0',
    withholding_amount TEXT NOT NULL DEFAULT '0',
    net_total          TEXT NOT NULL,
    line_total         TEXT NOT NULL,
    created_at         BIGINT NOT NULL,
    updated_at         BIGINT NOT NULL
);");
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_invoice_lines_no ON invoice_lines(invoice_id, line_no);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_invoice_lines_invoice ON invoice_lines(company_id, invoice_id);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_invoice_lines_material ON invoice_lines(company_id, material_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
