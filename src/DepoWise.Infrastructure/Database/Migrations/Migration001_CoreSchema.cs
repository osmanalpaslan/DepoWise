using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// Çekirdek tenant/güvenlik/audit/dosya/sync şeması.
/// Standart kolonlar: id TEXT PK, company_id TEXT, created_at/updated_at INTEGER (Unix ms),
/// version INTEGER (optimistic concurrency), is_deleted INTEGER (soft-delete).
/// Para alanları ilgili modül fazlarında decimal-as-TEXT + currency_code ile gelir.
/// </summary>
public sealed class Migration001_CoreSchema : IMigration
{
    public int Version => 1;
    public string Name => "core_schema";

    public void Up(DbConnection conn, DbTransaction tx)
    {
        Exec(conn, tx, @"
CREATE TABLE companies (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE branches (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    parent_id TEXT NULL,
    name TEXT NOT NULL,
    kind TEXT NOT NULL DEFAULT 'branch',           -- branch | site
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id),
    FOREIGN KEY (parent_id) REFERENCES branches(id)
);
CREATE INDEX ix_branches_company ON branches(company_id, is_deleted);

CREATE TABLE roles (
    id TEXT PRIMARY KEY,
    company_id TEXT NULL,                           -- NULL = sistem rolü (tüm firmalar)
    role_key TEXT NOT NULL,
    name TEXT NOT NULL,
    is_system INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_roles_key ON roles(COALESCE(company_id,''), role_key);

CREATE TABLE users (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    username TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    full_name TEXT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE UNIQUE INDEX ux_users_username ON users(company_id, username);

CREATE TABLE user_roles (
    user_id TEXT NOT NULL,
    role_id TEXT NOT NULL,
    PRIMARY KEY (user_id, role_id),
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (role_id) REFERENCES roles(id)
);

CREATE TABLE user_permissions (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    user_id TEXT NOT NULL,
    module_key TEXT NOT NULL,
    can_view INTEGER NOT NULL DEFAULT 0,
    can_create INTEGER NOT NULL DEFAULT 0,
    can_edit INTEGER NOT NULL DEFAULT 0,
    can_delete INTEGER NOT NULL DEFAULT 0,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (user_id) REFERENCES users(id)
);
CREATE UNIQUE INDEX ux_user_permissions ON user_permissions(user_id, module_key);

CREATE TABLE audit_logs (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    user_id TEXT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    action TEXT NOT NULL,                           -- create | update | delete | restore | reverse
    before_json TEXT NULL,
    after_json TEXT NULL,
    correlation_id TEXT NULL,
    created_at INTEGER NOT NULL
);
CREATE INDEX ix_audit_company_time ON audit_logs(company_id, created_at);
CREATE INDEX ix_audit_entity ON audit_logs(entity_type, entity_id);

CREATE TABLE file_records (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    kind TEXT NOT NULL DEFAULT 'photo',
    storage_provider TEXT NOT NULL DEFAULT 'local',
    storage_key TEXT NOT NULL,
    mime TEXT NULL,
    size_bytes INTEGER NULL,
    sha256 TEXT NULL,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    is_deleted INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);
CREATE INDEX ix_file_entity ON file_records(entity_type, entity_id, is_deleted);

CREATE TABLE sync_devices (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    device_name TEXT NOT NULL,
    enroll_key_hash TEXT NULL,
    status TEXT NOT NULL DEFAULT 'pending',         -- pending | active | revoked
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (company_id) REFERENCES companies(id)
);

CREATE TABLE sync_outbox (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    operation_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    payload_hash TEXT NOT NULL,
    base_version INTEGER NULL,
    device_id TEXT NULL,
    status TEXT NOT NULL DEFAULT 'pending',         -- pending | sent | acked | rejected | conflict
    created_at INTEGER NOT NULL
);
CREATE UNIQUE INDEX ux_outbox_operation ON sync_outbox(operation_id);

CREATE TABLE sync_inbox (
    id TEXT PRIMARY KEY,
    company_id TEXT NOT NULL,
    operation_id TEXT NOT NULL,
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    payload_json TEXT NOT NULL,
    result TEXT NOT NULL DEFAULT 'applied',          -- applied | already_applied | rejected | conflict
    applied_at INTEGER NOT NULL
);
CREATE UNIQUE INDEX ux_inbox_operation ON sync_inbox(operation_id);
");
    }

    private static void Exec(DbConnection conn, DbTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
