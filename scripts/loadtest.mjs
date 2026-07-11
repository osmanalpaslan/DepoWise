// Basit yük testi (kurulum gerektirmez — sadece Node). Belirtilen URL'e N eşzamanlı istekle
// belirtilen süre boyunca yüklenir; saniyedeki istek (req/s), gecikme (p50/p95/max) ve hata oranı raporlanır.
//
// Kullanım:
//   node scripts/loadtest.mjs <url> [concurrency=50] [durationSec=10] [method=GET] [bodyJson]
// Örnekler:
//   node scripts/loadtest.mjs http://127.0.0.1:5224/api/public/companies 50 10
//   node scripts/loadtest.mjs http://127.0.0.1:5224/health 100 15
//
// NOT: Canlı (prod) sunucuya yük bindirmek onu yavaşlatabilir + rate-limit'e takılabilir. Ölçümü tercihen
// YEREL sunucuda yap. Gerçek kapasite için üretim boyutunda makinede + kademeli concurrency ile ölçülmeli.

const url = process.argv[2];
const concurrency = parseInt(process.argv[3] || "50", 10);
const durationSec = parseInt(process.argv[4] || "10", 10);
const method = (process.argv[5] || "GET").toUpperCase();
const body = process.argv[6];

if (!url) { console.error("HATA: url gerekli. Örn: node scripts/loadtest.mjs http://127.0.0.1:5224/health 50 10"); process.exit(1); }

const latencies = [];
let ok = 0, err = 0, done = false;
const opts = { method, headers: body ? { "Content-Type": "application/json" } : {}, body };

async function worker() {
  while (!done) {
    const t0 = performance.now();
    try {
      const r = await fetch(url, opts);
      const dt = performance.now() - t0;
      latencies.push(dt);
      if (r.status >= 200 && r.status < 400) ok++; else err++;
      await r.arrayBuffer(); // gövdeyi tüket
    } catch { err++; latencies.push(performance.now() - t0); }
  }
}

function pct(arr, p) {
  if (arr.length === 0) return 0;
  const s = [...arr].sort((a, b) => a - b);
  return s[Math.min(s.length - 1, Math.floor((p / 100) * s.length))];
}

console.log(`Yük testi: ${method} ${url} | eşzamanlı=${concurrency} | süre=${durationSec}s`);
const start = performance.now();
const workers = Array.from({ length: concurrency }, () => worker());
setTimeout(() => { done = true; }, durationSec * 1000);
await Promise.all(workers);
const elapsed = (performance.now() - start) / 1000;

const total = ok + err;
console.log("──────── SONUÇ ────────");
console.log(`Toplam istek : ${total}`);
console.log(`Başarılı     : ${ok}`);
console.log(`Hatalı       : ${err} (${total ? ((err / total) * 100).toFixed(1) : 0}%)`);
console.log(`İstek/saniye : ${(total / elapsed).toFixed(0)} req/s`);
console.log(`Gecikme p50  : ${pct(latencies, 50).toFixed(0)} ms`);
console.log(`Gecikme p95  : ${pct(latencies, 95).toFixed(0)} ms`);
console.log(`Gecikme max  : ${Math.max(0, ...latencies).toFixed(0)} ms`);
