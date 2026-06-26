import { test } from "node:test";
import assert from "node:assert/strict";
import { ensureRunnable, showCompanyFilter, ReportNotRunError } from "../src/lib/reports/gate.ts";
import { dryRun, validateRow, SAMPLE_HEADERS, type ImportRow } from "../src/lib/reports/import.ts";
import { RoleKeys, type Session } from "../src/lib/security/permissions.ts";

const session = (roles: string[] = []): Session => ({
  userId: "u1", companyId: "A", roleKeys: roles, permissions: [], buttons: [],
});

test("rapor filtre tıklanmadan çalışmaz", () => {
  assert.throws(() => ensureRunnable({ executed: false }), ReportNotRunError);
  ensureRunnable({ executed: true }); // ok
});

test("firma filtresi yalnız süper admin", () => {
  assert.equal(showCompanyFilter(session([RoleKeys.SuperAdmin])), true);
  assert.equal(showCompanyFilter(session([RoleKeys.CompanyAdmin])), false);
});

test("import örnek başlık", () => {
  assert.ok(SAMPLE_HEADERS.includes("Kod"));
  assert.ok(SAMPLE_HEADERS.includes("Birim Fiyat"));
});

test("import satır doğrulama", () => {
  assert.equal(validateRow({ rowNumber: 1, values: { Kod: "M-1", Ad: "İyi", "Birim Fiyat": "12.5" } }), null);
  assert.match(validateRow({ rowNumber: 2, values: { Kod: "", Ad: "x" } })!, /Kod/);
  assert.match(validateRow({ rowNumber: 3, values: { Kod: "M", Ad: "x", "Birim Fiyat": "abc" } })!, /sayısal/);
  assert.match(validateRow({ rowNumber: 4, values: { Kod: "M", Ad: "x", "Para Birimi": "GBP" } })!, /para birimi/i);
});

test("dry-run satır bazlı hata, yazmaz", () => {
  const rows: ImportRow[] = [
    { rowNumber: 1, values: { Kod: "M-1", Ad: "Bir" } },
    { rowNumber: 2, values: { Kod: "", Ad: "Kodsuz" } },
  ];
  const res = dryRun(rows);
  assert.equal(res.dryRun, true);
  assert.equal(res.total, 2);
  assert.equal(res.valid, 1);
  assert.equal(res.failed, 1);
  assert.equal(res.errors.length, 1);
});
