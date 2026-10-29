// ⭐ 2026-10-29 (kullanıcı onayı "evet hesabı oluştur") — canlı duman testi için AYRI hesap: `duman.test`.
// Neden: .env.test.local'daki test kullanıcısı yanlışlıkla kullanıcının KENDİ süper admin hesabıydı (iki adımlı girişi kullanıcının
// telefonunda → test kod üretemiyordu). Bu betik:
//   1) yayın hesabıyla (DEPOWISE_ADMIN_USER/PASS, kullanıcı ortam değişkeni) girip `duman.test` hesabını açar (süper admin rolü — testler
//      firma seçimi ve yönetici ekranları ister), şifre rastgele 32 karakter;
//   2) yeni hesapla girip iki adımlı girişi kurar ve onaylar;
//   3) .env.test.local'da test kullanıcısını yeni hesaba çevirir (eski kullanıcı/şifre satırları SİLİNİR — gerçek hesabın şifresi dosyada
//      kalmasın), gizli anahtar + kurtarma kodlarını ekler.
// Hiçbir şifre/anahtar/kod ekrana yazılmaz. Hesap zaten varsa durur (yeniden çalıştırmak güvenli).
// Kullanım (PowerShell):  $env:DEPOWISE_ADMIN_USER=[Environment]::GetEnvironmentVariable("DEPOWISE_ADMIN_USER","User"); …PASS aynı; node scripts/duman_test_hesabi_ac.mjs
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";
import { totp } from "./lib/yayin_ortak.mjs";

const KULLANICI = "duman.test";
const kok = path.join(path.dirname(fileURLToPath(import.meta.url)), "..");
const envYol = path.join(kok, ".env.test.local");
const metin = fs.readFileSync(envYol, "utf8");
const env = Object.fromEntries(metin.replace(/^﻿/, "").split(/\r?\n/).filter(l => /^[A-Z_]+=/.test(l))
  .map(l => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1).trim()]));
const api = (env.DEPOWISE_TEST_API || "https://depowise-erp.fly.dev").replace(/\/$/, "");
const ADIM_DOSYASI = path.join(os.tmpdir(), "dw_e2e_totp_adim.txt");
/** Henüz kullanılmamış adımla doğrulama kodu (sunucu aynı kodu iki kez kabul etmez; ±1 adım tolerans) — duman yardımcısıyla aynı kural. */
async function siradakiKod(gizli) {
  let son = -1; try { son = parseInt(fs.readFileSync(ADIM_DOSYASI, "utf8"), 10); } catch {}
  const simdi = () => Math.floor(Date.now() / 30000);
  const adim = Math.max(simdi() - 1, son + 1);
  while (adim > simdi() + 1) await new Promise(r => setTimeout(r, 1000));
  fs.writeFileSync(ADIM_DOSYASI, String(adim));
  return totp(gizli, adim * 30000);
}
/** 2026-10-30: yönetici açtığı hesap ilk girişte KENDİ şifresini belirlemek zorunda (web "Yeni Şifre Belirleyin") → uygulamanın kendi
 *  ucuyla (change-initial-password) rastgele yeni şifreye geçilir; yeni şifre .env.test.local'a yazılır. Değer yazdırılmaz. */
async function ilkSifreyiBelirle(sifre, gizli) {
  const g = await istek("/api/auth/login", { username: KULLANICI, password: sifre, totpCode: await siradakiKod(gizli) });
  if (g.durum !== 200 || !g.j.token) { console.error("HATA: ilk şifre için giriş " + g.durum); process.exit(1); }
  if (!g.j.mustChangePassword) return sifre;
  const yeni = rastgeleSifre();
  const d = await istek("/api/auth/change-initial-password", { newPassword: yeni }, g.j.token);
  if (d.durum !== 200) { console.error("HATA: ilk şifre değişimi " + d.durum + " " + (d.j.error || "")); process.exit(1); }
  return yeni;
}
function dosyayaYaz(degerler) {
  const m = fs.readFileSync(envYol, "utf8"); const sat = m.includes("\r\n") ? "\r\n" : "\n";
  const satirlar = m.split(/\r?\n/).map(l => { const k = l.slice(0, l.indexOf("=")); return k in degerler ? k + "=" + degerler[k] : l; });
  fs.writeFileSync(envYol, satirlar.join(sat));
}
if (env.DEPOWISE_TEST_USER === KULLANICI && env.DEPOWISE_TEST_TOTP) {
  const yeni = await ilkSifreyiBelirle(env.DEPOWISE_TEST_PASS, env.DEPOWISE_TEST_TOTP);
  if (yeni !== env.DEPOWISE_TEST_PASS) { dosyayaYaz({ DEPOWISE_TEST_PASS: yeni }); console.log("Tamam: duman.test ilk girişte şifre belirledi; yeni şifre .env.test.local'a yazıldı (yazdırılmadı)."); }
  else console.log("Zaten kurulu (duman.test + anahtar dosyada, şifre belirlenmiş).");
  process.exit(0);
}
const yUser = process.env.DEPOWISE_ADMIN_USER, yPass = process.env.DEPOWISE_ADMIN_PASS;
if (!yUser || !yPass) { console.error("HATA: DEPOWISE_ADMIN_USER/PASS ortam değişkeni yok"); process.exit(1); }

