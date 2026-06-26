import { test } from "node:test";
import assert from "node:assert/strict";
import { validateImage, safeFileName, detectImage, MAX_BYTES } from "../src/lib/files/validation.ts";

const jpeg = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3]);
const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1]);
const fake = new Uint8Array([0, 1, 2, 3, 4]);

test("geçerli jpeg/png magic-byte tespiti", () => {
  assert.equal(detectImage(jpeg)?.mime, "image/jpeg");
  assert.equal(detectImage(png)?.mime, "image/png");
  assert.equal(detectImage(fake), null);
});

test("sahte dosya reddedilir", () => {
  assert.equal(validateImage("sahte.jpg", "image/jpeg", fake).ok, false);
});

test("MIME-içerik uyuşmazlığı reddedilir", () => {
  assert.equal(validateImage("x.jpg", "image/jpeg", png).ok, false);
});

test("büyük dosya reddedilir", () => {
  const big = new Uint8Array(MAX_BYTES + 1);
  big[0] = 0xff; big[1] = 0xd8; big[2] = 0xff;
  assert.equal(validateImage("big.jpg", "image/jpeg", big).ok, false);
});

test("geçerli dosya kabul + detected mime", () => {
  const r = validateImage("resim.jpg", "image/jpeg", jpeg);
  assert.equal(r.ok, true);
  assert.equal(r.detectedMime, "image/jpeg");
});

test("güvenli dosya adı path traversal temizler", () => {
  const name = safeFileName("../../etc/passwd", "jpg");
  assert.ok(!name.includes("/"));
  assert.ok(!name.includes(".."));
  assert.ok(name.endsWith(".jpg"));
});
