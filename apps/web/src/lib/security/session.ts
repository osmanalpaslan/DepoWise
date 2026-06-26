import { cookies } from "next/headers";
import type { Session } from "./permissions.ts";

export const SESSION_COOKIE = "depowise_session";

// Sunucu tarafı oturum çözümü. Faz 03 iskeleti: imzalı cookie + kullanıcı deposu
// Faz 05+ ile bağlanacak. Şimdilik geçerli oturum yoksa null (fail-closed).
export async function getServerSession(): Promise<Session | null> {
  const store = await cookies();
  const raw = store.get(SESSION_COOKIE)?.value;
  if (!raw) return null;
  // Not: imza doğrulama + DB session lookup Faz 05'te eklenecek. İmzasız değer kabul edilmez.
  return null;
}

export class UnauthorizedError extends Error {}

export async function requireSession(): Promise<Session> {
  const s = await getServerSession();
  if (!s) throw new UnauthorizedError("Oturum gerekli.");
  return s;
}
