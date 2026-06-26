CREATE TABLE "material_request_items" (
	"id" text PRIMARY KEY NOT NULL,
	"request_id" text NOT NULL,
	"material_id" text NOT NULL,
	"quantity" numeric NOT NULL,
	"vehicle_id" text,
	"note" text
);
--> statement-breakpoint
CREATE TABLE "material_requests" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"doc_no" text NOT NULL,
	"request_date" bigint NOT NULL,
	"branch_id" text,
	"requester_id" text,
	"warehouse_id" text,
	"approver_id" text,
	"description" text,
	"status" text DEFAULT 'draft' NOT NULL,
	"approved_by" text,
	"approved_at" bigint,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "request_status_history" (
	"id" text PRIMARY KEY NOT NULL,
	"request_id" text NOT NULL,
	"from_status" text,
	"to_status" text NOT NULL,
	"by_user" text,
	"reason" text,
	"created_at" bigint NOT NULL
);
--> statement-breakpoint
CREATE INDEX "ix_material_request_items" ON "material_request_items" USING btree ("request_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_material_requests_no" ON "material_requests" USING btree ("company_id","doc_no");--> statement-breakpoint
CREATE INDEX "ix_material_requests" ON "material_requests" USING btree ("company_id","status","created_at");--> statement-breakpoint
CREATE INDEX "ix_request_status_history" ON "request_status_history" USING btree ("request_id","created_at");