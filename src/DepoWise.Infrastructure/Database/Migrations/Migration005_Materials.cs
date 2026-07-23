using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Malzeme ana verisi + tanımlar (kategori/marka/birim/tedarikçi) + muadil + uyumlu araç +
/// stok hareket defteri (ana kaynak) ve bakiye cache + kur snapshot tablosu.
/// Para: TEXT (invariant decimal) + currency_code. Stok bakiyesi DOĞRUDAN değişmez; ledger üzerinden.
/// </summary>
public sealed class Migration005_Materials : IMigration
{
    public int Version => 5;
    public string Name => "materials_and_ledger";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
-- Tanımlar
CREATE TABLE material_categories (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    parent_id TEXT NULL,                 -- alt kategori
    name TEXT NOT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (parent_id) REFERENCES material_categories(id)
);
CREATE UNIQUE INDEX ux_mat_categories ON material_categories(company_id, COALESCE(parent_id,''), name);

CREATE TABLE brands (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    brand_type TEXT NOT NULL DEFAULT 'material',  -- material | vehicle
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_brands ON brands(company_id, brand_type, name);

CREATE TABLE units (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_units ON units(company_id, name);

CREATE TABLE suppliers (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    name TEXT NOT NULL,
    phone TEXT NULL, note TEXT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_suppliers ON suppliers(company_id, name);

-- Malzeme kartı
CREATE TABLE materials (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    code TEXT NOT NULL,
    name TEXT NOT NULL,
    type TEXT NULL,
    category_id TEXT NULL,
    unit_id TEXT NULL,
    brand_id TEXT NULL,
    supplier_id TEXT NULL,
    min_stock TEXT NOT NULL DEFAULT '0',      -- decimal (invariant)
    unit_price TEXT NOT NULL DEFAULT '0',     -- decimal (invariant)
    currency_code TEXT NOT NULL DEFAULT 'TRY',
    description TEXT NULL,
    external_equivalent_note TEXT NULL,
    created_at BIGINT NOT NULL, updated_at BIGINT NOT NULL,
    version BIGINT NOT NULL DEFAULT 1, is_deleted BIGINT NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (category_id) REFERENCES material_categories(id),
    FOREIGN KEY (unit_id) REFERENCES units(id),
    FOREIGN KEY (brand_id) REFERENCES brands(id),
    FOREIGN KEY (supplier_id) REFERENCES suppliers(id)
);
CREATE UNIQUE INDEX ux_materials_code ON materials(company_id, code);
CREATE INDEX ix_materials_company ON materials(company_id, is_deleted);

-- Muadil malzeme (çift yönlü; servis simetrik + döngü güvenli yazar)
CREATE TABLE material_equivalents (
    material_id TEXT NOT NULL,
    equivalent_material_id TEXT NOT NULL,
    PRIMARY KEY (material_id, equivalent_material_id),
    CHECK (material_id <> equivalent_material_id),
    FOREIGN KEY (material_id) REFERENCES materials(id),
    FOREIGN KEY (equivalent_material_id) REFERENCES materials(id)
);

-- Uyumlu araç (vehicle_id FK Faz 08'de eklenecek; şimdilik serbest metin referans)
CREATE TABLE material_compatible_vehicles (
    material_id TEXT NOT NULL,
    vehicle_id TEXT NOT NULL,
    PRIMARY KEY (material_id, vehicle_id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);

-- Stok hareket defteri (ANA KAYNAK)
CREATE TABLE stock_movements (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    branch_id TEXT NULL,
    movement_type TEXT NOT NULL,              -- opening | in | out | transfer | adjustment
    direction BIGINT NOT NULL,               -- +1 | -1
    quantity TEXT NOT NULL,                   -- decimal (invariant), pozitif
    unit_price TEXT NULL,                     -- işlem anı birim fiyat (snapshot)
    currency_code TEXT NULL,
    fx_rate TEXT NULL,                        -- işlem anı kur snapshot (baz para birimine)
    operation_id TEXT NOT NULL,               -- idempotency
    note TEXT NULL,
    created_at BIGINT NOT NULL,
    FOREIGN KEY (material_id) REFERENCES materials(id)
);
CREATE UNIQUE INDEX ux_stock_movements_operation ON stock_movements(operation_id);
CREATE INDEX ix_stock_movements_material ON stock_movements(material_id, created_at);

-- Bakiye cache (ledger ile aynı transaction'da güncellenir; doğrudan değiştirilmez)
CREATE TABLE stock_balances (
    company_id TEXT NOT NULL,
    material_id TEXT NOT NULL,
    quantity TEXT NOT NULL DEFAULT '0',       -- decimal (invariant)
    updated_at BIGINT NOT NULL,
    PRIMARY KEY (material_id),
    FOREIGN KEY (material_id) REFERENCES materials(id)
);

-- Kur (manuel + tarihçe); işlem anı snapshot stock_movements.fx_rate'e yazılır
CREATE TABLE fx_rates (
    id TEXT PRIMARY KEY,
    company_id TEXT NULL,                      -- NULL = global
    currency_code TEXT NOT NULL,              -- USD | EUR ... (baz: TRY)
    rate_to_base TEXT NOT NULL,               -- 1 birim yabancı = rate_to_base TRY
    as_of BIGINT NOT NULL,
    created_at BIGINT NOT NULL
);
CREATE INDEX ix_fx_rates ON fx_rates(currency_code, as_of);";
        cmd.ExecuteNonQuery();
    }
}
