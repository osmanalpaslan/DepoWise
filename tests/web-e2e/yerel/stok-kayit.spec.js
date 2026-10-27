// ⭐ M-69 (AR-GE 2026-10-26, F-022) — WEB STOK KAYIT AKIŞLARI, GERÇEK TARAYICIDA (yerel, boş veritabanı).
// Canlı testler yalnız okur; buradaki testler KAYIT YAZAR: yeni malzeme girişi (çift tıklamada TEK kayıt), depo çıkışı,
// hareket iptali. Sonuçlar API'den okunur (ekranda "başarılı" yazması yetmez — veritabanındaki gerçek etki sınanır).
const { test, expect } = require('@playwright/test');
const { api, tohumla, subeyleGiris, secim, otomatikSec, yaz, onayla } = require('./yardimci-yerel');

const KOD = 'E2E-' + Date.now().toString(36).toUpperCase();
let ids;

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser }) => { ids = await tohumla(browser); });

async function stok(kod) {
  const r = await api(`/api/materials/grid?page=1&pageSize=5&code=${encodeURIComponent(kod)}`);
  const satir = (r.items || []).find(x => x.code === kod);
  return satir ? Number(satir.stock) : null;
}

test('Yeni malzeme girişi: çift tıklama ve çift onay TEK kayıt üretir', async ({ page }) => {
  await subeyleGiris(page, 'E2E Depo');
  await page.goto('/stock');
  await page.waitForLoadState('networkidle');
  await yaz(page, 'Kod *', KOD);
  await yaz(page, 'Ad *', 'E2E test malzemesi');
  await secim(page, 'Birim *', 'Adet');
  await yaz(page, 'Eklenecek Stok', 5);
  await secim(page, 'Teslim Eden / Alan (Personel) *', 'Ali Depocu');

  const kaydet = page.getByRole('button', { name: 'Kaydet', exact: true });
  await kaydet.dblclick();                                       // kullanıcı iki kez bastı
  const pencere = page.locator('.mud-dialog');
  await expect(pencere).toHaveCount(1);                          // tek onay penceresi
  await pencere.getByRole('button', { name: 'Evet, Kaydet' }).dblclick();   // onayı da iki kez
  await expect(page.locator('.mud-dialog')).toHaveCount(0, { timeout: 15_000 });
  await expect(page.getByText('Son Stok Hareketleri')).toBeVisible();
  await expect.poll(() => stok(KOD), { timeout: 15_000 }).toBe(5);
  await page.waitForTimeout(2_000);                              // ikinci (kopya) gönderim gecikmeli gelirse yakala
  expect(await stok(KOD)).toBe(5);                               // 10 olsaydı kayıt iki kez yazılmıştı
});

test('Depo çıkışı stoğu düşürür', async ({ page }) => {
  await subeyleGiris(page, 'E2E Depo');
  await page.goto('/stock');
  await page.waitForLoadState('networkidle');
  await page.getByText('Depo Çıkışı', { exact: true }).first().click();
  await otomatikSec(page, 'Malzeme *', KOD);
  await yaz(page, 'Çıkacak Miktar', 2);
  await secim(page, 'Teslim Eden / Alan (Personel) *', 'Ali Depocu');
  await page.getByRole('button', { name: 'Kaydet', exact: true }).click();
  await onayla(page, 'Evet, Kaydet');
  await expect.poll(() => stok(KOD), { timeout: 15_000 }).toBe(3);
});

test('Hareket iptali stoğu geri alır (ters kayıt)', async ({ page }) => {
  await subeyleGiris(page, 'E2E Depo');
  await page.goto('/stock');
  await page.waitForLoadState('networkidle');
  const satir = page.locator('tr', { hasText: KOD }).filter({ hasText: /Çıkış|çıkış/ }).first();
  await satir.getByRole('button', { name: 'İptal' }).click();
  await onayla(page, 'Evet, İptal');
  await expect.poll(() => stok(KOD), { timeout: 15_000 }).toBe(5);
});
