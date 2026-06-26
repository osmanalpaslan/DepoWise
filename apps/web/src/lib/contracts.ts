// Ortak sözleşmeler — .NET DepoWise.Application.Common ile fonksiyonel eşit.
// Web ve masaüstü aynı hata kodlarını ve sayfalama biçimini üretir.

export const ErrorCodes = {
  Validation: "validation_error",
  Unauthorized: "unauthorized",
  Forbidden: "forbidden",
  NotFound: "not_found",
  Conflict: "conflict",
  TenantViolation: "tenant_violation",
  IdempotencyReplay: "idempotency_replay",
  Internal: "internal_error",
} as const;

export type ErrorCode = (typeof ErrorCodes)[keyof typeof ErrorCodes];

export interface ApiError {
  code: ErrorCode;
  message: string;
  correlationId: string;
  fields?: Record<string, string[]>;
}

export function apiError(
  code: ErrorCode,
  message: string,
  correlationId: string,
  fields?: Record<string, string[]>,
): ApiError {
  return fields ? { code, message, correlationId, fields } : { code, message, correlationId };
}

// Keyset (cursor) pagination sözleşmesi.
export const MAX_PAGE_LIMIT = 200;

export interface PageRequest {
  limit: number;
  cursor?: string;
}

export function normalizedLimit(limit: number): number {
  if (!Number.isFinite(limit) || limit < 1) return 1;
  return limit > MAX_PAGE_LIMIT ? MAX_PAGE_LIMIT : Math.floor(limit);
}

export interface PagedResult<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

export function pagedResult<T>(items: T[], nextCursor: string | null): PagedResult<T> {
  return { items, nextCursor, hasMore: nextCursor !== null };
}

// Zaman: dış sözleşmede Unix ms (merkezde UTC).
export const unixNowMs = (): number => Date.now();

// İstek ilişkilendirme kimliği.
export const newCorrelationId = (): string =>
  globalThis.crypto?.randomUUID().replace(/-/g, "") ??
  Math.random().toString(16).slice(2);
