using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Yakıt depo girişi + araç dağıtımı (fiyat snapshot, sayaç/log) + günlük faaliyet (hareket/transfer/bakım).
/// Bakım tipi günlük faaliyet ORTAK bakım servisini kullanır → tek kayıt; burada stok DÜŞMEZ.
/// </summary>
public sealed class Migration009_FuelDailyActivity : IMigration
{
    public int Version => 9;
    public string Name => "fuel_and_daily_activity";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
CREATE TABLE fuel_depot_entries (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    supplier_id TEXT NULL,
    liters TEXT NOT NULL,                     -- decimal
    unit_price TEXT NOT NULL DEFAULT '0',     -- decimal
    currency_code TEXT NOT NULL DEFAULT 'TRY',
    fx_rate TEXT NULL,
    invoice_no TEXT NULL,
    note TEXT NULL,
    entry_date BIGINT NOT NULL,
    operation_id TEXT NOT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE UNIQUE INDEX ux_fuel_depot_op ON fuel_depot_entries(operation_id);
CREATE INDEX ix_fuel_depot_company ON fuel_depot_entries(company_id, entry_date);

CREATE TABLE fuel_distributions (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    vehicle_id TEXT NOT NULL,
    prev_meter TEXT NULL,
    current_meter TEXT NULL,
    liters TEXT NOT NULL,                     -- decimal
    unit_price TEXT NOT NULL DEFAULT '0',     -- snapshot
    currency_code TEXT NOT NULL DEFAULT 'TRY',
    fx_rate TEXT NULL,
    personnel_id TEXT NULL,
    distribution_date BIGINT NOT NULL,
    note TEXT NULL,
    operation_id TEXT NOT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(id)
);
CREATE UNIQUE INDEX ux_fuel_dist_op ON fuel_distributions(operation_id);
CREATE INDEX ix_fuel_dist_company ON fuel_distributions(company_id, distribution_date);
CREATE INDEX ix_fuel_dist_vehicle ON fuel_distributions(vehicle_id, distribution_date);

CREATE TABLE daily_activities (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    activity_type TEXT NOT NULL,              -- maintenance | movement
    movement_kind TEXT NULL,                  -- movement | transfer (hareket tipinde)
    vehicle_id TEXT NULL,
    from_location_id TEXT NULL,
    to_location_id TEXT NULL,
    operator_id TEXT NULL,
    duration_days BIGINT NULL,
    description TEXT NULL,
    maintenance_id TEXT NULL,                 -- bakım tipinde: ortak bakım kaydına REFERANS
    source_module TEXT NOT NULL DEFAULT 'daily_activity',
    stock_processed BIGINT NOT NULL DEFAULT 0,
    activity_date BIGINT NOT NULL,
    operation_id TEXT NOT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (vehicle_id) REFERENCES vehicles(id),
    FOREIGN KEY (maintenance_id) REFERENCES vehicle_maintenances(id)
);
CREATE UNIQUE INDEX ux_daily_activities_op ON daily_activities(operation_id);
CREATE INDEX ix_daily_activities ON daily_activities(company_id, activity_type, activity_date);
CREATE INDEX ix_daily_activities_vehicle ON daily_activities(vehicle_id, activity_date);";
        cmd.ExecuteNonQuery();
    }
}
