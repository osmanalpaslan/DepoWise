using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Araç lookups (tip/kategori/model) + araç şablonları + araç kartı + sayaç geçmişi.
/// Sayaç (current_meter) geriye gidemez; tüm değişimler vehicle_meter_logs'a yazılır.
/// </summary>
public sealed class Migration007_Vehicles : IMigration
{
    public int Version => 7;
    public string Name => "vehicles_and_meters";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE vehicle_types (
    id TEXT PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
    created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1, is_deleted INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_vehicle_types ON vehicle_types(company_id, name);

CREATE TABLE vehicle_categories (
    id TEXT PRIMARY KEY, company_id TEXT NOT NULL, name TEXT NOT NULL,
    created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1, is_deleted INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_vehicle_categories ON vehicle_categories(company_id, name);

CREATE TABLE vehicle_models (
    id TEXT PRIMARY KEY, company_id TEXT NOT NULL, brand_id TEXT NULL, name TEXT NOT NULL,
    created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1, is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (brand_id) REFERENCES brands(id)
);
CREATE UNIQUE INDEX ux_vehicle_models ON vehicle_models(company_id, COALESCE(brand_id,''), name);

CREATE TABLE vehicle_templates (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    internal_code TEXT NULL,                 -- otomatik iç kod örneği (ör. KM-001)
    vehicle_type_id TEXT NULL,
    category_id TEXT NULL,
    brand_id TEXT NULL,
    vehicle_model_id TEXT NULL,
    production_year INTEGER NULL,
    default_meter_unit TEXT NOT NULL DEFAULT 'km',  -- km | hour
    created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1, is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE UNIQUE INDEX ux_vehicle_templates_name ON vehicle_templates(company_id, name);

CREATE TABLE vehicle_template_materials (
    template_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    PRIMARY KEY (template_id, material_id),
    FOREIGN KEY (template_id) REFERENCES vehicle_templates(id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);

CREATE TABLE vehicles (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    internal_code TEXT NOT NULL,
    plate TEXT NULL,
    production_year INTEGER NULL,
    current_meter TEXT NOT NULL DEFAULT '0',  -- decimal (invariant)
    meter_unit TEXT NOT NULL DEFAULT 'km',    -- km | hour
    branch_id TEXT NULL,
    driver_personnel_id TEXT NULL,
    chassis_no TEXT NULL,
    engine_no TEXT NULL,
    status TEXT NOT NULL DEFAULT 'active',     -- active | passive | maintenance
    status_note TEXT NULL,
    vehicle_type_id TEXT NULL,
    category_id TEXT NULL,
    brand_id TEXT NULL,
    vehicle_model_id TEXT NULL,
    template_id TEXT NULL,
    created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1, is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (branch_id) REFERENCES branches(id),
    FOREIGN KEY (driver_personnel_id) REFERENCES personnel(id),
    FOREIGN KEY (template_id) REFERENCES vehicle_templates(id)
);
CREATE UNIQUE INDEX ux_vehicles_internal_code ON vehicles(company_id, internal_code);
CREATE INDEX ix_vehicles_company ON vehicles(company_id, is_deleted);

CREATE TABLE vehicle_meter_logs (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    vehicle_id TEXT NOT NULL,
    old_value TEXT NOT NULL,
    new_value TEXT NOT NULL,
    source TEXT NOT NULL,                      -- vehicle_form | maintenance | fuel ...
    created_at INTEGER NOT NULL,
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);
CREATE INDEX ix_vehicle_meter_logs ON vehicle_meter_logs(vehicle_id, created_at);";
        cmd.ExecuteNonQuery();
    }
}
