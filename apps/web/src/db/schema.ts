// Merkezi PostgreSQL şeması (Drizzle) — yerel SQLite çekirdek şemasıyla fonksiyonel eşit.
// Standart kolonlar: id, company_id, created_at/updated_at (bigint Unix ms), version, is_deleted.
// Para alanları ilgili modül fazlarında numeric + currency_code ile gelir.
import {
  pgTable,
  text,
  bigint,
  bigserial,
  boolean,
  integer,
  numeric,
  uniqueIndex,
  index,
} from "drizzle-orm/pg-core";

const createdAt = () => bigint("created_at", { mode: "number" }).notNull();
const updatedAt = () => bigint("updated_at", { mode: "number" }).notNull();
const version = () => integer("version").notNull().default(1);
const isDeleted = () => boolean("is_deleted").notNull().default(false);

export const companies = pgTable("companies", {
  id: text("id").primaryKey(),
  name: text("name").notNull(),
  createdAt: createdAt(),
  updatedAt: updatedAt(),
  version: version(),
  isDeleted: isDeleted(),
});

export const branches = pgTable(
  "branches",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    parentId: text("parent_id"),
    name: text("name").notNull(),
    kind: text("kind").notNull().default("branch"), // branch | site
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [index("ix_branches_company").on(t.companyId, t.isDeleted)],
);

export const roles = pgTable(
  "roles",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id"), // null = sistem rolü
    roleKey: text("role_key").notNull(),
    name: text("name").notNull(),
    isSystem: boolean("is_system").notNull().default(false),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_roles_key").on(t.companyId, t.roleKey)],
);

export const users = pgTable(
  "users",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    username: text("username").notNull(),
    passwordHash: text("password_hash").notNull(),
    fullName: text("full_name"),
    isActive: boolean("is_active").notNull().default(true),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_users_username").on(t.companyId, t.username)],
);

export const userRoles = pgTable(
  "user_roles",
  {
    userId: text("user_id").notNull(),
    roleId: text("role_id").notNull(),
  },
  (t) => [uniqueIndex("ux_user_roles").on(t.userId, t.roleId)],
);

export const userPermissions = pgTable(
  "user_permissions",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    userId: text("user_id").notNull(),
    moduleKey: text("module_key").notNull(),
    canView: boolean("can_view").notNull().default(false),
    canCreate: boolean("can_create").notNull().default(false),
    canEdit: boolean("can_edit").notNull().default(false),
    canDelete: boolean("can_delete").notNull().default(false),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
  },
  (t) => [uniqueIndex("ux_user_permissions").on(t.userId, t.moduleKey)],
);

export const auditLogs = pgTable(
  "audit_logs",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    userId: text("user_id"),
    entityType: text("entity_type").notNull(),
    entityId: text("entity_id").notNull(),
    action: text("action").notNull(),
    beforeJson: text("before_json"),
    afterJson: text("after_json"),
    correlationId: text("correlation_id"),
    createdAt: createdAt(),
  },
  (t) => [
    index("ix_audit_company_time").on(t.companyId, t.createdAt),
    index("ix_audit_entity").on(t.entityType, t.entityId),
  ],
);

export const fileRecords = pgTable(
  "file_records",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    entityType: text("entity_type").notNull(),
    entityId: text("entity_id").notNull(),
    kind: text("kind").notNull().default("photo"),
    storageProvider: text("storage_provider").notNull().default("local"),
    storageKey: text("storage_key").notNull(),
    mime: text("mime"),
    sizeBytes: bigint("size_bytes", { mode: "number" }),
    sha256: text("sha256"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [index("ix_file_entity").on(t.entityType, t.entityId, t.isDeleted)],
);

export const syncDevices = pgTable("sync_devices", {
  id: text("id").primaryKey(),
  companyId: text("company_id").notNull(),
  deviceName: text("device_name").notNull(),
  enrollKeyHash: text("enroll_key_hash"),
  status: text("status").notNull().default("pending"), // pending | active | revoked
  createdAt: createdAt(),
  updatedAt: updatedAt(),
  version: version(),
});

export const syncOutbox = pgTable(
  "sync_outbox",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    operationId: text("operation_id").notNull(),
    entityType: text("entity_type").notNull(),
    entityId: text("entity_id").notNull(),
    payloadJson: text("payload_json").notNull(),
    payloadHash: text("payload_hash").notNull(),
    baseVersion: integer("base_version"),
    deviceId: text("device_id"),
    status: text("status").notNull().default("pending"),
    createdAt: createdAt(),
  },
  (t) => [uniqueIndex("ux_outbox_operation").on(t.operationId)],
);

