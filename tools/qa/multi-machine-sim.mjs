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
// ── FIN-01 (FINAL stabilizasyon): koruma GUCLENDIRILDI, gevsetilmedi ──
// Simulasyon YALNIZ yerel hedefte calisir. Uzak/prod/Neon/fly hostlari yapisal olarak reddedilir;
// yanlislikla gercek bir adres verilirse program BASLAMADAN durur (canliya tek istek bile gitmez).
try {
  const host = new URL(API).hostname.toLowerCase();
  const yerel = host === 'localhost' || host === '127.0.0.1' || host === '::1' || host === '[::1]';
  if (!yerel) {
    console.error(`RED: simulasyon yalniz YEREL hedefte calisir (localhost/127.0.0.1). Verilen host: ${host}`);
    process.exit(2);
  }
} catch {
  console.error('RED: API adresi cozumlenemedi: ' + API);
  process.exit(2);
}
if (/fly\.dev|neon\.tech/i.test(API)) {
  console.error('RED: uzak/prod host tespit edildi — simulasyon durduruldu.');
  process.exit(2);
}
// FINAL tohum modu: SIM_SEED=7500 gibi bir toplam verilirse simulasyondan ONCE sentetik veri uretilir.
const SEED_TOTAL = parseInt(process.env.SIM_SEED || '0', 10);
// Kosuya ozgu operation-id oneki: GERCEK istemciler GUID uretir; deterministik id'ler art arda
// kosularda (firma-ustu UNIQUE op-id semasi yuzunden — FIN-B1) sahte "zaten islendi" uretiyordu.
const RUN = Date.now().toString(36);

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

/** FIN-01: ikili (xlsx/png) indiren uclar icin cagri — icerik dogrulamasi sihirli baytlarla yapilir. */
async function callBin(token, path, op = path) {
  const t0 = performance.now();
  try {
    const resp = await fetch(API + path, { headers: token ? { Authorization: 'Bearer ' + token } : {} });
    const buf = new Uint8Array(await resp.arrayBuffer());
    lat.push({ op, ms: performance.now() - t0, status: resp.status });
    bump(statusCount, String(resp.status)); bump(opCount, op);
    if (resp.status >= 500) note('KRITIK', op, `HTTP ${resp.status} (sunucu hatasi)`);
    return { status: resp.status, bytes: buf };
  } catch (e) {
    lat.push({ op, ms: performance.now() - t0, status: 0 });
    bump(statusCount, 'AG-HATASI');
    note('YUKSEK', op, 'Ag hatasi: ' + e.message);
    return { status: 0, bytes: new Uint8Array(0) };
  }
}
const pngMi = b => b.length > 8 && b[0] === 0x89 && b[1] === 0x50 && b[2] === 0x4E && b[3] === 0x47;
const xlsxMi = b => b.length > 4 && b[0] === 0x50 && b[1] === 0x4B;   // 'PK' zip imzasi

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
  machines.push({ id: username, token: s.token, branchId, actor: p.json?.id, created: { materials: [], vehicles: [], personnel: [], equipment: [] } });
}
console.log(`Kurulum tamam: firma=${companyName}, ${branchIds.length} sube, ${machines.length} makine\n`);

// Ortak tanımlar (tek sefer)
const M0 = machines[0].token;
const unit = (await call(M0, 'POST', '/api/lookups/units', { name: 'Adet' }, 'tanim-ekle')).json?.id;
const cat = (await call(M0, 'POST', '/api/lookups/material_categories', { name: 'Genel' }, 'tanim-ekle')).json?.id;
const maintDef = (await call(M0, 'POST', '/api/maintenance/definitions', { name: 'Periyodik Bakim', intervalValue: 10000, intervalUnit: 'km' }, 'bakim-tanim')).json?.id;

// Ortak "sıcak kayıt" — çakışma senaryosu için herkesin düzenleyeceği kayıt
const sharedMat = (await call(M0, 'POST', '/api/materials', { code: 'ORTAK-001', name: 'Ortak Malzeme', unitId: unit, categoryId: cat }, 'malzeme-olustur')).json?.id;

