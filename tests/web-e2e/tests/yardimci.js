// Ortak giriş yardımcısı: kimlik → (süper adminse firma) → şube adımlarını tamamlar. Canlıda YALNIZ OKUR.
const { expect } = require('@playwright/test');

async function girisYap(page, user, pass, firmaAdi) {
  await page.goto('/login');
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
  // İlk girişte güvenlik gereği "Yeni Şifre Belirleyin" adımı gelebilir → aynı test şifresiyle tamamla.
  const belirle = page.locator('button.dw-btn', { hasText: 'ŞİFREYİ BELİRLE' });
  if (await belirle.waitFor({ timeout: 5_000 }).then(() => true).catch(() => false)) {
    const yeni = page.locator('input[type=password]:visible');
    for (const i of [0, 1]) { await yeni.nth(i).click(); await expect(yeni.nth(i)).toBeEditable(); await yeni.nth(i).fill(pass); }
    await belirle.click();
    await page.waitForTimeout(1500);
  }
  // Sonraki adımlar (süper adminde FİRMA, sonra ŞUBE): seçim kutusu varsa seçili/ilk değerle devam et.
  for (let adim = 0; adim < 3 && new URL(page.url()).pathname.startsWith('/login'); adim++) {
    const kutu = page.locator('select.dw-select:visible');
    await Promise.race([
      page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 30_000 }).catch(() => {}),
      kutu.first().waitFor({ timeout: 30_000 }).catch(() => {}),
    ]);
    if (!new URL(page.url()).pathname.startsWith('/login')) break;
    if (await kutu.count()) {
      if (firmaAdi && adim === 0) await kutu.first().selectOption({ label: firmaAdi }).catch(() => {});
      else if (!(await kutu.first().inputValue())) {
        const ilk = await kutu.first().locator('option').evaluateAll(o => o.map(x => x.value).filter(v => v));
        if (ilk.length) await kutu.first().selectOption(ilk[0]);
      }
    }
    // Zamanlama yarışı (2026-10-10): sayfa panoya geçerken düğme kaybolabilir → yalnız giriş sayfası hâlâ
    // açıkken ve düğme görünürken tıkla; tıklama sırasında sayfa değişirse bu bir hata değildir.
    const dugme = page.locator('button.dw-btn:visible').first();
    if (new URL(page.url()).pathname.startsWith('/login') && await dugme.isVisible()) {
      await dugme.click({ timeout: 10_000 }).catch(() => {});
    }
    await page.waitForTimeout(1500);
  }
  await page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 30_000 });
}

/** API'ye doğrudan giriş (sunucu tarafı yetki kapısını test etmek için) → Bearer token. */
async function apiToken(request, user, pass, companyId, branchId) {
  const api = (process.env.DEPOWISE_TEST_API || 'https://depowise-erp.fly.dev').replace(/\/$/, '');
  const r = await request.post(api + '/api/auth/login', { data: { username: user, password: pass, companyId, branchId } });
  expect(r.ok(), 'API girişi başarılı olmalı').toBeTruthy();
  const j = await r.json();
  return { api, token: j.token ?? j.accessToken };
}

module.exports = { girisYap, apiToken };
