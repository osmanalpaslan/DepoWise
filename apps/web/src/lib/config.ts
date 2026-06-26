import { z } from "zod";

// Merkezi config doğrulaması — fail-closed (analiz §9). Eksik/zayıf sır başlangıçta hata.
// Sırlar yalnız environment'tan okunur; repoya yazılmaz.
const schema = z.object({
  DATABASE_URL: z.string().min(1).optional(),
  SESSION_SECRET: z.string().min(16).optional(),
  APP_BASE_URL: z.string().url().default("http://localhost:3000"),
  DEPOWISE_ENVIRONMENT: z.enum(["Development", "Staging", "Production"]).default("Development"),
});

export type AppConfig = z.infer<typeof schema>;

export interface ConfigResult {
  ok: boolean;
  config: AppConfig;
  missing: string[];
}

export function loadConfig(): ConfigResult {
  const parsed = schema.safeParse(process.env);
  const env = parsed.success ? parsed.data : schema.parse({ ...process.env, DATABASE_URL: undefined });
  const config: AppConfig = parsed.success
    ? parsed.data
    : {
        APP_BASE_URL: env.APP_BASE_URL,
        DEPOWISE_ENVIRONMENT: env.DEPOWISE_ENVIRONMENT,
      };

  const isProd = config.DEPOWISE_ENVIRONMENT === "Production";
  const missing: string[] = [];
  // Üretimde DB ve oturum sırrı ZORUNLU (fail-closed); geliştirmede uyarı niteliğinde.
  if (!config.DATABASE_URL) missing.push("DATABASE_URL");
  if (!config.SESSION_SECRET) missing.push("SESSION_SECRET");

  return { ok: isProd ? missing.length === 0 : true, config, missing };
}