async function istek(yol, govde, jeton) {
  const r = await fetch(api + yol, { method: govde === undefined ? "GET" : "POST",
    headers: { "Content-Type": "application/json", ...(jeton ? { Authorization: "Bearer " + jeton } : {}) },
    body: govde === undefined ? undefined : JSON.stringify(govde) });
  return { durum: r.status, j: await r.json().catch(() => ({})) };
}
function rastgeleSifre() {
  const k = "ABCDEFGHJKLMNPQRSTUVWXYZ", ku = "abcdefghijkmnopqrstuvwxyz", r = "23456789", o = "!@#%*-_+=";
  const hepsi = k + ku + r + o;
  const sec = (s) => s[crypto.randomInt(s.length)];
  const c = [sec(k), sec(ku), sec(r), sec(o)];
  while (c.length < 32) c.push(sec(hepsi));
  for (let i = c.length - 1; i > 0; i--) { const j = crypto.randomInt(i + 1); [c[i], c[j]] = [c[j], c[i]]; }
  return c.join("");
}

const yg = await istek("/api/auth/login", { username: yUser, password: yPass });
if (yg.durum !== 200 || !yg.j.token) { console.error("HATA: yayın hesabı girişi " + yg.durum + (yg.j.mfaRequired ? " (kod istiyor)" : "")); process.exit(1); }

const sifre = rastgeleSifre();
const ac = await istek("/api/users", {
  username: KULLANICI, password: sifre, fullName: "Duman Testi (otomatik)", roleKeys: ["role-super-admin"],
  notes: "YALNIZ otomatik canlı duman testi (tests/web-e2e). Yalnız okur. İki adımlı giriş açık; anahtar .env.test.local + Drive DepoWise_Anahtarlar.",
}, yg.j.token);
if (ac.durum !== 200) { console.error("HATA: hesap açılamadı " + ac.durum + " " + (ac.j.error || "")); process.exit(1); }

const g = await istek("/api/auth/login", { username: KULLANICI, password: sifre });
if (g.durum !== 200 || !g.j.token) { console.error("HATA: yeni hesap girişi " + g.durum); process.exit(1); }
const kur = await istek("/api/auth/mfa/setup", {}, g.j.token);
if (kur.durum !== 200 || !kur.j.secret) { console.error("HATA: iki adımlı kurulum " + kur.durum + " " + (kur.j.error || "")); process.exit(1); }
const adim = Math.floor(Date.now() / 30000);
const onay = await istek("/api/auth/mfa/confirm", { code: totp(kur.j.secret, adim * 30000) }, g.j.token);
if (onay.durum !== 200) { console.error("HATA: iki adımlı onay " + onay.durum + " " + (onay.j.error || "")); process.exit(1); }

// .env.test.local: eski test kullanıcısı satırları (gerçek hesabın şifresi dahil) çıkar, yenisi yazılır.
fs.writeFileSync(ADIM_DOSYASI, String(adim));   // kurulum adımı kullanıldı
const sonSifre = await ilkSifreyiBelirle(sifre, kur.j.secret);
const sat = metin.includes("\r\n") ? "\r\n" : "\n";
const satirlar = metin.split(/\r?\n/).filter(l => !/^(DEPOWISE_TEST_USER|DEPOWISE_TEST_PASS|DEPOWISE_TEST_TOTP|DEPOWISE_TEST_KURTARMA)=/.test(l));
const ekle = [
  `# 2026-10-29: duman testi AYRI hesapla (${KULLANICI}, süper admin rolü, iki adımlı giriş AÇIK). Eski satırlar kullanıcının kendi hesabıydı → silindi.`,
  `DEPOWISE_TEST_USER=${KULLANICI}`, `DEPOWISE_TEST_PASS=${sonSifre}`,
  `DEPOWISE_TEST_TOTP=${kur.j.secret}`, `DEPOWISE_TEST_KURTARMA=${(onay.j.recoveryCodes || []).join(",")}`,
];
const bas = satirlar.findIndex(l => l.startsWith("DEPOWISE_TEST_API="));
satirlar.splice(bas >= 0 ? bas : satirlar.length, 0, ...ekle);
fs.writeFileSync(envYol, satirlar.join(sat));
fs.writeFileSync(path.join(os.tmpdir(), "dw_e2e_totp_adim.txt"), String(adim));   // bu adım kullanıldı (duman yardımcısı okur)
console.log(`Tamam: ${KULLANICI} açıldı, iki adımlı giriş kuruldu; şifre, anahtar ve ${(onay.j.recoveryCodes || []).length} kurtarma kodu .env.test.local'a yazıldı (değerler yazdırılmadı).`);
