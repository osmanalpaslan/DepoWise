import { test } from "node:test";
import assert from "node:assert/strict";
import { securityHeaders } from "../src/lib/security/headers.ts";
import { RateLimiter } from "../src/lib/security/ratelimit.ts";
import { issueCsrfToken, verifyCsrf } from "../src/lib/security/csrf.ts";
import { redact, isSensitiveKey, redactObject } from "../src/lib/security/redact.ts";

test("güvenlik başlıkları mevcut (CSP/nosniff/frame/referrer)", () => {
  const h = securityHeaders(false);
  const keys = h.map((x) => x.key);
  assert.ok(keys.includes("Content-Security-Policy"));
  assert.ok(keys.includes("X-Content-Type-Options"));
  assert.ok(keys.includes("X-Frame-Options"));
  assert.ok(keys.includes("Referrer-Policy"));
  assert.equal(h.find((x) => x.key === "X-Content-Type-Options")?.value, "nosniff");
  // HSTS yalnız üretimde
  assert.ok(!keys.includes("Strict-Transport-Security"));
  assert.ok(securityHeaders(true).some((x) => x.key === "Strict-Transport-Security"));
});

test("rate limit login 5/5dk, pencere sonrası açılır", () => {
  let now = 0;
  const rl = RateLimiter.login(() => now);
  for (let i = 0; i < 5; i++) assert.equal(rl.check("ip").allowed, true);
  assert.equal(rl.check("ip").allowed, false);
  now += 5 * 60_000 + 1000;
  assert.equal(rl.check("ip").allowed, true);
});

test("rate limit anahtar bazlı izole", () => {
  let now = 0;
  const rl = new RateLimiter(2, 60_000, () => now);
  assert.equal(rl.check("a").allowed, true);
  assert.equal(rl.check("a").allowed, true);
  assert.equal(rl.check("a").allowed, false);
  assert.equal(rl.check("b").allowed, true);
});

test("CSRF double-submit doğrulama (fail-closed)", () => {
  const t = issueCsrfToken();
  assert.equal(verifyCsrf(t, t), true);
  assert.equal(verifyCsrf(t, "baska"), false);
  assert.equal(verifyCsrf(null, t), false);
  assert.equal(verifyCsrf(t, undefined), false);
});

test("redaction secret maskeler, PII'siz", () => {
  const red = redact("user=admin password=Gizli token=abc.def authorization=Bearer eyJ");
  assert.ok(!red.includes("Gizli"));
  assert.ok(!red.includes("abc.def"));
  assert.ok(!red.includes("eyJ"));
  assert.ok(red.includes("user=admin"));
  assert.equal(isSensitiveKey("Password"), true);
  assert.equal(isSensitiveKey("username"), false);
  assert.equal(redactObject({ token: "x", name: "Ali" }).token, "***");
});
