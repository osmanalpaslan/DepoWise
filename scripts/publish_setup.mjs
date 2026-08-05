// AlpnexSetup.exe kurulum aracini sunucuya yukler (Super Admin login + POST /api/setup).
// Kullanim: node scripts/publish_setup.mjs <exeYolu>
// Ortam: DEPOWISE_ADMIN_USER / DEPOWISE_ADMIN_PASS  (istege bagli DEPOWISE_API)
import fs from "node:fs";

const API = process.env.DEPOWISE_API || "https://depowise-erp.fly.dev";
const user = process.env.DEPOWISE_ADMIN_USER;
const pass = process.env.DEPOWISE_ADMIN_PASS;
const [exePath] = process.argv.slice(2);

if (!user || !pass) { console.error("HATA: DEPOWISE_ADMIN_USER / DEPOWISE_ADMIN_PASS yok."); process.exit(1); }
if (!exePath || !fs.existsSync(exePath)) { console.error("HATA: exe bulunamadi: " + exePath); process.exit(1); }

const loginResp = await fetch(`${API}/api/auth/login`, {
  method: "POST", headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ username: user, password: pass }),
});
if (!loginResp.ok) { console.error(`HATA: login ${loginResp.status} - ${await loginResp.text()}`); process.exit(1); }
const login = await loginResp.json();
if (!login.token) { console.error("HATA: token yok: " + JSON.stringify(login)); process.exit(1); }
if (!login.isSuperAdmin) { console.error("HATA: super admin degil; setup yukleme yetkisi yok."); process.exit(1); }
console.log(`Giris OK (superAdmin, firma=${login.companyName}). AlpnexSetup.exe yukleniyor...`);

const buf = fs.readFileSync(exePath);
const form = new FormData();
form.append("file", new Blob([buf], { type: "application/octet-stream" }), "AlpnexSetup.exe");

const up = await fetch(`${API}/api/setup`, { method: "POST", headers: { Authorization: `Bearer ${login.token}` }, body: form });
if (!up.ok) { console.error(`HATA: yukleme ${up.status} - ${await up.text()}`); process.exit(1); }
console.log(`YUKLENDI: ${(buf.length/1048576).toFixed(1)} MB → sunucu /api/setup/download`);

// Dogrulama: indirme ucu 200 mu
const dl = await fetch(`${API}/api/setup/download`, { method: "GET" });
console.log(`DOGRULAMA: /api/setup/download HTTP ${dl.status}`);
