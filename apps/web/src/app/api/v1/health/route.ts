import { NextResponse } from "next/server";
import { newCorrelationId, unixNowMs } from "@/lib/contracts";
import { loadConfig } from "@/lib/config";

export const dynamic = "force-dynamic";

// GET /api/v1/health — config fail-closed durumunu ve zaman damgasını döndürür.
export async function GET() {
  const correlationId = newCorrelationId();
  const { ok, config, missing } = loadConfig();

  const body = {
    status: ok ? "ok" : "degraded",
    environment: config.DEPOWISE_ENVIRONMENT,
    timeMs: unixNowMs(),
    correlationId,
    checks: {
      configOk: ok,
      missingSecrets: missing,
    },
  };

  return NextResponse.json(body, {
    status: ok ? 200 : 503,
    headers: { "x-correlation-id": correlationId },
  });
}
