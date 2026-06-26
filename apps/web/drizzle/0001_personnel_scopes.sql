CREATE TABLE "personnel" (
	"id" text PRIMARY KEY NOT NULL,
	"company_id" text NOT NULL,
	"branch_id" text,
	"full_name" text NOT NULL,
	"title" text,
	"phone" text,
	"is_active" boolean DEFAULT true NOT NULL,
	"created_at" bigint NOT NULL,
	"updated_at" bigint NOT NULL,
	"version" integer DEFAULT 1 NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "user_scopes" (
	"user_id" text NOT NULL,
	"company_id" text NOT NULL,
	"branch_id" text NOT NULL
);
--> statement-breakpoint
CREATE INDEX "ix_personnel_company" ON "personnel" USING btree ("company_id","is_deleted");--> statement-breakpoint
CREATE INDEX "ix_personnel_branch" ON "personnel" USING btree ("branch_id");--> statement-breakpoint
CREATE UNIQUE INDEX "ux_user_scopes" ON "user_scopes" USING btree ("user_id","branch_id");