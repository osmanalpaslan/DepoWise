import { test } from "node:test";
import assert from "node:assert/strict";
import {
  allowedBranchIds,
  ensureBranchAllowed,
  canManageCompany,
  visibleCompanies,
} from "../src/lib/org/scope.ts";
import { ForbiddenError } from "../src/lib/security/tenant.ts";
import { RoleKeys, type Session } from "../src/lib/security/permissions.ts";

const session = (roles: string[] = [], company = "A"): Session => ({
  userId: "u1",
  companyId: company,
  roleKeys: roles,
  permissions: [],
  buttons: [],
});

test("firma yönetimi yalnız süper admin", () => {
  assert.equal(canManageCompany(session()), false);
  assert.equal(canManageCompany(session([RoleKeys.CompanyAdmin])), false);
  assert.equal(canManageCompany(session([RoleKeys.SuperAdmin])), true);
});

test("normal admin başka firmayı göremez, süper admin tümünü", () => {
  const all = ["A", "B", "C"];
  assert.deepEqual(visibleCompanies(session([RoleKeys.CompanyAdmin]), all), ["A"]);
  assert.deepEqual(visibleCompanies(session([RoleKeys.SuperAdmin]), all), all);
});

test("kapsam: açık scope öncelikli, admin → tüm şubeler, diğer → boş", () => {
  const all = ["b1", "b2", "b3"];
  assert.deepEqual([...allowedBranchIds(session([RoleKeys.CompanyAdmin]), [], all)], all);
  assert.equal(allowedBranchIds(session(), [], all).size, 0);
  assert.deepEqual([...allowedBranchIds(session(), ["b1"], all)], ["b1"]);
});

test("şube seçimi kapsam dışına taşamaz", () => {
  const allowed = allowedBranchIds(session(), ["b1"], ["b1", "b2"]);
  ensureBranchAllowed(allowed, "b1"); // ok
  ensureBranchAllowed(allowed, null); // şubesiz ok
  assert.throws(() => ensureBranchAllowed(allowed, "b2"), ForbiddenError);
});
