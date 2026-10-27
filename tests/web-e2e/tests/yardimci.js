// Ortak giriş yardımcısı: kimlik → (süper adminse firma) → şube adımlarını tamamlar. Canlıda YALNIZ OKUR.
const { expect } = require('@playwright/test');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

// ⭐ M-01 (2026-10-28): süper admin test hesabında iki adımlı giriş AÇIK. Gizli anahtar yalnız bu bilgisayarda
// (.env.test.local → DEPOWISE_TEST_TOTP). Sunucu aynı kodu iki kez kabul etmez ve ±1 adım (30 sn) tolerans tanır →
// her girişte henüz kullanılmamış en küçük adım seçilir; gerekirse bir sonraki adım beklenir. Kullanılan adım dosyada
// tutulur (Playwright hata sonrası çalışanı yeniden başlatır; bellekteki sayaç kaybolurdu).
const ADIM_DOSYASI = path.join(os.tmpdir(), 'dw_e2e_totp_adim.txt');
function totpKodu(gizliB32, adim) {
  const A = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let tampon = 0, bit = 0; const b = [];
  for (const c of gizliB32.replace(/\s/g, '').toUpperCase()) { tampon = (tampon << 5) | A.indexOf(c); bit += 5; if (bit >= 8) { b.push((tampon >> (bit - 8)) & 255); bit -= 8; } }
  let n = BigInt(adim);
  const sayac = Buffer.alloc(8);
  for (let i = 7; i >= 0; i--) { sayac[i] = Number(n & 0xffn); n >>= 8n; }
  const h = crypto.createHmac('sha1', Buffer.from(b)).update(sayac).digest();
  const o = h[h.length - 1] & 15;
  return String((((h[o] & 127) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3]) % 1000000).padStart(6, '0');
}
/** Kullanılmamış bir doğrulama kodu (gizli anahtar tanımlı değilse null). */
async function birKerelikKod() {
  const gizli = process.env.DEPOWISE_TEST_TOTP;
  if (!gizli) return null;
  let son = -1;
  try { son = parseInt(fs.readFileSync(ADIM_DOSYASI, 'utf8'), 10) || -1; } catch (e) {}
  const simdi = () => Math.floor(Date.now() / 30000);
  const adim = Math.max(simdi() - 1, son + 1);
  while (adim > simdi() + 1) await new Promise(r => setTimeout(r, 1000));   // tolerans penceresine girene kadar bekle
  fs.writeFileSync(ADIM_DOSYASI, String(adim));
  return totpKodu(gizli, adim);
}

async function girisYap(page, user, pass, firmaAdi) {
  // 2026-10-11: giriş sayfasında açılış animasyonu (oturum başına bir kez) formu örter → testlerde atlanır;
  // animasyonun kendisi talep-1011.spec.js'te ayrıca sınanır.
  await page.addInitScript(() => { try { sessionStorage.setItem('dw_acilis', '1'); } catch (e) {} });
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
  // M-01: iki adımlı giriş — "Doğrulama Kodu" adımı (açık hesap) ya da zorunlu kurulum ekranı (henüz kurulmamış).
  const dogrula = page.locator('button.dw-btn', { hasText: 'DOĞRULA' });
  const kurulum = page.locator('button.dw-btn', { hasText: 'ETKİNLEŞTİR' });
  const ikinci = await Promise.race([
    dogrula.waitFor({ timeout: 8_000 }).then(() => 'kod').catch(() => null),
    kurulum.waitFor({ timeout: 8_000 }).then(() => 'kurulum').catch(() => null),
    page.locator('select.dw-select:visible').first().waitFor({ timeout: 8_000 }).then(() => null).catch(() => null),
  ]);
  if (ikinci === 'kurulum') throw new Error('Test hesabında iki adımlı giriş kurulmamış (DEPOWISE_TEST_TOTP) — scripts/e2e_test_hesabi_mfa.mjs');
  if (ikinci === 'kod') {
    const kod = await birKerelikKod();
    if (!kod) throw new Error('.env.test.local içinde DEPOWISE_TEST_TOTP yok — test hesabı doğrulama kodu istiyor');
    await page.getByPlaceholder('123456').fill(kod);
    await dogrula.click();
    await expect(dogrula).toBeHidden({ timeout: 20_000 });
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
  return await yeniliklerKapat(page);   // true = "Yenilikler" penceresi açılmıştı
}

/** 2026-10-11: yayından sonra girişte bir kez "Yenilikler" penceresi açılır (her test temiz tarayıcıyla başladığı için
 *  her girişte görünür) → varsa "Tamam" ile kapat ki sonraki tıklamaları örtmesin. Pencere açıldıysa true döner. */
async function yeniliklerKapat(page) {
  const pencere = page.locator('.mud-dialog', { hasText: 'Yenilikler:' });
  const acildi = await pencere.waitFor({ timeout: 8_000 }).then(() => true).catch(() => false);
  if (acildi) {
    await pencere.getByRole('button', { name: 'Tamam' }).click();
    await expect(pencere).toBeHidden({ timeout: 10_000 });
  }
  return acildi;
}

/** API'ye doğrudan giriş (sunucu tarafı yetki kapısını test etmek için) → Bearer token. */
async function apiToken(request, user, pass, companyId, branchId) {
  const api = (process.env.DEPOWISE_TEST_API || 'https://depowise-erp.fly.dev').replace(/\/$/, '');
  const r = await request.post(api + '/api/auth/login', { data: { username: user, password: pass, companyId, branchId } });
  expect(r.ok(), 'API girişi başarılı olmalı').toBeTruthy();
  const j = await r.json();
  return { api, token: j.token ?? j.accessToken };
}

module.exports = { girisYap, apiToken, yeniliklerKapat, birKerelikKod };
