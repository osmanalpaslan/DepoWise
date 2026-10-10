// ⭐ 2026-10-10 — DepoWise WEB uçtan uca (E2E) testleri.
// Varsayılan koşuda KAPALI: scripts/run_tests.ps1 bunu çalıştırmaz. Claude, web arayüzü değişen işlerde
// açıkça çalıştırır:  cd tests/web-e2e && npx playwright test
// Hedef adres ve test kullanıcısı repo kökündeki .env.test.local'dan okunur (git'e girmez; CLAUDE.md §7.0.1:
// yalnız TEST kullanıcısı). Testler canlı sitede YALNIZ OKUR — kayıt oluşturmaz/değiştirmez.
const fs = require('fs');
const path = require('path');
const { defineConfig } = require('@playwright/test');

const envPath = path.join(__dirname, '..', '..', '.env.test.local');
if (fs.existsSync(envPath)) {
  for (const line of fs.readFileSync(envPath, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^([A-Z_]+)=(.*)$/);
    if (m && !process.env[m[1]]) process.env[m[1]] = m[2].replace(/^"|"$/g, '');
  }
}

module.exports = defineConfig({
  testDir: './tests',
  timeout: 90_000,
  retries: 0,                 // flaky test retry ile gizlenmez (.claude/rules/testing.md)
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.DEPOWISE_TEST_WEB || 'https://depowise-web.fly.dev',
    browserName: 'chromium',
    headless: true,
    locale: 'tr-TR',
    viewport: { width: 1440, height: 900 },
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
