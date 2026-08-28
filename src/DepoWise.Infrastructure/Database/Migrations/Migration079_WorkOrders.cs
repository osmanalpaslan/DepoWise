using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ EMR-01 (ADR-170, 2026-08-28) — İŞ EMRİ: VERİ MODELİ ═══
///
/// Yol haritası FAZ 3 / SIRA 7. Ürün kararları KESİN (kullanıcı, 2026-08-28 — F_ISEMRI_00_ANALIZ.md):
/// PK-F1 akış Taslak→Atandı→Devam Ediyor⇄Beklemede→Tamamlandı (+İptal), onay katmanı YOK ·
/// PK-F2 tamamlanan YENİDEN AÇILAMAZ · PK-F3 tüketim MEVCUT stok çıkışıyla · PK-F4 puantaj YOK ·
/// PK-F5 yalnız şantiye/saha bağı (proje türetilir) · PK-F6 numara elle + benzersiz ·
/// PK-F7 alt/tekrarlayan iş emri YOK · PK-F8 kullanım metriği YOK · PK-F9 bakım yalnız BAĞ.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız DÖRT yeni tablo (CREATE); mevcut hiçbir tabloya ALTER dahi yok;
/// backfill yok. Rollback: dört DROP + <c>DELETE FROM schema_migrations WHERE version=79;</c>
///
/// <b>Geçmiş DEFTERİ:</b> work_order_status_history append-only'dir (request_status_history emsali) —
/// durum kim/ne zaman değişti izi sonsuza dek kalır; UPDATE/silme yolu yoktur.
/// </summary>
public sealed class Migration079_WorkOrders : IMigration
{
    public int Version => 79;
    public string Name => "work_orders";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE work_orders (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    wo_no TEXT NOT NULL,                      -- PK-F6: elle + firma içi benzersiz (servis anlaşılır hatayla korur)
    title TEXT NOT NULL,
    description TEXT NULL,
    status TEXT NOT NULL DEFAULT 'draft',     -- draft|assigned|in_progress|on_hold|completed|cancelled (PK-F1/F2)
    priority TEXT NOT NULL DEFAULT 'normal',  -- normal|high|urgent|critical (mevcut RequestPriority seti)
    branch_id TEXT NULL,                      -- PK-F5: şantiye/saha — kapsam anahtarı; proje buradan türetilir
    cost_center_id TEXT NULL,                 -- tüketimde stok belgesine D dış-bağıyla aktarılır
    assignee_personnel_id TEXT NULL,          -- ana sorumlu
    planned_start BIGINT NULL,                -- İŞ GÜNÜ (ADR-162; geri-tarih btn-backdate)
    planned_end BIGINT NULL,
    actual_start BIGINT NULL,                 -- Devam Ediyor'a İLK geçişte otomatik (iş günü)
    actual_end BIGINT NULL,                   -- Tamamlandı'da otomatik
    closing_note TEXT NULL,
    created_by TEXT NOT NULL,
    completed_by TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- iptal DURUMDUR (PK-F1); fiziksel silme YOK
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id),
    FOREIGN KEY (cost_center_id) REFERENCES cost_centers(id),
    FOREIGN KEY (assignee_personnel_id) REFERENCES personnel(id)
);
CREATE UNIQUE INDEX ux_wo_no ON work_orders(company_id, wo_no);
CREATE INDEX ix_wo_company ON work_orders(company_id, is_deleted);
CREATE INDEX ix_wo_status ON work_orders(company_id, status);

-- ATAMALAR (PK-F4/F8: yalnız atama — saat/sayaç/maliyet YOK). Zimmet DEĞİLDİR (mülkiyet taşımaz);
-- araç sürücü ataması ve ekipman kayıtlarına DOKUNULMAZ. resource polymorphic (file_records emsali).
CREATE TABLE work_order_assignments (
    id TEXT PRIMARY KEY,
    work_order_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    resource_type TEXT NOT NULL,              -- personnel | vehicle | equipment
    resource_id TEXT NOT NULL,
    note TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- atama kaldırma = soft (kim atanmıştı izi kalır)
    FOREIGN KEY (work_order_id) REFERENCES work_orders(id),
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE INDEX ix_woa_wo ON work_order_assignments(work_order_id, is_deleted);
CREATE INDEX ix_woa_company ON work_order_assignments(company_id, is_deleted);

-- İLİŞKİLİ KAYIT BAĞLARI: tüketim stok belgeleri + bakım kayıtları (PK-F9: yalnız bağ) + siparişler.
-- Maliyet özetinin kaynağı buradaki stok belgeleridir (tek kaynak — çift sayım yok).
CREATE TABLE work_order_links (
    id TEXT PRIMARY KEY,
    work_order_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,                -- stock_document | vehicle_maintenance | purchase_order
    entity_id TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (work_order_id) REFERENCES work_orders(id),
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE UNIQUE INDEX ux_wol_entity ON work_order_links(company_id, entity_type, entity_id);
CREATE INDEX ix_wol_wo ON work_order_links(work_order_id, is_deleted);

-- DURUM GEÇMİŞİ DEFTERİ (append-only; request_status_history emsali) — silme/güncelleme yolu YOK.
CREATE TABLE work_order_status_history (
    id TEXT PRIMARY KEY,
    work_order_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    from_status TEXT NULL,
    to_status TEXT NOT NULL,
    user_id TEXT NOT NULL,
    note TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (work_order_id) REFERENCES work_orders(id),
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE INDEX ix_wosh_wo ON work_order_status_history(work_order_id);";
        cmd.ExecuteNonQuery();
    }
}
