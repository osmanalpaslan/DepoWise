// DepoWise masaustu surumunu otomatik yayinlar (Super Admin login + paket yukleme).
// Tarayici GEREKMEZ; komut satirindan calisir.
//
// Gerekli ortam degiskenleri:
//   DEPOWISE_ADMIN_USER  - Super Admin kullanici adi
//   DEPOWISE_ADMIN_PASS  - Super Admin sifresi
//   (istege bagli) DEPOWISE_API - varsayilan https://depowise-erp.fly.dev
//
// Kullanim:
//   node scripts/publish_release.mjs <zipYolu> <surum> "<notlar>"
// Ornek:
//   node scripts/publish_release.mjs artifacts/rc/DepoWise-desktop-1.0.35.zip 1.0.35 "foto opt + guvenlik + login/sube"
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const API = process.env.DEPOWISE_API || "https://depowise-erp.fly.dev";
const user = process.env.DEPOWISE_ADMIN_USER;
const pass = process.env.DEPOWISE_ADMIN_PASS;
const [zipPath, version, notes = ""] = process.argv.slice(2);

if (!user || !pass) { console.error("HATA: DEPOWISE_ADMIN_USER / DEPOWISE_ADMIN_PASS ortam degiskeni yok."); process.exit(1); }
if (!zipPath || !version) { console.error("HATA: kullanim: node scripts/publish_release.mjs <zipYolu> <surum> \"<notlar>\""); process.exit(1); }
if (!fs.existsSync(zipPath)) { console.error("HATA: zip bulunamadi: " + zipPath); process.exit(1); }

// 1) Login (companyId yok => LoginAnyCompany; sube yok)
const loginResp = await fetch(`${API}/api/auth/login`, {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ username: user, password: pass }),
});
if (!loginResp.ok) { console.error(`HATA: login ${loginResp.status} - ${await loginResp.text()}`); process.exit(1); }
const login = await loginResp.json();
if (!login.token) { console.error("HATA: token alinamadi: " + JSON.stringify(login)); process.exit(1); }
if (!login.isSuperAdmin) { console.error("HATA: bu kullanici Super Admin degil; surum yayinlama yetkisi yok."); process.exit(1); }
console.log(`Giris OK (superAdmin, firma=${login.companyName}). Paket yukleniyor...`);

// 2) Checksum + boyut
const buf = fs.readFileSync(zipPath);
const checksum = crypto.createHash("sha256").update(buf).digest("hex");
const size = buf.length;

// 3) Multipart form ile yayinla
const form = new FormData();
form.append("version", version);
form.append("checksum", checksum);
form.append("sizeBytes", String(size));
form.append("minSupportedVersion", "0.0.0");
form.append("releaseNotes", notes);
form.append("signed", "0");
form.append("file", new Blob([buf], { type: "application/octet-stream" }), path.basename(zipPath));

const pubResp = await fetch(`${API}/api/releases`, {
  method: "POST",
  headers: { Authorization: `Bearer ${login.token}` },
  body: form,
});
if (!pubResp.ok) { console.error(`HATA: yayinla ${pubResp.status} - ${await pubResp.text()}`); process.exit(1); }
const pub = await pubResp.json();
console.log(`YAYINLANDI: surum=${version} checksum=${checksum.slice(0,12)}... boyut=${(size/1048576).toFixed(1)}MB downloadUrl=${pub.downloadUrl}`);

// 4) Dogrulama: latest artik bu surum mu?
const latest = await (await fetch(`${API}/api/releases/latest`)).json();
console.log(`DOGRULAMA: sunucudaki en guncel surum = ${latest?.version ?? "(yok)"}`);
