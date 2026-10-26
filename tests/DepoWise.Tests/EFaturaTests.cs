using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-52 (AR-GE 2026-10-27, F-141) — e-FATURA (UBL-TR) İÇE ALMA ═══
///  EF1 — önizleme: no, UUID, tarih, satıcı (VKN, ünvan, vergi dairesi), satırlar (miktar/birim/fiyat/iskonto/KDV/tevkifat), toplam
///  EF2 — yeni satıcı → tedarikçi carisi (VKN'li) + M-53 köprüsüyle tedarikçi tanımı; alış faturası stok etkilemeden kaydedilir
///  EF3 — aynı belge ikinci kez içe alınamaz (önizlemede uyarı; kayıt mevcut sonucu döndürür, kopya fatura yok)
///  EF4 — var olan cari VKN ile eşlenir (yeni cari açılmaz)
///  EF5 — toplam uyuşmazlığı uyarı verir; bozuk / fatura olmayan / satırsız dosya anlaşılır hatayla reddedilir
///  EF6 — güvenlik: DTD / dış varlık içeren dosya reddedilir (XXE)
/// </summary>
public class EFaturaTests : IDisposable
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_efat_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly EFaturaService _ef;
    private readonly PartyService _cari;
    private readonly SessionContext _y;

    public EFaturaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('EF','EF',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin("EF", "ef_admin", "Test!2026", RoleKeys.CompanyAdmin);
        _y = new SessionContext(uid, "EF", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _cari = new PartyService(_f);
        _ef = new EFaturaService(_f, _cari, new InvoiceService(_f, new DepoWise.Infrastructure.Materials.StockService(_f), new PartyLedgerService(_f)));
    }

    public void Dispose() { try { File.Delete(_yol); } catch { } }

    /// <summary>UBL-TR 1.2 örneği: 2 satır — (10 adet × 100, %10 iskonto, KDV %20) + (1 hizmet × 500, KDV %20, tevkifat %50).</summary>
    public static string Ornek(string uuid = "6f1f6a0e-1111-4c3b-9d8e-000000000001", string vkn = "1234567890", decimal? odenecek = null) => $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<Invoice xmlns=""urn:oasis:names:specification:ubl:schema:xsd:Invoice-2""
         xmlns:cac=""urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2""
         xmlns:cbc=""urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2"">
  <cbc:UBLVersionID>2.1</cbc:UBLVersionID>
  <cbc:CustomizationID>TR1.2</cbc:CustomizationID>
  <cbc:ProfileID>TICARIFATURA</cbc:ProfileID>
  <cbc:ID>ABC2026000000123</cbc:ID>
  <cbc:UUID>{uuid}</cbc:UUID>
  <cbc:IssueDate>2026-10-20</cbc:IssueDate>
  <cbc:InvoiceTypeCode>TEVKIFAT</cbc:InvoiceTypeCode>
  <cbc:DocumentCurrencyCode>TRY</cbc:DocumentCurrencyCode>
  <cac:AccountingSupplierParty><cac:Party>
    <cac:PartyIdentification><cbc:ID schemeID=""VKN"">{vkn}</cbc:ID></cac:PartyIdentification>
    <cac:PartyIdentification><cbc:ID schemeID=""MERSISNO"">0123456789000017</cbc:ID></cac:PartyIdentification>
    <cac:PartyName><cbc:Name>Yıldız Hırdavat Ltd. Şti.</cbc:Name></cac:PartyName>
    <cac:PartyTaxScheme><cac:TaxScheme><cbc:Name>Kadıköy</cbc:Name></cac:TaxScheme></cac:PartyTaxScheme>
  </cac:Party></cac:AccountingSupplierParty>
  <cac:AccountingCustomerParty><cac:Party>
    <cac:PartyIdentification><cbc:ID schemeID=""VKN"">9999999999</cbc:ID></cac:PartyIdentification>
    <cac:PartyName><cbc:Name>Alıcı Firma</cbc:Name></cac:PartyName>
  </cac:Party></cac:AccountingCustomerParty>
  <cac:LegalMonetaryTotal>
    <cbc:LineExtensionAmount currencyID=""TRY"">1400.00</cbc:LineExtensionAmount>
    <cbc:PayableAmount currencyID=""TRY"">{(odenecek ?? 1630.00m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}</cbc:PayableAmount>
  </cac:LegalMonetaryTotal>
  <cac:InvoiceLine>
    <cbc:ID>1</cbc:ID>
    <cbc:InvoicedQuantity unitCode=""C62"">10</cbc:InvoicedQuantity>
    <cbc:LineExtensionAmount currencyID=""TRY"">900.00</cbc:LineExtensionAmount>
    <cac:AllowanceCharge><cbc:ChargeIndicator>false</cbc:ChargeIndicator><cbc:MultiplierFactorNumeric>0.10</cbc:MultiplierFactorNumeric><cbc:Amount currencyID=""TRY"">100.00</cbc:Amount></cac:AllowanceCharge>
    <cac:TaxTotal><cbc:TaxAmount currencyID=""TRY"">180.00</cbc:TaxAmount>
      <cac:TaxSubtotal><cbc:TaxableAmount currencyID=""TRY"">900.00</cbc:TaxableAmount><cbc:TaxAmount currencyID=""TRY"">180.00</cbc:TaxAmount><cbc:Percent>20</cbc:Percent>
        <cac:TaxCategory><cac:TaxScheme><cbc:Name>KDV</cbc:Name><cbc:TaxTypeCode>0015</cbc:TaxTypeCode></cac:TaxScheme></cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>
    <cac:Item><cbc:Name>Matkap Ucu Seti</cbc:Name></cac:Item>
    <cac:Price><cbc:PriceAmount currencyID=""TRY"">100.00</cbc:PriceAmount></cac:Price>
  </cac:InvoiceLine>
  <cac:InvoiceLine>
    <cbc:ID>2</cbc:ID>
    <cbc:InvoicedQuantity unitCode=""C62"">1</cbc:InvoicedQuantity>
    <cbc:LineExtensionAmount currencyID=""TRY"">500.00</cbc:LineExtensionAmount>
    <cac:TaxTotal><cbc:TaxAmount currencyID=""TRY"">100.00</cbc:TaxAmount>
      <cac:TaxSubtotal><cbc:TaxableAmount currencyID=""TRY"">500.00</cbc:TaxableAmount><cbc:TaxAmount currencyID=""TRY"">100.00</cbc:TaxAmount><cbc:Percent>20</cbc:Percent>
        <cac:TaxCategory><cac:TaxScheme><cbc:Name>KDV</cbc:Name><cbc:TaxTypeCode>0015</cbc:TaxTypeCode></cac:TaxScheme></cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>
    <cac:WithholdingTaxTotal><cbc:TaxAmount currencyID=""TRY"">50.00</cbc:TaxAmount>
      <cac:TaxSubtotal><cbc:TaxableAmount currencyID=""TRY"">100.00</cbc:TaxableAmount><cbc:TaxAmount currencyID=""TRY"">50.00</cbc:TaxAmount><cbc:Percent>50</cbc:Percent>
        <cac:TaxCategory><cac:TaxScheme><cbc:Name>KDV TEVKIFAT</cbc:Name><cbc:TaxTypeCode>624</cbc:TaxTypeCode></cac:TaxScheme></cac:TaxCategory></cac:TaxSubtotal></cac:WithholdingTaxTotal>
    <cac:Item><cbc:Name>Montaj Hizmeti</cbc:Name></cac:Item>
    <cac:Price><cbc:PriceAmount currencyID=""TRY"">500.00</cbc:PriceAmount></cac:Price>
  </cac:InvoiceLine>
</Invoice>";

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void EF1_Onizleme_Alanlari()
    {
        var t = _ef.Onizle(_y, Ornek());
        Assert.Equal(("ABC2026000000123", "1234567890", "Yıldız Hırdavat Ltd. Şti.", "Kadıköy", "TRY"),
            (t.FaturaNo, t.SaticiVkn, t.SaticiUnvan, t.SaticiVergiDairesi, t.ParaBirimi));
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), t.Tarih);
        Assert.Equal(2, t.Satirlar.Count);
        var s1 = t.Satirlar[0];
        Assert.Equal(("Matkap Ucu Seti", "C62", 10m, 100m, 10m, 20m, 0m), (s1.Aciklama, s1.Birim, s1.Miktar, s1.BirimFiyat, s1.IskontoOrani, s1.KdvOrani, s1.TevkifatOrani));
        Assert.Equal(50m, t.Satirlar[1].TevkifatOrani);
        Assert.Equal(1630m, t.HesaplananToplam);                              // 900+180 + 500+100−50
        Assert.Null(t.CariId);
        Assert.False(t.DahaOnceAlindi);
    }

    [Fact]
    public void EF2_Yeni_Satici_Cari_Ve_Tanim_Acar_Fatura_Kaydeder()
    {
        var r = _ef.IceAl(_y, Ornek());
        Assert.False(r.AlreadyExisted);
        Assert.Null(r.StockDocumentId);                                       // stok etkilenmez
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM parties WHERE company_id='EF' AND tax_no='1234567890' AND party_type='supplier' AND supplier_id IS NOT NULL"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM suppliers WHERE company_id='EF' AND name='Yıldız Hırdavat Ltd. Şti.'"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM invoices WHERE company_id='EF' AND external_no='ABC2026000000123' AND direction='purchase'"));
        Assert.Equal(2L, Say($"SELECT COUNT(*) FROM invoice_lines WHERE invoice_id='{r.Id}'"));
    }

    [Fact]
    public void EF3_Ayni_Belge_Ikinci_Kez_Alinamaz()
    {
        var ilk = _ef.IceAl(_y, Ornek());
        var t = _ef.Onizle(_y, Ornek());
        Assert.True(t.DahaOnceAlindi);
        Assert.Contains(t.Uyarilar, u => u.Contains("daha önce"));
        Assert.NotNull(t.CariId);                                             // ilk alımda açılan cariyle eşlendi
        var ikinci = _ef.IceAl(_y, Ornek());
        Assert.True(ikinci.AlreadyExisted);
        Assert.Equal(ilk.Id, ikinci.Id);
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM invoices WHERE company_id='EF'"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM parties WHERE company_id='EF'"));
    }

    [Fact]
    public void EF4_Var_Olan_Cari_VKN_Ile_Eslenir()
    {
        var c = _cari.Create(_y, new NewParty("C-100", "Yıldız (eski kayıt)", PartyTypes.Both, TaxNo: "1234567890"));
        var t = _ef.Onizle(_y, Ornek(uuid: "6f1f6a0e-1111-4c3b-9d8e-000000000004"));
        Assert.Equal(c, t.CariId);
        _ef.IceAl(_y, Ornek(uuid: "6f1f6a0e-1111-4c3b-9d8e-000000000004"));
        Assert.Equal(1L, Say("SELECT COUNT(*) FROM parties WHERE company_id='EF'"));
    }

    [Fact]
    public void EF5_Toplam_Uyarisi_Ve_Bozuk_Dosyalar()
    {
        var t = _ef.Onizle(_y, Ornek(odenecek: 1700m));
        Assert.Contains(t.Uyarilar, u => u.Contains("Hesaplanan toplam"));
        Assert.Throws<ArgumentException>(() => _ef.Onizle(_y, "bu bir xml değil"));
        Assert.Throws<ArgumentException>(() => _ef.Onizle(_y, "<Order xmlns=\"x\"/>"));
        var satirsiz = System.Text.RegularExpressions.Regex.Replace(Ornek(), "<cac:InvoiceLine>[\\s\\S]*?</cac:InvoiceLine>", "");
        Assert.Throws<ArgumentException>(() => _ef.Onizle(_y, satirsiz));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM invoices WHERE company_id='EF'"));
    }

    [Fact]
    public void EF6_Dis_Varlik_Iceren_Dosya_Reddedilir()
    {
        var xxe = "<?xml version=\"1.0\"?><!DOCTYPE Invoice [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]>" +
                  "<Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"><ID>&x;</ID></Invoice>";
        var ex = Assert.Throws<ArgumentException>(() => _ef.Onizle(_y, xxe));
        Assert.Contains("geçerli bir e-Fatura", ex.Message);
    }
}