// ── FIN-01 KURULUM EKLERİ (FAZ 1-5 modulleri + guvenlik problari) ─────────
// (1) IKINCI TENANT — tenant sizinti probu icin isaretli kayit: A firmasinin hicbir kullanicisi
//     bu kaydi liste/arama/detay yoluyla GOREMEMELI.
const TENANT_MARKER = 'TENANT-B-GIZLI-' + Date.now();
let tenantBMatId = null;
{
  const compB = await call(SU, 'POST', '/api/companies', { name: 'SIM-B ' + Date.now(), maxUsers: 20, maxAdmins: 5, machineQuota: 5 }, 'firmaB-olustur');
  const cB = compB.json?.id;
  if (cB) {
    // Is kurallari: firmada kullanici acmadan ONCE en az bir sube olmali VE kullaniciya sube secilmeli.
    const bB = await call(SU, 'POST', '/api/branches', { name: 'B Merkez', kind: 'branch', companyId: cB }, 'subeB-olustur');
    const un = 'simb' + rnd(10000, 99999), pw = 'Sim-B-Sifre-1';
    const cu = await call(SU, 'POST', '/api/users', { username: un, password: pw, fullName: 'Sim B Admin', roleKeys: ['role-company-admin'], companyId: cB, branchId: bB.json?.id, canViewAllBranches: true }, 'kullaniciB-olustur');
    if (cu.status === 200) {
      const sB = await login(un, pw);
      if (sB?.token) {
        const ub = (await call(sB.token, 'POST', '/api/lookups/units', { name: 'Adet' }, 'tanim-ekle')).json?.id;
        tenantBMatId = (await call(sB.token, 'POST', '/api/materials', { code: TENANT_MARKER, name: 'B Firmasinin Gizli Kaydi', unitId: ub }, 'malzemeB-olustur')).json?.id;
      }
    }
  }
  if (!tenantBMatId) note('ORTA', 'Kurulum', 'Tenant-B isaretli kaydi olusturulamadi — tenant probu kismi.');
}

// (2) YETKISIZ PERSONEL — hicbir modul yetkisi olmayan role-staff kullanicisi (yetki kapisi probu).
let staffToken = null;
{
  const un = 'simstaff' + rnd(10000, 99999), pw = 'Sim-Staff-Sifre-1';
  const cu = await call(SU, 'POST', '/api/users', { username: un, password: pw, fullName: 'Sim Yetkisiz', roleKeys: ['role-staff'], companyId, branchId: branchIds[0] }, 'staff-olustur');
  if (cu.status === 200) staffToken = (await login(un, pw))?.token ?? null;
  if (!staffToken) note('ORTA', 'Kurulum', 'Yetkisiz staff kullanicisi kurulamadi — yetki problari kismi.');
}

// (3) Ortak maliyet merkezi + IE/siparis icin sira sayaci.
const sharedCC = (await call(M0, 'POST', '/api/cost-centers', { name: 'Sim Maliyet Merkezi', code: 'MM-SIM' }, 'maliyet-merkezi')).json?.id ?? null;
let woSeq = 0, poSeq = 0;

