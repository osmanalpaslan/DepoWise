CREATE TABLE "stock_count_lines" (
	"id" text PRIMARY KEY NOT NULL,
	"document_id" text NOT NULL,
	"material_id" text NOT NULL,
	"system_qty" numeric NOT NULL,
	"counted_qty" numeric NOT NULL,
	"diff_qty" numeric NOT NULL,
	"reason" text
);
--> statement-breakpoint
CREATE TABLE "stock_documents" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"doc_type" text NOT NULL,
	"doc_no" text NOT NULL,
	"doc_date" bigint NOT NULL,
	"from_branch_id" text,
	"to_branch_id" text,
	"personnel_id" text,
	"vehicle_id" text,
	"note" text,
	"status" text DEFAULT 'active' NOT NULL,
	"group_id" text,
	"created_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
ALTER TABLE "stock_movements" ADD COLUMN "document_id" text;--> statement-breakpoint
ALTER TABLE "stock_movements" ADD COLUMN "branch_from_id" text;--> statement-breakpoint
ALTER TABLE "stock_movements" ADD COLUMN "is_reversed" boolean DEFAULT false NOT NULL;--> statement-breakpoint
ALTER TABLE "stock_movements" ADD COLUMN "reverses_movement_id" text;--> statement-breakpoint
CREATE INDEX "ix_stock_count_lines_doc" ON "stock_count_lines" USING btree ("document_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_stock_documents_no" ON "stock_documents" USING btree ("company_id","doc_type","doc_no");--> statement-breakpoint
CREATE INDEX "ix_stock_documents_company" ON "stock_documents" USING btree ("company_id","doc_type","created_at");