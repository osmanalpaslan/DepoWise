// ═══ ALPNEX — BİLDİRİM SESLERİ: kaynaktan indir + dönüştür (kullanıcı seçimi 2026-10-11) ═══
//
// GEÇMİŞ: 2026-10-10'da Octave paketine geçildi; kullanıcı "hâlâ sert/yüksek, daha yumuşak olsun" dedi.
// 2026-10-11'de 4 ücretsiz paketten ~40 aday ölçüldü (yüksek frekans payı = sertlik), aynı seviyeye getirildi ve
// masaüstünde "DepoWise Ses Adaylari" klasöründe kullanıcıya dinletildi. Kullanıcı aşağıdaki 4 sesi SEÇTİ.
//
// EŞLEME (anlam → kaynak) — hepsi CC0 (kamu malı; ticari kullanım serbest, atıf gerekmez):
//   dugme-uyari  ← uisfx  minimal/info        (onay/uyarı penceresi)    github.com/romainsimon/uisfx
//   bildirim     ← akx    Polite              (uyarı/duyuru)            github.com/akx/Notifications (CC0 seçeneği)
//   mesaj-gelen  ← akx    Glass               (sohbette gelen mesaj)
//   mesaj-giden  ← Kenney drop_002            (sohbette giden mesaj)    github.com/Calinou/kenney-interface-sounds
//
// ÇIKTI: 44.1 kHz · 16-bit · MONO · WAV · tepe −16 dB (eskisinden ~10 dB daha kısık, YUMUŞAK) · 40 ms sönümlü bitiş.
// Masaüstü (winmm) ve web (<audio>) AYNI dosyaları kullanır → iki ortamın sesi birebir aynıdır.
// KULLANIM: node scripts/ses_guncelle.mjs   (gerekli: internet; ffmpeg-static geçici klasöre kurulur)

import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { execFileSync, execSync, spawnSync } from "node:child_process";

const kok = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const hedefler = [path.join(kok, "src/DepoWise.Desktop/Assets/Sounds"), path.join(kok, "src/DepoWise.Web/wwwroot/sounds")];
const TEPE_DB = -16;
const sesler = [
  ["dugme-uyari", "https://raw.githubusercontent.com/romainsimon/uisfx/main/packages/uisfx/sounds/minimal/info.ogg"],
  ["bildirim", "https://raw.githubusercontent.com/akx/Notifications/master/WAV/Polite.wav"],
  ["mesaj-gelen", "https://raw.githubusercontent.com/akx/Notifications/master/WAV/Glass.wav"],
  ["mesaj-giden", "https://raw.githubusercontent.com/Calinou/kenney-interface-sounds/master/addons/kenney_interface_sounds/drop_002.wav"],
];

const gecici = fs.mkdtempSync(path.join(os.tmpdir(), "dw-ses-"));
execSync("npm init -y && npm install ffmpeg-static@5.2.0 --silent", { cwd: gecici, stdio: "ignore" });
const ff = path.join(gecici, "node_modules/ffmpeg-static/ffmpeg.exe");

for (const [ad, url] of sesler) {
  const kaynak = path.join(gecici, ad + path.extname(url));
  const yanit = await fetch(url);
  if (!yanit.ok) throw new Error(`${url} indirilemedi: ${yanit.status}`);
  fs.writeFileSync(kaynak, Buffer.from(await yanit.arrayBuffer()));
  const olcum = spawnSync(ff, ["-hide_banner", "-i", kaynak, "-af", "volumedetect", "-f", "null", "-"], { encoding: "utf8" }).stderr;   // ffmpeg ölçümü stderr'e yazar
  const tepe = Number((/max_volume: (-?[\d.]+) dB/.exec(olcum) ?? [])[1] ?? 0);
  const sure = (() => { const m = /Duration: (\d+):(\d+):([\d.]+)/.exec(olcum); return m ? +m[1] * 3600 + +m[2] * 60 + parseFloat(m[3]) : 0; })();
  const wav = path.join(gecici, ad + ".wav");
  execFileSync(ff, ["-hide_banner", "-loglevel", "error", "-y", "-i", kaynak,
    "-af", `volume=${(TEPE_DB - tepe).toFixed(1)}dB,afade=t=out:st=${Math.max(0, sure - 0.04).toFixed(3)}:d=0.04`,
    "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le",
    // Ek bilgi bölümü (LIST/INFO) YAZILMAZ → standart 44 baytlık başlık (testler ve eski oynatıcılar bunu bekler).
    "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:a", "+bitexact", wav]);
  for (const h of hedefler) fs.copyFileSync(wav, path.join(h, ad + ".wav"));
  console.log(`${ad}.wav ← ${url.split("/").slice(-1)[0]} (tepe ${tepe} → ${TEPE_DB} dB)`);
}
fs.rmSync(gecici, { recursive: true, force: true });
