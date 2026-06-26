import { canSeeMenu, type Session } from "../security/permissions.ts";
import { APP_MODULES, type ModuleDef } from "./modules.ts";

// Menü kurucu — deny-by-default. .NET MenuBuilder ile aynı sonuç.
export function buildMenu(session: Session): ModuleDef[] {
  return APP_MODULES.filter((m) => canSeeMenu(session, m.key));
}
