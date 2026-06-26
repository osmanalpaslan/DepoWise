import { test } from "node:test";
import assert from "node:assert/strict";
import { equivalentGroup, assertNotSelf, type EquivalentEdge } from "../src/lib/materials/equivalents.ts";
import { isSupportedCurrency, parseAmount } from "../src/lib/materials/money.ts";

// Simetrik kenarlar (servis çift yön yazar)
const sym = (a: string, b: string): EquivalentEdge[] => [
  { materialId: a, equivalentId: b },
  { materialId: b, equivalentId: a },
];

test("muadil grup çift yönlü", () => {
  const edges = sym("m1", "m2");
  assert.deepEqual([...equivalentGroup("m1", edges)], ["m2"]);
  assert.deepEqual([...equivalentGroup("m2", edges)], ["m1"]);
});

test("muadil döngü güvenli (1-2-3-1) sonlanır", () => {
  const edges = [...sym("m1", "m2"), ...sym("m2", "m3"), ...sym("m3", "m1")];
  const g = equivalentGroup("m1", edges);
  assert.equal(g.size, 2);
  assert.ok(g.has("m2") && g.has("m3"));
});

test("muadil kendine reddedilir", () => {
  assert.throws(() => assertNotSelf("m1", "m1"));
});

test("para birimi: TRY/USD/EUR desteklenir, diğeri değil", () => {
  assert.equal(isSupportedCurrency("TRY"), true);
  assert.equal(isSupportedCurrency("USD"), true);
  assert.equal(isSupportedCurrency("GBP"), false);
  assert.equal(parseAmount("12.34"), 12.34);
  assert.equal(parseAmount(null), 0);
});
