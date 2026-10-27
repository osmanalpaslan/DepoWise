// ⭐ M-01 (2026-10-28) — canlı duman testi hesabında (DEPOWISE_TEST_USER, süper admin yetkili TEST hesabı) iki adımlı girişi açar.
// Web süper admini kurulum ekranına zorladığı için duman testi bu olmadan ilerleyemez. Gizli anahtar ve kurtarma kodları
// YALNIZ repo kökündeki .env.test.local'a yazılır (git'e girmez); hiçbiri ekrana yazdırılmaz. Gerçek yönetici hesabına DOKUNMAZ.
// Zaten açıksa ve anahtar dosyadaysa hiçbir şey yapmaz.
// Kullanım:  node scripts/e2e_test_hesabi_mfa.mjs
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { totp } from "./lib/yayin_ortak.mjs";

const kok = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const envYol = path.join(kok, ".env.test.local");
const metin = fs.readFileSync(envYol, "utf8");
const env = Object.fromEntries(metin.replace(/^﻿/, "").split(/\r?\n/).filter(l => /^[A-Z_]+=/.test(l))
  .map(l => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1).trim()]));
const api = (env.DEPOWISE_TEST_API || "https://depowise-erp.fly.dev").replace(/\/$/, "");
if (!env.DEPOWISE_TEST_USER || !env.DEPOWISE_TEST_PASS) { console.error("HATA: .env.test.local içinde test kullanıcısı yok"); process.exit(1); }

async function istek(yol, govde, jeton) {
  const r = await fetch(api + yol, { method: govde === undefined ? "GET" : "POST",
    headers: { "Content-Type": "application/json", ...(jeton ? { Authorization: "Bearer " + jeton } : {}) },
    body: govde === undefined ? undefined : JSON.stringify(govde) });
  return { durum: r.status, j: await r.json().catch(() => ({})) };
}

const giris = await istek("/api/auth/login", { username: env.DEPOWISE_TEST_USER, password: env.DEPOWISE_TEST_PASS });
if (giris.durum !== 200) {
  console.log(giris.j.mfaRequired ? (env.DEPOWISE_TEST_TOTP ? "Zaten açık (anahtar dosyada)." : "HATA: hesapta iki adımlı giriş açık ama anahtar dosyada yok.") : "HATA: giriş " + giris.durum);
  process.exit(giris.j.mfaRequired && env.DEPOWISE_TEST_TOTP ? 0 : 1);
}
const jeton = giris.j.token;
const durum = await istek("/api/auth/mfa/status", undefined, jeton);
if (!durum.j.canSetup) { console.error("HATA: bu hesap süper admin değil, kurulum gerekmez."); process.exit(1); }
if (durum.j.enabled) { console.error("HATA: açık görünüyor ama giriş kod istemedi — inceleyin."); process.exit(1); }

const kur = await istek("/api/auth/mfa/setup", {}, jeton);
if (kur.durum !== 200 || !kur.j.secret) { console.error("HATA: kurulum " + kur.durum + " " + (kur.j.error || "")); process.exit(1); }
const adim = Math.floor(Date.now() / 30000);
const onay = await istek("/api/auth/mfa/confirm", { code: totp(kur.j.secret, adim * 30000) }, jeton);
if (onay.durum !== 200) { console.error("HATA: onay " + onay.durum + " " + (onay.j.error || "")); process.exit(1); }

const sat = metin.includes("\r\n") ? "\r\n" : "\n";
fs.appendFileSync(envYol, `${sat}# M-01 (2026-10-28): duman testi hesabının iki adımlı giriş anahtarı + kurtarma kodları (yalnız bu bilgisayar)${sat}`
  + `DEPOWISE_TEST_TOTP=${kur.j.secret}${sat}DEPOWISE_TEST_KURTARMA=${(onay.j.recoveryCodes || []).join(",")}${sat}`);
fs.writeFileSync(path.join(os.tmpdir(), "dw_e2e_totp_adim.txt"), String(adim));   // bu adım kullanıldı (duman testi yardımcısı okur)
console.log(`Tamam: iki adımlı giriş açıldı, anahtar ve ${(onay.j.recoveryCodes || []).length} kurtarma kodu .env.test.local'a yazıldı (değerler yazdırılmadı).`);
