using DepoWise.Application.Security;

namespace DepoWise.Infrastructure.Accounting;

/// <summary>
/// ═══ FAZ 3b (ADR-223) — ÖN MUHASEBE ALAN KAPISI ═══
///
/// Ön muhasebenin dört servisi (cari, cari defteri, fatura, kasa/banka) aynı üç soruyu sorar.
/// Soru burada TEK kez yazılır; her serviste tekrarlanan bir <c>FieldAccess.Gorunur(...)</c>
/// zinciri oluşmaz ve ekran/alan anahtarı yazım hatasına açık kalmaz.
///
/// <b>Karar burada VERİLMEZ</b> — <see cref="FieldAccess"/> verir; burası yalnız doğru anahtarı
/// geçirir. Alan korumalı değilse (varsayılan) hepsi <c>true</c> döner → bugünkü davranış.
///
/// <b>Kullanım kuralı:</b> bu metotlar <b>sorgu başına bir kez</b> çağrılır, satır döngüsünün
/// İÇİNDE değil (talimat §37). Karar oturuma bağlıdır, satır verisine değil.
/// </summary>
internal static class AccountingFieldGate
{
    /// <summary>Cari borç / alacak / bakiye görünür mü?</summary>
    internal static bool CariBakiye(SessionContext s)
        => FieldAccess.Gorunur(s, FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance);

    /// <summary>Fatura genel toplamı görünür mü?</summary>
    internal static bool FaturaTutari(SessionContext s)
        => FieldAccess.Gorunur(s, FieldProtectionCatalog.Invoices, FieldProtectionCatalog.GrandTotal);

    /// <summary>Kasa/banka hareket tutarı görünür mü?</summary>
    internal static bool HareketTutari(SessionContext s)
        => FieldAccess.Gorunur(s, FieldProtectionCatalog.Finance, FieldProtectionCatalog.Amount);

    /// <summary>Kasa/banka hesap bakiyesi (giriş/çıkış toplamı) görünür mü?</summary>
    internal static bool HesapBakiyesi(SessionContext s)
        => FieldAccess.Gorunur(s, FieldProtectionCatalog.Finance, FieldProtectionCatalog.Balance);

    /// <summary>⭐ FAZ 3c-2: MALZEME birim fiyatı görünür mü? Fatura satırı, malzemenin birim
    /// fiyatını taşır; bu yüzden ön muhasebe de aynı karara bakmak zorundadır (kaçak kanal).</summary>
    internal static bool MalzemeBirimFiyati(SessionContext s)
        => FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);
}
