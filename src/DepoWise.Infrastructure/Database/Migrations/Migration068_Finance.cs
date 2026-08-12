using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// G4-3 — KASA / BANKA ALTYAPISI (kullanıcı isteği 2026-08-12).
///
/// Üç tablo:
/// <list type="bullet">
/// <item><c>finance_accounts</c> — kasa VE banka hesapları (tek tablo, <c>account_kind</c> ayırır).</item>
/// <item><c>finance_transactions</c> — para hareketi defteri (tahsilat/ödeme/transfer/açılış/düzeltme).</item>
/// <item><c>invoice_allocations</c> — bir tahsilatın/ödemenin HANGİ faturaya ne kadar sayıldığı.</item>
/// </list>
///
/// <b>NEDEN TEK HESAP TABLOSU:</b> kasa ile bankanın defter mantığı AYNIDIR (giriş/çıkış + bakiye).
/// Ayrı <c>cash_accounts</c> / <c>bank_accounts</c> açmak, aynı problemi çözen İKİ paralel para
/// sistemi demek olurdu. <c>account_kind</c> ile ayrılır; ileride POS ve çek/senet de aynı tabloya
/// yeni bir tür olarak girer — şema değişikliği gerekmez.
///
/// <b>BAKİYE SAKLANMAZ:</b> hesap bakiyesi <c>Σ(direction × amount)</c> ile hareketlerden hesaplanır
/// (<c>stock_balances</c> ve cari bakiyesi kararının aynısı). Elle yazılan, defterle uyuşmayabilecek
/// bir "bakiye" alanı YOKTUR.
///
/// <b>FATURA KALANI DA SAKLANMAZ:</b> faturanın kalan tutarı
/// <c>grand_total − Σ(iptal edilmemiş tahsisler)</c> ile hesaplanır. <c>invoices</c> tablosuna
/// <c>paid_total</c> gibi bir kolon EKLENMEDİ — eklenseydi tahsilat iptalinde defterle sessiz bir
/// fark oluşabilirdi.
///
/// <b>⚠️ PARALEL CARİ YOK:</b> tahsilat/ödemenin cari etkisi yalnız
/// <c>PartyLedgerService.AddFromDocumentTx</c> ile yazılır; sonucu
/// <c>finance_transactions.ledger_entry_id</c> olarak referanslanır.
///
/// <b>⚠️ PARALEL STOK YOK:</b> bu tablolar stok defterine HİÇ dokunmaz. Para hareketi stok hareketi
/// değildir; stokun tek yazıcısı <c>StockService</c> olmaya devam eder.
///
/// <b>SİLME YOK:</b> finansal hareket fiziksel silinmez. İptal, <c>is_reversed=1</c> + karşı yönde
/// YENİ hareket ile yürür (<c>reversal_of</c> orijinali işaret eder). Çift iptal engellenir.
///
/// <b>IDEMPOTENCY:</b> <c>finance_transactions.operation_id</c> ve
/// <c>invoice_allocations.operation_id</c> üzerinde kısmi tekil indeks vardır — aynı tahsilat iki kez
/// gönderilse bile kasa iki kez hareket etmez, cari iki kez etkilenmez, fatura iki kez kapanmaz.
///
/// <b>İÇ TRANSFER:</b> kasa→banka gibi transferler İKİ bacak olarak yazılır ve
/// <c>transfer_group_id</c> ile bağlanır (çıkış −X, giriş +X → net 0). Cari ETKİLENMEZ:
/// iç transferde <c>party_id</c> NULL'dır, çünkü kimseye borç doğmaz/kapanmaz.
///
/// <b>TÜRKİYE'YE ÖZGÜ DEĞERLER SABİTLENMEZ:</b> ödeme yöntemi (<c>payment_method</c>) serbest metin
/// alanıdır; banka/POS/çek gibi türler ileride VERİ olarak eklenir. KDV ve belge serisi BURADA
/// TEKRAR TANIMLANMAZ — onlar G4-2'nin (<c>vat_rates</c>, <c>invoice_series</c>) sorumluluğundadır.
///
/// Idempotent; boş veritabanında ve mevcut veritabanında güvenle çalışır.
/// </summary>
public sealed class Migration068_Finance : IMigration
{
    public int Version => 68;
    public string Name => "finance_accounts_transactions_allocations";

    /// <summary>Hesap türü — KASA (nakit).</summary>
    public const string KindCash = "cash";

    /// <summary>Hesap türü — BANKA.</summary>
    public const string KindBank = "bank";

    /// <summary>Hareket türü — TAHSİLAT: müşteriden para ALINDI (hesap +, cari alacağı azalır).</summary>
    public const string TxnReceipt = "receipt";

    /// <summary>Hareket türü — ÖDEME: tedarikçiye para VERİLDİ (hesap −, cari borcumuz azalır).</summary>
    public const string TxnPayment = "payment";

    /// <summary>Hareket türü — İÇ TRANSFER ÇIKIŞ bacağı (cari etkilenmez).</summary>
    public const string TxnTransferOut = "transfer_out";

