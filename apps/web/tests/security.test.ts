import { test } from "node:test";
import assert from "node:assert/strict";
import { hashPassword, verifyPassword } from "../src/lib/security/password.ts";
import {
  can,
  canSeeMenu,
  canUseButton,
  RoleKeys,
  type Session,
} from "../src/lib/security/permissions.ts";
import { resolveCompanyId, ForbiddenError } from "../src/lib/security/tenant.ts";

const session = (over: Partial<Session> = {}): Session => ({
  userId: "u1",
  companyId: "A",
  roleKeys: [],
  permissions: [],
  buttons: [],
  ...over,
});

test("parola hash doğrulanır, yanlış reddedilir (.NET ile aynı biçim)", async () => {
  const h = await hashPassword("S3cret!");
  assert.match(h, /^pbkdf2\$sha256\$/);
  assert.equal(await verifyPassword("S3cret!", h), true);
  assert.equal(await verifyPassword("yanlis", h), false);
});

test("deny-by-default: yetki yoksa erişim yok", () => {
  const s = session();
  assert.equal(can(s, "materials", "view"), false);
  assert.equal(canSeeMenu(s, "materials"), false);
  assert.equal(canUseButton(s, "btn-approve"), false);
});

test("sadece view verilince menü görünür, yazma reddedilir", () => {
  const s = session({
    permissions: [{ moduleKey: "materials", canView: true, canCreate: false, canEdit: false, canDelete: false }],
  });
  assert.equal(canSeeMenu(s, "materials"), true);
  assert.equal(can(s, "materials", "create"), false);
});

test("admin tam yetkili", () => {
  const s = session({ roleKeys: [RoleKeys.CompanyAdmin] });
  assert.equal(can(s, "materials", "delete"), true);
  assert.equal(canUseButton(s, "btn-reset-db"), true);
});

test("payload farklı firma reddedilir, süper admin seçebilir", () => {
  assert.throws(() => resolveCompanyId(session(), "B"), ForbiddenError);
  assert.equal(resolveCompanyId(session(), "A"), "A");
  assert.equal(resolveCompanyId(session({ roleKeys: [RoleKeys.SuperAdmin] }), "B"), "B");
});
