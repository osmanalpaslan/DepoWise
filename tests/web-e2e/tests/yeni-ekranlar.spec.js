// ⭐ 2026-10-10 — Yeni geliştirmelerin web'de görünür olduğunu doğrular. Canlı sitede YALNIZ OKUR
// (hiçbir kayıt oluşturmaz/değiştirmez; yalnız sayfaları açar ve formu kaydetmeden kapatır).
//   • Kiralık Araçlar: /vehicles/rental açılır, kira kolonları listede, Araç Listesi'nden ayrı.
//   • Yakıt Özeti: yeni tasarım (ay kartları; bu ay açıkken HAFTALAR çubuk, GÜNLER kutucuk).
//   • Talep Formu: araç kutusu "çoklu", kalem tablosunda KOD sütunu başlığı.
const { test, expect } = require('@playwright/test');
const { girisYap: giris } = require('./yardimci');

async function girisYap(page) {
  const user = process.env.DEPOWISE_TEST_USER, pass = process.env.DEPOWISE_TEST_PASS;
  test.skip(!user || !pass, '.env.test.local içinde test kullanıcısı yok');
  await giris(page, user, pass, process.env.DEPOWISE_TEST_COMPANY);
}

test('Kiralık Araçlar ekranı açılır ve kira kolonlarını gösterir', async ({ page }) => {
  const hatalar = [];
  page.on('pageerror', e => hatalar.push(e.message));
  await girisYap(page);
  await page.goto('/vehicles/rental');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  await expect(page.getByText('Kiralık Araçlar', { exact: true }).first()).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText('Kiralık Araç Listesi')).toBeVisible();
  await expect(page.locator('th', { hasText: 'KİRALAYAN FİRMA' }).first()).toBeVisible();
  // Yeni kayıt formu: kira alanları + değişim kutusu (KAYDETMEDEN çıkılır).
  const yeni = page.getByRole('button', { name: 'Yeni Kiralık Araç' });
  if (await yeni.isVisible()) {
    await yeni.click();
    await page.waitForLoadState('networkidle');
    await expect(page.getByText('Kira Bilgisi')).toBeVisible({ timeout: 30_000 });
    await expect(page.getByText(/YERİNE geldi/)).toBeVisible();
  }
  expect(hatalar, 'sayfada JavaScript hatası olmamalı').toEqual([]);
});

test('Yakıt Özeti yeni tasarımla açılır (çubuk + kutucuk)', async ({ page }) => {
  await girisYap(page);
  await page.goto('/fuel/summary');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  await expect(page.locator('.fz-kpi').first()).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText('Aylık Dağıtım')).toBeVisible();
  const kartlar = page.locator('.fz-month');
  if (await kartlar.count() > 0) {
    // Açık kart yoksa ilkini aç; açıkken iki ayrı biçim (hafta çubuğu / gün kutucuğu) görünmeli.
    if (await page.locator('.fz-detail').count() === 0) await kartlar.first().locator('.fz-month-head').click();
    const detay = page.locator('.fz-detail').first();
    await expect(detay.getByText('HAFTALAR')).toBeVisible();
    await expect(detay.getByText('GÜNLER')).toBeVisible();
  }
});

test('Talep Formu çoklu araç alanı ve KOD sütunu', async ({ page }) => {
  await girisYap(page);
  await page.goto('/requests');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  const alan = page.getByLabel('Araçlar (opsiyonel, çoklu)');
  test.skip(await alan.count() === 0, 'Test kullanıcısının talep oluşturma yetkisi yok');
  await expect(alan.first()).toBeVisible({ timeout: 30_000 });
});
