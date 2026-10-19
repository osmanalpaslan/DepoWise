// ═══ PARÇA STAGE — bir dosyadaki değişiklik parçalarından (hunk) YALNIZ seçilenleri index'e koyar ═══
// (guvenli-commit skill'i; 2026-10-19 — aynı dosyaya dokunan birden çok iş AYRI commit'e girsin diye)
//
// Kullanım:  node scripts/parca_stage.mjs <dosya> "<düzenli ifade>" [--dene]
//   Çalışma kopyası ile INDEX arasındaki fark sıfır bağlamla (-U0) parçalara bölünür; eklenen/silinen satırlarından biri
//   ifadeyle eşleşen parçalar `git apply --cached --unidiff-zero` ile index'e uygulanır. Diğer parçalar (kullanıcının işi
//   ya da sonraki işler) çalışma kopyasında kalır. --dene: hiçbir şey yazmadan seçilecek parçaları listeler.
// ⚠️ Bitişik iki işin satırları TEK parçaya düşebilir (araya değişmeyen satır girmezse) → böyle dosyada kismi_stage.mjs kullan.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";

const [, , dosya, ifade, bayrak] = process.argv;
if (!dosya || !ifade) { console.error('Kullanım: node scripts/parca_stage.mjs <dosya> "<düzenli ifade>" [--dene]'); process.exit(2); }
const re = new RegExp(ifade);
const fark = execFileSync("git", ["diff", "-U0", "--no-color", "--", dosya], { encoding: "utf8", maxBuffer: 64 << 20 });
if (!fark.trim()) { console.error("Fark yok: " + dosya); process.exit(1); }
const satirlar = fark.split("\n");
const ilk = satirlar.findIndex(l => l.startsWith("@@"));
const baslik = satirlar.slice(0, ilk).join("\n");
const parcalar = [];
let cur = null;
for (const l of satirlar.slice(ilk)) {
  if (l.startsWith("@@")) { cur = [l]; parcalar.push(cur); }
  else if (cur) cur.push(l);
}
const secilen = parcalar.filter(p => p.slice(1).some(l => (l.startsWith("+") || l.startsWith("-")) && re.test(l.slice(1))));
console.log(`${parcalar.length} parçadan ${secilen.length} seçildi (${dosya})`);
for (const p of secilen) console.log("  " + p[0] + "  " + (p.find(l => l.startsWith("+")) ?? p[1] ?? "").slice(0, 110));
if (bayrak === "--dene" || secilen.length === 0) process.exit(secilen.length === 0 ? 1 : 0);
// "\ No newline at end of file" satırları parçayla birlikte taşınır; sondaki boş satır yama biçimini bozmasın.
const yama = baslik + "\n" + secilen.map(p => p.filter((l, i) => !(i === p.length - 1 && l === "")).join("\n")).join("\n") + "\n";
const gecici = path.join(os.tmpdir(), "dw_parca_" + process.pid + ".patch");
fs.writeFileSync(gecici, yama);
try { execFileSync("git", ["apply", "--cached", "--unidiff-zero", "--whitespace=nowarn", gecici], { stdio: "inherit" }); }
finally { fs.rmSync(gecici, { force: true }); }
console.log("index ← seçilen parçalar uygulandı");
