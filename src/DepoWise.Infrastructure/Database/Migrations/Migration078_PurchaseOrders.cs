using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ STN-01 (ADR-169, 2026-08-28) — SATIN ALMA: SİPARİŞ VERİ MODELİ ═══
///
/// Yol haritası FAZ 2 / SIRA 6. Mevcut sistemde talep operasyon ZİNCİRİ (Purchasing→OrderPlaced→…→
/// Delivered, şartname 2026-08-08) ve yetkileri ZATEN vardı; eksik olan gerçek SİPARİŞ kaydıydı:
/// tedarikçi + satır fiyatları + mal kabul köprüsü. Bu migration yalnız o boşluğu doldurur.
///
/// <b>MODEL KARARLARI (mevcut üründen türetildi — ürün sorusu gerekmedi):</b>
/// talep bağı OPSİYONEL (talepli ve talepsiz alım tek modelde) · ayrı sipariş ONAY katmanı ve TEKLİF
/// aşaması EKLENMEDİ (mevcut üründe yok; talep zincirinin kendi onay/durum akışı geçerli) · sipariş
/// durumu asgari: open | closed (tüm satırlar kabul edilince otomatik) | cancelled · mal kabul MEVCUT
/// stok girişini (ReceiveInTx) çağırır — ikinci stok mekanizması YOK · proje bağı için project_id
/// EKLENMEDİ (teslim şubesi üzerinden türetilir — üçlü gerçeklik yasak, C kararı) · maliyet merkezi
/// başlıkta NULLABLE tutulur ve kabulde oluşan STOK BELGESİNE D'nin dış-bağıyla aktarılır (özet çift
/// saymaz: maliyet gerçekleşme anında, stok belgesinden okunur).
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız iki YENİ tablo (CREATE); mevcut hiçbir tabloya dokunulmaz;
/// eski talepler/stok/fatura/cari kayıtlarına bağ backfill EDİLMEZ.
/// Rollback: <c>DROP TABLE purchase_order_lines; DROP TABLE purchase_orders;
/// DELETE FROM schema_migrations WHERE version=78;</c>
///
/// <b>received_qty notu:</b> satırın kendi yaşam-döngüsü alanıdır (yeni kayıtlarda mal kabulle artar) —
/// mevcut veriyi değiştirme yasağına GİRMEZ; kabul hareketinin kendisi stok DEFTERİNDE durur
/// (izlenebilirlik + idempotency oradan doğrulanır).
/// </summary>
public sealed class Migration078_PurchaseOrders : IMigration
{
    public int Version => 78;
    public string Name => "purchase_orders";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE purchase_orders (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    order_no TEXT NOT NULL,                   -- firma içi benzersiz (servis anlaşılır hatayla korur)
    supplier_id TEXT NULL,
    request_id TEXT NULL,                     -- OPSİYONEL talep bağı
    branch_id TEXT NULL,                      -- teslim deposu (BranchAccess kapsamı buradan)
    cost_center_id TEXT NULL,                 -- kabulde stok belgesine D dış-bağıyla aktarılır
    status TEXT NOT NULL DEFAULT 'open',      -- open | closed | cancelled
    order_date BIGINT NOT NULL,               -- İŞ GÜNÜ (ADR-162; geri-tarih btn-backdate)
    note TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (supplier_id) REFERENCES suppliers(id),
    FOREIGN KEY (request_id) REFERENCES material_requests(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id),
    FOREIGN KEY (cost_center_id) REFERENCES cost_centers(id)
);
CREATE UNIQUE INDEX ux_po_order_no ON purchase_orders(company_id, order_no);
CREATE INDEX ix_po_company ON purchase_orders(company_id, is_deleted);

CREATE TABLE purchase_order_lines (
    id TEXT PRIMARY KEY,
    order_id TEXT NOT NULL,
    company_id TEXT NOT NULL,                 -- tenant filtreleri JOIN'siz (Migration062 deseni)
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL,                   -- decimal (invariant)
    unit_price TEXT NULL,                     -- decimal; boş = fiyatsız sipariş satırı
    currency_code TEXT NULL,                  -- boş = TRY
    received_qty TEXT NOT NULL DEFAULT '0',   -- mal kabulle artar (stok defteriyle birlikte, aynı tx)
    note TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (order_id) REFERENCES purchase_orders(id),
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);
CREATE INDEX ix_pol_order ON purchase_order_lines(order_id);
CREATE INDEX ix_pol_company ON purchase_order_lines(company_id, is_deleted);";
        cmd.ExecuteNonQuery();
    }
}
