// ⭐ M-69 (AR-GE 2026-10-26, F-022) — YEREL kayıt akışı testleri. Yalnız scripts/run_web_e2e_yerel.ps1 ile çalışır:
// o betik boş, geçici bir veritabanıyla yerel API + web başlatır. Bu yapılandırma .env.test.local'ı OKUMAZ ve yalnız
// localhost adresini kabul eder — canlı siteye kayıt yazan bir test YANLIŞLIKLA bile koşamaz.
const { defineConfig } = require('@playwright/test');

const web = process.env.DEPOWISE_E2E_WEB || '';
const api = process.env.DEPOWISE_E2E_API || '';
for (const adres of [web, api]) {
  if (!/^http:\/\/localhost:\d+$/.test(adres))
    throw new Error(`GÜVENLİK: yerel kayıt testleri yalnız http://localhost adresinde koşar (gelen: "${adres}"). ` +
      'scripts/run_web_e2e_yerel.ps1 ile çalıştırın.');
}

module.exports = defineConfig({
  testDir: './yerel',
  timeout: 120_000,
  retries: 0,                 // flaky test retry ile gizlenmez (.claude/rules/testing.md)
  workers: 1,                 // testler aynı veritabanını sırayla kullanır
  reporter: [['list']],
  use: {
    baseURL: web,
    browserName: 'chromium',
    headless: true,
    locale: 'tr-TR',
    viewport: { width: 1440, height: 900 },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
