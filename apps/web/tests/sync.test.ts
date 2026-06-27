import { test } from "node:test";
import assert from "node:assert/strict";
import { classifyPush, isCritical, pullPage, type PushContext, type ServerChange } from "../src/lib/sync/sync.ts";

const ctx = (over: Partial<PushContext> = {}): PushContext => ({
  inboxHas: () => false,
  currentVersion: () => null,
  ...over,
});

test("kritik entity tespiti", () => {
  assert.equal(isCritical("stock_movement"), true);
  assert.equal(isCritical("vehicle_maintenance"), true);
  assert.equal(isCritical("material"), false);
});

test("retry: inbox'ta varsa already_applied", () => {
  const r = classifyPush({ operationId: "op1", entityType: "material", entityId: "m1" }, ctx({ inboxHas: () => true }));
  assert.equal(r.result, "already_applied");
});

test("kritik: doğrulayıcı yoksa reddedilir (LWW yok)", () => {
  const r = classifyPush({ operationId: "o", entityType: "stock_movement", entityId: "s1" }, ctx());
  assert.equal(r.result, "rejected");
});

test("kritik: sunucu doğrulaması red/kabul", () => {
  const validate = (op: { entityId: string }) => (op.entityId === "bad" ? { ok: false, reason: "Negatif" } : { ok: true });
  assert.equal(classifyPush({ operationId: "o1", entityType: "stock_movement", entityId: "bad" }, ctx({ validateCritical: validate })).result, "rejected");
  assert.equal(classifyPush({ operationId: "o2", entityType: "stock_movement", entityId: "ok" }, ctx({ validateCritical: validate })).result, "accepted");
});

test("düşük-riskli: version uyuşmazlığı conflict (kör LWW yok)", () => {
  const r = classifyPush(
    { operationId: "o", entityType: "material", entityId: "m1", baseVersion: 99 },
    ctx({ currentVersion: () => 1 }),
  );
  assert.equal(r.result, "conflict");
});

test("pull: bozuk sayfa rollback, cursor ilerlemez", () => {
  const good: ServerChange[] = [
    { seq: 1, valid: true, entityType: "material", entityId: "m1" },
    { seq: 2, valid: true, entityType: "material", entityId: "m2" },
  ];
  assert.equal(pullPage(good, 0).nextCursor, 2);

  const broken: ServerChange[] = [{ seq: 3, valid: false, entityType: "material", entityId: "mx" }];
  assert.throws(() => pullPage(broken, 2));
});
