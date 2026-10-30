// ⭐ 2026-10-30 — Sohbet emoji kısayolu GERÇEK tarayıcıda (yerel, boş veritabanı). Kullanıcı bildirimi: "kısayolu yazıp boşluk
// bıraktıktan sonra emoji birkaç saniye sonra beliriyor". Kutuya ":) " yazılınca emoji HEMEN (1 sn içinde) kutuda görünmeli;
// 🙂 düğmesinden seçilen emoji mesaja eklenmeli.
const { test, expect } = require('@playwright/test');
const { api, tohumla, subeyleGiris } = require('./yardimci-yerel');

test('Sohbet: kısayol yazarken hemen emojiye döner, seçiciden emoji eklenir', async ({ page, browser }) => {
  const ids = await tohumla(browser);
  const ad = 'Emoji Alıcı';
  const kisiler = await api('/api/chat/users');
  if (!kisiler.some(k => k.fullName === ad))
    await api('/api/users', { username: 'emojialici', password: 'Gecici2026!', fullName: ad, roleKeys: ['role-company-admin'], branchId: ids.depo });

  await subeyleGiris(page, 'E2E Depo');
  await page.locator('.dw-chat-bar button', { hasText: 'Sohbet' }).click();
  await page.locator('.dw-chat-person', { hasText: ad }).click();
  const kutu = page.locator('input.dw-chat-yaz').first();
  await kutu.click();
  await page.keyboard.type('selam :) ');
  await expect(kutu).toHaveValue('selam 🙂 ', { timeout: 1_000 });   // HEMEN — eskiden birkaç saniye sonra
  await page.keyboard.type('(y) ');
  await expect(kutu).toHaveValue('selam 🙂 👍 ', { timeout: 1_000 });

  await page.locator('button.dw-emoji-dugme').first().click();
  await page.locator('input.dw-emoji-ara').fill('kamyon');
  await page.locator('button.dw-emoji-sec', { hasText: '🚚' }).click();
  await expect(kutu).toHaveValue('selam 🙂 👍 🚚');
});
