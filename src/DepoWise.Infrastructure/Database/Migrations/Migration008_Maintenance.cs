using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Bakım tanımı (ana/alt + periyot km/saat/gün + araç kapsamı), bakım kaydı + kullanılan malzeme,
/// muayene/sigorta/kasko/kalibrasyon belgeleri. Bakım stok düşümü tek transaction (Faz 09 servis).
/// </summary>
public sealed class Migration008_Maintenance : IMigration
{
    public int Version => 8;
    public string Name => "maintenance_and_inspections";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE maintenance_definitions (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    parent_def_id TEXT NULL,                 -- alt tanım (NULL = ana)
    name TEXT NOT NULL,
    interval_value TEXT NOT NULL DEFAULT '0',-- decimal
    interval_unit TEXT NOT NULL DEFAULT 'km',-- km | hour | day
    description TEXT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (parent_def_id) REFERENCES maintenance_definitions(id)
);
CREATE INDEX ix_maint_defs_company ON maintenance_definitions(company_id, is_deleted);

CREATE TABLE maintenance_definition_vehicles (
    definition_id TEXT NOT NULL,
    vehicle_id TEXT NOT NULL,
    PRIMARY KEY (definition_id, vehicle_id),
    FOREIGN KEY (definition_id) REFERENCES maintenance_definitions(id),
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);

CREATE TABLE vehicle_maintenances (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    vehicle_id TEXT NOT NULL,
    maintenance_def_id TEXT NOT NULL,
    sub_definition_id TEXT NULL,
    technician_id TEXT NULL,
    description TEXT NULL,
    sub_definition_note TEXT NULL,
    performed_km TEXT NULL,
    performed_hour TEXT NULL,
    performed_date BIGINT NULL,
    next_due_km TEXT NULL,
    next_due_hour TEXT NULL,
    next_due_date BIGINT NULL,
    operation_id TEXT NOT NULL,
    is_cancelled BIGINT NOT NULL DEFAULT 0,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(id),
    FOREIGN KEY (maintenance_def_id) REFERENCES maintenance_definitions(id)
);
CREATE UNIQUE INDEX ux_vehicle_maintenances_op ON vehicle_maintenances(operation_id);
CREATE INDEX ix_vehicle_maintenances ON vehicle_maintenances(vehicle_id, maintenance_def_id, created_at);

CREATE TABLE maintenance_materials (
    id TEXT PRIMARY KEY,
    maintenance_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL,
    unit_price TEXT NULL,
    FOREIGN KEY (maintenance_id) REFERENCES vehicle_maintenances(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);
CREATE INDEX ix_maintenance_materials ON maintenance_materials(maintenance_id);

CREATE TABLE vehicle_inspections (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    vehicle_id TEXT NOT NULL,
    doc_type TEXT NOT NULL,                   -- inspection | insurance | kasko | calibration
    last_date BIGINT NULL,
    next_date BIGINT NULL,
    result TEXT NULL,
    place TEXT NULL,
    note TEXT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);
CREATE INDEX ix_vehicle_inspections ON vehicle_inspections(vehicle_id, doc_type, is_deleted);";
        cmd.ExecuteNonQuery();
    }
}
