CREATE TABLE "vehicle_categories" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"name" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "vehicle_meter_logs" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"vehicle_id" text NOT NULL,
	"old_value" numeric NOT NULL,
	"new_value" numeric NOT NULL,
	"source" text NOT NULL,
	"created_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "vehicle_models" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"brand_id" text,
	"name" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "vehicle_template_materials" (
	"template_id" text NOT NULL,
	"material_id" text NOT NULL
);
--> statement-breakpoint
CREATE TABLE "vehicle_templates" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"name" text NOT NULL,
	"internal_code" text,
	"vehicle_type_id" text,
	"category_id" text,
	"brand_id" text,
	"vehicle_model_id" text,
	"production_year" integer,
	"default_meter_unit" text DEFAULT 'km' NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "vehicle_types" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"name" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "vehicles" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"internal_code" text NOT NULL,
	"plate" text,
	"production_year" integer,
	"current_meter" numeric DEFAULT '0' NOT NULL,
	"meter_unit" text DEFAULT 'km' NOT NULL,
	"branch_id" text,
	"driver_personnel_id" text,
	"chassis_no" text,
	"engine_no" text,
	"status" text DEFAULT 'active' NOT NULL,
	"status_note" text,
	"vehicle_type_id" text,
	"category_id" text,
	"brand_id" text,
	"vehicle_model_id" text,
	"template_id" text,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX "ux_vehicle_categories" ON "vehicle_categories" USING btree ("company_id","name");--> statement-breakpoint
CREATE INDEX "ix_vehicle_meter_logs" ON "vehicle_meter_logs" USING btree ("vehicle_id","created_at");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_vehicle_models" ON "vehicle_models" USING btree ("company_id","brand_id","name");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_vehicle_template_materials" ON "vehicle_template_materials" USING btree ("template_id","material_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_vehicle_templates_name" ON "vehicle_templates" USING btree ("company_id","name");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_vehicle_types" ON "vehicle_types" USING btree ("company_id","name");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_vehicles_internal_code" ON "vehicles" USING btree ("company_id","internal_code");--> statement-breakpoint
CREATE INDEX "ix_vehicles_company" ON "vehicles" USING btree ("company_id","is_deleted");