export const syncInbox = pgTable(
  "sync_inbox",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    operationId: text("operation_id").notNull(),
    entityType: text("entity_type").notNull(),
    entityId: text("entity_id").notNull(),
    payloadJson: text("payload_json").notNull(),
    result: text("result").notNull().default("applied"),
    appliedAt: bigint("applied_at", { mode: "number" }).notNull(),
  },
  (t) => [uniqueIndex("ux_inbox_operation").on(t.operationId)],
);

export const personnel = pgTable(
  "personnel",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    branchId: text("branch_id"),
    fullName: text("full_name").notNull(),
    title: text("title"),
    phone: text("phone"),
    isActive: boolean("is_active").notNull().default(true),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [
    index("ix_personnel_company").on(t.companyId, t.isDeleted),
    index("ix_personnel_branch").on(t.branchId),
  ],
);

export const userScopes = pgTable(
  "user_scopes",
  {
    userId: text("user_id").notNull(),
    companyId: text("company_id").notNull(),
    branchId: text("branch_id").notNull(),
  },
  (t) => [uniqueIndex("ux_user_scopes").on(t.userId, t.branchId)],
);

// ---- Faz 06: Malzeme + tanımlar + stok defteri ----
export const materialCategories = pgTable(
  "material_categories",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    parentId: text("parent_id"),
    name: text("name").notNull(),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_mat_categories").on(t.companyId, t.parentId, t.name)],
);

