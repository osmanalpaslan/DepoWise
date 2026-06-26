import { isSuperAdmin, type Session } from "../security/permissions.ts";
import { resolveCompanyId } from "../security/tenant.ts";

// Rapor kapısı — .NET ReportGate ile aynı. Ağır rapor Sorgula/Filtrele tıklanmadan çalışmaz.
export interface ReportRequest {
  executed: boolean;
  fromDate?: number | null;
  toDate?: number | null;
  companyId?: string | null;
}

export class ReportNotRunError extends Error {}

export function ensureRunnable(req: ReportRequest): void {
  if (!req.executed) throw new ReportNotRunError("Rapor, Sorgula/Filtrele tıklanmadan çalışmaz.");
}

// Firma filtresi yalnız Süper Admin'e görünür.
export const showCompanyFilter = (s: Session): boolean => isSuperAdmin(s);

// Hedef firma fail-closed (diğer adminler kendi firmasına kilitli).
export const resolveReportCompany = (s: Session, requested?: string | null): string =>
  resolveCompanyId(s, requested ?? null);
