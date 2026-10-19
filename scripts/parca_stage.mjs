// ═══ PARÇA STAGE — bir dosyadaki değişiklik parçalarından (hunk) YALNIZ seçilenleri index'e koyar ═══
// (guvenli-commit skill'i; 2026-10-19 — aynı dosyaya dokunan birden çok iş AYRI commit'e girsin diye)
//
// Kullanım:  node scripts/parca_stage.mjs <dosya> "<düzenli ifade>" [--dene]
//   Çalışma kopyası ile INDEX arasındaki fark sıfır bağlamla (-U0) parçalara bölünür; eklenen/silinen satırlarından biri
//   ifadeyle eşleşen parçalar INDEX metnine eski-taraf satır numarasıyla uygulanır (her parçada silinecek satırların index'te
//   birebir durduğu doğrulanır; tutmazsa hiçbir şey yazılmaz). Diğer parçalar çalışma kopyasında kalır.
//   --dene: hiçbir şey yazmadan seçilecek parçaları listeler.
// ⚠️ 2026-10-19: ilk sürüm `git apply --cached --unidiff-zero` kullanıyordu; araya seçilmeyen parça girince ekleme bloklarını
//    YANLIŞ satıra koydu (M-26 commit'i derlenmez hâle geldi). Bu yüzden uygulama artık burada, doğrulamalı yapılır.
//    Yine de commit öncesi index'i ayrı klasörde derleyerek doğrula (scripts/index_derle.ps1 yoksa checkout-index + build).
// ⚠️ Bitişik iki işin satırları TEK parçaya düşebilir (araya değişmeyen satır girmezse) → böyle dosyada kismi_stage.mjs kullan.
import { execFileSync } from "node:child_process";

const [, , dosya, ifade, bayrak] = process.argv;
if (!dosya || !ifade) { console.error('Kullanım: node scripts/parca_stage.mjs <dosya> "<düzenli ifade>" [--dene]'); process.exit(2); }
const re = new RegExp(ifade);
const git = (args, input) => execFileSync("git", args, { encoding: "utf8", maxBuffer: 64 << 20, input });
const fark = git(["diff", "-U0", "--no-color", "--no-ext-diff", "--", dosya]);
if (!fark.trim()) { console.error("Fark yok: " + dosya); process.exit(1); }

// Parçaları ayrıştır
const parcalar = [];
let cur = null;
for (const l of fark.split("\n")) {
  const m = l.match(/^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/);
  if (m) { cur = { baslik: l, a: +m[1], b: m[2] === undefined ? 1 : +m[2], eksi: [], arti: [] }; parcalar.push(cur); continue; }
  if (!cur) continue;
  if (l.startsWith("-")) cur.eksi.push(l.slice(1));
  else if (l.startsWith("+")) cur.arti.push(l.slice(1));
  else if (l.startsWith("\\")) { console.error("Dosya sonu satır sonu farkı var — bu araçla değil kismi_stage.mjs ile stage et."); process.exit(1); }
}
const secilen = parcalar.filter(p => [...p.eksi, ...p.arti].some(x => re.test(x)));
console.log(`${parcalar.length} parçadan ${secilen.length} seçildi (${dosya})`);
for (const p of secilen) console.log("  " + p.baslik.slice(0, 40) + "  " + (p.arti[0] ?? p.eksi[0] ?? "").slice(0, 110));
if (bayrak === "--dene" || secilen.length === 0) process.exit(secilen.length === 0 ? 1 : 0);

// Index metnine uygula (sondan başa — öndeki parçalar sonrakilerin satır numarasını kaydırmasın)
const metin = git(["show", ":" + dosya]);
const sondaYeniSatir = metin.endsWith("\n");
const satir = (sondaYeniSatir ? metin.slice(0, -1) : metin).split("\n");
for (const p of [...secilen].sort((x, y) => y.a - x.a)) {
  const bas = p.b === 0 ? p.a : p.a - 1;          // b=0: a. satırdan SONRA ekle; değilse a. satırdan başlayarak değiştir
  const mevcut = satir.slice(bas, bas + p.b);
  if (mevcut.length !== p.eksi.length || mevcut.some((x, i) => x !== p.eksi[i])) {
    console.error("UYUŞMAZLIK — index'te beklenen satırlar yok: " + p.baslik + "\nHiçbir şey yazılmadı."); process.exit(1);
  }
  satir.splice(bas, p.b, ...p.arti);
}
const yeni = satir.join("\n") + (sondaYeniSatir ? "\n" : "");
const sha = git(["hash-object", "-w", "--no-filters", "--stdin"], yeni).trim();
const mod = git(["ls-files", "-s", "--", dosya]).split(" ")[0] || "100644";
git(["update-index", "--cacheinfo", `${mod},${sha},${dosya}`]);
console.log("index ← seçilen parçalar uygulandı (doğrulamalı)");
