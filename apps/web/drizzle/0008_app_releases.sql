CREATE TABLE "app_releases" (
	"id" text PRIMARY KEY NOT NULL,
	"version" text NOT NULL,
	"checksum_sha256" text NOT NULL,
	"size_bytes" bigint DEFAULT 0 NOT NULL,
	"min_supported_version" text DEFAULT '0.0.0' NOT NULL,
	"release_notes" text,
	"signed" boolean DEFAULT false NOT NULL,
	"published_at" bigint NOT NULL,
	"created_at" bigint NOT NULL,
	"is_deleted" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE UNIQUE INDEX "ux_app_releases_version" ON "app_releases" USING btree ("version");