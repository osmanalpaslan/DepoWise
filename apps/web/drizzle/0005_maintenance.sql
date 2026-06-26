CREATE TABLE "maintenance_definition_vehicles" (
	"definition_id" text NOT NULL,
	"vehicle_id" text NOT NULL
);
--> statement-breakpoint
CREATE TABLE "maintenance_definitions" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"parent_def_id" text,
	"name" text NOT NULL,
	"interval_value" numeric DEFAULT '0' NOT NULL,
	"interval_unit" text DEFAULT 'km' NOT NULL,
	"description" text,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "maintenance_materials" (
	"id" text PRIMARY KEY NOT NULL,
	"maintenance_id" text NOT NULL,
	"material_id" text NOT NULL,
	"quantity" numeric NOT NULL,
	"unit_price" numeric
);
--> statement-breakpoint
CREATE TABLE "vehicle_inspections" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"vehicle_id" text NOT NULL,
	"doc_type" text NOT NULL,
	"last_date" bigint,
	"next_date" bigint,
	"result" text,
	"place" text,
	"note" text,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "vehicle_maintenances" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"vehicle_id" text NOT NULL,
	"maintenance_def_id" text NOT NULL,
	"sub_definition_id" text,
	"technician_id" text,
	"description" text,
	"sub_definition_note" text,
	"performed_km" numeric,
	"performed_hour" numeric,
	"performed_date" bigint,
	"next_due_km" numeric,
	"next_due_hour" numeric,
	"next_due_date" bigint,
	"operation_id" text NOT NULL,
	"is_cancelled" boolean DEFAULT false NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX "ux_maint_def_vehicles" ON "maintenance_definition_vehicles" USING btree ("definition_id","vehicle_id");--> statement-breakpoint
CREATE INDEX "ix_maint_defs_company" ON "maintenance_definitions" USING btree ("company_id","is_deleted");--> statement-breakpoint
CREATE INDEX "ix_maintenance_materials" ON "maintenance_materials" USING btree ("maintenance_id");--> statement-breakpoint
CREATE INDEX "ix_vehicle_inspections" ON "vehicle_inspections" USING btree ("vehicle_id","doc_type","is_deleted");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_vehicle_maintenances_op" ON "vehicle_maintenances" USING btree ("operation_id");--> statement-breakpoint
CREATE INDEX "ix_vehicle_maintenances" ON "vehicle_maintenances" USING btree ("vehicle_id","maintenance_def_id","created_at");