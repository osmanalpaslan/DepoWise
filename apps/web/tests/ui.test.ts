import { test } from "node:test";
import assert from "node:assert/strict";
import { buildMenu } from "../src/lib/ui/menu.ts";
import { validateDate, validateNumeric, parseDate } from "../src/lib/ui/validation.ts";
import { MultiSelectState } from "../src/lib/ui/multiselect.ts";
import { canShowAddButton, isFieldVisible, type FieldDefinition } from "../src/lib/ui/fields.ts";
import { RoleKeys, type Session, type ModulePermission } from "../src/lib/security/permissions.ts";
import { defaultTheme, themeToCssVars } from "../src/lib/theme/tokens.ts";

const session = (perms: ModulePermission[] = [], roles: string[] = []): Session => ({
  userId: "u1",
  companyId: "A",
  roleKeys: roles,
  permissions: perms,
  buttons: [],
});

test("menü deny-by-default: yetkisiz gizli, dashboard her zaman", () => {
  const menu = buildMenu(session());
  assert.ok(menu.some((m) => m.key === "dashboard"));
  assert.ok(!menu.some((m) => m.key === "materials"));
});

test("menü: yetkili modül görünür, admin tümünü görür", () => {
  const withPerm = buildMenu(session([{ moduleKey: "materials", canView: true, canCreate: false, canEdit: false, canDelete: false }]));
  assert.ok(withPerm.some((m) => m.key === "materials"));
  const admin = buildMenu(session([], [RoleKeys.CompanyAdmin]));
  assert.ok(admin.some((m) => m.key === "users"));
});

test("tarih gerçek takvim", () => {
  assert.equal(validateDate("15/06/2026").ok, true);
  assert.equal(validateDate("29/02/2024").ok, true);
  assert.equal(validateDate("29/02/2025").ok, false);
  assert.equal(validateDate("31/02/2026").ok, false);
  assert.equal(validateDate("13/13/2026").ok, false);
  assert.equal(validateDate("1/1/2026").ok, false);
  assert.equal(parseDate("2026-06-15"), null);
});

test("numerik negatif/sınır", () => {
  assert.equal(validateNumeric(-1).ok, false);
  assert.equal(validateNumeric(-1, { allowNegative: true }).ok, true);
  assert.equal(validateNumeric(5, { min: 10 }).ok, false);
  assert.equal(validateNumeric(150, { max: 100 }).ok, false);
  assert.equal(validateNumeric(50, { min: 10, max: 100 }).ok, true);
  assert.equal(validateNumeric(null).ok, false);
});

test("çoklu seçim: arama seçimi korur, tümünü seç yalnız filtreyi ekler (Türkçe duyarsız)", () => {
  const ms = new MultiSelectState(["Ankara", "İstanbul", "İzmir", "Bursa"], (x) => x);
  ms.toggle("Bursa", true);
  ms.search("iz");
  assert.ok(!ms.filtered().includes("Bursa"));
  assert.equal(ms.isSelected("Bursa"), true);

  ms.search("i");
  ms.selectAllFiltered();
  assert.equal(ms.isSelected("İstanbul"), true);
  assert.equal(ms.isSelected("İzmir"), true);
  assert.equal(ms.isSelected("Ankara"), false);
});

test('alan "+" butonu yetki yoksa gizli', () => {
  const f: FieldDefinition = { key: "brand", label: "Marka", type: "lookup", moduleKey: "materials", isLookup: true, allowAdd: true };
  const reader = session([{ moduleKey: "materials", canView: true, canCreate: false, canEdit: false, canDelete: false }]);
  const writer = session([{ moduleKey: "materials", canView: true, canCreate: true, canEdit: false, canDelete: false }]);
  assert.equal(isFieldVisible(reader, f), true);
  assert.equal(canShowAddButton(reader, f), false);
  assert.equal(canShowAddButton(writer, f), true);
});

test("tema token → CSS değişkenleri (renk sabit değil)", () => {
  const vars = themeToCssVars(defaultTheme);
  assert.equal(vars["--brand-primary"], defaultTheme.primary);
  assert.equal(vars["--brand-radius"], "8px");
});
