// AlpnexSetup.exe kurulum aracini sunucuya yukler (Super Admin login + POST /api/setup).
// Kullanim: node scripts/publish_setup.mjs <exeYolu>
// Ortam: DEPOWISE_ADMIN_USER / DEPOWISE_ADMIN_PASS  (istege bagli DEPOWISE_API; iki adimli giris / imza: scripts/lib/yayin_ortak.mjs)
// ⭐ M-01 (2026-10-27): sunucu kurulum aracini yalniz IMZALI kabul eder (imza: "kurulum-araci" etiketi + dosya ozeti).
import fs from "node:fs";
import crypto from "node:crypto";
import { superAdminGiris, imzala, KURULUM_ARACI_ETIKETI } from "./lib/yayin_ortak.mjs";

const API = process.env.DEPOWISE_API || "https://depowise-erp.fly.dev";
const [exePath] = process.argv.slice(2);
if (!exePath || !fs.existsSync(exePath)) { console.error("HATA: exe bulunamadi: " + exePath); process.exit(1); }

let login;
try { login = await superAdminGiris(API); } catch (e) { console.error("HATA: " + e.message); process.exit(1); }
console.log(`Giris OK (superAdmin, firma=${login.companyName}). AlpnexSetup.exe yukleniyor...`);

const buf = fs.readFileSync(exePath);
const ozet = crypto.createHash("sha256").update(buf).digest("hex");
const form = new FormData();
form.append("signature", imzala(KURULUM_ARACI_ETIKETI, ozet));
form.append("file", new Blob([buf], { type: "application/octet-stream" }), "AlpnexSetup.exe");

const up = await fetch(`${API}/api/setup`, { method: "POST", headers: { Authorization: `Bearer ${login.token}` }, body: form });
if (!up.ok) { console.error(`HATA: yukleme ${up.status} - ${await up.text()}`); process.exit(1); }
console.log(`YUKLENDI: ${(buf.length/1048576).toFixed(1)} MB → sunucu /api/setup/download`);

// Dogrulama: indirme ucu 200 mu
const dl = await fetch(`${API}/api/setup/download`, { method: "GET" });
console.log(`DOGRULAMA: /api/setup/download HTTP ${dl.status}`);
