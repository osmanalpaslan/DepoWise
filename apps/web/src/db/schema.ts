// Merkezi PostgreSQL şeması (Drizzle). Faz 01 iskeleti: yalnız health probe tablosu.
// Gerçek tenant tabloları Faz 02'de eklenecek.
import { pgTable, bigserial, bigint } from "drizzle-orm/pg-core";

export const healthCheck = pgTable("_health_check", {
  id: bigserial("id", { mode: "number" }).primaryKey(),
  ts: bigint("ts", { mode: "number" }).notNull(),
});
