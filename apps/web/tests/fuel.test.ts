import { test } from "node:test";
import assert from "node:assert/strict";
import { depotBalance, currentFuelPrice, distributionCost, consumptionPer100Km } from "../src/lib/fuel/fuel.ts";

test("depo bakiyesi = girişler - dağıtımlar", () => {
  const balance = depotBalance(
    [{ liters: 100, unitPrice: 40 }, { liters: 100, unitPrice: 55 }],
    [{ liters: 30, unitPrice: 40 }],
  );
  assert.equal(balance, 170);
});

test("güncel fiyat = en son depo girişi", () => {
  assert.equal(currentFuelPrice([{ liters: 100, unitPrice: 40 }, { liters: 50, unitPrice: 55 }]), 55);
  assert.equal(currentFuelPrice([]), 0);
});

test("dağıtım maliyeti snapshot fiyatla (geçmiş değişmez)", () => {
  assert.equal(distributionCost({ liters: 10, unitPrice: 40 }), 400);
});

test("tüketim L/100km güvenli (km<=0 → null veri kalitesi uyarısı)", () => {
  assert.equal(consumptionPer100Km(20, 200), 10);
  assert.equal(consumptionPer100Km(20, 0), null);
  assert.equal(consumptionPer100Km(20, -5), null);
});
