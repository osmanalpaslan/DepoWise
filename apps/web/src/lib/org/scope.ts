import { isAdmin, isSuperAdmin, type Session } from "../security/permissions.ts";
import { ForbiddenError } from "../security/tenant.ts";

// Organizasyon kapsamı — .NET ScopeResolver/CompanyService ile aynı karar mantığı (saf).
// DB erişimi web tarafında Faz sonrası bağlanacak; burada kurallar parite için doğrulanır.

// Açık kapsam varsa onu uygula; yoksa admin → tüm firma şubeleri, admin değil → boş.
export function allowedBranchIds(
  session: Session,
  explicitScopes: string[],
  allCompanyBranches: string[],
): Set<string> {
  if (explicitScopes.length > 0) return new Set(explicitScopes);
  if (!isAdmin(session)) return new Set();
  return new Set(allCompanyBranches);
}

export function isBranchAllowed(allowed: Set<string>, branchId: string | null): boolean {
  if (branchId === null) return true; // şubesiz kayıt (firma geneli)
  return allowed.has(branchId);
}

export function ensureBranchAllowed(allowed: Set<string>, branchId: string | null): void {
  if (!isBranchAllowed(allowed, branchId)) {
    throw new ForbiddenError("Şube kapsam dışı: bu şubeye erişiminiz yok.");
  }
}

// Firma yönetimi yalnız Süper Admin.
export const canManageCompany = (session: Session): boolean => isSuperAdmin(session);

// Görünür firmalar: Süper Admin → tümü; diğer → yalnız kendi firması.
export function visibleCompanies(session: Session, allCompanyIds: string[]): string[] {
  return isSuperAdmin(session) ? allCompanyIds : allCompanyIds.filter((c) => c === session.companyId);
}
