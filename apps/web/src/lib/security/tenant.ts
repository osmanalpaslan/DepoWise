import { isSuperAdmin, type Session } from "./permissions.ts";

export class ForbiddenError extends Error {}

// company_id YALNIZ session'dan. Payload farklı firma taşırsa (süper admin değilse) reddedilir.
export function resolveCompanyId(session: Session, payloadCompanyId?: string | null): string {
  if (
    payloadCompanyId &&
    payloadCompanyId !== session.companyId &&
    !isSuperAdmin(session)
  ) {
    throw new ForbiddenError("Tenant ihlali: farklı firma erişimi reddedildi.");
  }
  return isSuperAdmin(session) && payloadCompanyId ? payloadCompanyId : session.companyId;
}

export function ensureOwnership(session: Session, recordCompanyId: string): void {
  if (isSuperAdmin(session)) return;
  if (recordCompanyId !== session.companyId) {
    throw new ForbiddenError("Tenant ihlali: kayıt başka firmaya ait.");
  }
}
