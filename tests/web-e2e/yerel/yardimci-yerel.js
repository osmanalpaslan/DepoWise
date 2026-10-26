// ⭐ M-69 — yerel kayıt testleri için yardımcılar. Adresler yalnız localhost (playwright.yerel.config.js denetler).
const { expect } = require('@playwright/test');
const { girisYap } = require('../tests/yardimci.js');

const API = process.env.DEPOWISE_E2E_API;
const SIFRE = process.env.DEPOWISE_E2E_SIFRE || 'E2eYerel!2026';
const FIRMA = 'DEPOWISE';          // sunucunun boş veritabanında kurduğu tohum firma
const KULLANICI = 'admin';         // tohum firma yöneticisi (ilk girişte şifre belirlenir)

async function apiGiris() {
  const r = await fetch(API + '/api/auth/login', {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: KULLANICI, password: SIFRE, companyId: FIRMA }),
  });
  expect(r.ok, 'API girişi').toBeTruthy();
  return (await r.json()).token;
}

async function api(yol, govde, yontem) {
  const token = await apiGiris();
  const r = await fetch(API + yol, {
    method: yontem || (govde ? 'POST' : 'GET'),
    headers: { 'Content-Type': 'application/json', Authorization: 'Bearer ' + token },
    body: govde ? JSON.stringify(govde) : undefined,
  });
  const metin = await r.text();
  if (!r.ok) throw new Error(`${yol} → ${r.status} ${metin}`);
  return metin ? JSON.parse(metin) : null;
}

/** Boş veritabanına bir kez: ilk giriş (şifre belirleme) + şube/personel tohumu. Tekrar çağrılırsa mevcut olanı döndürür. */
async function tohumla(browser) {
  const sayfa = await browser.newPage();
  await girisYap(sayfa, KULLANICI, SIFRE);   // ilk girişte "Yeni Şifre Belirleyin" adımını aynı şifreyle tamamlar
  await sayfa.close();
  const subeler = await api('/api/branches');
  let depo = subeler.find(b => b.name === 'E2E Depo');
  if (!depo) depo = { id: (await api('/api/branches', { name: 'E2E Depo' })).id };
  let santiye = subeler.find(b => b.name === 'E2E Şantiye');
  if (!santiye) santiye = { id: (await api('/api/branches', { name: 'E2E Şantiye' })).id };
  const personel = await api('/api/personnel');
  let ali = (personel.items ?? personel).find(p => p.fullName === 'Ali Depocu' && p.branchId === depo.id);
  if (!ali) ali = { id: (await api('/api/personnel', { fullName: 'Ali Depocu', title: null, phone: null, branchId: depo.id })).id };
  // Boş veritabanında birim tanımı yok → "Adet" (yeni malzeme formunda "Birim *" zorunlu).
  const birimler = await api('/api/lookups/units');
  if (!birimler.some(u => u.name === 'Adet')) await api('/api/lookups/units', { name: 'Adet' });
  return { depo: depo.id, santiye: santiye.id, personel: ali.id };
}

/** Web'e şube seçerek giriş (giriş sayfasındaki şube kutusundan adıyla). */
async function subeyleGiris(page, subeAdi) {
  await page.addInitScript(() => { try { sessionStorage.setItem('dw_acilis', '1'); } catch (e) {} });
  await page.goto('/login');
  await page.waitForLoadState('networkidle');
  const kAdi = page.getByPlaceholder('Kullanıcı adı');
  await kAdi.click(); await expect(kAdi).toBeEditable(); await kAdi.fill(KULLANICI);
  const parola = page.locator('input[type=password]').first();
  await parola.click(); await expect(parola).toBeEditable(); await parola.fill(SIFRE);
  await page.locator('button.dw-btn').first().click();
  const kutu = page.locator('select.dw-select:visible').first();
  await kutu.waitFor({ timeout: 30_000 });
  await kutu.selectOption({ label: subeAdi });
  await page.locator('button.dw-btn:visible').first().click();
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 30_000 });
  const yenilik = page.locator('.mud-dialog', { hasText: 'Yenilikler:' });
  if (await yenilik.waitFor({ timeout: 5_000 }).then(() => true).catch(() => false)) {
    await yenilik.getByRole('button', { name: 'Tamam' }).click();
    await expect(yenilik).toBeHidden();
  }
}

/** MudSelect: etiketine göre aç, seçeneği metniyle seç. */
async function secim(page, etiket, secenek) {
  const alan = page.locator('.mud-input-control', { has: page.locator('label', { hasText: etiket }) }).first();
  await alan.locator('.mud-select-input, .mud-input-slot, input').first().click();
  await page.locator('.mud-popover-open .mud-list-item', { hasText: secenek }).first().click();
  await expect(page.locator('.mud-popover-open .mud-list-item')).toHaveCount(0);
}

/** MudTextField/NumericField: etiketine göre doldur. */
async function yaz(page, etiket, deger) {
  const alan = page.locator('.mud-input-control', { has: page.locator('label', { hasText: etiket }) }).first().locator('input, textarea').first();
  await alan.click();
  await alan.fill(String(deger));
  await alan.press('Tab');
}

/** Onay penceresini (Dialog.Confirm) olumlu düğmeyle kapatır. */
async function onayla(page, dugme) {
  const pencere = page.locator('.mud-dialog').last();
  await pencere.waitFor();
  await pencere.getByRole('button', { name: dugme }).click();
  await expect(page.locator('.mud-dialog')).toHaveCount(0, { timeout: 15_000 });
}

module.exports = { api, tohumla, subeyleGiris, secim, yaz, onayla, FIRMA };