    /// <summary>Hareket türü — İÇ TRANSFER GİRİŞ bacağı (cari etkilenmez).</summary>
    public const string TxnTransferIn = "transfer_in";

    /// <summary>Hareket türü — AÇILIŞ bakiyesi (cari etkilenmez).</summary>
    public const string TxnOpening = "opening";

    /// <summary>Hareket türü — gerekçeli DÜZELTME (cari etkilenmez).</summary>
    public const string TxnAdjustment = "adjustment";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        // ─────────── KASA / BANKA HESAPLARI ───────────
        // branch_id: bu hesabın hangi şubeye ait olduğu. NULL = firma geneli (şube filtresinde
        // GİZLENMEZ — BranchScope kuralı: NULL kayıtlar her şubede görünür).
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS finance_accounts (
    id             TEXT PRIMARY KEY,
    company_id     TEXT NOT NULL,
    code           TEXT NOT NULL,
    name           TEXT NOT NULL,
    account_kind   TEXT NOT NULL,
    currency_code  TEXT NOT NULL DEFAULT 'TRY',
    branch_id      TEXT NULL,
    bank_name      TEXT NULL,
    bank_branch    TEXT NULL,
    account_no     TEXT NULL,
    iban           TEXT NULL,
    note           TEXT NULL,
    is_default     BIGINT NOT NULL DEFAULT 0,
    is_active      BIGINT NOT NULL DEFAULT 1,
    created_at     BIGINT NOT NULL,
    updated_at     BIGINT NOT NULL,
    version        BIGINT NOT NULL DEFAULT 1,
    is_deleted     BIGINT NOT NULL DEFAULT 0
);");
        // Hesap KODU firma içinde benzersiz — ama YALNIZ silinmemişler arasında (silinen kod
        // yeniden kullanılabilsin). Kısmi indeks iki lehçede de desteklenir.
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_finance_accounts_code ON finance_accounts(company_id, code) WHERE is_deleted = 0;");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_finance_accounts_company ON finance_accounts(company_id, is_deleted, account_kind);");

        // ─────────── PARA HAREKETİ DEFTERİ ───────────
        // amount TEXT decimal (Money) — SQL SUM ile toplanmaz, C#'ta decimal ile toplanır.
        // direction: +1 GİRİŞ (para geldi), -1 ÇIKIŞ (para gitti).
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS finance_transactions (
    id                 TEXT PRIMARY KEY,
    company_id         TEXT NOT NULL,
    account_id         TEXT NOT NULL,
    branch_id          TEXT NULL,
    txn_date           BIGINT NOT NULL,
    txn_type           TEXT NOT NULL,
    direction          BIGINT NOT NULL,
    amount             TEXT NOT NULL,
    currency_code      TEXT NOT NULL DEFAULT 'TRY',
    party_id           TEXT NULL,
    description        TEXT NULL,
    doc_no             TEXT NULL,
    payment_method     TEXT NULL,
    reference_no       TEXT NULL,
    source_type        TEXT NULL,
    source_id          TEXT NULL,
    transfer_group_id  TEXT NULL,
    counter_account_id TEXT NULL,
    ledger_entry_id    TEXT NULL,
    operation_id       TEXT NULL,
    is_reversed        BIGINT NOT NULL DEFAULT 0,
    reversal_of        TEXT NULL,
    reversal_reason    TEXT NULL,
    created_at         BIGINT NOT NULL,
    created_by         TEXT NULL,
    updated_at         BIGINT NOT NULL
);");
        // IDEMPOTENCY: aynı operation_id ile ikinci hareket YAZILAMAZ (stok ve cari ile aynı kural).
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_finance_txn_op ON finance_transactions(company_id, operation_id) WHERE operation_id IS NOT NULL;");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_finance_txn_account ON finance_transactions(company_id, account_id, txn_date);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_finance_txn_party ON finance_transactions(company_id, party_id, txn_date);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_finance_txn_group ON finance_transactions(company_id, transfer_group_id);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_finance_txn_source ON finance_transactions(company_id, source_type, source_id);");

        // ─────────── FATURA KAPAMA (TAHSİS) ───────────
        // Bir tahsilat birden çok faturaya bölünebilir; bir fatura birden çok tahsilatla kapanabilir.
        // Faturanın KALANI bu tablodan hesaplanır — invoices'ta "ödenen" kolonu YOKTUR.
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS invoice_allocations (
    id             TEXT PRIMARY KEY,
    company_id     TEXT NOT NULL,
    invoice_id     TEXT NOT NULL,
    transaction_id TEXT NOT NULL,
    amount         TEXT NOT NULL,
    allocated_at   BIGINT NOT NULL,
    operation_id   TEXT NULL,
    is_reversed    BIGINT NOT NULL DEFAULT 0,
    created_at     BIGINT NOT NULL,
    created_by     TEXT NULL,
    updated_at     BIGINT NOT NULL
);");
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_invoice_alloc_op ON invoice_allocations(company_id, operation_id) WHERE operation_id IS NOT NULL;");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_invoice_alloc_invoice ON invoice_allocations(company_id, invoice_id, is_reversed);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_invoice_alloc_txn ON invoice_allocations(company_id, transaction_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