export const brands = pgTable(
  "brands",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    name: text("name").notNull(),
    brandType: text("brand_type").notNull().default("material"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_brands").on(t.companyId, t.brandType, t.name)],
);

export const units = pgTable(
  "units",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    name: text("name").notNull(),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_units").on(t.companyId, t.name)],
);

export const suppliers = pgTable(
  "suppliers",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    name: text("name").notNull(),
    phone: text("phone"),
    note: text("note"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_suppliers").on(t.companyId, t.name)],
);

export const materials = pgTable(
  "materials",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    code: text("code").notNull(),
    name: text("name").notNull(),
    type: text("type"),
    categoryId: text("category_id"),
    unitId: text("unit_id"),
    brandId: text("brand_id"),
    supplierId: text("supplier_id"),
    minStock: numeric("min_stock").notNull().default("0"),
    unitPrice: numeric("unit_price").notNull().default("0"),
    currencyCode: text("currency_code").notNull().default("TRY"),
    description: text("description"),
    externalEquivalentNote: text("external_equivalent_note"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [
    uniqueIndex("ux_materials_code").on(t.companyId, t.code),
    index("ix_materials_company").on(t.companyId, t.isDeleted),
  ],
);

export const materialEquivalents = pgTable(
  "material_equivalents",
  {
    materialId: text("material_id").notNull(),
    equivalentMaterialId: text("equivalent_material_id").notNull(),
  },
  (t) => [uniqueIndex("ux_material_equivalents").on(t.materialId, t.equivalentMaterialId)],
);

export const materialCompatibleVehicles = pgTable(
  "material_compatible_vehicles",
  {
    materialId: text("material_id").notNull(),
    vehicleId: text("vehicle_id").notNull(),
  },
  (t) => [uniqueIndex("ux_material_compat").on(t.materialId, t.vehicleId)],
);

export const stockMovements = pgTable(
  "stock_movements",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    materialId: text("material_id").notNull(),
    branchId: text("branch_id"),
    movementType: text("movement_type").notNull(),
    direction: integer("direction").notNull(),
    quantity: numeric("quantity").notNull(),
    unitPrice: numeric("unit_price"),
    currencyCode: text("currency_code"),
    fxRate: numeric("fx_rate"),
    operationId: text("operation_id").notNull(),
    note: text("note"),
    createdAt: createdAt(),
    documentId: text("document_id"),
    branchFromId: text("branch_from_id"),
    isReversed: boolean("is_reversed").notNull().default(false),
    reversesMovementId: text("reverses_movement_id"),
  },
  (t) => [
    uniqueIndex("ux_stock_movements_operation").on(t.operationId),
    index("ix_stock_movements_material").on(t.materialId, t.createdAt),
  ],
);

export const stockDocuments = pgTable(
  "stock_documents",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    docType: text("doc_type").notNull(), // in | out | transfer | count
    docNo: text("doc_no").notNull(),
    docDate: bigint("doc_date", { mode: "number" }).notNull(),
    fromBranchId: text("from_branch_id"),
    toBranchId: text("to_branch_id"),
    personnelId: text("personnel_id"),
    vehicleId: text("vehicle_id"),
    note: text("note"),
    status: text("status").notNull().default("active"),
    groupId: text("group_id"),
    createdAt: createdAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [
    uniqueIndex("ux_stock_documents_no").on(t.companyId, t.docType, t.docNo),
    index("ix_stock_documents_company").on(t.companyId, t.docType, t.createdAt),
  ],
);

export const stockCountLines = pgTable(
  "stock_count_lines",
  {
    id: text("id").primaryKey(),
    documentId: text("document_id").notNull(),
    materialId: text("material_id").notNull(),
    systemQty: numeric("system_qty").notNull(),
    countedQty: numeric("counted_qty").notNull(),
    diffQty: numeric("diff_qty").notNull(),
    reason: text("reason"),
  },
  (t) => [index("ix_stock_count_lines_doc").on(t.documentId)],
);

export const stockBalances = pgTable("stock_balances", {
  companyId: text("company_id").notNull(),
  materialId: text("material_id").primaryKey(),
  quantity: numeric("quantity").notNull().default("0"),
  updatedAt: updatedAt(),
});

export const fxRates = pgTable(
  "fx_rates",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id"),
    currencyCode: text("currency_code").notNull(),
    rateToBase: numeric("rate_to_base").notNull(),
    asOf: bigint("as_of", { mode: "number" }).notNull(),
    createdAt: createdAt(),
  },
  (t) => [index("ix_fx_rates").on(t.currencyCode, t.asOf)],
);

// ---- Faz 08: Araçlar + şablonlar + sayaç ----
export const vehicleTypes = pgTable(
  "vehicle_types",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    name: text("name").notNull(),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_vehicle_types").on(t.companyId, t.name)],
);

export const vehicleCategories = pgTable(
  "vehicle_categories",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    name: text("name").notNull(),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_vehicle_categories").on(t.companyId, t.name)],
);

export const vehicleModels = pgTable(
  "vehicle_models",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    brandId: text("brand_id"),
    name: text("name").notNull(),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_vehicle_models").on(t.companyId, t.brandId, t.name)],
);

export const vehicleTemplates = pgTable(
  "vehicle_templates",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    name: text("name").notNull(),
    internalCode: text("internal_code"),
    vehicleTypeId: text("vehicle_type_id"),
    categoryId: text("category_id"),
    brandId: text("brand_id"),
    vehicleModelId: text("vehicle_model_id"),
    productionYear: integer("production_year"),
    defaultMeterUnit: text("default_meter_unit").notNull().default("km"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [uniqueIndex("ux_vehicle_templates_name").on(t.companyId, t.name)],
);

export const vehicleTemplateMaterials = pgTable(
  "vehicle_template_materials",
  {
    templateId: text("template_id").notNull(),
    materialId: text("material_id").notNull(),
  },
  (t) => [uniqueIndex("ux_vehicle_template_materials").on(t.templateId, t.materialId)],
);

export const vehicles = pgTable(
  "vehicles",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    internalCode: text("internal_code").notNull(),
    plate: text("plate"),
    productionYear: integer("production_year"),
    currentMeter: numeric("current_meter").notNull().default("0"),
    meterUnit: text("meter_unit").notNull().default("km"),
    branchId: text("branch_id"),
    driverPersonnelId: text("driver_personnel_id"),
    chassisNo: text("chassis_no"),
    engineNo: text("engine_no"),
    status: text("status").notNull().default("active"),
    statusNote: text("status_note"),
    vehicleTypeId: text("vehicle_type_id"),
    categoryId: text("category_id"),
    brandId: text("brand_id"),
    vehicleModelId: text("vehicle_model_id"),
    templateId: text("template_id"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [
    uniqueIndex("ux_vehicles_internal_code").on(t.companyId, t.internalCode),
    index("ix_vehicles_company").on(t.companyId, t.isDeleted),
  ],
);

export const vehicleMeterLogs = pgTable(
  "vehicle_meter_logs",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    vehicleId: text("vehicle_id").notNull(),
    oldValue: numeric("old_value").notNull(),
    newValue: numeric("new_value").notNull(),
    source: text("source").notNull(),
    createdAt: createdAt(),
  },
  (t) => [index("ix_vehicle_meter_logs").on(t.vehicleId, t.createdAt)],
);

// ---- Faz 09: Bakım + muayene/sigorta ----
export const maintenanceDefinitions = pgTable(
  "maintenance_definitions",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    parentDefId: text("parent_def_id"),
    name: text("name").notNull(),
    intervalValue: numeric("interval_value").notNull().default("0"),
    intervalUnit: text("interval_unit").notNull().default("km"),
    description: text("description"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [index("ix_maint_defs_company").on(t.companyId, t.isDeleted)],
);

export const maintenanceDefinitionVehicles = pgTable(
  "maintenance_definition_vehicles",
  {
    definitionId: text("definition_id").notNull(),
    vehicleId: text("vehicle_id").notNull(),
  },
  (t) => [uniqueIndex("ux_maint_def_vehicles").on(t.definitionId, t.vehicleId)],
);

export const vehicleMaintenances = pgTable(
  "vehicle_maintenances",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    vehicleId: text("vehicle_id").notNull(),
    maintenanceDefId: text("maintenance_def_id").notNull(),
    subDefinitionId: text("sub_definition_id"),
    technicianId: text("technician_id"),
    description: text("description"),
    subDefinitionNote: text("sub_definition_note"),
    performedKm: numeric("performed_km"),
    performedHour: numeric("performed_hour"),
    performedDate: bigint("performed_date", { mode: "number" }),
    nextDueKm: numeric("next_due_km"),
    nextDueHour: numeric("next_due_hour"),
    nextDueDate: bigint("next_due_date", { mode: "number" }),
    operationId: text("operation_id").notNull(),
    isCancelled: boolean("is_cancelled").notNull().default(false),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [
    uniqueIndex("ux_vehicle_maintenances_op").on(t.operationId),
    index("ix_vehicle_maintenances").on(t.vehicleId, t.maintenanceDefId, t.createdAt),
  ],
);

export const maintenanceMaterials = pgTable(
  "maintenance_materials",
  {
    id: text("id").primaryKey(),
    maintenanceId: text("maintenance_id").notNull(),
    materialId: text("material_id").notNull(),
    quantity: numeric("quantity").notNull(),
    unitPrice: numeric("unit_price"),
  },
  (t) => [index("ix_maintenance_materials").on(t.maintenanceId)],
);

export const vehicleInspections = pgTable(
  "vehicle_inspections",
  {
    id: text("id").primaryKey(),
    companyId: text("company_id").notNull(),
    vehicleId: text("vehicle_id").notNull(),
    docType: text("doc_type").notNull(),
    lastDate: bigint("last_date", { mode: "number" }),
    nextDate: bigint("next_date", { mode: "number" }),
    result: text("result"),
    place: text("place"),
    note: text("note"),
    createdAt: createdAt(),
    updatedAt: updatedAt(),
    version: version(),
    isDeleted: isDeleted(),
  },
  (t) => [index("ix_vehicle_inspections").on(t.vehicleId, t.docType, t.isDeleted)],
);

// Açılış/health probe tablosu (Faz 01'den korunur).
export const healthCheck = pgTable("_health_check", {
  id: bigserial("id", { mode: "number" }).primaryKey(),
  ts: bigint("ts", { mode: "number" }).notNull(),
});
