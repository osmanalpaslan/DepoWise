import { NextResponse } from "next/server";
import { apiError, ErrorCodes, newCorrelationId } from "@/lib/contracts";
import { getServerSession } from "@/lib/security/session";

export const dynamic = "force-dynamic";

// GET /api/v1/me — korumalı uç. Oturum yoksa deny-by-default ile 401 (UI'a güvenmez).
export async function GET() {
  const correlationId = newCorrelationId();
  const session = await getServerSession();
  if (!session) {
    return NextResponse.json(
      apiError(ErrorCodes.Unauthorized, "Oturum gerekli.", correlationId),
      { status: 401, headers: { "x-correlation-id": correlationId } },
    );
  }
  return NextResponse.json(
    {
      userId: session.userId,
      companyId: session.companyId,
      roleKeys: session.roleKeys,
      correlationId,
    },
    { headers: { "x-correlation-id": correlationId } },
  );
}
