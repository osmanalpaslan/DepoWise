// Yetki modeli — .NET DepoWise.Application.Security ile fonksiyonel eşit. Deny-by-default.

export const RoleKeys = {
  SuperAdmin: "role-super-admin",
  CompanyAdmin: "role-company-admin",
  Manager: "role-manager",
  Warehouse: "role-warehouse",
  Operation: "role-operation",
  ReadOnly: "role-readonly",
} as const;

export type PermissionAction = "view" | "create" | "edit" | "delete";

export interface ModulePermission {
  moduleKey: string;
  canView: boolean;
  canCreate: boolean;
  canEdit: boolean;
  canDelete: boolean;
}

// Herkese açık (yetkiden muaf, yalnız okuma) modüller.
const PUBLIC_MODULES = new Set(["dashboard", "about"]);
export const isPublicModule = (key: string): boolean => PUBLIC_MODULES.has(key);

export interface Session {
  userId: string;
  companyId: string;
  roleKeys: string[];
  permissions: ModulePermission[];
  buttons: string[];
}

export const isSuperAdmin = (s: Session): boolean => s.roleKeys.includes(RoleKeys.SuperAdmin);
export const isCompanyAdmin = (s: Session): boolean => s.roleKeys.includes(RoleKeys.CompanyAdmin);
export const isAdmin = (s: Session): boolean => isSuperAdmin(s) || isCompanyAdmin(s);

export function can(s: Session, moduleKey: string, action: PermissionAction): boolean {
  if (isPublicModule(moduleKey)) return action === "view";
  if (isAdmin(s)) return true;
  const p = s.permissions.find((x) => x.moduleKey === moduleKey);
  if (!p) return false; // deny-by-default
  switch (action) {
    case "view":
      return p.canView;
    case "create":
      return p.canCreate;
    case "edit":
      return p.canEdit;
    case "delete":
      return p.canDelete;
  }
}

export const canSeeMenu = (s: Session, moduleKey: string): boolean => can(s, moduleKey, "view");

export const canUseButton = (s: Session, buttonKey: string): boolean =>
  isAdmin(s) || s.buttons.includes(buttonKey);
