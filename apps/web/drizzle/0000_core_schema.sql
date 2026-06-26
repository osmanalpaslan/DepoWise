CREATE TABLE "audit_logs" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"user_id" text,
	"entity_type" text NOT NULL,
	"entity_id" text NOT NULL,
	"action" text NOT NULL,
	"before_json" text,
	"after_json" text,
	"correlation_id" text,
	"created_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "branches" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"parent_id" text,
	"name" text NOT NULL,
	"kind" text DEFAULT 'branch' NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "companies" (
	"id" text PRIMARY KEY NOT NULL,
	"name" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "file_records" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"entity_type" text NOT NULL,
	"entity_id" text NOT NULL,
	"kind" text DEFAULT 'photo' NOT NULL,
	"storage_provider" text DEFAULT 'local' NOT NULL,
	"storage_key" text NOT NULL,
	"mime" text,
	"size_bytes" bigint,
	"sha256" text,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "_health_check" (
	"id" bigserial PRIMARY KEY NOT NULL,
	"ts" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "roles" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text,
	"role_key" text NOT NULL,
	"name" text NOT NULL,
	"is_system" boolean DEFAULT false NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "sync_devices" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"device_name" text NOT NULL,
	"enroll_key_hash" text,
	"status" text DEFAULT 'pending' NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL
);
--> statement-breakpoint
CREATE TABLE "sync_inbox" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"operation_id" text NOT NULL,
	"entity_type" text NOT NULL,
	"entity_id" text NOT NULL,
	"payload_json" text NOT NULL,
	"result" text DEFAULT 'applied' NOT NULL,
	"applied_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "sync_outbox" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"operation_id" text NOT NULL,
	"entity_type" text NOT NULL,
	"entity_id" text NOT NULL,
	"payload_json" text NOT NULL,
	"payload_hash" text NOT NULL,
	"base_version" integer,
	"device_id" text,
	"status" text DEFAULT 'pending' NOT NULL,
	"created_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "user_permissions" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"user_id" text NOT NULL,
	"module_key" text NOT NULL,
	"can_view" boolean DEFAULT false NOT NULL,
	"can_create" boolean DEFAULT false NOT NULL,
	"can_edit" boolean DEFAULT false NOT NULL,
	"can_delete" boolean DEFAULT false NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL
);
--> statement-breakpoint
CREATE TABLE "user_roles" (
	"user_id" text NOT NULL,
	"role_id" text NOT NULL
);
--> statement-breakpoint
CREATE TABLE "users" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"username" text NOT NULL,
	"password_hash" text NOT NULL,
	"full_name" text,
	"is_active" boolean DEFAULT true NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE INDEX "ix_audit_company_time" ON "audit_logs" USING btree ("company_id","created_at");--> statement-breakpoint
CREATE INDEX "ix_audit_entity" ON "audit_logs" USING btree ("entity_type","entity_id");--> statement-breakpoint
CREATE INDEX "ix_branches_company" ON "branches" USING btree ("company_id","is_deleted");--> statement-breakpoint
CREATE INDEX "ix_file_entity" ON "file_records" USING btree ("entity_type","entity_id","is_deleted");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_roles_key" ON "roles" USING btree ("company_id","role_key");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_inbox_operation" ON "sync_inbox" USING btree ("operation_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_outbox_operation" ON "sync_outbox" USING btree ("operation_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_user_permissions" ON "user_permissions" USING btree ("user_id","module_key");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_user_roles" ON "user_roles" USING btree ("user_id","role_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_users_username" ON "users" USING btree ("company_id","username");