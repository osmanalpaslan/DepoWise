import { test } from "node:test";
import assert from "node:assert/strict";
import {
  shouldAdvance,
  isValidDirectSet,
  setMeter,
  applyTemplate,
  MeterBackwardError,
} from "../src/lib/vehicles/meter.ts";

test("sayaç doğrudan geriye reddedilir", () => {
  assert.equal(isValidDirectSet(1000, 900), false);
  assert.throws(() => setMeter(1000, 900), MeterBackwardError);
  assert.equal(setMeter(1000, 1500), 1500);
});

test("ileri-yön: büyük ilerler, küçük no-op (geçmiş kaydı engellemez)", () => {
  assert.equal(shouldAdvance(1000, 1200), true);
  assert.equal(shouldAdvance(1000, 800), false);
  assert.equal(shouldAdvance(1000, 1000), false);
});

test("şablon boş alanları doldurur, kullanıcı değeri öncelikli", () => {
  const filled = applyTemplate({}, { brandId: "b1", productionYear: 2020, defaultMeterUnit: "hour" });
  assert.equal(filled.brandId, "b1");
  assert.equal(filled.productionYear, 2020);
  assert.equal(filled.meterUnit, "hour");

  const userWins = applyTemplate({ brandId: "b2" }, { brandId: "b1" });
  assert.equal(userWins.brandId, "b2");
});
