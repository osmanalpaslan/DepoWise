namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ EKRAN İÇİ "SON SEÇİM" TERCİH ANAHTARLARI ═══ (ADR-182 · PK-V1=A, 2026-08-29)
///
/// Bazı formlarda kullanıcı hemen her kayıtta AYNI kişiyi/değeri seçer; bunu her seferinde yeniden
/// seçmek gereksiz iştir. Bu anahtarlar, o değerin KİŞİSEL olarak hatırlandığı tercih kaydını adlandırır
/// (<c>UserListPreferenceService.GetLastChoice / SaveLastChoice</c>).
///
/// <b>Neden PAYLAŞIMLI dosya:</b> aynı anahtarı web ve masaüstü ayrı ayrı yazsaydı biri değiştiğinde
/// iki platform sessizce FARKLI tercihleri okurdu (kullanıcı web'de seçtiğini masaüstünde göremezdi ve
/// bunu hata sanardı). Dosya SAF katalogdur — yalnız <c>const</c> içerir, hiçbir bağımlılığı yoktur →
/// web'in "her şeyi API'den al" sınırını gevşetmez.
///
/// <b>MIGRATION YOKTUR:</b> değer mevcut <c>user_list_preferences</c> tablosunda, bu ekrana AYRILMIŞ bir
/// <c>list_key</c> altında saklanır; hiçbir liste ekranının kolon tercihiyle çakışmaz.
/// </summary>
public static class UserPrefKeys
{
    /// <summary>
    /// Yakıt Dağıtımı — "Yakıtı Veren" alanında EN SON seçilen personel.
    /// ⚠️ "Yakıtı Alan" için BİLİNÇLİ OLARAK anahtar YOKTUR: kullanıcı kuralı gereği alan her işlemde
    /// değişken kalır ve ön-seçilmez (bkz. ADR-182 / PK-V1).
    /// </summary>
    public const string FuelGiver = "pref:fuel-giver";
}
