import { test } from "node:test";
import assert from "node:assert/strict";
import { Ledger, applyDelta, NegativeStockError } from "../src/lib/stock/ledger.ts";

test("giriş artırır, çıkış azaltır", () => {
  const l = new Ledger();
  l.receiveIn("in1", "m1", 10);
  l.issueOut("out1", "m1", 4);
  assert.equal(l.balance("m1"), 6);
});

test("negatif stok engellenir", () => {
  const l = new Ledger();
  l.receiveIn("in1", "m1", 3);
  assert.throws(() => l.issueOut("out1", "m1", 5), NegativeStockError);
  assert.equal(l.balance("m1"), 3);
});

test("aynı operation_id ikinci hareket üretmez (idempotent)", () => {
  const l = new Ledger();
  l.receiveIn("dup", "m1", 10);
  l.receiveIn("dup", "m1", 10);
  assert.equal(l.balance("m1"), 10);
  assert.equal(l.movementCount(), 1);
});

test("transfer toplam stoğu değiştirmez, 2 hareket", () => {
  const l = new Ledger();
  l.receiveIn("in1", "m1", 10);
  l.transfer("trf1", "m1", 4);
  assert.equal(l.balance("m1"), 10);
  assert.equal(l.movementCount(), 3); // 1 giriş + 2 transfer
});

test("iptal ters hareketle bakiyeyi geri alır (fiziksel silme yok)", () => {
  const l = new Ledger();
  l.receiveIn("in1", "m1", 10, "docA");
  l.reverseDocument("docA");
  assert.equal(l.balance("m1"), 0);
  assert.equal(l.movementCount(), 2); // orijinal + ters
});

test("applyDelta negatif guard", () => {
  assert.equal(applyDelta(5, -3), 2);
  assert.throws(() => applyDelta(2, -5), NegativeStockError);
  assert.equal(applyDelta(2, -5, true), -3);
});
