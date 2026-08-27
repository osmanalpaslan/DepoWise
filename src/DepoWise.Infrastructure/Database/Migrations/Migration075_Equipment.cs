using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ EKP-01 (ADR-166, 2026-08-28) — VARLIK / EKİPMAN YÖNETİMİ: VERİ MODELİ ═══
///
/// Yol haritası FAZ 1 / SIRA 3. Ürün kararları (kullanıcı, 2026-08-28):
/// PK-E1 <b>AYRI ekipman tablosu</b> (vehicles genelleştirilmedi — 93 dosyalık araç yüzeyine tür filtresi
/// eklemek canlı sistemde sessiz-bozulma riskiydi) · PK-E2 bakım entegrasyonu İLK SÜRÜMDE YOK (sonraki
/// küçük iş) · PK-E3 yakıt/muayene ekipmana uygulanmaz.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız iki YENİ tablo (CREATE); mevcut hiçbir tabloya (vehicles dahil)
/// dokunulmaz; hiçbir araç kaydı taşınmaz/etiketlenmez — bugün Araçlar'daki iş makineleri orada kalır.
/// Rollback: <c>DROP TABLE equipment; DROP TABLE equipment_types;
/// DELETE FROM schema_migrations WHERE version=75;</c>
///
/// <b>SENKRON:</b> ekipman, araçlar gibi İŞ VERİSİDİR ve masaüstünde çevrimdışı erişilir →
/// <c>BusinessSyncService.Tables</c>'a eklenir (equipment_types lookup bloğuna, equipment vehicles'ın
/// yanına; branch kapsamı <c>branch_id</c> üzerinden — vehicles ile aynı desen).
///
/// <b>GELECEK BAĞLAR (şimdi UYGULANMADI):</b> Zimmet/İş Emri/Barkod, projenin kanıtlı
/// entity_type+entity_id desenine bağlanacak; Evrak zaten "equipment" tipini harita satırıyla alabilir.
/// Edinim/bakım alanları İCAT EDİLMEDİ — gerekirse eklemeli kolonla gelir.
/// </summary>
public sealed class Migration075_Equipment : IMigration
{
    public int Version => 75;
    public string Name => "equipment";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
-- Ekipman türleri: serbest tanım (vehicle_types deseni) → tür genişletme migration GEREKTİRMEZ.
CREATE TABLE equipment_types (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE INDEX ix_equipment_types_company ON equipment_types(company_id, is_deleted);

CREATE TABLE equipment (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    code TEXT NOT NULL,                       -- ekipman kodu (vehicles.internal_code deseni)
    name TEXT NOT NULL,
    type_id TEXT NULL,
    status TEXT NOT NULL DEFAULT 'active',    -- active | passive | maintenance (vehicles.status ile aynı küme)
    status_note TEXT NULL,
    branch_id TEXT NULL,                      -- şube/şantiye/saha bağı (BranchAccess kapsamı bununla uygulanır)
    serial_no TEXT NULL,
    location TEXT NULL,
    description TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,     -- fiziksel silme YOK; Çöp Kutusu geri getirir
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id),
    FOREIGN KEY (type_id) REFERENCES equipment_types(id)
);
CREATE UNIQUE INDEX ux_equipment_code ON equipment(company_id, code);
CREATE INDEX ix_equipment_company ON equipment(company_id, is_deleted);
CREATE INDEX ix_equipment_branch ON equipment(branch_id);";
        cmd.ExecuteNonQuery();
    }
}
