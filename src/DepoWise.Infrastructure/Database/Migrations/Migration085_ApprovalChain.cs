using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187 + ADR-188) — HİYERARŞİ + ONAY ZİNCİRİ ═══
///
/// Üç yeni tablo: <c>user_hierarchy</c> (PK-EK-02) · <c>approval_instance</c> + <c>approval_step</c>
/// (PK-EK-03). <b>Mevcut hiçbir tabloya ALTER YOK</b> — özellikle <c>users</c> (PK-EK-02: hiyerarşi
/// sütunu eklenmez), <c>material_requests</c> ve <c>purchase_orders</c> DOKUNULMAZ
/// (ADR-188 §2: `purchase_orders.status` sözleşmesi `open|closed|cancelled` olarak KALIR).
/// <b>Backfill YOK</b>, veri dönüşümü YOK → hiyerarşi tanımlanana kadar hiçbir davranış değişmez
/// (İK-3 "opsiyonel zincir"in doğrudan sonucu).
///
/// <b>FK kararı (Migration084 içtihadı):</b> kullanıcı referanslarına (<c>user_id</c>,
/// <c>manager_user_id</c>, <c>approver_user_id</c>, <c>acted_by</c>) <b>FK verilmez</b>: <c>users</c>
/// masaüstüne senkronlanmaz ve <c>user_hierarchy</c> lookup aynasıyla masaüstüne indiğinde
/// <c>foreign_keys=ON</c> altında FK ihlaliyle kırardı. Bütünlük SUNUCU servis katmanında zorlanır.
///
/// <b>SNAPSHOT (PK-EK-04):</b> <c>approval_step.approver_user_id</c> süreç BAŞLARKEN yazılır ve bir
/// daha hesaplanmaz; sonradan hiyerarşi/ekip değişse bile açık süreç etkilenmez.
///
/// <b>SENKRON:</b> <c>user_hierarchy</c> yalnız <c>/api/lookups/sync</c> AYNASINA girer (sunucu
/// otoriteli, masaüstü yazmaz). <c>approval_instance</c>/<c>approval_step</c> <b>HİÇBİR senkron
/// yoluna girmez</b> (PK-EK-05/İK-9: onay yalnız çevrimiçi, sunucu otoritesindedir).
///
/// Runner migration'ı tek transaction'da çalıştırır → hata olursa şema 84'te kalır.
/// </summary>
public sealed class Migration085_ApprovalChain : IMigration
{
    public int Version => 85;
    public string Name => "approval_chain";

    /// <summary>Onay motorunun tanıdığı TEK varlık kümesi (PK-EK-01: İş Emri KAPSAM DIŞI).
    /// Kod tarafındaki doğrulama <c>ApprovalEntityTypes</c> ile aynı listedir.</summary>
    public static readonly string[] EntityTypes = { "material_request", "purchase_order" };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE user_hierarchy (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,                 -- tenant sınırı (İK-8: firma bazlı; branch_id YOK)
    user_id TEXT NOT NULL,                    -- ast
    manager_user_id TEXT NOT NULL,            -- üst; self-reference/döngü/derinlik serviste engellenir
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
    -- users'a FK YOK: Migration084 ile aynı gerekçe (users masaüstüne inmez; ayna FK ihlaliyle kırardı).
);
-- Bir kullanıcının AKTİF tek üstü olur (İK-2 zincirinin tekilliği buradan gelir).
-- Kısmi indeks: yumuşak silinen ilişki yeniden kurulabilir.
CREATE UNIQUE INDEX ux_user_hierarchy_active ON user_hierarchy(company_id, user_id) WHERE is_deleted = 0;
CREATE INDEX ix_user_hierarchy_manager ON user_hierarchy(company_id, manager_user_id, is_deleted);

CREATE TABLE approval_instance (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,                -- YALNIZ 'material_request' | 'purchase_order'
    entity_id TEXT NOT NULL,
    status TEXT NOT NULL,                     -- 'pending' | 'approved' | 'rejected' | 'cancelled'
    started_by TEXT NULL,                     -- süreci başlatan kullanıcı (self-approval kapısı bunu kullanır)
    started_at BIGINT NOT NULL,
    snapshot_at BIGINT NOT NULL,              -- zincirin DONDURULDUĞU an (PK-EK-04)
    closed_at BIGINT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
-- Bir varlığın AÇIK tek süreci olur → çift süreç başlatma veritabanı seviyesinde de engellenir.
CREATE UNIQUE INDEX ux_approval_instance_open
    ON approval_instance(company_id, entity_type, entity_id) WHERE is_deleted = 0 AND status = 'pending';
CREATE INDEX ix_approval_instance_entity ON approval_instance(company_id, entity_type, entity_id, is_deleted);

CREATE TABLE approval_step (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    instance_id TEXT NOT NULL,
    step_no BIGINT NOT NULL,                  -- 1'den başlar; sırayla ilerler
    approver_user_id TEXT NOT NULL,           -- ⭐ SNAPSHOT: süreç başlarken yazılır, ASLA yeniden hesaplanmaz
    status TEXT NOT NULL,                     -- 'pending' | 'approved' | 'rejected' | 'skipped'
    acted_by TEXT NULL,
    acted_at BIGINT NULL,
    reason TEXT NULL,                         -- ret gerekçesi (İK-10: görünürlük daraltılmaz)
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (instance_id) REFERENCES approval_instance(id)
);
CREATE UNIQUE INDEX ux_approval_step_no ON approval_step(instance_id, step_no);
-- ""Onaylamalarım"" sorgusu (ALT FAZ 3) ve step sahipliği kontrolü bu indeksi kullanır.
CREATE INDEX ix_approval_step_approver ON approval_step(company_id, approver_user_id, status);";
        cmd.ExecuteNonQuery();
    }
}
