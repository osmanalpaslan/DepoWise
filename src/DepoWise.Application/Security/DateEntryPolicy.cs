using DepoWise.Application.Common;

namespace DepoWise.Application.Security;

/// <summary>
/// ═══ TRH-01 — GERİ / İLERİ TARİHLİ İŞLEM KAPISI ═══ (kullanıcı isteği 2026-08-27)
///
/// <b>Ayrım.</b> Bir kaydın İKİ tarihi vardır ve bunlar birbirinden bağımsızdır:
/// <list type="bullet">
///   <item><b>İşlem tarihi</b> (iş günü — <c>doc_date</c>, <c>entry_date</c>, <c>performed_date</c>…):
///   işin GERÇEKTEN yapıldığı gün. İş gereği geçmiş ya da gelecek olabilir (dün gelen malı bugün
///   girmek gibi). Raporlar bu tarihe göre süzer.</item>
///   <item><b>Kayıt anı</b> (<c>created_at</c>): kaydın sisteme GİRİLDİĞİ an. Daima gerçek saattir,
///   kullanıcı değiştiremez. Log/denetim bunu gösterir → geçmişe kayıt girilse bile "ne zaman
///   girildiği" izlenebilir kalır.</item>
/// </list>
///
/// <b>Bu sınıfın işi.</b> İşlem tarihini BUGÜNDEN farklı bir güne taşımak bir YETKİDİR
/// (<see cref="SpecialButtons.BackDate"/>). Arayüzde alan kilitlenir, ama arayüz kilidi güvenlik
/// değildir — asıl kapı burasıdır ve hem masaüstü hem API aynı servis üzerinden buradan geçer.
///
/// <b>Neden reddetmek yerine "şimdi"ye çekiyor.</b> Yetkisiz bir istekte hata fırlatmak, saat dilimi
/// farkı yüzünden MEŞRU bir aynı-gün kaydını da reddedebilirdi (istemci yerel gece yarısını gönderir;
/// sunucu UTC'dedir). Sessizce "şimdi"ye çekmek fail-closed'dır: yetkisiz kimse farklı bir iş gününe
/// kayıt açamaz, meşru kullanıcı da hiçbir şey kaybetmez — çünkü ikisi de AYNI iş gününe düşer.
/// </summary>
public static class DateEntryPolicy
{
    /// <summary>
    /// İstenen işlem tarihini yetkiye göre süzer.
    /// </summary>
    /// <param name="s">Oturum (yetki buradan okunur).</param>
    /// <param name="istenen">İstemcinin gönderdiği işlem tarihi; <c>null</c> = "şimdi".</param>
    /// <returns>Yetki varsa istenen tarih; yoksa <c>null</c> (çağıran "şimdi"yi kullanır).</returns>
    public static long? Uygula(SessionContext s, long? istenen)
    {
        if (istenen is null) return null;                                   // zaten "şimdi"
        return AccessControl.CanUseButton(s, SpecialButtons.BackDate) ? istenen : null;
    }

    /// <summary>Kullanıcı işlem tarihini değiştirebilir mi — arayüz alanı buna göre kilitlenir.
    /// Sunucu kararı <see cref="Uygula"/>'dadır; bu yalnız görünüm içindir.</summary>
    public static bool Serbest(SessionContext s) => AccessControl.CanUseButton(s, SpecialButtons.BackDate);
}
