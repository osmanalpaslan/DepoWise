import { test } from "node:test";
import assert from "node:assert/strict";
import { alertLevel, progress, nextDue, consumedFor } from "../src/lib/maintenance/alerts.ts";

test("eşik yüzdeleri (%85/95/100)", () => {
  assert.equal(alertLevel(progress(80, 100)), "normal");
  assert.equal(alertLevel(progress(85, 100)), "approaching");
  assert.equal(alertLevel(progress(94, 100)), "approaching");
  assert.equal(alertLevel(progress(95, 100)), "critical");
  assert.equal(alertLevel(progress(99, 100)), "critical");
  assert.equal(alertLevel(progress(100, 100)), "overdue");
  assert.equal(alertLevel(progress(150, 100)), "overdue");
});

test("interval 0 → progress 0 (uyarı yok)", () => {
  assert.equal(progress(50, 0), 0);
});

test("sonraki hedef km/gün", () => {
  assert.equal(nextDue("km", 1000, 5000), 6000);
  const day = 24 * 60 * 60 * 1000;
  assert.equal(nextDue("day", 0, 30), 30 * day);
});

test("tüketilen hesaplama (yeni bakım uyarıyı temizler mantığı)", () => {
  // performed 1000, current 1098, interval 100 → %98 kritik
  assert.equal(alertLevel(progress(consumedFor("km", 1000, 1098), 100)), "critical");
  // yeni bakım performed 1098, current 1098 → tüketilen 0 → normal
  assert.equal(alertLevel(progress(consumedFor("km", 1098, 1098), 100)), "normal");
});
