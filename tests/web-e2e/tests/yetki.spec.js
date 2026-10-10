// YETKİ testi: yalnız "Personel" rolündeki test kullanıcısı (Alpnex Test firması — gerçek firmadan AYRI).
// Yönetici ekranlarını görmemeli; asıl kapı SUNUCUDA (API 403). Canlıda YALNIZ OKUR.
const { test, expect } = require('@playwright/test');
const { girisYap, apiToken } = require('./yardimci');

const U = process.env.DEPOWISE_TEST_STAFF_USER, P = process.env.DEPOWISE_TEST_STAFF_PASS;

test.describe('kısıtlı kullanıcı (Personel)', () => {
  test.skip(!U || !P, '.env.test.local içinde kısıtlı test kullanıcısı yok');

  test('sunucu yönetici uçlarını reddeder (403)', async ({ request }) => {
    const { api, token } = await apiToken(request, U, P, process.env.DEPOWISE_TEST_STAFF_COMPANY, process.env.DEPOWISE_TEST_STAFF_BRANCH);
    const h = { Authorization: 'Bearer ' + token };
    // Kullanıcı listesi aynı firmadaki herkese BİLİNÇLİ olarak açık (UserService.ListUsers) — ama yönetici
    // olmayana ROLLER gizlenir. Bu kural da test edilir.
    const liste = await request.get(api + '/api/users', { headers: h });
    expect(liste.status()).toBe(200);
    for (const k of await liste.json()) expect(k.roles ?? '', `${k.username} rolü personele görünmemeli`).toBe('');
    for (const yol of ['/api/companies', '/api/audit', '/api/server/status']) {
      const r = await request.get(api + yol, { headers: h });
      expect([401, 403], `${yol} personel için kapalı olmalı (gelen: ${r.status()})`).toContain(r.status());
    }
  });

  test('menüde yönetici bağlantıları görünmez', async ({ page }) => {
    await girisYap(page, U, P);
    await expect(page.locator('#blazor-error-ui')).toBeHidden();
    const menu = (await page.locator('nav, .mud-navmenu, .mud-drawer').allInnerTexts()).join('\n');
    test.info().annotations.push({ type: 'menu', description: menu.replace(/\s+/g, ' ').slice(0, 600) });
    for (const yasak of ['Kullanıcılar', 'Firmalar', 'Kalıcı Silme', 'Yetkiler']) expect(menu).not.toContain(yasak);
  });
});
