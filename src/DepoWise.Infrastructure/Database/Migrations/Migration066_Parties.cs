using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// G4-1 — ÖN MUHASEBE / CARİ ALTYAPISI (kullanıcı isteği 2026-08-12).
///
/// İki tablo: <c>parties</c> (cari kartı) ve <c>party_ledger</c> (cari hesap hareketi).
///
/// <b>⚠️ STOKLA SINIR:</b> bu tablolar stok defterinin ALTERNATİFİ DEĞİLDİR. Stok hareketinin tek
/// yazıcısı <c>StockService</c> olmaya DEVAM eder. G4-2'de fatura geldiğinde stok etkisi yine
/// <c>StockService.ReceiveIn/IssueOut</c> üzerinden yürüyecek; burada ikinci bir stok gerçekliği
/// oluşturulmaz. <c>party_ledger</c> yalnız PARA hareketini (borç/alacak) tutar.
///
/// <b>BAKİYE SAKLANMAZ:</b> cari bakiyesi <c>Σ(direction × amount)</c> ile hareketlerden hesaplanır —
/// stok defterindeki desenin aynısı. Elle yazılan, defterle uyuşmayabilecek bir "bakiye" alanı YOKTUR.
///
/// <b>MEVCUT <c>suppliers</c> DOKUNULMAZ:</b> malzeme kartı ona bağlıdır ve veri TAŞINMAZ.
/// İsteğe bağlı <c>parties.supplier_id</c> ile EŞLEME kurulur (ileride "bu tedarikçi = bu cari").
///
/// <b>Türkiye'ye özgü alanlar yapılandırılabilir kalır:</b> KDV oranı, belge serisi, tevkifat gibi
/// kurallar BU TABLOLARDA SABİTLENMEZ — G4-2'de kendi yapılandırma tablolarıyla gelir.
/// Idempotent; boş veritabanında ve mevcut veritabanında güvenle çalışır.
/// </summary>
public sealed class Migration066_Parties : IMigration
{
    public int Version => 66;
    public string Name => "parties_and_party_ledger";

    /// <summary>Cari tipi — tek metin alan (enum tablosu açılmadı; değerler koddaki katalogdan gelir).</summary>
    public const string TypeCustomer = "customer";
    public const string TypeSupplier = "supplier";
    public const string TypeBoth = "both";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS parties (
    id             TEXT PRIMARY KEY,
    company_id     TEXT NOT NULL,
    code           TEXT NOT NULL,
    title          TEXT NOT NULL,
    party_type     TEXT NOT NULL,
    is_person      BIGINT NOT NULL DEFAULT 0,
    tax_office     TEXT NULL,
    tax_no         TEXT NULL,
    national_id    TEXT NULL,
    phone          TEXT NULL,
    email          TEXT NULL,
    address        TEXT NULL,
    city           TEXT NULL,
    district       TEXT NULL,
    currency_code  TEXT NOT NULL DEFAULT 'TRY',
    note           TEXT NULL,
    is_active      BIGINT NOT NULL DEFAULT 1,
    supplier_id    TEXT NULL,
    created_at     BIGINT NOT NULL,
    updated_at     BIGINT NOT NULL,
    version        BIGINT NOT NULL DEFAULT 1,
    is_deleted     BIGINT NOT NULL DEFAULT 0
);");
        // Cari KODU firma içinde benzersiz — ama YALNIZ silinmemişler arasında (silinen kod yeniden
        // kullanılabilsin). Kısmi indeks iki lehçede de desteklenir (Migration063 aynı deseni kullanır).
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_parties_code ON parties(company_id, code) WHERE is_deleted = 0;");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_parties_company ON parties(company_id, is_deleted);");

        Exec(conn, tx, @"
CREATE TABLE IF NOT EXISTS party_ledger (
    id             TEXT PRIMARY KEY,
    company_id     TEXT NOT NULL,
    party_id       TEXT NOT NULL,
    branch_id      TEXT NULL,
    entry_date     BIGINT NOT NULL,
    doc_type       TEXT NOT NULL,
    doc_no         TEXT NULL,
    description    TEXT NULL,
    direction      BIGINT NOT NULL,
    amount         TEXT NOT NULL,
    currency_code  TEXT NOT NULL DEFAULT 'TRY',
    fx_rate        TEXT NULL,
    due_date       BIGINT NULL,
    source_type    TEXT NULL,
    source_id      TEXT NULL,
    operation_id   TEXT NULL,
    is_reversed    BIGINT NOT NULL DEFAULT 0,
    created_at     BIGINT NOT NULL,
    created_by     TEXT NULL
);");
        // IDEMPOTENCY: aynı operation_id ile ikinci hareket YAZILAMAZ (stok defteriyle aynı kural).
        // G4-2'de fatura/tahsilat tekrar gönderilse bile cari ikinci kez borçlanmaz.
        Exec(conn, tx, "CREATE UNIQUE INDEX IF NOT EXISTS ux_party_ledger_op ON party_ledger(company_id, operation_id) WHERE operation_id IS NOT NULL;");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_party_ledger_party ON party_ledger(company_id, party_id, entry_date);");
        Exec(conn, tx, "CREATE INDEX IF NOT EXISTS ix_party_ledger_source ON party_ledger(company_id, source_type, source_id);");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
