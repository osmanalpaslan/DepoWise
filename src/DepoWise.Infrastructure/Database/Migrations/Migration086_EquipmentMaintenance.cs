using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ 7b — BAKIM-EKİPMAN GENİŞLETMESİ (PK-F9, ADR-191) ═══
///
/// Ekipman bakım/muayene hattı <b>AYRI TABLOLARLA</b> kurulur (FAZ 2 kararı = SEÇENEK B).
///
/// <b>Neden ayrı tablo (Seçenek A neden elendi):</b> mevcut <c>vehicle_maintenances</c>'ı ekipmana
/// açmak <c>vehicle_id</c>'yi nullable yapmayı gerektirirdi. SQLite <c>DROP NOT NULL</c> desteklemez
/// → tablo yeniden kurulmalıydı. Ancak <c>vehicle_maintenances</c>'a <b>İKİ tablo FK veriyor</b>
/// (<c>maintenance_materials.maintenance_id</c> ve <c>daily_activity.maintenance_id</c>) ve masaüstünde
/// <c>PRAGMA foreign_keys=ON</c>; <c>MigrationRunner</c> her migration'ı transaction içinde çalıştırdığı
/// için FK zorlaması kapatılamaz (SQLite'ta bu pragma transaction içinde no-op'tur).
/// Projedeki üç yeniden-kurma içtihadı (Migration062/064/072) da <b>gelen FK'si SIFIR</b> tablolardadır.
/// Bu yüzden burada <b>hiç ALTER yoktur</b>: yalnız CREATE TABLE + CREATE INDEX.
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> mevcut araç bakım tabloları (<c>vehicle_maintenances</c>,
/// <c>maintenance_materials</c>, <c>vehicle_inspections</c>, <c>maintenance_definition_vehicles</c>)
/// <b>HİÇ DEĞİŞMEZ</b>; veri taşınmaz, backfill yapılmaz. Rollback = 4 DROP + schema_migrations satırı.
///
/// <b>operation_id sözleşmesi:</b> <c>vehicle_maintenances</c> FIN-B1/Migration082 kapsamındaydı ve
/// benzersizliği <c>(company_id, operation_id)</c>'ye taşındı. Yeni tablo <b>doğrudan yeni sözleşmeyle</b>
/// kurulur — eski firma-kör sözleşme tekrarlanmaz.
///
/// <b>Sayaç yok (PK-F8):</b> ekipmanda sayaç/kullanım kaydı YOKTUR. <c>performed_km/hour</c> alanları
/// tanım aralığı gün DIŞINDA bir birimse kullanıcı girişini SAKLAMAK için durur; hiçbir sayaç ilerletilmez.
/// </summary>
public sealed class Migration086_EquipmentMaintenance : IMigration
{
    public int Version => 86;
    public string Name => "equipment_maintenance";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
-- Bakım tanımı ↔ EKİPMAN eşlemesi. Araç karşılığı: maintenance_definition_vehicles.
-- ⚠️ Araç tablosunda company_id YOKTUR (Migration008); burada BİLİNÇLİ olarak EKLENDİ:
-- Migration062 çocuk tablolara firma taşıma yönünü belirledi, yeni tablo o yönde kurulur.
CREATE TABLE maintenance_definition_equipment (
    definition_id TEXT NOT NULL,
    equipment_id TEXT NOT NULL,
    company_id TEXT NOT NULL,
    PRIMARY KEY (definition_id, equipment_id),
    FOREIGN KEY (definition_id) REFERENCES maintenance_definitions(id),
    FOREIGN KEY (equipment_id) REFERENCES equipment(id)
);
CREATE INDEX ix_maint_def_equipment_eq ON maintenance_definition_equipment(company_id, equipment_id);

-- Ekipman bakım kaydı — vehicle_maintenances alan kümesinin birebir karşılığı (vehicle_id → equipment_id).
CREATE TABLE equipment_maintenances (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    equipment_id TEXT NOT NULL,
    maintenance_def_id TEXT NOT NULL,
    sub_definition_id TEXT NULL,
    technician_id TEXT NULL,
    description TEXT NULL,
    sub_definition_note TEXT NULL,
    performed_km TEXT NULL,                   -- decimal metin (araç tarafıyla aynı saklama biçimi)
    performed_hour TEXT NULL,
    performed_date BIGINT NULL,
    next_due_km TEXT NULL,
    next_due_hour TEXT NULL,
    next_due_date BIGINT NULL,
    op_branch_id TEXT NULL,                   -- kaydı İŞLEYEN şube (araç tarafındaki karşılığıyla aynı anlam)
    operation_id TEXT NOT NULL,
    is_cancelled BIGINT NOT NULL DEFAULT 0,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (equipment_id) REFERENCES equipment(id),
    FOREIGN KEY (maintenance_def_id) REFERENCES maintenance_definitions(id)
);
-- ⭐ FIN-B1 (Migration082) sözleşmesi: idempotency anahtarı FİRMA KAPSAMLIDIR.
CREATE UNIQUE INDEX ux_equipment_maintenances_op ON equipment_maintenances(company_id, operation_id);
CREATE INDEX ix_equipment_maintenances ON equipment_maintenances(equipment_id, maintenance_def_id, created_at);
CREATE INDEX ix_equipment_maintenances_company ON equipment_maintenances(company_id, is_deleted);

-- Ekipman bakımında kullanılan malzeme — maintenance_materials karşılığı.
-- from_team_stock: Migration059 ile araç tarafına eklenen alanın karşılığı (bakım ekibi stoğu).
CREATE TABLE equipment_maintenance_materials (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    maintenance_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL,
    unit_price TEXT NULL,
    from_team_stock BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (maintenance_id) REFERENCES equipment_maintenances(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);
CREATE INDEX ix_equipment_maintenance_materials ON equipment_maintenance_materials(maintenance_id);

-- Ekipman muayene/belge — vehicle_inspections karşılığı (aynı doc_type kümesi).
CREATE TABLE equipment_inspections (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    equipment_id TEXT NOT NULL,
    doc_type TEXT NOT NULL,                   -- inspection | insurance | kasko | calibration
    last_date BIGINT NULL,
    next_date BIGINT NULL,
    result TEXT NULL,
    place TEXT NULL,
    note TEXT NULL,
    created_at BIGINT NOT NULL,
    updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1,
    is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (equipment_id) REFERENCES equipment(id)
);
CREATE INDEX ix_equipment_inspections ON equipment_inspections(equipment_id, doc_type, is_deleted);";
        cmd.ExecuteNonQuery();
    }
}
