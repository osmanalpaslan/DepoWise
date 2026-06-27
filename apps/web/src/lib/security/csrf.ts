import { randomBytes, timingSafeEqual } from "node:crypto";

// CSRF double-submit token: cookie + header aynı değeri taşır; sunucu eşitliği sabit-zamanlı doğrular.
export const CSRF_COOKIE = "depowise_csrf";
export const CSRF_HEADER = "x-csrf-token";

export const issueCsrfToken = (): string => randomBytes(32).toString("hex");

export function verifyCsrf(cookieValue: string | null | undefined, headerValue: string | null | undefined): boolean {
  if (!cookieValue || !headerValue) return false; // fail-closed
  const a = Buffer.from(cookieValue);
  const b = Buffer.from(headerValue);
  if (a.length !== b.length) return false;
  return timingSafeEqual(a, b);
}
