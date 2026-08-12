using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-1c — CARİNİN ÇEVRİMDIŞI/SENKRON KAPSAMI (kullanıcı isteği 2026-08-12).
///
/// <b>🔴 KAPATILAN AÇIK:</b> G4-1/G4-1b turlarında <c>parties</c> ve <c>party_ledger</c> senkron tablo
/// listesine EKLENMEMİŞTİ. Masaüstü çevrimdışı cari açıp elle hareket girebildiği için bu kayıtlar
/// <b>sunucuya HİÇ ulaşmıyordu</b>: web'de görünmüyor, ikinci makineye gitmiyordu. Masaüstünün
/// çevrimdışı çalışması projenin temel gereksinimidir (kullanıcı kuralı 12) — bu yüzden gerçek bir
/// veri kaybı yoluydu.
///
/// Bu testler kapsamı KİLİTLER: biri listeden düşerse test kırılır.
/// </summary>
public class PartySyncTests
{
    /// <summary>1 — Cari tabloları senkronda TAŞINIR.</summary>
    [Fact]
    public void S1_Cari_Tablolari_Senkron_Listesinde()
    {
        Assert.Contains("parties", BusinessSyncService.Tables);
        Assert.Contains("party_ledger", BusinessSyncService.Tables);
    }

    /// <summary>2 — ⭐ SIRA: <c>parties</c> ÖNCE gitmeli — <c>party_ledger.party_id</c> onu referans alır.
    /// Ters sırada, sunucuda henüz olmayan bir cariye hareket yazılmaya çalışılırdı.</summary>
    [Fact]
    public void S2_Parties_Ledgerdan_Once_Gonderilir()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.True(t.IndexOf("parties") < t.IndexOf("party_ledger"),
            "parties, party_ledger'dan ÖNCE gönderilmelidir (yabancı anahtar sırası).");
    }

    /// <summary>3 — Senkron yolu YETKİ KAPISINI ATLAMAZ: cari tabloları "parties" modülüne bağlıdır,
    /// yani kullanıcı ancak cari Create/Edit yetkisi varsa push edebilir.</summary>
    [Fact]
    public void S3_Cari_Tablolari_Parties_Yetkisine_Bagli()
    {
        Assert.Equal(PartyService.Module, BusinessSyncService.ModuleOf("parties"));
        Assert.Equal(PartyService.Module, BusinessSyncService.ModuleOf("party_ledger"));
    }

    /// <summary>4 — Türetilmiş veri senkronda TAŞINMAZ: cari bakiyesi saklanmadığı için taşınacak
    /// bir bakiye tablosu YOKTUR (stok tarafındaki <c>stock_balances</c> kararının cari karşılığı).</summary>
    [Fact]
    public void S4_Turetilmis_Bakiye_Senkronda_Yok()
    {
        Assert.DoesNotContain("party_balances", BusinessSyncService.Tables);
        Assert.DoesNotContain("stock_balances", BusinessSyncService.Tables);
    }
}
