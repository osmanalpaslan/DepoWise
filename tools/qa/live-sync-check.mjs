#!/usr/bin/env node
// DepoWise — CANLI ESITLEME QA (§7). Gercek sunucuya karsi ucdan uca sozlesme kontrolu.
// Kullanim:  node tools/qa/live-sync-check.mjs
// Kimlik:    .env.test.local  (git'e GIRMEZ) -> DEPOWISE_TEST_USER / _PASS / _API
// Kural:     yalnizca TEST hesabi kullanilir; gercek yonetici hesaplari burada kullanilmaz.
// Cikti:     her kontrol icin GECTI/KALDI; parola asla ekrana basilmaz.

import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..', '..');
const envFile = path.join(root, '.env.test.local');
if (!fs.existsSync(envFile)) {
  console.error('HATA: .env.test.local yok. Test kullanicisini oraya yaz (bkz. CLAUDE.md 7.0.1).');
  process.exit(2);
}
const env = Object.fromEntries(
  fs.readFileSync(envFile, 'utf8').split(/\r?\n/)
    .filter(l => l.trim() && !l.trim().startsWith('#') && l.includes('='))
    .map(l => [l.slice(0, l.indexOf('=')).trim(), l.slice(l.indexOf('=') + 1).trim()]));

const API = (env.DEPOWISE_TEST_API || 'https://depowise-erp.fly.dev').replace(/\/$/, '');
const USER = env.DEPOWISE_TEST_USER, PASS = env.DEPOWISE_TEST_PASS;
if (!USER || !PASS) { console.error('HATA: DEPOWISE_TEST_USER/PASS eksik.'); process.exit(2); }

let pass = 0, fail = 0;
const check = (name, ok, detail = '') => {
  if (ok) { pass++; console.log(`  GECTI  ${name}${detail ? ' — ' + detail : ''}`); }
  else { fail++; console.log(`  KALDI  ${name}${detail ? ' — ' + detail : ''}`); }
};

const jwtBody = t => JSON.parse(Buffer.from(t.split('.')[1].replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8'));

console.log(`\nDepoWise canli esitleme QA — ${API}  (kullanici: ${USER})\n`);

// 1) Yetkisiz erisim reddedilmeli (guvenlik, §7.12)
const anon = await fetch(`${API}/api/sync/business-version`);
check('Tokensiz business-version reddedilir', anon.status === 401 || anon.status === 403, `HTTP ${anon.status}`);

// 2) Giris
const login = await fetch(`${API}/api/auth/login`, {
  method: 'POST', headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ username: USER, password: PASS })
});
if (!login.ok) {
  check('Test kullanicisi girisi', false, `HTTP ${login.status} — hesap yok/parola farkli`);
  console.log(`\nSONUC: ${pass} gecti, ${fail} kaldi\n`); process.exit(1);
}
const token = (await login.json()).token;
check('Test kullanicisi girisi', !!token);
const claims = jwtBody(token);
const companyId = claims.company; // JwtTokens.CompanyClaim = "company"
check('Token firma (tenant) tasiyor', !!companyId, `company=${companyId}`);
const auth = { Authorization: `Bearer ${token}` };

// 3) Surum ucuz tek sayi olmali
const verResp = await fetch(`${API}/api/sync/business-version`, { headers: auth });
const ver = verResp.ok ? (await verResp.json()).version : null;
check('business-version sayi doner', typeof ver === 'number', `version=${ver}`);

// 4) DELTA: since=version -> bos/kucuk olmali (bant israfi yok)
const dResp = await fetch(`${API}/api/sync/business-pull?since=${ver}`, { headers: auth });
const delta = dResp.ok ? await dResp.json() : null;
const countRows = snap => Object.values(snap?.tables ?? {}).reduce((a, r) => a + (Array.isArray(r) ? r.length : 0), 0);
check('Delta cekme (since=version) bos doner', dResp.ok && countRows(delta) === 0, `${countRows(delta)} satir`);

// 5) TAM snapshot + CANLI TENANT SIZINTI kontrolu (§7.12)
const fResp = await fetch(`${API}/api/sync/business-pull?since=0`, { headers: auth });
const full = fResp.ok ? await fResp.json() : null;
const total = countRows(full);
check('Tam snapshot cekilir', fResp.ok && total > 0, `${total} satir`);
const leaks = [];
for (const [t, rows] of Object.entries(full?.tables ?? {}))
  for (const r of (Array.isArray(rows) ? rows : []))
    if (r && r.company_id && r.company_id !== companyId) leaks.push(`${t}:${r.id}`);
check('Baska firma verisi SIZMIYOR', leaks.length === 0, leaks.length ? leaks.slice(0, 3).join(', ') : `${total} satir tarandi`);

// 6) Tablo ozeti (araclar canlida kaybolmustu — gorunur olsun)
const summary = Object.entries(full?.tables ?? {}).filter(([, r]) => Array.isArray(r) && r.length)
  .map(([t, r]) => `${t}=${r.length}`).sort();
console.log(`\n  Tablolar: ${summary.join('  ') || '(bos)'}`);

console.log(`\nSONUC: ${pass} gecti, ${fail} kaldi\n`);
process.exit(fail ? 1 : 0);
