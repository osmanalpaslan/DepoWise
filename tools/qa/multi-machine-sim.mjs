#!/usr/bin/env node
/**
 * DepoWise — ÇOK MAKİNELİ GERÇEKÇİ KULLANIM SİMÜLASYONU (§7 QA)
 *
 * Amaç: N sanal makine/kullanıcı AYNI ANDA ve birbirine yakın zamanlarda, gerçek bir insan gibi
 * bütün ekranlarda kayıt oluştursun/düzenlesin. Aranan şey hız değil, MANTIK HATASI:
 *   - kaybolan güncelleme (iki kişi aynı kaydı yazınca biri sessizce siliniyor mu?)
 *   - mükerrer kod kabulü, tenant sızıntısı, beklenmeyen 500
 *   - stok/yakıt bakiyesinin tutarsız kalması
 *
 * ⚠️ CANLI SUNUCUDA ÇALIŞTIRMA. Varsayılan hedef yerel sunucudur; gerçek veriyi çöpe çevirir.
 *
 * Kullanım:
 *   node tools/qa/multi-machine-sim.mjs [api] [makineSayisi] [turSayisi]
 *   node tools/qa/multi-machine-sim.mjs http://127.0.0.1:5299 10 12
 */

const API = (process.argv[2] || 'http://127.0.0.1:5299').replace(/\/$/, '');
const MACHINES = parseInt(process.argv[3] || '10', 10);
const ROUNDS = parseInt(process.argv[4] || '12', 10);
const SEED_USER = process.env.SIM_SEED_USER || 'superadmin';
const SEED_PASS = process.env.SIM_SEED_PASS || 'SimTest-2026';

if (/depowise-erp\.fly\.dev/.test(API)) {
  console.error('RED: bu simulasyon CANLI sunucuda calistirilamaz (gercek veriyi kirletir).');
  process.exit(2);
}

// ── Ölçüm ve bulgu toplama ────────────────────────────────────────────────
const lat = [];                 // {op, ms, status}
const findings = [];            // mantık hatası bulguları
const statusCount = new Map();
const opCount = new Map();
const note = (sev, screen, msg) => findings.push({ sev, screen, msg });
const bump = (m, k) => m.set(k, (m.get(k) || 0) + 1);

