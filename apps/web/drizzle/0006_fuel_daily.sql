CREATE TABLE "daily_activities" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"activity_type" text NOT NULL,
	"movement_kind" text,
	"vehicle_id" text,
	"from_location_id" text,
	"to_location_id" text,
	"operator_id" text,
	"duration_days" integer,
	"description" text,
	"maintenance_id" text,
	"source_module" text DEFAULT 'daily_activity' NOT NULL,
	"stock_processed" boolean DEFAULT false NOT NULL,
	"activity_date" bigint NOT NULL,
	"operation_id" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "fuel_depot_entries" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"supplier_id" text,
	"liters" numeric NOT NULL,
	"unit_price" numeric DEFAULT '0' NOT NULL,
	"currency_code" text DEFAULT 'TRY' NOT NULL,
	"fx_rate" numeric,
	"invoice_no" text,
	"note" text,
	"entry_date" bigint NOT NULL,
	"operation_id" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "fuel_distributions" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"vehicle_id" text NOT NULL,
	"prev_meter" numeric,
	"current_meter" numeric,
	"liters" numeric NOT NULL,
	"unit_price" numeric DEFAULT '0' NOT NULL,
	"currency_code" text DEFAULT 'TRY' NOT NULL,
	"fx_rate" numeric,
	"personnel_id" text,
	"distribution_date" bigint NOT NULL,
	"note" text,
	"operation_id" text NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX "ux_daily_activities_op" ON "daily_activities" USING btree ("operation_id");--> statement-breakpoint
CREATE INDEX "ix_daily_activities" ON "daily_activities" USING btree ("company_id","activity_type","activity_date");--> statement-breakpoint
CREATE INDEX "ix_daily_activities_vehicle" ON "daily_activities" USING btree ("vehicle_id","activity_date");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_fuel_depot_op" ON "fuel_depot_entries" USING btree ("operation_id");--> statement-breakpoint
CREATE INDEX "ix_fuel_depot_company" ON "fuel_depot_entries" USING btree ("company_id","entry_date");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_fuel_dist_op" ON "fuel_distributions" USING btree ("operation_id");--> statement-breakpoint
CREATE INDEX "ix_fuel_dist_company" ON "fuel_distributions" USING btree ("company_id","distribution_date");--> statement-breakpoint
CREATE INDEX "ix_fuel_dist_vehicle" ON "fuel_distributions" USING btree ("vehicle_id","distribution_date");