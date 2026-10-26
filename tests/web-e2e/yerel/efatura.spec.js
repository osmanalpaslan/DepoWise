// ⭐ M-52 (AR-GE 2026-10-27) — e-Fatura (UBL-TR XML) içe alma, gerçek tarayıcıda dosya yükleyerek (yerel, boş veritabanı):
// Faturalar › e-Fatura İçe Al → dosya → özet onayı → alış faturası; aynı dosya ikinci kez "daha önce içe alınmış" der.
const { test, expect } = require('@playwright/test');
const { api, tohumla, subeyleGiris, onayla } = require('./yardimci-yerel');

function ornek(uuid) {
  return `<?xml version="1.0" encoding="UTF-8"?>
<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
  xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
  xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
  <cbc:ID>E2E2026000000001</cbc:ID><cbc:UUID>${uuid}</cbc:UUID><cbc:IssueDate>2026-10-20</cbc:IssueDate>
  <cbc:DocumentCurrencyCode>TRY</cbc:DocumentCurrencyCode>
  <cac:AccountingSupplierParty><cac:Party>
    <cac:PartyIdentification><cbc:ID schemeID="VKN">1112223334</cbc:ID></cac:PartyIdentification>
    <cac:PartyName><cbc:Name>E2E Tedarik AŞ</cbc:Name></cac:PartyName>
  </cac:Party></cac:AccountingSupplierParty>
  <cac:LegalMonetaryTotal><cbc:PayableAmount currencyID="TRY">120.00</cbc:PayableAmount></cac:LegalMonetaryTotal>
  <cac:InvoiceLine><cbc:ID>1</cbc:ID><cbc:InvoicedQuantity unitCode="C62">1</cbc:InvoicedQuantity>
    <cac:TaxTotal><cac:TaxSubtotal><cbc:Percent>20</cbc:Percent></cac:TaxSubtotal></cac:TaxTotal>
    <cac:Item><cbc:Name>Eldiven</cbc:Name></cac:Item><cac:Price><cbc:PriceAmount currencyID="TRY">100.00</cbc:PriceAmount></cac:Price>
  </cac:InvoiceLine>
</Invoice>`;
}

test('e-Fatura dosyası içe alınır, ikinci kez alınamaz', async ({ page, browser }) => {
  await tohumla(browser);
  const uuid = 'e2e-' + Date.now();
  const dosya = { name: 'efatura.xml', mimeType: 'application/xml', buffer: Buffer.from(ornek(uuid), 'utf8') };
  await subeyleGiris(page, 'E2E Depo');
  await page.goto('/invoices');
  await page.waitForLoadState('networkidle');

  await page.locator('input[type=file][accept=".xml"]').first().setInputFiles(dosya);
  const pencere = page.locator('.mud-dialog');
  await expect(pencere).toContainText('E2E Tedarik AŞ');
  await expect(pencere).toContainText('E2E2026000000001');
  await onayla(page, 'Evet, İçe Al');
  await expect(page.getByText('e-Fatura içe alındı:')).toBeVisible({ timeout: 15_000 });
  const faturalar = await api('/api/invoices?search=E2E2026000000001');
  expect(JSON.stringify(faturalar)).toContain('E2E2026000000001');

  await page.locator('input[type=file][accept=".xml"]').first().setInputFiles(dosya);
  await expect(page.getByText('daha önce içe alınmış')).toBeVisible({ timeout: 15_000 });
});
