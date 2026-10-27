// ⭐ M-01 (2026-10-27) — SÜPER ADMİN İKİ ADIMLI GİRİŞ, GERÇEK TARAYICIDA (yerel, boş veritabanı).
// Akış: süper admin web'e girer → (ilk şifre, özel kod) → QR'lı kurulum ekranı → uygulamanın kodu → kurtarma kodları (bir kez) →
// yeniden girişte "Doğrulama Kodu" adımı: yanlış kod reddedilir, doğru kodla girilir. Kod, ekranda gösterilen gizli anahtardan
// RFC 6238 ile üretilir (telefondaki uygulamanın yaptığının aynısı).
const { test, expect } = require('@playwright/test');
const crypto = require('node:crypto');

const KULLANICI = 'superadmin';
const SIFRE = process.env.DEPOWISE_E2E_SIFRE || 'E2eYerel!2026';

test.describe.configure({ mode: 'serial' });

function totp(gizliB32, kayma = 0) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let tampon = 0, bit = 0; const b = [];
  for (const c of gizliB32.replace(/\s/g, '').toUpperCase()) { tampon = (tampon << 5) | A.indexOf(c); bit += 5; if (bit >= 8) { b.push((tampon >> (bit - 8)) & 255); bit -= 8; } }
  let adim = BigInt(Math.floor(Date.now() / 30000) + kayma);
  const sayac = Buffer.alloc(8);
  for (let i = 7; i >= 0; i--) { sayac[i] = Number(adim & 0xffn); adim >>= 8n; }
  const h = crypto.createHmac('sha1', Buffer.from(b)).update(sayac).digest();
  const o = h[h.length - 1] & 15;
  return String((((h[o] & 127) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3]) % 1000000).padStart(6, '0');
}

async function kimlik(page) {
  await page.addInitScript(() => { try { sessionStorage.setItem('dw_acilis', '1'); } catch (e) {} });
  await page.goto('/login');
  await page.waitForLoadState('networkidle');
  const k = page.getByPlaceholder('Kullanıcı adı');
  await k.click(); await expect(k).toBeEditable(); await k.fill(KULLANICI);
  const p = page.locator('input[type=password]').first();
  await p.click(); await expect(p).toBeEditable(); await p.fill(SIFRE);
  await page.locator('button.dw-btn').first().click();
}

/** Firma + şube adımlarını ilk seçenekle geçer; özel kod adımı çıkarsa doldurur. */
async function firmaSubeOzelKod(page) {
  for (let i = 0; i < 8; i++) {
    const dugme = page.locator('button.dw-btn:visible').first();
    await dugme.waitFor({ timeout: 30_000 });
    const metin = (await dugme.innerText()).trim();
    if (metin.includes('ŞİFREYİ BELİRLE')) {
      const y = page.locator('input[type=password]:visible');
      for (const n of [0, 1]) { await y.nth(n).fill(SIFRE); }
      await dugme.click(); await page.waitForTimeout(1200); continue;
    }
    if (metin.includes('KODU OLUŞTUR')) {
      const y = page.locator('input[type=password]:visible');
      for (const n of [0, 1]) { await y.nth(n).fill('OzelKod2026'); }
      await dugme.click(); await page.waitForTimeout(1200); continue;
    }
    if (metin.includes('ETKİNLEŞTİR') || metin.includes('DOĞRULA')) return;
    const kutu = page.locator('select.dw-select:visible').first();
    if (await kutu.count()) {
      const ilk = await kutu.locator('option').evaluateAll(o => o.map(x => x.value).filter(v => v));
      if (ilk.length && !(await kutu.inputValue())) await kutu.selectOption(ilk[0]);
    }
    await dugme.click(); await page.waitForTimeout(1500);
    if (!new URL(page.url()).pathname.startsWith('/login')) return;
  }
}

let gizli = null;

test('Süper admin iki adımlı girişi web girişinde kurar, kurtarma kodlarını bir kez görür', async ({ page }) => {
  await kimlik(page);
  await firmaSubeOzelKod(page);
  await expect(page.getByRole('heading', { name: 'İki Adımlı Giriş' })).toBeVisible({ timeout: 30_000 });
  await expect(page.locator('img[alt="Doğrulama uygulaması QR kodu"]')).toBeVisible();
  gizli = (await page.locator('code').first().innerText()).trim();
  expect(gizli).toMatch(/^[A-Z2-7]{32}$/);

  const kodKutusu = page.getByPlaceholder('123456');
  await kodKutusu.fill('000000');                                         // yanlış kod etkinleştirmez
  await page.locator('button.dw-btn', { hasText: 'ETKİNLEŞTİR' }).click();
  await expect(page.getByText('Doğrulama kodu hatalı')).toBeVisible();
  await kodKutusu.fill(totp(gizli));
  await page.locator('button.dw-btn', { hasText: 'ETKİNLEŞTİR' }).click();

  await expect(page.getByRole('heading', { name: 'Kurtarma Kodları' })).toBeVisible({ timeout: 15_000 });
  const kodlar = await page.locator('div', { hasText: /^[A-Z2-7]{5}-[A-Z2-7]{5}$/ }).allInnerTexts();
  expect(new Set(kodlar.map(k => k.trim())).size).toBe(8);
  await page.locator('button.dw-btn', { hasText: 'KODLARI SAKLADIM' }).click();
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 30_000 });
});

test('Yeniden girişte kod sorulur: yanlış kod reddedilir, doğru kodla girilir', async ({ page }) => {
  expect(gizli, 'önceki test gizli anahtarı okumalıydı').toBeTruthy();
  await kimlik(page);
  await expect(page.getByRole('heading', { name: 'Doğrulama Kodu' })).toBeVisible({ timeout: 30_000 });
  const kutu = page.getByPlaceholder('123456');
  await kutu.fill('111111');
  await page.locator('button.dw-btn', { hasText: 'DOĞRULA' }).click();
  await expect(page.getByText('Doğrulama kodu hatalı')).toBeVisible();
  await expect(page).toHaveURL(/\/login/);

  await kutu.fill(totp(gizli, 1));                                        // kurulumda kullanılan adımdan SONRAKİ kod
  await page.locator('button.dw-btn', { hasText: 'DOĞRULA' }).click();
  await expect(page.getByRole('heading', { name: 'Süper Admin' })).toBeVisible({ timeout: 15_000 });   // kod kabul → firma adımı
  await firmaSubeOzelKod(page);
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 30_000 });
});
