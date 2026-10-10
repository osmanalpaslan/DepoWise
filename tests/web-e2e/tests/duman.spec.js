// "Duman testi" (smoke test): sitenin temel akışları ayakta mı? Canlı sitede YALNIZ OKUR.
const { test, expect } = require('@playwright/test');

async function girisYap(page) {
  const user = process.env.DEPOWISE_TEST_USER, pass = process.env.DEPOWISE_TEST_PASS;
  test.skip(!user || !pass, '.env.test.local içinde test kullanıcısı yok');
  await page.goto('/login');
  // Blazor Server: sayfa etkileşimli olana kadar bekle (sunucu bağlantısı kurulsun).
  await page.waitForLoadState('networkidle');
  // Alanlar otomatik doldurmaya karşı TIKLANANA kadar kilitli (readonly) — önce tıkla, kilit açılsın.
  const kAdi = page.getByPlaceholder('Kullanıcı adı');
  await kAdi.click();
  await expect(kAdi).toBeEditable();
  await kAdi.fill(user);
  const parola = page.locator('input[type=password]').first();
  await parola.click();
  await expect(parola).toBeEditable();
  await parola.fill(pass);
  await page.locator('button.dw-btn').first().click();
  // Sonraki adımlar (süper adminde FİRMA, sonra ŞUBE): seçim kutusu varsa seçili/ilk değerle devam et.
  // Firma DEPOWISE_TEST_COMPANY ile (görünen ad) seçilebilir; verilmezse varsayılan seçili kalır.
  for (let adim = 0; adim < 3 && new URL(page.url()).pathname.startsWith('/login'); adim++) {
    const kutu = page.locator('select.dw-select:visible');
    await Promise.race([
      page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 30_000 }).catch(() => {}),
      kutu.first().waitFor({ timeout: 30_000 }).catch(() => {}),
    ]);
    if (!new URL(page.url()).pathname.startsWith('/login')) break;
    if (await kutu.count()) {
      const istenen = process.env.DEPOWISE_TEST_COMPANY;
      if (istenen && adim === 0) await kutu.first().selectOption({ label: istenen }).catch(() => {});
      else if (!(await kutu.first().inputValue())) {
        const ilk = await kutu.first().locator('option').evaluateAll(o => o.map(x => x.value).filter(v => v));
        if (ilk.length) await kutu.first().selectOption(ilk[0]);
      }
    }
    await page.locator('button.dw-btn:visible').first().click();
    await page.waitForTimeout(1500);
  }
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 30_000 });
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
