// ═══ KISMİ STAGE — kullanıcının commitlenmemiş değişikliklerine dokunmadan YALNIZ verilen yamayı commit'e hazırlar ═══
// (guvenli-commit skill'i; 2026-10-11'de oturum geçici klasöründeki program_apply.mjs'ten kalıcılaştırıldı)
//
// Kullanım:
//   node scripts/kismi_stage.mjs <dosya> <yama.mjs> work    → yamayı ÇALIŞMA KOPYASINA uygular (kullanıcı hunk'ları korunur)
//   node scripts/kismi_stage.mjs <dosya> <yama.mjs> stage   → yamayı HEAD sürümüne uygular, sonucu INDEX'e koyar
// <yama.mjs>: `export default [[eskiMetin, yeniMetin], ...]` — her eskiMetin dosyada TAM 1 kez geçmeli (yoksa durur).
// Satır sonları korunur (CRLF/LF). Bir yamayı HEAD'e iki kez uygulama: önceki commit'e girdiyse yalnız yenisini ver.
import fs from "node:fs";
import path from "node:path";
import { execSync } from "node:child_process";
import { pathToFileURL } from "node:url";

const [, , dosya, yamaYolu, mod] = process.argv;
if (!dosya || !yamaYolu || !["work", "stage"].includes(mod)) {
  console.error("Kullanım: node scripts/kismi_stage.mjs <dosya> <yama.mjs> work|stage"); process.exit(2);
}
const kok = execSync("git rev-parse --show-toplevel", { encoding: "utf8" }).trim();
const rel = path.relative(kok, path.resolve(dosya)).split(path.sep).join("/");
const yama = (await import(pathToFileURL(path.resolve(yamaYolu)).href)).default;

let s = mod === "stage"
  ? execSync(`git show HEAD:${rel}`, { cwd: kok, encoding: "utf8", maxBuffer: 64 << 20 })
  : fs.readFileSync(path.join(kok, rel), "utf8");
const crlf = s.includes("\r\n");
s = s.replace(/\r\n/g, "\n");
for (const [eski, yeni] of yama) {
  const n = s.split(eski).length - 1;
  if (n !== 1) { console.error(`HATA: eşleşme ${n} (1 olmalı): ${eski.slice(0, 100)}`); process.exit(1); }
  s = s.replace(eski, () => yeni);
}
if (crlf) s = s.replace(/\n/g, "\r\n");

if (mod === "stage") {
  const gecici = path.join(process.env.TEMP || "/tmp", "dw_kismi_stage.tmp");
  fs.writeFileSync(gecici, s);
  const sha = execSync(`git hash-object -w "${gecici}"`, { cwd: kok, encoding: "utf8" }).trim();
  execSync(`git update-index --cacheinfo 100644,${sha},${rel}`, { cwd: kok });
  fs.rmSync(gecici, { force: true });
  console.log(`index ← HEAD + yama (${rel}, ${sha.slice(0, 8)})`);
} else {
  fs.writeFileSync(path.join(kok, rel), s);
  console.log(`çalışma kopyası güncellendi (${rel})`);
}