// ── Bir makinenin insan gibi davranışı ────────────────────────────────────
async function humanRound(m, round) {
  const tag = `${m.id}-${round}`;
  // FIN-01: FAZ 1-5 modulleri havuza EKLENDI (eski davranislar aynen korunur).
  const action = pick(['malzeme', 'malzeme', 'arac', 'personel', 'stok', 'yakit', 'faaliyet', 'liste', 'liste', 'duzenle',
    'ekipman', 'zimmet', 'isemri', 'satinalma', 'takvim', 'duyuru', 'arama', 'pano', 'excel', 'qr']);

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

  // ── FIN-01: FAZ 1-5 MODUL SENARYOLARI (eklemeli) ────────────────────────

  else if (action === 'ekipman') {
    const code = `E-${m.id}-${round}`;
    const r = await call(m.token, 'POST', '/api/equipment', { code, name: 'Ekipman ' + tag, serialNo: 'SN-' + tag }, 'ekipman-olustur');
    if (r.status === 200 && r.json?.id) m.created.equipment.push({ id: r.json.id, code });
    else if (r.status !== 200) note('ORTA', 'Ekipman', `Yeni ekipman reddedildi (${r.status}): ${r.text.slice(0, 120)}`);
    await think();
    // Ayni kod TEKRAR → REDDEDILMELI (mukerrer korumasi — malzeme deseninin aynisi)
    const dup = await call(m.token, 'POST', '/api/equipment', { code, name: 'Mukerrer ' + tag }, 'ekipman-mukerrer');
    if (dup.status === 200) note('KRITIK', 'Ekipman', `MUKERRER EKIPMAN KODU KABUL EDILDI: "${code}"`);
  }

  else if (action === 'zimmet') {
    const e = pick(m.created.equipment);
    if (!e || !m.actor) return;
    // IDEMPOTENCY: ayni operationId ile CIFT gonderim → tek islem kalmali (retry ikinci hareket uretmez).
    const opId = `sim-zmt-${RUN}-${tag}`;
    const i1 = await call(m.token, 'POST', '/api/assignments/issue',
      { assetType: 'equipment', assetId: e.id, personnelId: m.actor, quantity: 1, operationId: opId, branchId: m.branchId }, 'zimmet-ver');
    if (i1.status !== 200) { note('ORTA', 'Zimmet', `Teslim reddedildi (${i1.status}): ${i1.text.slice(0, 120)}`); return; }
    await call(m.token, 'POST', '/api/assignments/issue',
      { assetType: 'equipment', assetId: e.id, personnelId: m.actor, quantity: 1, operationId: opId, branchId: m.branchId }, 'zimmet-ver-tekrar');
    // NOT: holdings 'search' parametresi varlik ETIKETINE (ada) bakar, koda degil → assetId ile suzeriz.
    const h = await call(m.token, 'GET', '/api/assignments/holdings?assetType=equipment', undefined, 'zimmet-liste');
    const rows = Array.isArray(h.json) ? h.json.filter(x => x.assetId === e.id) : [];
    const qty = rows.length === 1 ? parseFloat(String(rows[0].quantity).replace(',', '.')) : rows.length;
    if (rows.length !== 1 || !(qty === 1)) note('KRITIK', 'Zimmet', `Idempotent retry BOZUK: ${e.code} icin beklenen 1 zimmet, gorunen ${rows.length} satir / miktar ${qty}.`);
    await think();
    const ret = await call(m.token, 'POST', '/api/assignments/return',
      { assetType: 'equipment', assetId: e.id, personnelId: m.actor, quantity: 1, operationId: `sim-iade-${RUN}-${tag}`, branchId: m.branchId }, 'zimmet-iade');
    if (ret.status !== 200 && ret.status !== 400) note('ORTA', 'Zimmet', `Iade beklenmedik (${ret.status}): ${ret.text.slice(0, 120)}`);
  }

  else if (action === 'isemri') {
    const woNo = `IE-SIM-${++woSeq}-${m.id}`;
    const gecmis = Date.now() - rnd(1, 20) * 86400000;   // bazen GECIKMIS is emri (dashboard/bildirim beslemesi)
    const r = await call(m.token, 'POST', '/api/work-orders', {
      woNo, title: 'Is Emri ' + tag, branchId: m.branchId, costCenterId: sharedCC ?? undefined,
      priority: pick(['normal', 'high', 'urgent']), plannedEnd: round % 3 === 0 ? gecmis : Date.now() + rnd(1, 30) * 86400000,
    }, 'isemri-olustur');
    if (r.status !== 200) { note('ORTA', 'Is Emirleri', `IE reddedildi (${r.status}): ${r.text.slice(0, 120)}`); return; }
    const id = r.json?.id;
    await think();
    if (id && round % 2 === 0) {
      const st = await call(m.token, 'POST', `/api/work-orders/${id}/status`, { status: 'in_progress' }, 'isemri-durum');
      if (st.status !== 200 && st.status !== 400) note('ORTA', 'Is Emirleri', `Durum gecisi beklenmedik (${st.status}): ${st.text.slice(0, 120)}`);
    }
  }

  else if (action === 'satinalma') {
    const mat = pick(m.created.materials);
    if (!mat) return;
    const r = await call(m.token, 'POST', '/api/purchasing', {
      orderNo: `PO-SIM-${++poSeq}-${m.id}`, branchId: m.branchId,
      lines: [{ materialId: mat, quantity: 10, unitPrice: 7.5 }],
    }, 'siparis-olustur');
    if (r.status !== 200) { note('ORTA', 'Satin Alma', `Siparis reddedildi (${r.status}): ${r.text.slice(0, 120)}`); return; }
    const poId = r.json?.id;
    const lines = await call(m.token, 'GET', `/api/purchasing/${poId}/lines`, undefined, 'siparis-kalem');
    const lineId = Array.isArray(lines.json) ? lines.json[0]?.id : null;
    if (!lineId) return;
    // IDEMPOTENCY: ayni operationId ile CIFT mal kabul → stok/received IKI KEZ islenmemeli.
    const opId = `sim-rcv-${RUN}-${tag}`;
    const rc1 = await call(m.token, 'POST', `/api/purchasing/${poId}/receive`, { lines: [{ lineId, quantity: 4 }], operationId: opId }, 'mal-kabul');
    if (rc1.status !== 200) { note('ORTA', 'Satin Alma', `Mal kabul reddedildi (${rc1.status}): ${rc1.text.slice(0, 120)}`); return; }
    await call(m.token, 'POST', `/api/purchasing/${poId}/receive`, { lines: [{ lineId, quantity: 4 }], operationId: opId }, 'mal-kabul-tekrar');
    const lines2 = await call(m.token, 'GET', `/api/purchasing/${poId}/lines`, undefined, 'siparis-kalem');
    const rec = Array.isArray(lines2.json) ? parseFloat(String(lines2.json[0]?.receivedQty ?? '0').replace(',', '.')) : -1;
    if (rec !== 4) note('KRITIK', 'Satin Alma', `Idempotent mal kabul BOZUK: beklenen teslim 4, gorunen ${rec} (ayni operationId iki kez islendi?).`);
  }

  else if (action === 'takvim') {
    const r = await call(m.token, 'POST', '/api/calendar/events',
      { title: 'Etkinlik ' + tag, startDate: Date.now() + rnd(-5, 20) * 86400000, branchId: m.branchId }, 'takvim-olustur');
    if (r.status !== 200) note('ORTA', 'Takvim', `Etkinlik reddedildi (${r.status}): ${r.text.slice(0, 120)}`);
    await think();
    const from = Date.now() - 30 * 86400000, to = Date.now() + 30 * 86400000;
    const g = await call(m.token, 'GET', `/api/calendar?from=${from}&to=${to}`, undefined, 'takvim-goruntule');
    if (g.status !== 200) note('YUKSEK', 'Takvim', `Takvim okunamadi (${g.status}).`);
  }

  else if (action === 'duyuru') {
    // Bazen pencere DISI (pasif) duyuru — aktiflik turetilir; pasif olan yonetici-disina gorunmemeli.
    const pasif = round % 4 === 0;
    const r = await call(m.token, 'POST', '/api/announcements', {
      title: 'Duyuru ' + tag, body: 'Sim duyurusu', importance: round % 3 === 0 ? 'important' : 'normal',
      branchId: round % 5 === 0 ? m.branchId : undefined,
      publishStart: pasif ? Date.now() - 10 * 86400000 : undefined,
      publishEnd: pasif ? Date.now() - 5 * 86400000 : undefined,
    }, 'duyuru-olustur');
    if (r.status !== 200) note('ORTA', 'Duyurular', `Duyuru reddedildi (${r.status}): ${r.text.slice(0, 120)}`);
    const g = await call(m.token, 'GET', '/api/announcements', undefined, 'duyuru-liste');
    if (g.status !== 200) note('YUKSEK', 'Duyurular', `Duyuru listesi okunamadi (${g.status}).`);
  }

  else if (action === 'arama') {
    // Kendi kaydini TAM kodla bul + baska firmanin isaretli kaydini ASLA bulma.
    const mat = pick(m.created.materials);
    if (mat) {
      const kendi = await call(m.token, 'GET', `/api/materials/${mat}`, undefined, 'malzeme-detay');
      const code = kendi.json?.code;
      if (code) {
        const s1 = await call(m.token, 'GET', `/api/search?q=${encodeURIComponent(code)}`, undefined, 'arama');
        const bulundu = Array.isArray(s1.json) && s1.json.some(gr => (gr.hits ?? []).some(h => h.label === code || h.subLabel === code));
        if (s1.status === 200 && !bulundu) note('YUKSEK', 'Global Arama', `Kendi kaydi TAM kodla bulunamadi: ${code}`);
      }
    }
    const s2 = await call(m.token, 'GET', `/api/search?q=${encodeURIComponent(TENANT_MARKER)}`, undefined, 'arama-tenant');
    if (Array.isArray(s2.json) && s2.json.some(gr => (gr.hits ?? []).length > 0))
      note('KRITIK', 'Tenant', `ARAMA BASKA FIRMANIN KAYDINI DONDURDU: ${TENANT_MARKER}`);
  }

  else if (action === 'pano') {
    const d = await call(m.token, 'GET', '/api/dashboard', undefined, 'dashboard');
    if (d.status !== 200) note('YUKSEK', 'Dashboard', `Dashboard okunamadi (${d.status}).`);
    const c = await call(m.token, 'GET', '/api/alerts/count', undefined, 'bildirim-sayac');
    if (c.status !== 200) note('YUKSEK', 'Bildirim', `Bildirim sayaci okunamadi (${c.status}).`);
    if (round % 6 === 0) {
      const ra = await call(m.token, 'POST', '/api/alerts/read-all', {}, 'bildirim-tumu-okundu');
      if (ra.status !== 200) note('ORTA', 'Bildirim', `Tumunu-okundu beklenmedik (${ra.status}).`);
    }
  }

  else if (action === 'excel') {
    const src = pick(['materials', 'vehicles', 'equipment', 'work-orders', 'announcements']);
    const r = await callBin(m.token, `/api/export/${src}`, 'excel-export');
    if (r.status === 200 && !xlsxMi(r.bytes)) note('YUKSEK', 'Excel Merkezi', `Export ${src}: yanit gecerli XLSX degil.`);
    else if (r.status !== 200) note('ORTA', 'Excel Merkezi', `Export ${src} reddedildi (${r.status}).`);
  }

  else if (action === 'qr') {
    const mat = pick(m.created.materials);
    if (!mat) return;
    const r = await callBin(m.token, `/api/qr/materials/${mat}`, 'qr-uret');
    if (r.status === 200 && !pngMi(r.bytes)) note('YUKSEK', 'Barkod/QR', 'QR yaniti gecerli PNG degil.');
    else if (r.status !== 200) note('ORTA', 'Barkod/QR', `QR reddedildi (${r.status}).`);
  }
}

