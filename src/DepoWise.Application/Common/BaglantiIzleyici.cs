namespace DepoWise.Application.Common;

/// <summary>
/// ⭐ BAG-01 (denetim 2026-08-26) — "SUNUCUYA ULAŞILAMIYOR" KARARI.
///
/// <para><b>Neden ayrı sınıf:</b> web arayüzü (Blazor) birim testinden çalıştırılamaz — web projesi
/// ortak dosyaların aynasını derlediği için test projesine referans verilemez. Bu yüzden <b>kararın
/// kendisi</b> (neyin "ulaşılamıyor" sayılacağı) buraya alınmıştır; <c>ApiClient</c> yalnız bunu kullanır.
/// Böylece riskli olan kısım — <b>ağ hatası ile yetki hatasının ayrımı</b> — gerçekten test edilir.</para>
///
/// <para><b>Kural (bilinçli olarak dar):</b></para>
/// <list type="bullet">
///   <item>Yalnız <b>taşıma katmanı</b> hatası "ulaşılamıyor"dur: bağlantı kurulamadı ya da zaman aşımı.</item>
///   <item>Sunucudan BİR YANIT geldiyse — <b>401/403/404/500 dahil</b> — bağlantı VARDIR. Bunlar
///     uygulama hatasıdır ve bağlantı uyarısı göstermezler. (Aksi halde oturumu biten kullanıcıya
///     "internetiniz yok" denirdi.)</item>
///   <item>Bu sınıf <b>oturum yönetimine dokunmaz</b>: hiçbir yerde çıkış yaptırmaz.</item>
///   <item>Olay yalnız durum <b>DEĞİŞTİĞİNDE</b> tetiklenir → her istekte gereksiz arayüz çizimi olmaz.</item>
/// </list>
/// </summary>
public sealed class BaglantiIzleyici
{
    /// <summary>Son denemede sunucuya ulaşılamadıysa <c>true</c>.</summary>
    public bool Ulasilamiyor { get; private set; }

    /// <summary>Yalnız durum değiştiğinde tetiklenir.</summary>
    public event Action? Degisti;

    /// <summary>
    /// Bir isteği çalıştırır ve sonucuna göre bağlantı durumunu günceller.
    /// İstisnayı <b>yutmaz</b> — çağıranların mevcut davranışı değişmez.
    /// </summary>
    public async Task<T> Calistir<T>(Func<Task<T>> istek)
    {
        try
        {
            var sonuc = await istek();
            Ayarla(false);              // yanıt geldi (hata kodu olsa bile) → bağlantı var
            return sonuc;
        }
        catch (Exception ex) when (TasimaHatasi(ex))
        {
            Ayarla(true);
            throw;
        }
    }

    /// <summary>
    /// Bu istisna "sunucuya ulaşılamadı" anlamına mı geliyor?
    /// <c>HttpRequestException</c> = bağlantı kurulamadı · <c>TaskCanceledException</c> = zaman aşımı.
    /// Başka her istisna (JSON, doğrulama, iş kuralı) bağlantı sorunu SAYILMAZ.
    ///
    /// <para>⚠️ <b><c>TaskCanceledException</c> neden güvenle "zaman aşımı" sayılıyor:</b> web istemcisi
    /// (<c>ApiClient</c>) hiçbir isteğe <c>CancellationToken</c> GEÇİRMEZ (doğrulandı: dosyada hiç
    /// <c>CancellationToken</c> yok). Dolayısıyla bu istisnanın tek kaynağı <c>HttpClient.Timeout</c>'tur;
    /// kullanıcının sayfadan ayrılması yanlışlıkla "internet yok" uyarısı üretemez. İleride isteklere
    /// iptal jetonu eklenirse bu ayrım yeniden gözden geçirilmelidir.</para>
    /// </summary>
    public static bool TasimaHatasi(Exception ex)
        => ex is HttpRequestException || ex is TaskCanceledException;

    private void Ayarla(bool ulasilamiyor)
    {
        if (Ulasilamiyor == ulasilamiyor) return;
        Ulasilamiyor = ulasilamiyor;
        Degisti?.Invoke();
    }
}
