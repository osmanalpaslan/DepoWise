import { test } from "node:test";
import assert from "node:assert/strict";
import { parseSemVer, compareSemVer, checkUpdate, isValidChecksum, verifyChecksum, type UpdatePackage } from "../src/lib/update/update.ts";

test("SemVer parse + karşılaştırma", () => {
  assert.equal(parseSemVer("1.0"), null);
  assert.equal(parseSemVer("x.y.z"), null);
  assert.equal(compareSemVer(parseSemVer("1.0.0")!, parseSemVer("1.0.1")!), -1);
  assert.equal(compareSemVer(parseSemVer("1.2.0")!, parseSemVer("1.1.9")!), 1);
  assert.equal(compareSemVer(parseSemVer("2.0.0")!, parseSemVer("2.0.0")!), 0);
});

test("güncelleme kontrolü + min supported + signed uyarısı", () => {
  const signed: UpdatePackage = { version: "1.0.0", checksumSha256: "A".repeat(64), minSupportedVersion: "0.5.0", signed: true };
  const c1 = checkUpdate("0.0.0", signed);
  assert.equal(c1.updateAvailable, true);
  assert.equal(c1.signedWarning, false);

  const unsigned: UpdatePackage = { ...signed, signed: false, minSupportedVersion: "1.0.0" };
  const c2 = checkUpdate("0.0.0", unsigned);
  assert.equal(c2.signedWarning, true);
  assert.equal(c2.belowMinSupported, true);
});

test("checksum biçimi + doğrulama (bozuk paket kurulmaz)", () => {
  assert.equal(isValidChecksum("kisa"), false);
  assert.equal(isValidChecksum("a".repeat(64)), true);
  assert.equal(verifyChecksum("ABCD", "abcd"), true);
  assert.equal(verifyChecksum("ABCD", "ef01"), false);
});

test("yayın yoksa güncelleme yok", () => {
  const r = checkUpdate("1.0.0", null);
  assert.equal(r.updateAvailable, false);
});
