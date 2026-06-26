CREATE TABLE "brands" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"name" text NOT NULL,
	"brand_type" text DEFAULT 'material' NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "fx_rates" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text,
	"currency_code" text NOT NULL,
	"rate_to_base" numeric NOT NULL,
	"as_of" bigint NOT NULL,
	"created_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "material_categories" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"parent_id" text,
	"name" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "material_compatible_vehicles" (
	"material_id" text NOT NULL,
	"vehicle_id" text NOT NULL
);
--> statement-breakpoint
CREATE TABLE "material_equivalents" (
	"material_id" text NOT NULL,
	"equivalent_material_id" text NOT NULL
);
--> statement-breakpoint
CREATE TABLE "materials" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"code" text NOT NULL,
	"name" text NOT NULL,
	"type" text,
	"category_id" text,
	"unit_id" text,
	"brand_id" text,
	"supplier_id" text,
	"min_stock" numeric DEFAULT '0' NOT NULL,
	"unit_price" numeric DEFAULT '0' NOT NULL,
	"currency_code" text DEFAULT 'TRY' NOT NULL,
	"description" text,
	"external_equivalent_note" text,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "stock_balances" (
	"company_id" text NOT NULL,
	"material_id" text PRIMARY KEY NOT NULL,
	"quantity" numeric DEFAULT '0' NOT NULL,
	"updated_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "stock_movements" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"material_id" text NOT NULL,
	"branch_id" text,
	"movement_type" text NOT NULL,
	"direction" integer NOT NULL,
	"quantity" numeric NOT NULL,
	"unit_price" numeric,
	"currency_code" text,
	"fx_rate" numeric,
	"operation_id" text NOT NULL,
	"note" text,
	"created_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE TABLE "suppliers" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"name" text NOT NULL,
	"phone" text,
	"note" text,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "units" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"name" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX "ux_brands" ON "brands" USING btree ("company_id","brand_type","name");--> statement-breakpoint
CREATE INDEX "ix_fx_rates" ON "fx_rates" USING btree ("currency_code","as_of");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_mat_categories" ON "material_categories" USING btree ("company_id","parent_id","name");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_material_compat" ON "material_compatible_vehicles" USING btree ("material_id","vehicle_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_material_equivalents" ON "material_equivalents" USING btree ("material_id","equivalent_material_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_materials_code" ON "materials" USING btree ("company_id","code");--> statement-breakpoint
CREATE INDEX "ix_materials_company" ON "materials" USING btree ("company_id","is_deleted");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_stock_movements_operation" ON "stock_movements" USING btree ("operation_id");--> statement-breakpoint
CREATE INDEX "ix_stock_movements_material" ON "stock_movements" USING btree ("material_id","created_at");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_suppliers" ON "suppliers" USING btree ("company_id","name");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_units" ON "units" USING btree ("company_id","name");