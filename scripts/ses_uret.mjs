// ═══ ALPNEX — BİLDİRİM SESLERİNİ ÜRETİR (kullanıcı isteği 2026-09-07) ═══
//
// NEDEN KENDİMİZ ÜRETİYORUZ:
//   İnternetten "ücretsiz" ses indirmek lisans riski taşır (çoğu "ücretsiz" ses aslında atıf ister
//   ya da ticari kullanımı kısıtlar) ve dosyanın kaynağı zamanla kaybolur. Buradaki sesler saf
//   matematikle üretilir → TELİF SORUNU YOKTUR, tamamen bize aittir, çevrimdışı üretilir ve
//   gerektiğinde bu betikle birebir yeniden üretilebilir.
//
// ÇIKTI: 44.1 kHz · 16-bit · MONO · WAV (hem masaüstü winmm hem tarayıcı <audio> bunu doğrudan çalar)
//
// KULLANIM:  node scripts/ses_uret.mjs
//   Dosyalar hem web'e hem masaüstüne yazılır (tek kaynak, iki ortamda AYNI ses).

import fs from "node:fs";
import path from "node:path";

const ORAN = 44100;             // örnekleme frekansı
const HEDEFLER = [
  "src/DepoWise.Web/wwwroot/sounds",
  "src/DepoWise.Desktop/Assets/Sounds",
];

/** Tek bir nota: frekans (Hz), süre (sn), tepe ses düzeyi (0..1), başlangıç gecikmesi (sn). */
function nota(frekans, sure, düzey, gecikme) {
  return { frekans, sure, düzey, gecikme };
}

/**
 * Notaları toplayıp 16-bit PCM örnek dizisi üretir.
 * Her notaya kısa bir yükselme/inme zarfı uygulanır — zarfsız sinüs "tık" sesi çıkarır.
 */
function örnekle(notalar) {
  const toplamSure = Math.max(...notalar.map(n => n.gecikme + n.sure)) + 0.03;
  const uzunluk = Math.ceil(toplamSure * ORAN);
  const veri = new Float32Array(uzunluk);

  for (const n of notalar) {
    const bas = Math.floor(n.gecikme * ORAN);
    const adet = Math.floor(n.sure * ORAN);
    const yukselme = Math.floor(0.006 * ORAN);           // 6 ms
    const inme = Math.floor(adet * 0.55);                 // yumuşak sönüm
    for (let i = 0; i < adet; i++) {
      const t = i / ORAN;
      // Hafif ikinci harmonik: saf sinüsten daha "dolu" ve daha az tiz duyulur.
      let s = Math.sin(2 * Math.PI * n.frekans * t) + 0.18 * Math.sin(4 * Math.PI * n.frekans * t);
      let zarf = 1;
      if (i < yukselme) zarf = i / yukselme;
      else if (i > adet - inme) zarf = Math.pow((adet - i) / inme, 1.6);
      const j = bas + i;
      if (j < uzunluk) veri[j] += s * n.düzey * zarf;
    }
  }

  // Kırpılmayı önle (birden çok nota üst üste binebilir).
  let tepe = 0;
  for (const v of veri) tepe = Math.max(tepe, Math.abs(v));
  const ölçek = tepe > 0.98 ? 0.98 / tepe : 1;

  const pcm = new Int16Array(uzunluk);
  for (let i = 0; i < uzunluk; i++) pcm[i] = Math.max(-32768, Math.min(32767, Math.round(veri[i] * ölçek * 32767)));
  return pcm;
}

/** 16-bit mono WAV başlığı + veri. */
function wav(pcm) {
  const veriByte = pcm.length * 2;
  const b = Buffer.alloc(44 + veriByte);
  b.write("RIFF", 0);
  b.writeUInt32LE(36 + veriByte, 4);
  b.write("WAVE", 8);
  b.write("fmt ", 12);
  b.writeUInt32LE(16, 16);           // fmt uzunluğu
  b.writeUInt16LE(1, 20);            // PCM
  b.writeUInt16LE(1, 22);            // mono
  b.writeUInt32LE(ORAN, 24);
  b.writeUInt32LE(ORAN * 2, 28);     // byte/sn
  b.writeUInt16LE(2, 32);            // blok hizası
  b.writeUInt16LE(16, 34);           // bit
  b.write("data", 36);
  b.writeUInt32LE(veriByte, 40);
  for (let i = 0; i < pcm.length; i++) b.writeInt16LE(pcm[i], 44 + i * 2);
  return b;
}

// ── SESLER ────────────────────────────────────────────────────────────────────
// Tasarım ilkesi: KISA ve ALÇAK sesli. Bu bir ofis uygulaması; ses dikkat çekmeli ama
// rahatsız etmemeli. Dördü birbirinden AÇIKÇA ayrılır (yön, nota sayısı ve tını farklı).

const SESLER = {
  // 1) Buton uyarı penceresi: iki İNEN nota — "dur, bak" hissi. Kısa ve nettir.
  "dugme-uyari": örnekle([
    nota(660, 0.10, 0.32, 0.00),
    nota(440, 0.16, 0.32, 0.09),
  ]),

  // 2) Gelen mesaj: iki ÇIKAN nota — "bir şey geldi". Ayırt edici ve olumlu.
  "mesaj-gelen": örnekle([
    nota(587.33, 0.09, 0.28, 0.00),   // re
    nota(880.00, 0.16, 0.28, 0.08),   // la
  ]),

  // 3) Giden mesaj: TEK, kısa ve DAHA ALÇAK nota — kendi eylemin, dikkat istemez.
  "mesaj-giden": örnekle([
    nota(987.77, 0.07, 0.16, 0.00),   // si
  ]),

  // 4) Uyarı / duyuru: üç notalı yumuşak çan (do-mi-sol). Diğerlerinden uzun ve
  //    "bilgilendirici" durur; birden çok uyarı gelse bile YALNIZ BİR KEZ çalar.
  "bildirim": örnekle([
    nota(523.25, 0.14, 0.24, 0.00),   // do
    nota(659.25, 0.14, 0.24, 0.11),   // mi
    nota(783.99, 0.26, 0.24, 0.22),   // sol
  ]),
};

for (const klasör of HEDEFLER) {
  fs.mkdirSync(klasör, { recursive: true });
  for (const [ad, pcm] of Object.entries(SESLER)) {
    const yol = path.join(klasör, ad + ".wav");
    fs.writeFileSync(yol, wav(pcm));
    console.log(`${yol}  ${(fs.statSync(yol).size / 1024).toFixed(1)} KB`);
  }
}
console.log("Bitti. Sesler saf matematikle üretildi — telif sorunu yoktur.");
