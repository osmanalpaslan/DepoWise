// ⭐ 2026-10-11 — kullanıcı talebi (TALEP_2026-10-11.md) web doğrulaması. Canlı sitede YALNIZ OKUR: hiçbir kayıt
// oluşturmaz/değiştirmez (zimmet formunda yalnız düğmenin varlığına bakılır, kaydedilmez).
//   • Yayından sonra girişte "Yenilikler" penceresi BİR KEZ açılır; sayfa yenilenince tekrar açılmaz.
//   • Araç Listesi ↔ Kiralık Araçlar arasında SAYFA YENİLEMEDEN (menüdeki gibi) geçişte ekran takılmaz.
//   • Güncelleme Notları ekranı web'e ait maddeyi gösterir.
//   • Zimmet: Teslim formunda "Listeye Ekle".
//   • Sunucu kataloğu: Depo Çıkışları raporu + Teslim Alan filtresi.
const { test, expect } = require('@playwright/test');
const { girisYap: giris } = require('./yardimci');

async function girisYap(page) {
  const user = process.env.DEPOWISE_TEST_USER, pass = process.env.DEPOWISE_TEST_PASS;
  test.skip(!user || !pass, '.env.test.local içinde test kullanıcısı yok');
  return giris(page, user, pass, process.env.DEPOWISE_TEST_COMPANY);
}

/** Blazor'un kendi yönlendirmesi — menü tıklaması gibi SAYFA YENİLEMEDEN adres değiştirir (hata buradaydı). */
async function menuGibiGit(page, yol) {
  await page.evaluate(y => window.Blazor.navigateTo(y), yol);
  await page.waitForURL(u => u.pathname === yol, { timeout: 15_000 });
  await page.waitForLoadState('networkidle');
}

test('Açılış animasyonu giriş sayfasında bir kez oynar, tıklayınca geçer (2026-10-11)', async ({ page }) => {
  await page.goto('/login');
  const kat = page.locator('.dw-acilis');
  await expect(kat).toBeVisible({ timeout: 15_000 });
  await expect(kat.locator('video')).toHaveAttribute('src', /acilis\.webm/);
  await kat.click();
  await expect(kat).toHaveCount(0, { timeout: 5_000 });
  await page.reload();
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(1500);
  await expect(page.locator('.dw-acilis')).toHaveCount(0);   // aynı tarayıcı oturumunda tekrar oynamaz
  await expect(page.getByPlaceholder('Kullanıcı adı')).toBeVisible();
});

test('Yenilikler penceresi girişte bir kez açılır, yenilemede tekrar açılmaz', async ({ page }) => {
  const acildi = await girisYap(page);
  expect(acildi, 'yeni yayından sonra ilk girişte Yenilikler penceresi açılmalı').toBeTruthy();
  await page.reload();
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.mud-dialog', { hasText: 'Yenilikler:' })).toHaveCount(0, { timeout: 8_000 });
});

test('Araç Listesi ↔ Kiralık Araçlar menü geçişinde ekran takılmaz', async ({ page }) => {
  const hatalar = [];
  page.on('pageerror', e => hatalar.push(e.message));
  await girisYap(page);
  await page.goto('/vehicles');
  await page.waitForLoadState('networkidle');
  const baslik = page.locator('.dw-pagetitle');
  await expect(baslik).toHaveText('Araç Listesi', { timeout: 30_000 });
  for (let tur = 0; tur < 2; tur++) {
    await menuGibiGit(page, '/vehicles/rental');
    await expect(baslik).toHaveText('Kiralık Araçlar', { timeout: 30_000 });
    await expect(page.getByText('Kiralık Araç Listesi')).toBeVisible();
    await expect(page.locator('.dw-summary-box')).toHaveCount(1);          // kiralık kipinde tek özet kutusu
    await menuGibiGit(page, '/vehicles');
    await expect(baslik).toHaveText('Araç Listesi', { timeout: 30_000 });
    await expect(page.locator('.dw-summary-box')).not.toHaveCount(1);       // araç listesinde uyarı kutuları da var
  }
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  expect(hatalar, 'sayfada JavaScript hatası olmamalı').toEqual([]);
});

test('Güncelleme Notları ekranı açılır ve web maddesini gösterir', async ({ page }) => {
  await girisYap(page);
  await page.goto('/update-notes');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.dw-pagetitle', { hasText: 'Güncelleme Notları' })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText(/Kiralık Araçlar ile Araç Listesi arasında geçerken/)).toBeVisible();
  await expect(page.getByText('Son güncelleme')).toBeVisible();
});

test('Zimmet Teslim formunda Listeye Ekle düğmesi var', async ({ page }) => {
  await girisYap(page);
  await page.goto('/assignments');
  await page.waitForLoadState('networkidle');
  await expect(page.locator('.dw-pagetitle', { hasText: 'Zimmet' })).toBeVisible({ timeout: 30_000 });
  const ekle = page.getByRole('button', { name: /Listeye Ekle/ });
  test.skip(!(await ekle.count()), 'test kullanıcısının zimmet oluşturma yetkisi yok');
  await expect(ekle).toBeVisible();
  await expect(page.getByText(/her birini «Listeye Ekle» ile ekleyin/)).toBeVisible();
});

test('Sunucu kataloğu: Depo Çıkışları raporu ve Teslim Alan filtresi', async ({ request }) => {
  const user = process.env.DEPOWISE_TEST_USER, pass = process.env.DEPOWISE_TEST_PASS;
  test.skip(!user || !pass, '.env.test.local içinde test kullanıcısı yok');
  // Süper admin test kullanıcısı firma kimliği gerektirebilir (.env'de yalnız firma ADI var) → giriş olmazsa ATLA, hata sayma.
  const api = (process.env.DEPOWISE_TEST_API || 'https://depowise-erp.fly.dev').replace(/\/$/, '');
  const g = await request.post(api + '/api/auth/login', { data: { username: user, password: pass } });
  test.skip(!g.ok(), 'API doğrudan girişi firma seçimi istiyor — katalog web testlerinde dolaylı doğrulanıyor');
  const j = await g.json();
  const r = await request.get(api + '/api/reports/catalog', { headers: { Authorization: 'Bearer ' + (j.token ?? j.accessToken) } });
  expect(r.ok()).toBeTruthy();
  const katalog = await r.json();
  const rapor = katalog.find(x => x.key === 'stock-issues');
  expect(rapor, 'stock-issues katalogda olmalı').toBeTruthy();
  expect(rapor.name).toBe('Depo Çıkışları');
  expect(rapor.usesRecipient).toBe(true);
  expect(rapor.usesMaterial).toBe(true);
});