async function call(token, method, path, body, op = path) {
  const t0 = performance.now();
  let resp, text;
  try {
    resp = await fetch(API + path, {
      method,
      headers: { 'content-type': 'application/json', ...(token ? { Authorization: 'Bearer ' + token } : {}) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    text = await resp.text();
  } catch (e) {
    lat.push({ op, ms: performance.now() - t0, status: 0 });
    bump(statusCount, 'AG-HATASI');
    note('YUKSEK', op, 'Ag hatasi: ' + e.message);
    return { status: 0, json: null, text: '' };
  }
  const ms = performance.now() - t0;
  lat.push({ op, ms, status: resp.status });
  bump(statusCount, String(resp.status));
  bump(opCount, op);
  if (resp.status >= 500) note('KRITIK', op, `HTTP ${resp.status} (sunucu hatasi): ${text.slice(0, 200)}`);
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { }
  return { status: resp.status, json, text };
}

const login = async (u, p) => (await call(null, 'POST', '/api/auth/login', { username: u, password: p }, 'login')).json;
const sleep = ms => new Promise(r => setTimeout(r, ms));
const rnd = (a, b) => a + Math.floor(Math.random() * (b - a + 1));
const pick = arr => arr[rnd(0, arr.length - 1)];
const think = () => sleep(rnd(80, 400)); // insan gibi: ekranı okuma/yazma payı

// ── Kurulum ───────────────────────────────────────────────────────────────
console.log(`\nDepoWise cok-makineli simulasyon — ${API}\nMakine: ${MACHINES}  Tur: ${ROUNDS}\n`);

const su = await login(SEED_USER, SEED_PASS);
if (!su?.token) { console.error('HATA: seed super admin girisi basarisiz. Yerel sunucu acik mi?'); process.exit(1); }
const SU = su.token;

const companyName = 'SIM ' + Date.now();
const comp = await call(SU, 'POST', '/api/companies', { name: companyName, maxUsers: 200, maxAdmins: 50, machineQuota: 50 }, 'firma-olustur');
const companyId = comp.json?.id;
if (!companyId) { console.error('HATA: firma olusturulamadi: ' + comp.text.slice(0, 300)); process.exit(1); }

const branchIds = [];
for (const bn of ['Merkez Depo', 'Karaman Santiye', 'Konya Santiye']) {
  const b = await call(SU, 'POST', '/api/branches', { name: bn, kind: 'branch', companyId }, 'sube-olustur');
  if (b.json?.id) branchIds.push(b.json.id);
}
if (!branchIds.length) { console.error('HATA: sube olusturulamadi.'); process.exit(1); }

// 10 ayrı kullanıcı = 10 ayrı makine/insan
const machines = [];
for (let i = 1; i <= MACHINES; i++) {
  const username = `sim${String(i).padStart(2, '0')}`;
  const password = 'Sim-' + Date.now() + '-' + i;
  const branchId = branchIds[i % branchIds.length];
  const cu = await call(SU, 'POST', '/api/users', {
    username, password, fullName: 'Sim Kullanici ' + i,
    roleKeys: ['role-company-admin'], companyId, branchId, canViewAllBranches: true,
  }, 'kullanici-olustur');
  if (cu.status !== 200) { console.error(`HATA: kullanici ${username} olusturulamadi (${cu.status}): ${cu.text.slice(0, 200)}`); process.exit(1); }
  const s = await login(username, password);
  if (!s?.token) { console.error(`HATA: ${username} giris yapamadi.`); process.exit(1); }
  // Her makinenin bir "işlemi yapan personeli" olur (stok/yakıt işlemlerinde zorunlu).
  const p = await call(s.token, 'POST', '/api/personnel', { fullName: 'Depocu ' + i, title: 'Depo', branchId }, 'personel-olustur');
  machines.push({ id: username, token: s.token, branchId, actor: p.json?.id, created: { materials: [], vehicles: [], personnel: [] } });
}
console.log(`Kurulum tamam: firma=${companyName}, ${branchIds.length} sube, ${machines.length} makine\n`);

// Ortak tanımlar (tek sefer)
const M0 = machines[0].token;
const unit = (await call(M0, 'POST', '/api/lookups/units', { name: 'Adet' }, 'tanim-ekle')).json?.id;
const cat = (await call(M0, 'POST', '/api/lookups/material_categories', { name: 'Genel' }, 'tanim-ekle')).json?.id;
const maintDef = (await call(M0, 'POST', '/api/maintenance/definitions', { name: 'Periyodik Bakim', intervalValue: 10000, intervalUnit: 'km' }, 'bakim-tanim')).json?.id;

// Ortak "sıcak kayıt" — çakışma senaryosu için herkesin düzenleyeceği kayıt
const sharedMat = (await call(M0, 'POST', '/api/materials', { code: 'ORTAK-001', name: 'Ortak Malzeme', unitId: unit, categoryId: cat }, 'malzeme-olustur')).json?.id;

// ── Bir makinenin insan gibi davranışı ────────────────────────────────────
async function humanRound(m, round) {
  const tag = `${m.id}-${round}`;
  const action = pick(['malzeme', 'malzeme', 'arac', 'personel', 'stok', 'yakit', 'faaliyet', 'liste', 'liste', 'duzenle']);

  if (action === 'malzeme') {
    const code = `M-${m.id}-${round}`;
    const r = await call(m.token, 'POST', '/api/materials', { code, name: 'Malzeme ' + tag, unitId: unit, categoryId: cat }, 'malzeme-olustur');
    if (r.status === 200 && r.json?.id) m.created.materials.push(r.json.id);
    else if (r.status !== 200) note('ORTA', 'Malzemeler', `Yeni malzeme reddedildi (${r.status}): ${r.text.slice(0, 120)}`);
    await think();
    // Aynı kodu TEKRAR dene → REDDEDİLMELİ (mükerrer koruması)
    const dup = await call(m.token, 'POST', '/api/materials', { code, name: 'Mukerrer ' + tag, unitId: unit }, 'malzeme-mukerrer');
    if (dup.status === 200) note('KRITIK', 'Malzemeler', `MUKERRER KOD KABUL EDILDI: "${code}" iki kez olusturuldu.`);
  }

  else if (action === 'arac') {
    const r = await call(m.token, 'POST', '/api/vehicles', {
      internalCode: `A-${m.id}-${round}`, plate: `06 ${m.id.slice(-2)} ${round}`, productionYear: rnd(2010, 2024),
      currentMeter: rnd(0, 200000), meterUnit: 'km', branchId: m.branchId, status: 'active',
    }, 'arac-olustur');
    if (r.status === 200 && r.json?.id) m.created.vehicles.push(r.json.id);
    else note('ORTA', 'Araclar', `Yeni arac reddedildi (${r.status}): ${r.text.slice(0, 120)}`);
  }

  else if (action === 'personel') {
    const r = await call(m.token, 'POST', '/api/personnel', { fullName: `Personel ${tag}`, title: 'Operator', branchId: m.branchId }, 'personel-olustur');
    if (r.status === 200 && r.json?.id) m.created.personnel.push(r.json.id);
    else note('ORTA', 'Personel', `Yeni personel reddedildi (${r.status}): ${r.text.slice(0, 120)}`);
  }

  else if (action === 'stok') {
    // Mal kabul (yeni malzeme + açılış miktarı), sonra çıkış
    const code = `S-${m.id}-${round}`;
    const rec = await call(m.token, 'POST', '/api/stock/receive', {
      code, name: 'Stok Malzeme ' + tag, unitId: unit, categoryId: cat,
      quantity: 100, unitPrice: 25, branchId: m.branchId, personnelId: m.actor,
    }, 'stok-giris');
    if (rec.status !== 200) { note('ORTA', 'Stok Giris', `Mal kabul reddedildi (${rec.status}): ${rec.text.slice(0, 120)}`); return; }
    const matId = rec.json?.materialId ?? rec.json?.id;
    await think();
    if (matId) {
      const iss = await call(m.token, "POST", "/api/stock/issue", { materialId: matId, quantity: 30, branchId: m.branchId, personnelId: m.actor }, 'stok-cikis');
      if (iss.status !== 200) note('ORTA', 'Stok Cikis', `Cikis reddedildi (${iss.status}): ${iss.text.slice(0, 120)}`);
      // MANTIK: eldekinden fazlasını çıkarmak REDDEDİLMELİ
      const over = await call(m.token, "POST", "/api/stock/issue", { materialId: matId, quantity: 100000, branchId: m.branchId, personnelId: m.actor }, 'stok-asiri-cikis');
      if (over.status === 200) note('KRITIK', 'Stok Cikis', 'Elde olmayan miktar cikisi KABUL EDILDI (negatif stok).');
    }
  }

  else if (action === 'yakit') {
    const dep = await call(m.token, 'POST', '/api/fuel/depot', { liters: 1000, unitPrice: 42.5 }, 'yakit-depo');
    if (dep.status !== 200) { note('ORTA', 'Yakit', `Depo girisi reddedildi (${dep.status}): ${dep.text.slice(0, 120)}`); return; }
    await think();
    const v = pick(m.created.vehicles);
    if (v) {
      const dist = await call(m.token, 'POST', '/api/fuel/distribute', { vehicleId: v, liters: 50, currentMeter: rnd(200001, 300000) }, 'yakit-dagitim');
      if (dist.status !== 200 && dist.status !== 400) note('ORTA', 'Yakit', `Dagitim beklenmedik (${dist.status}): ${dist.text.slice(0, 120)}`);
    }
  }

  else if (action === 'faaliyet') {
    const v = pick(m.created.vehicles);
    if (!v) return;
    const r = await call(m.token, 'POST', '/api/daily/movement', {
      movementKind: 'transfer', vehicleId: v, description: 'Simulasyon hareketi ' + tag,
    }, 'faaliyet-hareket');
    if (r.status !== 200 && r.status !== 400) note('ORTA', 'Gunluk Faaliyet', `Hareket beklenmedik (${r.status}): ${r.text.slice(0, 120)}`);
    if (maintDef) {
      const mt = await call(m.token, 'POST', '/api/maintenance', { vehicleId: v, definitionId: maintDef, description: 'Bakim ' + tag, performedKm: rnd(1000, 50000) }, 'bakim-kaydi');
      if (mt.status !== 200 && mt.status !== 400) note('ORTA', 'Bakim', `Bakim kaydi beklenmedik (${mt.status}): ${mt.text.slice(0, 120)}`);
    }
  }

  else if (action === 'liste') {
    // Gerçek kullanıcı çoğunlukla LİSTE okur
    for (const p of ['/api/materials?page=1&pageSize=50', '/api/vehicles?page=1&pageSize=50', '/api/personnel']) {
      const r = await call(m.token, 'GET', p, undefined, 'liste' + p.split('?')[0]);
      // TENANT SIZINTISI: başka firmanın kaydı görünüyor mu?
      const rows = Array.isArray(r.json) ? r.json : (r.json?.items ?? []);
      for (const row of rows) {
        if (row?.companyId && row.companyId !== companyId) {
          note('KRITIK', 'Tenant', `BASKA FIRMANIN KAYDI GORUNDU: ${p} -> ${row.id}`); break;
        }
      }
    }
  }

  else if (action === 'duzenle') {
    // ORTAK kaydı düzenle → başkalarıyla çakışması BEKLENİR (düzenleme kilidi burada sınanır)
    if (!sharedMat) return;
    const d = await call(m.token, 'GET', `/api/materials/${sharedMat}`, undefined, 'malzeme-detay');
    if (d.status !== 200) return;
    const v = d.json?.version;
    await think(); // insan formu doldururken geçen süre — ÇAKIŞMA PENCERESİ
    const w = await call(m.token, 'PUT', `/api/materials/${sharedMat}`, {
      code: 'ORTAK-001', name: `Ortak (${m.id} yazdi)`, unitId: unit, version: v,
    }, 'malzeme-duzenle');
    if (w.status === 200) return { wroteShared: { machine: m.id, version: v } };
    if (w.status !== 409) note('ORTA', 'Duzenleme kilidi', `Beklenmeyen yanit ${w.status}: ${w.text.slice(0, 120)}`);
    return { blockedShared: { machine: m.id, version: v } };
  }
}

// ── Eşzamanlı koşu ────────────────────────────────────────────────────────
const sharedWrites = [];   // {machine, version}
const sharedBlocked = [];

const t0 = performance.now();
await Promise.all(machines.map(async m => {
  for (let round = 1; round <= ROUNDS; round++) {
    const res = await humanRound(m, round);
    if (res?.wroteShared) sharedWrites.push(res.wroteShared);
    if (res?.blockedShared) sharedBlocked.push(res.blockedShared);
    await think();
  }
}));
const elapsed = (performance.now() - t0) / 1000;

// ── ÇAKIŞMA TESTİ: aynı anda aynı kaydı yaz ───────────────────────────────
// Tam eşzamanlı: hepsi AYNI sürümü okur, sonra hepsi birden yazar. Tam olarak 1 tanesi kazanmalı.
if (sharedMat) {
  const d = await call(M0, 'GET', `/api/materials/${sharedMat}`, undefined, 'malzeme-detay');
  const v = d.json?.version;
  const results = await Promise.all(machines.map(m =>
    call(m.token, 'PUT', `/api/materials/${sharedMat}`, { code: 'ORTAK-001', name: `Yaris ${m.id}`, unitId: unit, version: v }, 'yaris-yazma')));
  const winners = results.filter(r => r.status === 200).length;
  const conflicts = results.filter(r => r.status === 409).length;
  console.log(`ES ZAMANLI YARIS (${machines.length} makine ayni surumu yaziyor): kazanan=${winners}, 409=${conflicts}`);
  if (winners !== 1) note('KRITIK', 'Duzenleme kilidi', `Ayni surume ${winners} yazma BASARILI oldu — kaybolan guncelleme riski (1 olmaliydi).`);
  if (winners + conflicts !== machines.length) note('ORTA', 'Duzenleme kilidi', `Beklenmeyen yanitlar: ${machines.length - winners - conflicts} adet.`);
}

// ── Rapor ─────────────────────────────────────────────────────────────────
const pct = (arr, p) => arr.length ? arr.slice().sort((a, b) => a - b)[Math.min(arr.length - 1, Math.floor(arr.length * p))] : 0;
const all = lat.map(x => x.ms);
const writes = lat.filter(x => !x.op.startsWith('liste')).map(x => x.ms);
const reads = lat.filter(x => x.op.startsWith('liste')).map(x => x.ms);

console.log(`\n─── SONUC ───`);
console.log(`Sure: ${elapsed.toFixed(1)}s | Istek: ${lat.length} | ~${(lat.length / elapsed).toFixed(1)} istek/sn`);
console.log(`Gecikme (ms)  tumu p50=${pct(all, .5).toFixed(0)} p95=${pct(all, .95).toFixed(0)} max=${Math.max(...all).toFixed(0)}`);
console.log(`   yazma      p50=${pct(writes, .5).toFixed(0)} p95=${pct(writes, .95).toFixed(0)}`);
console.log(`   okuma      p50=${pct(reads, .5).toFixed(0)} p95=${pct(reads, .95).toFixed(0)}`);
console.log(`HTTP: ${[...statusCount.entries()].sort().map(([k, v]) => `${k}=${v}`).join('  ')}`);
console.log(`Ortak kayit: ${sharedWrites.length} yazma basarili, ${sharedBlocked.length} kilitle engellendi`);

// En yavaş 5 işlem (darboğaz adayı)
const byOp = new Map();
for (const x of lat) { if (!byOp.has(x.op)) byOp.set(x.op, []); byOp.get(x.op).push(x.ms); }
const slow = [...byOp.entries()].map(([op, a]) => ({ op, n: a.length, p95: pct(a, .95) })).sort((a, b) => b.p95 - a.p95).slice(0, 5);
console.log(`\nEn yavas islemler (p95 ms):`);
for (const s of slow) console.log(`   ${s.p95.toFixed(0)} ms  ${s.op}  (${s.n} istek)`);

console.log(`\n─── BULGULAR (${findings.length}) ───`);
if (!findings.length) console.log('Mantik hatasi bulunamadi.');
else {
  const seen = new Map();
  for (const f of findings) {
    const k = `${f.sev}|${f.screen}|${f.msg.slice(0, 90)}`;
    seen.set(k, (seen.get(k) || 0) + 1);
  }
  for (const [k, n] of [...seen.entries()].sort()) {
    const [sev, screen, msg] = k.split('|');
    console.log(`[${sev}] ${screen}: ${msg}${n > 1 ? `  (x${n})` : ''}`);
  }
}
console.log('');
