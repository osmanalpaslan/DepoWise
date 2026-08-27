using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ MLY-01 (ADR-168, 2026-08-28) — MALİYET MERKEZİ: VERİ MODELİ ═══
///
/// Yol haritası FAZ 2 / SIRA 5. Model kararları (kullanıcı talimatından türetildi — ürün sorusu gerekmedi):
/// tek işlem = TEK maliyet merkezi · yüzde/çoklu dağıtım YOK · geçmiş kayıtlara backfill YOK
/// (yalnız yeni işlemler bağ taşır) · şube/proje/araç boyutları TEKRAR EDİLMEZ (üçlü gerçeklik yasak —
/// onların maliyeti mevcut branch_id/vehicle_id üzerinden zaten raporlanabiliyor; maliyet merkezi
/// bunların YERİNE değil YANINA gelen YENİ bir boyuttur: departman, iş, özel merkez...).
///
/// <b>NEDEN BAĞ TABLOSU (mevcut tablolara kolon DEĞİL):</b> stok belge zinciri 5 katmanlıdır
/// (IssueOut→RunDocument→…→insert) ve fatura/sayım/dağıtım da oradan geçer; kolon eklemek canlı
/// stock_documents/fuel/bakım tablolarına ALTER + tüm imza zinciri + çağıranlar demekti. DIŞ BAĞ
/// (<c>cost_center_links</c>, file_records'un entity deseni) mevcut tablolara ve servis zincirine
/// SIFIR dokunuşla aynı bilgiyi taşır; bağ kayıt oluştuktan hemen sonra yazılır (maliyet bağı
/// bilgilendiricidir — stok/para bütünlüğünü etkilemez; yazılamazsa kayıt "merkezsiz" kalır ve
/// sonradan atanabilir). UNIQUE(entity) → tek kayıt tek merkez KURALI şemada kilitlidir.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız iki YENİ tablo (CREATE); mevcut hiçbir tabloya ALTER dahi yok;
/// UPDATE/DELETE/backfill yok. Rollback: iki DROP + schema_migrations satırı.
/// </summary>
public sealed class Migration077_CostCenters : IMigration
{
    public int Version => 77;
    public string Name => "cost_centers";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE cost_centers (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    code TEXT NULL,
    name TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'active',    -- active | passive
    description TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- yumuşak silme işareti + Çöp Kutusu
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE INDEX ix_cost_centers_company ON cost_centers(company_id, is_deleted);

-- Kayıt → merkez bağı. entity_type: stock_document | fuel_depot_entry | fuel_distribution | vehicle_maintenance.
-- UNIQUE(company, entity) = tek kayıt TEK merkez (model kararı şemada).
CREATE TABLE cost_center_links (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    cost_center_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (cost_center_id) REFERENCES cost_centers(id)
);
CREATE UNIQUE INDEX ux_ccl_entity ON cost_center_links(company_id, entity_type, entity_id);
CREATE INDEX ix_ccl_center ON cost_center_links(company_id, cost_center_id, is_deleted);";
        cmd.ExecuteNonQuery();
    }
}
