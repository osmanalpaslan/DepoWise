// Log redaction — .NET LogRedactor ile aynı. Ham secret/PII loglanmaz.
const KEY_VALUE =
  /(password|passwd|pwd|token|secret|authorization|auth|api[_-]?key|connection[_-]?string|conn[_-]?str|session|cookie)("?\s*[:=]\s*"?)([^"'\s,;}]+)/gi;
const BEARER = /bearer\s+[A-Za-z0-9\-._~+/]+=*/gi;
const SENSITIVE = /^(password|passwd|pwd|token|secret|authorization|auth|api[_-]?key|connection[_-]?string|conn[_-]?str|session|cookie)$/i;
const MASK = "***";

export function redact(input: string | null | undefined): string {
  if (!input) return input ?? "";
  let s = input.replace(BEARER, `Bearer ${MASK}`);
  s = s.replace(KEY_VALUE, (_m, k, sep) => `${k}${sep}${MASK}`);
  return s;
}

export const isSensitiveKey = (key: string): boolean => SENSITIVE.test(key);

// Obje alan bazlı redaction (loglanmadan önce).
export function redactObject(obj: Record<string, unknown>): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(obj)) {
    out[k] = isSensitiveKey(k) ? MASK : typeof v === "string" ? redact(v) : v;
  }
  return out;
}
