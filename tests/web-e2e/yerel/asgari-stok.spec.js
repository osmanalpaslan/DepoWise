// ⭐ M-31 (AR-GE 2026-10-26) — Asgari Stok Önerileri web akışı, gerçek tarayıcıda (yerel, boş veritabanı):
// depo asgarisi API ile tanımlanır → öneri ekranda görünür → satır seçilip talep oluşturulur → talebin API'de oluştuğu doğrulanır.
const { test, expect } = require('@playwright/test');
const { api, tohumla, subeyleGiris, onayla } = require('./yardimci-yerel');

const KOD = 'ASG-' + Date.now().toString(36).toUpperCase();

test('Asgari stok önerisinden talep oluşturulur', async ({ page, browser }) => {
  const ids = await tohumla(browser);
  const mat = (await api('/api/materials', { code: KOD, name: 'Asgari e2e malzemesi', minStock: 0, unitPrice: 0, openingStock: 0 })).id;
  await api('/api/stock/receive', {
    operationId: 'asg-' + KOD, materialId: mat, code: KOD, name: 'Asgari e2e malzemesi',
    quantity: 2, unitPrice: 0, branchId: ids.depo, personnelId: ids.personel,
  });
  await api(`/api/materials/${mat}/location-mins`, { locationId: ids.depo, minQty: 6 }, 'PUT');
  const onceki = (await api('/api/requests')).length ?? 0;

  await subeyleGiris(page, 'E2E Depo');
  await page.goto('/stock/min-suggestions');
  await page.waitForLoadState('networkidle');
  const satir = page.locator('tr', { hasText: KOD });
  await expect(satir).toBeVisible();
  await expect(satir).toContainText('Depoya özel');
  await satir.locator('input[type=checkbox]').check();
  await page.getByRole('button', { name: 'Seçilenlerden Talep Oluştur' }).click();
  await onayla(page, 'Evet, Oluştur');
  await expect(page.getByText('Talep oluşturuldu:')).toBeVisible({ timeout: 15_000 });
  await expect.poll(async () => (await api('/api/requests')).length, { timeout: 15_000 }).toBe(onceki + 1);
});
