// ═══ ALPNEX — BİLDİRİM SESLERİ: Octave paketinden indir + dönüştür (kullanıcı isteği 2026-10-10) ═══
//
// NEDEN: Eskiden sesler scripts/ses_uret.mjs ile matematiksel olarak üretiliyordu; kullanıcı
// "uygulama sesleri aşırı kötü" dedi ve GitHub'daki ücretsiz/popüler bir paketin kullanılmasını istedi.
//
// KAYNAK: Octave — "A free library of UI sounds, handmade for iOS" (github.com/scopegate/octave, ~1000★).
// LİSANS: kişisel/açık kaynak/TİCARİ kullanım ücretsiz, atıf zorunlu değil. Yasak olan: SETİN TAMAMINI
//   satmak/barındırmak/kiralamak. Biz yalnız 4 sesi uygulamaya gömüyoruz (bkz. THIRD_PARTY_NOTICES.md).
//
// EŞLEME (anlam → ses):
//   dugme-uyari  ← beeps/beep-brightpop  (onay/uyarı penceresi: kısa, yumuşak "pop")
//   mesaj-gelen  ← beeps/beep-xylo       (gelen mesaj: ksilofon, iMessage benzeri)
//   mesaj-giden  ← slides/slide-paper    (giden mesaj: kısa "vınn" kaydırma)
//   bildirim     ← beeps/beep-piano      (uyarı/duyuru: daha belirgin piyano tonu)
//
// ÇIKTI: 44.1 kHz · 16-bit · MONO · WAV, tepe seviyesi dengelenmiş (masaüstü winmm + tarayıcı <audio>).
// KULLANIM: node scripts/ses_guncelle.mjs   (gerekli: internet; ffmpeg-static geçici klasöre kurulur)

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync, execSync, spawnSync } from "node:child_process";

const kok = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const hedefler = [path.join(kok, "src/DepoWise.Desktop/Assets/Sounds"), path.join(kok, "src/DepoWise.Web/wwwroot/sounds")];
const sesler = [
  ["dugme-uyari", "beeps/beep-brightpop", -6],
  ["mesaj-gelen", "beeps/beep-xylo", -4],
  ["mesaj-giden", "slides/slide-paper", -6],
  ["bildirim", "beeps/beep-piano", -3],
];

const gecici = fs.mkdtempSync(path.join(os.tmpdir(), "dw-ses-"));
execSync("npm init -y && npm install ffmpeg-static@5.2.0 --silent", { cwd: gecici, stdio: "ignore" });
const ff = path.join(gecici, "node_modules/ffmpeg-static/ffmpeg.exe");

for (const [ad, kaynak, tepeDb] of sesler) {
  const aif = path.join(gecici, ad + ".aif");
  const yanit = await fetch(`https://raw.githubusercontent.com/scopegate/octave/master/Octave-Sounds/${kaynak}.aif`);
  if (!yanit.ok) throw new Error(`${kaynak} indirilemedi: ${yanit.status}`);
  fs.writeFileSync(aif, Buffer.from(await yanit.arrayBuffer()));
  const olcum = spawnSync(ff, ["-hide_banner", "-i", aif, "-af", "volumedetect", "-f", "null", "-"], { encoding: "utf8" }).stderr;   // ffmpeg ölçümü stderr'e yazar
  const tepe = Number((/max_volume: (-?[\d.]+) dB/.exec(olcum) ?? [])[1] ?? 0);
  const wav = path.join(gecici, ad + ".wav");
  execFileSync(ff, ["-hide_banner", "-loglevel", "error", "-y", "-i", aif, "-af", `volume=${(tepeDb - tepe).toFixed(2)}dB,apad=pad_dur=0.04`,
    "-ar", "44100", "-ac", "1", "-sample_fmt", "s16", wav]);
  for (const h of hedefler) fs.copyFileSync(wav, path.join(h, ad + ".wav"));
  console.log(`${ad}.wav ← ${kaynak} (tepe ${tepe} → ${tepeDb} dB)`);
}
fs.rmSync(gecici, { recursive: true, force: true });
