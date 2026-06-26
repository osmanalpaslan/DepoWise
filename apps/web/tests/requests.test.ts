import { test } from "node:test";
import assert from "node:assert/strict";
import { canTransition, isTerminal, nextDocNo } from "../src/lib/requests/status.ts";

test("durum geçişleri (çift onay/yetkisiz geçiş engellenir)", () => {
  assert.equal(canTransition("pending", "approved"), true);
  assert.equal(canTransition("pending", "rejected"), true);
  assert.equal(canTransition("approved", "approved"), false); // çift onay
  assert.equal(canTransition("approved", "rejected"), false);
  assert.equal(canTransition("draft", "approved"), false); // önce pending
  assert.equal(canTransition("rejected", "approved"), false);
});

test("terminal durumlar", () => {
  assert.equal(isTerminal("approved"), true);
  assert.equal(isTerminal("rejected"), true);
  assert.equal(isTerminal("cancelled"), true);
  assert.equal(isTerminal("pending"), false);
});

test("belge no TLP-YYYY-NNNN artar", () => {
  assert.equal(nextDocNo(2026, []), "TLP-2026-0001");
  assert.equal(nextDocNo(2026, ["TLP-2026-0001", "TLP-2026-0007"]), "TLP-2026-0008");
  assert.equal(nextDocNo(2026, ["TLP-2025-0099"]), "TLP-2026-0001"); // farklı yıl
});
