using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-2 — FATURANIN ÇEVRİMDIŞI/SENKRON KAPSAMI (kullanıcı isteği 2026-08-12).
///
/// Masaüstü çevrimdışı fatura kesebildiği için fatura tabloları senkronda TAŞINMAK ZORUNDA;
/// aksi halde çevrimdışı kesilen fatura sunucuya HİÇ ulaşmaz (web'de görünmez, ikinci makineye
/// gitmez) — G4-1c'de cari için kapatılan açığın aynısı.
///
/// Bu testler kapsamı ve SIRAYI kilitler: biri bozulursa test kırılır.
/// </summary>
public class InvoiceSyncTests
{
    /// <summary>1 — Fatura tabloları senkronda TAŞINIR.</summary>
    [Fact]
    public void V1_Fatura_Tablolari_Senkron_Listesinde()
    {
        Assert.Contains("invoices", BusinessSyncService.Tables);
        Assert.Contains("invoice_lines", BusinessSyncService.Tables);
        Assert.Contains("invoice_series", BusinessSyncService.Tables);
        Assert.Contains("vat_rates", BusinessSyncService.Tables);
    }

    /// <summary>
    /// 2 — ⭐ SIRA (yabancı anahtar): seri ÖNCE, sonra fatura, EN SON satırlar.
    /// Ters sırada, sunucuda henüz olmayan bir faturaya satır yazılmaya çalışılırdı.
    /// </summary>
    [Fact]
    public void V2_FK_Sirasi_Dogru()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("invoice_series") < t.IndexOf("invoices"),
            "invoice_series, invoices'tan ÖNCE gönderilmelidir.");
        Assert.True(t.IndexOf("invoices") < t.IndexOf("invoice_lines"),
            "invoices, invoice_lines'tan ÖNCE gönderilmelidir.");
    }

    /// <summary>
    /// 3 — Fatura, KAYNAKLARINDAN sonra gider: cari ve malzeme sunucuda zaten olmalı
    /// (fatura ikisini de referans alır).
    /// </summary>
    [Fact]
    public void V3_Fatura_Cari_Ve_Malzemeden_Sonra()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("parties") < t.IndexOf("invoices"),
            "parties, invoices'tan ÖNCE gönderilmelidir.");
        Assert.True(t.IndexOf("materials") < t.IndexOf("invoice_lines"),
            "materials, invoice_lines'tan ÖNCE gönderilmelidir.");
    }

    /// <summary>
    /// 4 — Senkron yolu YETKİ KAPISINI ATLAMAZ: fatura tabloları "invoices" modülüne bağlıdır,
    /// yani kullanıcı ancak fatura Create/Edit yetkisi varsa push edebilir.
    /// Cari yetkisi fatura push etmeye YETMEZ (modüller ayrı).
    /// </summary>
    [Fact]
    public void V4_Fatura_Tablolari_Invoices_Yetkisine_Bagli()
    {
        Assert.Equal(InvoiceService.Module, BusinessSyncService.ModuleOf("invoices"));
        Assert.Equal(InvoiceService.Module, BusinessSyncService.ModuleOf("invoice_lines"));
        Assert.Equal(InvoiceService.Module, BusinessSyncService.ModuleOf("invoice_series"));
        Assert.Equal(InvoiceService.Module, BusinessSyncService.ModuleOf("vat_rates"));
        Assert.NotEqual(PartyService.Module, BusinessSyncService.ModuleOf("invoices"));
    }

    /// <summary>
    /// 5 — Faturanın stok ve cari etkisi KENDİ tablolarıyla taşınır; fatura senkronda bunları
    /// YENİDEN ÜRETMEZ. Yani sunucu bir faturayı uygularken ikinci bir cari borcu veya ikinci bir
    /// stok hareketi oluşmaz — etkiler zaten party_ledger ve stock_movements ile geldi.
    /// </summary>
    [Fact]
    public void V5_Etkiler_Kendi_Tablolariyla_Tasinir()
    {
        Assert.Contains("party_ledger", BusinessSyncService.Tables);
        Assert.Contains("stock_movements", BusinessSyncService.Tables);
        // Türetilmiş bakiye tabloları senkronda YOK (stok kuralının aynısı).
        Assert.DoesNotContain("stock_balances", BusinessSyncService.Tables);
        Assert.DoesNotContain("invoice_totals", BusinessSyncService.Tables);
    }
}
