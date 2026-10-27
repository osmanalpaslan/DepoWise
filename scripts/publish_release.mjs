// DepoWise masaustu surumunu otomatik yayinlar (Super Admin login + paket yukleme).
// Tarayici GEREKMEZ; komut satirindan calisir.
//
// Gerekli ortam degiskenleri:
//   DEPOWISE_ADMIN_USER  - Super Admin kullanici adi
//   DEPOWISE_ADMIN_PASS  - Super Admin sifresi
//   (istege bagli) DEPOWISE_API - varsayilan https://depowise-erp.fly.dev
//
// ⭐ M-01 (2026-10-27): paket IMZALANIR (gizli anahtar: scripts/lib/yayin_ortak.mjs) — sunucu ve masaustu imzasizi reddeder.
//   Imza ayrica "<zip>.imza" dosyasina yazilir (web "Guncelleme Yonetimi" formuna yapistirmak icin).
//   Hesapta iki adimli giris aciksa kod DEPOWISE_ADMIN_TOTP / DEPOWISE_ADMIN_KOD ile verilir.
//   --min X.Y.Z : en dusuk desteklenen surum (eski masaustleri ZORUNLU guncellemeye girer; varsayilan 0.0.0).
//
// Kullanim:
//   node scripts/publish_release.mjs <zipYolu> <surum> "<notlar>" [--min X.Y.Z]
// Ornek:
//   node scripts/publish_release.mjs artifacts/rc/DepoWise-desktop-1.0.35.zip 1.0.35 "foto opt + guvenlik + login/sube"
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { superAdminGiris, imzala } from "./lib/yayin_ortak.mjs";

const API = process.env.DEPOWISE_API || "https://depowise-erp.fly.dev";
const argv = process.argv.slice(2);
const minIdx = argv.indexOf("--min");
const minSupported = minIdx >= 0 ? argv[minIdx + 1] : "0.0.0";
if (minIdx >= 0) argv.splice(minIdx, 2);
const [zipPath, version, notes = ""] = argv;

if (!zipPath || !version) { console.error("HATA: kullanim: node scripts/publish_release.mjs <zipYolu> <surum> \"<notlar>\" [--min X.Y.Z]"); process.exit(1); }
if (!fs.existsSync(zipPath)) { console.error("HATA: zip bulunamadi: " + zipPath); process.exit(1); }
if (!/^\d+\.\d+\.\d+$/.test(minSupported || "")) { console.error("HATA: --min X.Y.Z biciminde olmali."); process.exit(1); }

// 1) Checksum + boyut + IMZA (girişten önce: anahtar yoksa sunucuya hiç gidilmez)
const buf = fs.readFileSync(zipPath);
const checksum = crypto.createHash("sha256").update(buf).digest("hex");
const size = buf.length;
let signature;
try { signature = imzala(version, checksum); } catch (e) { console.error("HATA: imza uretilemedi: " + e.message); process.exit(1); }
fs.writeFileSync(zipPath + ".imza", signature);

// 2) Login (companyId yok => LoginAnyCompany; sube yok; iki adimli giris kodu gerekirse ortamdan)
let login;
try { login = await superAdminGiris(API); } catch (e) { console.error("HATA: " + e.message); process.exit(1); }
console.log(`Giris OK (superAdmin, firma=${login.companyName}). Paket yukleniyor... (en dusuk desteklenen surum: ${minSupported})`);

// 3) Multipart form ile yayinla
const form = new FormData();
form.append("version", version);
form.append("checksum", checksum);
form.append("sizeBytes", String(size));
form.append("minSupportedVersion", minSupported);
form.append("releaseNotes", notes);
form.append("signed", "1");
form.append("signature", signature);
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
console.log(`DOGRULAMA: sunucudaki en guncel surum = ${latest?.version ?? "(yok)"} · imzali=${latest?.signature ? "evet" : "HAYIR"} · en dusuk=${latest?.minSupportedVersion}`);

// ── 5) YEREL ARŞİV TEMİZLİĞİ (kullanıcı isteği 2026-08-11) ──────────────────────────────────
// SORUN: her yayında artifacts/rc altında bir .zip (~85 MB) + açılmış klasör (~240 MB) kalıyordu.
// Bir ayda 88 sürüm birikip 28 GB disk yemişti. Sunucuda zaten bir koruma var (ADR-070:
// ReleaseStore.PruneOld en yeni 3 paketi tutar) ama YERELDE yoktu.
//
// KURAL: paket sunucuya BAŞARIYLA yüklendikten SONRA, yerelde yalnız EN YENİ 3 sürüm tutulur.
// Yükleme başarısızsa buraya hiç gelinmez → elde paket kalmadan silme riski yok.
// Silinenler yeniden üretilebilir (paketleme komutu) ve git'te değildir (.gitignore: artifacts/).
const KEEP = 3;
try {
  const rcDir = path.dirname(path.resolve(zipPath));
  // "DepoWise-desktop-1.2.3.zip" / "desktop-1.2.3" → 1.2.3 · sürüm çıkaramadığımız ada DOKUNULMAZ.
  const verOf = (name) => (name.match(/(\d+)\.(\d+)\.(\d+)/) || []).slice(1).map(Number);
  const cmp = (a, b) => (a[0] - b[0]) || (a[1] - b[1]) || (a[2] - b[2]);

  const entries = fs.readdirSync(rcDir, { withFileTypes: true })
    .map((e) => ({ name: e.name, dir: e.isDirectory(), v: verOf(e.name) }))
    .filter((e) => e.v.length === 3);

  // Sürüm bazında grupla: bir sürümün hem .zip'i hem klasörü aynı anda silinir/kalır.
  const versions = [...new Set(entries.map((e) => e.v.join(".")))]
    .map((s) => s.split(".").map(Number))
    .sort(cmp)
    .reverse();
  const keep = new Set(versions.slice(0, KEEP).map((v) => v.join(".")));

  let freed = 0, removed = 0;
  for (const e of entries) {
    if (keep.has(e.v.join("."))) continue;
    const full = path.join(rcDir, e.name);
    try {
      freed += e.dir ? dirSize(full) : fs.statSync(full).size;
      fs.rmSync(full, { recursive: true, force: true });
      removed++;
    } catch { /* kilitli dosya → atla; bir sonraki yayında tekrar denenir */ }
  }
  console.log(removed > 0
    ? `TEMIZLIK: ${removed} eski oge silindi (~${(freed / 1073741824).toFixed(2)} GB). Yerelde tutulan surum: ${[...keep].join(", ")}`
    : `TEMIZLIK: silinecek eski surum yok (en fazla ${KEEP} surum tutuluyor).`);
} catch (err) {
  // Temizlik ASLA yayını başarısız saymaz — paket zaten sunucuda.
  console.warn("UYARI: yerel arsiv temizligi yapilamadi: " + (err?.message ?? err));
}

function dirSize(p) {
  let total = 0;
  for (const e of fs.readdirSync(p, { withFileTypes: true })) {
    const f = path.join(p, e.name);
    try { total += e.isDirectory() ? dirSize(f) : fs.statSync(f).size; } catch { /* atla */ }
  }
  return total;
}