// ── FIN-01 TOHUM (S3): ~SIM_SEED sentetik kayit — TAMAMI API/servis uzerinden (ham SQL yok) ──────
// Gercek dagilim: cogunluk malzeme+stok hareketi; kenar durumlar (pasif/pencere-disi/sube-hedefli/
// silinmis/kritik-stok/Turkce karakter) bilerek uretilir. Yalniz IZOLE yerel hedefte calisir (ust guard).
const seedCounts = {};
async function bulk(n, ad, fn) {
  let ok = 0, fail = 0;
  for (let i = 0; i < n; i += 10) {
    const grup = [];
    for (let j = i; j < Math.min(i + 10, n); j++) grup.push(fn(j).then(r => { r ? ok++ : fail++; }).catch(() => { fail++; }));
    await Promise.all(grup);
  }
  seedCounts[ad] = ok;
  if (fail > 0) note('ORTA', 'Tohum', `${ad}: ${fail}/${n} kayit uretilemedi.`);
  return ok;
}

if (SEED_TOTAL > 0) {
  console.log(`Tohum uretimi basliyor (~${SEED_TOTAL} kayit)...`);
  const tk = i => machines[i % machines.length];   // kayitlar makinelere/subelere dagitilir
  const N = p => Math.max(1, Math.round(SEED_TOTAL * p));
  const seedMats = [], seedVehicles = [], seedEquip = [];
  const trAd = ['Çimento', 'İnşaat Demiri Ø12', 'Sıva Harcı', 'Boya — Dış Cephe', 'Şantiye Güvenlik Ağı', 'Öğütülmüş Mıcır'];

  // Ek subeler (toplam ~20): kapsam kombinasyonlari icin.
  for (let i = branchIds.length; i < 20; i++) {
    const b = await call(SU, 'POST', '/api/branches', { name: `Sim Şantiye ${i + 1}`, kind: i % 2 ? 'site' : 'branch', companyId }, 'sube-olustur');
    if (b.json?.id) branchIds.push(b.json.id);
  }
  seedCounts['sube'] = branchIds.length;

  await bulk(N(.44), 'malzeme', async i => {
    const r = await call(tk(i).token, 'POST', '/api/materials', {
      code: `SEED-M-${i}`, name: `${pick(trAd)} ${i}`, unitId: unit, categoryId: cat,
      minStock: i % 7 === 0 ? 50 : undefined,   // kritik stok adaylari
    }, 'tohum-malzeme');
    if (r.json?.id) seedMats.push(r.json.id);
    return r.status === 200;
  });
  await bulk(N(.05), 'arac', async i => {
    const r = await call(tk(i).token, 'POST', '/api/vehicles', {
      internalCode: `SEED-A-${i}`, plate: i % 3 ? `42 SIM ${100 + i}` : undefined, productionYear: rnd(2005, 2025),
      currentMeter: rnd(0, 400000), meterUnit: i % 5 ? 'km' : 'saat', branchId: tk(i).branchId, status: i % 9 === 0 ? 'passive' : 'active',
    }, 'tohum-arac');
    if (r.json?.id) seedVehicles.push(r.json.id);
    return r.status === 200;
  });
  await bulk(N(.04), 'personel', async i =>
    (await call(tk(i).token, 'POST', '/api/personnel', {
      fullName: `Sim Personel ${i} ĞÜŞİÖÇ`, title: pick(['Operatör', 'Şoför', 'Depocu', 'Şef']),
      branchId: i % 6 === 0 ? undefined : tk(i).branchId, isActive: i % 11 !== 0,
    }, 'tohum-personel')).status === 200);
  await bulk(N(.03), 'ekipman', async i => {
    const r = await call(tk(i).token, 'POST', '/api/equipment', {
      code: `SEED-E-${i}`, name: `Jeneratör ${i}`, serialNo: `SN${1000 + i}`, branchId: tk(i).branchId,
      status: i % 8 === 0 ? 'maintenance' : 'active',
    }, 'tohum-ekipman');
    if (r.json?.id) seedEquip.push(r.json.id);
    return r.status === 200;
  });
  // Stok hareketleri: mal kabul (+bazen cikis) — defter buyur, bakiye kombinasyonlari olusur.
  await bulk(N(.20), 'stok-hareket', async i => {
    const m = tk(i);
    const rec = await call(m.token, 'POST', '/api/stock/receive', {
      code: `SEED-S-${i}`, name: `Stoklu Malzeme ${i}`, unitId: unit, categoryId: cat,
      quantity: rnd(10, 500), unitPrice: rnd(1, 90), branchId: m.branchId, personnelId: m.actor,
    }, 'tohum-stok-giris');
    const matId = rec.json?.materialId ?? rec.json?.id;
    if (matId && i % 2 === 0)
      await call(m.token, 'POST', '/api/stock/issue', { materialId: matId, quantity: rnd(1, 9), branchId: m.branchId, personnelId: m.actor }, 'tohum-stok-cikis');
    return rec.status === 200;
  });
  await bulk(N(.03), 'yakit', async i => {
    const m = tk(i);
    const dep = await call(m.token, 'POST', '/api/fuel/depot', { liters: rnd(200, 2000), unitPrice: 40 + (i % 10) }, 'tohum-yakit-depo');
    const v = seedVehicles[i % Math.max(1, seedVehicles.length)];
    if (v && dep.status === 200)
      await call(m.token, 'POST', '/api/fuel/distribute', { vehicleId: v, liters: rnd(10, 80), currentMeter: 400000 + i * 7 }, 'tohum-yakit-dagitim');
    return dep.status === 200;
  });
  await bulk(N(.02), 'isemri', async i => {
    const m = tk(i);
    const r = await call(m.token, 'POST', '/api/work-orders', {
      woNo: `SEED-IE-${i}`, title: `Bakım İş Emri ${i}`, branchId: m.branchId, costCenterId: sharedCC ?? undefined,
      priority: pick(['normal', 'normal', 'high', 'urgent']),
      plannedEnd: i % 4 === 0 ? Date.now() - rnd(1, 15) * 86400000 : Date.now() + rnd(1, 45) * 86400000,
    }, 'tohum-isemri');
    if (r.json?.id && i % 3 === 0) await call(m.token, 'POST', `/api/work-orders/${r.json.id}/status`, { status: 'in_progress' }, 'tohum-isemri-durum');
    return r.status === 200;
  });
  await bulk(N(.015), 'siparis', async i => {
    const m = tk(i);
    const mat = seedMats[i % Math.max(1, seedMats.length)];
    if (!mat) return false;
    const r = await call(m.token, 'POST', '/api/purchasing', {
      orderNo: `SEED-PO-${i}`, branchId: m.branchId, lines: [{ materialId: mat, quantity: rnd(5, 50), unitPrice: rnd(2, 40) }],
    }, 'tohum-siparis');
    return r.status === 200;
  });
  await bulk(N(.015), 'takvim', async i =>
    (await call(tk(i).token, 'POST', '/api/calendar/events', {
      title: `Planlı İş ${i}`, startDate: Date.now() + rnd(-20, 40) * 86400000,
      branchId: i % 3 === 0 ? tk(i).branchId : undefined,
    }, 'tohum-takvim')).status === 200);
  await bulk(N(.006), 'duyuru', async i =>
    (await call(tk(i).token, 'POST', '/api/announcements', {
      title: `Sim Duyuru ${i}`, body: 'Tohum duyurusu — ĞÜŞİÖÇ', importance: i % 3 === 0 ? 'important' : 'normal',
      branchId: i % 4 === 0 ? tk(i).branchId : undefined,
      publishStart: i % 5 === 0 ? Date.now() - 9 * 86400000 : undefined,
      publishEnd: i % 5 === 0 ? Date.now() - 2 * 86400000 : undefined,   // pencere DISI → pasif
    }, 'tohum-duyuru')).status === 200);
  await bulk(N(.01), 'zimmet', async i => {
    const m = tk(i);
    const e = seedEquip[i % Math.max(1, seedEquip.length)];
    if (!e || !m.actor) return false;
    return (await call(m.token, 'POST', '/api/assignments/issue', {
      assetType: 'equipment', assetId: e, personnelId: m.actor, quantity: 1, operationId: `seed-zmt-${RUN}-${i}`, branchId: m.branchId,
    }, 'tohum-zimmet')).status === 200;
  });
  await bulk(N(.005), 'proje', async i =>
    (await call(tk(i).token, 'POST', '/api/projects', {
      name: `Sim Proje ${i}`, branchIds: i % 2 === 0 ? [tk(i).branchId] : undefined,
    }, 'tohum-proje')).status === 200);
  await bulk(20, 'maliyet-merkezi', async i =>
    (await call(M0, 'POST', '/api/cost-centers', { name: `Sim MM ${i}`, code: `SMM-${i}`, status: i % 6 === 0 ? 'Pasif' : 'Aktif' }, 'tohum-mm')).status === 200);
  // Soft-delete kenar durumu: uretilen malzemelerin ~%1'i silinir (Cop Kutusu'na gider; arama/exportta gorunmemeli).
  await bulk(Math.max(5, N(.01)), 'silinmis', async i => {
    const id = seedMats[(i * 37) % Math.max(1, seedMats.length)];
    if (!id) return false;
    return (await call(M0, 'DELETE', `/api/materials/${id}`, undefined, 'tohum-sil')).status === 200;
  });
  const toplam = Object.entries(seedCounts).map(([k, v]) => `${k}=${v}`).join('  ');
  console.log(`Tohum tamam: ${toplam}  (toplam ~${Object.values(seedCounts).reduce((a, b) => a + b, 0)})\n`);
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

// ── FIN-01 GUVENLIK PROBLARI: yetki / tenant / public-read / soft-delete / salt-okunurluk ────────
console.log('\nGuvenlik problari calisiyor...');
{
  const anyMat = machines.flatMap(m => m.created.materials)[0] ?? sharedMat;

  // (a) YETKISIZ KULLANICI (role-staff, hicbir modul yetkisi yok) — kaynak kapilari API seviyesinde tutmali.
  if (staffToken) {
    const beklenen403 = [
      ['GET', '/api/materials?page=1&pageSize=10', 'yetki-malzeme'],
      ['GET', '/api/equipment', 'yetki-ekipman'],
      ['GET', '/api/work-orders', 'yetki-isemri'],
      ['GET', '/api/purchasing', 'yetki-siparis'],
    ];
    for (const [mth, p, op] of beklenen403) {
      const r = await call(staffToken, mth, p, undefined, op);
      if (r.status === 200) note('KRITIK', 'Yetki', `YETKISIZ kullanici veri OKUDU: ${p} → 200`);
      else if (r.status !== 403) note('ORTA', 'Yetki', `${p}: beklenen 403, gelen ${r.status}`);
    }
    if (anyMat) {
      const ex = await callBin(staffToken, '/api/export/materials', 'yetki-export');
      if (ex.status === 200) note('KRITIK', 'Excel Merkezi', 'YETKISIZ kullanici export INDIRDI (export+kaynak kapisi delindi).');
      const qr = await callBin(staffToken, `/api/qr/materials/${anyMat}`, 'yetki-qr');
      if (qr.status === 200) note('KRITIK', 'Barkod/QR', 'YETKISIZ kullanici QR uretti (kaynak kapisi delindi).');
    }
    const ara = await call(staffToken, 'GET', `/api/search?q=${encodeURIComponent('SEED-M-1')}`, undefined, 'yetki-arama');
    if (Array.isArray(ara.json) && ara.json.some(g => (g.hits ?? []).length > 0))
      note('KRITIK', 'Global Arama', 'YETKISIZ kullanici aramada kaynak verisi gordu.');
    // PUBLIC-READ istisnasi (PK-J1): duyuru OKUMA herkese acik OLMALI; YAZMA kapali kalmali.
    const dOku = await call(staffToken, 'GET', '/api/announcements', undefined, 'publicread-duyuru');
    if (dOku.status !== 200) note('YUKSEK', 'Duyurular', `Public-read BOZULDU: duyuru okuma ${dOku.status} dondu (200 olmali).`);
    const dYaz = await call(staffToken, 'POST', '/api/announcements', { title: 'Yetkisiz Deneme' }, 'publicread-duyuru-yaz');
    if (dYaz.status === 200) note('KRITIK', 'Duyurular', 'YETKISIZ kullanici DUYURU YAZDI (yazma kapisi delindi).');
    const pano = await call(staffToken, 'GET', '/api/dashboard', undefined, 'yetki-dashboard');
    if (pano.status !== 200) note('ORTA', 'Dashboard', `Yetkisiz kullanicida dashboard ${pano.status} (200+bos beklenir).`);
  }

  // (b) TENANT: A makinesi B'nin kaydini ID ile bile ACAMAMALI.
  if (tenantBMatId) {
    const r = await call(M0, 'GET', `/api/materials/${tenantBMatId}`, undefined, 'tenant-detay');
    if (r.status === 200) note('KRITIK', 'Tenant', 'BASKA FIRMANIN KAYDI ID ILE ACILDI (detay ucu tenant kapisi delindi).');
    const qr = await callBin(M0, `/api/qr/materials/${tenantBMatId}`, 'tenant-qr');
    if (qr.status === 200) note('KRITIK', 'Tenant', 'BASKA FIRMANIN KAYDINA QR URETILDI.');
  }

  // (c) SOFT-DELETE: silinen kayit arama/exporta SIZMAMALI (Cop Kutusu'nda kalir).
  {
    const kod = 'SIM-SIL-' + Date.now();
    const c = await call(M0, 'POST', '/api/materials', { code: kod, name: 'Silinecek Kayit', unitId: unit }, 'sil-olustur');
    if (c.json?.id) {
      await call(M0, 'DELETE', `/api/materials/${c.json.id}`, undefined, 'sil');
      const s = await call(M0, 'GET', `/api/search?q=${encodeURIComponent(kod)}`, undefined, 'sil-arama');
      if (Array.isArray(s.json) && s.json.some(g => (g.hits ?? []).length > 0))
        note('KRITIK', 'Cop Kutusu', `SILINMIS kayit aramada gorundu: ${kod}`);
    }
  }

  // (d) SALT-OKUNURLUK: arama+dashboard+export+QR firtinasi kaydi DEGISTIRMEMELI (surum sabit kalmali).
  if (sharedMat) {
    const v1 = (await call(M0, 'GET', `/api/materials/${sharedMat}`, undefined, 'saltokunur-once')).json?.version;
    for (let i = 0; i < 5; i++) {
      await call(M0, 'GET', '/api/search?q=ORTAK-001', undefined, 'saltokunur-arama');
      await call(M0, 'GET', '/api/dashboard', undefined, 'saltokunur-dashboard');
      await callBin(M0, '/api/export/materials', 'saltokunur-export');
      await callBin(M0, `/api/qr/materials/${sharedMat}`, 'saltokunur-qr');
      await call(M0, 'GET', '/api/announcements', undefined, 'saltokunur-duyuru');
    }
    const v2 = (await call(M0, 'GET', `/api/materials/${sharedMat}`, undefined, 'saltokunur-sonra')).json?.version;
    if (v1 !== v2) note('KRITIK', 'Salt-okunurluk', `Salt-okunur islemler kaydi DEGISTIRDI: surum ${v1} → ${v2}.`);
  }
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
