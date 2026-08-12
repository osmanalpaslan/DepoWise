using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-3 — KASA/BANKANIN ÇEVRİMDIŞI/SENKRON KAPSAMI (kullanıcı isteği 2026-08-12).
///
/// <b>NEDEN KRİTİK:</b> G4-1'de <c>parties</c>/<c>party_ledger</c> senkron listesine eklenmemişti ve
/// çevrimdışı girilen cari kayıtları sunucuya HİÇ ulaşmıyordu (gerçek veri kaybı yolu). Kullanıcı
/// bu hatanın G4-3'te TEKRARLANMAMASINI açıkça istedi. Bu testler kapsamı ve SIRAYI kilitler.
/// </summary>
public class FinanceSyncTests
{
    /// <summary>1 — Kasa/banka tabloları senkronda TAŞINIR.</summary>
    [Fact]
    public void F1_Finans_Tablolari_Senkron_Listesinde()
    {
        Assert.Contains("finance_accounts", BusinessSyncService.Tables);
        Assert.Contains("finance_transactions", BusinessSyncService.Tables);
        Assert.Contains("invoice_allocations", BusinessSyncService.Tables);
    }

    /// <summary>
    /// 2 — ⭐ SIRA (yabancı anahtar): hesap tanımları ÖNCE, sonra hareketler.
    /// Ters sırada, sunucuda henüz olmayan bir hesaba hareket yazılmaya çalışılırdı.
    /// </summary>
    [Fact]
    public void F2_Hesap_Hareketten_Once()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("finance_accounts") < t.IndexOf("finance_transactions"),
            "finance_accounts, finance_transactions'tan ÖNCE gönderilmelidir.");
    }

    /// <summary>
    /// 3 — ⭐ Fatura kapaması EN SON gider: hem faturaya hem para hareketine bağlıdır.
    /// </summary>
    [Fact]
    public void F3_Kapama_En_Son()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("invoices") < t.IndexOf("invoice_allocations"),
            "invoices, invoice_allocations'tan ÖNCE gönderilmelidir.");
        Assert.True(t.IndexOf("finance_transactions") < t.IndexOf("invoice_allocations"),
            "finance_transactions, invoice_allocations'tan ÖNCE gönderilmelidir.");
    }

    /// <summary>
    /// 4 — Para hareketi KAYNAKLARINDAN sonra gider: cari kartı ve cari defteri sunucuda zaten olmalı
    /// (finance_transactions ikisine de referans verir).
    /// </summary>
    [Fact]
    public void F4_Hareket_Cari_Ve_Defterden_Sonra()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("parties") < t.IndexOf("finance_transactions"),
            "parties, finance_transactions'tan ÖNCE gönderilmelidir.");
        Assert.True(t.IndexOf("party_ledger") < t.IndexOf("finance_transactions"),
            "party_ledger, finance_transactions'tan ÖNCE gönderilmelidir.");
    }

    /// <summary>
    /// 5 — Senkron yolu YETKİ KAPISINI ATLAMAZ: kasa/banka tabloları "finance" modülüne bağlıdır.
    /// Fatura veya cari yetkisi TEK BAŞINA para hareketi push etmeye yetmez (modüller ayrı).
    /// </summary>
    [Fact]
    public void F5_Finans_Tablolari_Finance_Yetkisine_Bagli()
    {
        Assert.Equal(FinanceService.Module, BusinessSyncService.ModuleOf("finance_accounts"));
        Assert.Equal(FinanceService.Module, BusinessSyncService.ModuleOf("finance_transactions"));
        Assert.Equal(FinanceService.Module, BusinessSyncService.ModuleOf("invoice_allocations"));
        Assert.NotEqual(InvoiceService.Module, BusinessSyncService.ModuleOf("finance_transactions"));
        Assert.NotEqual(PartyService.Module, BusinessSyncService.ModuleOf("finance_transactions"));
    }

    /// <summary>
    /// 6 — Türetilmiş veri senkronda TAŞINMAZ: ne hesap bakiyesi ne faturanın "ödenen" tutarı
    /// saklandığı için taşınacak bir tablo YOKTUR (stock_balances kararının finansal karşılığı).
    /// </summary>
    [Fact]
    public void F6_Turetilmis_Bakiye_Senkronda_Yok()
    {
        Assert.DoesNotContain("finance_balances", BusinessSyncService.Tables);
        Assert.DoesNotContain("account_balances", BusinessSyncService.Tables);
        Assert.DoesNotContain("stock_balances", BusinessSyncService.Tables);
    }
}
