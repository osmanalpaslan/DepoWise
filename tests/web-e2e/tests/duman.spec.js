// "Duman testi" (smoke test): sitenin temel akışları ayakta mı? Canlı sitede YALNIZ OKUR.
const { test, expect } = require('@playwright/test');

const { girisYap: giris } = require('./yardimci');
async function girisYap(page) {
  const user = process.env.DEPOWISE_TEST_USER, pass = process.env.DEPOWISE_TEST_PASS;
  test.skip(!user || !pass, '.env.test.local içinde test kullanıcısı yok');
  await giris(page, user, pass, process.env.DEPOWISE_TEST_COMPANY);
}

test('giriş sayfası açılıyor', async ({ page }) => {
  const r = await page.goto('/login');
  expect(r?.status()).toBeLessThan(400);
  await expect(page.locator('input[type=password]').first()).toBeVisible();
});

test('test kullanıcısıyla giriş + ana sayfa + malzemeler + bakım uyarıları', async ({ page }) => {
  const hatalar = [];
  page.on('pageerror', e => hatalar.push(e.message));
  await girisYap(page);

  // Ana sayfa yüklendi, Blazor hata bandı görünmüyor.
  await expect(page.locator('#blazor-error-ui')).toBeHidden();

  // Malzemeler listesi: tablo satırları gelir.
  await page.goto('/materials');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('table, .mud-table').first()).toBeVisible({ timeout: 30_000 });

  // Bakım ekranı açılıyor (uyarı notları bu ekranda gösterilir).
  await page.goto('/maintenance');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('#blazor-error-ui')).toBeHidden();

  expect(hatalar, 'sayfada JavaScript hatası olmamalı').toEqual([]);
});
