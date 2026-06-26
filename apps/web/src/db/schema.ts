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

// Açılış/health probe tablosu (Faz 01'den korunur).
export const healthCheck = pgTable("_health_check", {
  id: bigserial("id", { mode: "number" }).primaryKey(),
  ts: bigint("ts", { mode: "number" }).notNull(),
});
