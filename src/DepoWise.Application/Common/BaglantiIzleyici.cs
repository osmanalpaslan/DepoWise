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

/// <summary>
/// ⭐ M-11 (AR-GE 2026-10-18, 05 F-041) — WEB KULLANICISININ GERÇEK ADRESİ.
///
/// <para><b>Sorun.</b> Web sunucusu API'yi kendi adresinden çağırır → API tüm web kullanıcılarını TEK adres görür; giriş
/// sınırı (30 deneme / 5 dk / adres) dolunca bir kişinin hataları (ya da sabah toplu giriş) HERKESİ durdurur.</para>
///
/// <para><b>Çözüm.</b> Web, kullanıcının adresini ve o adresin ortak gizli anahtarla imzasını (HMAC-SHA256) başlıkla iletir.
/// API yalnız imza tutarsa bu adrese güvenir; anahtar tanımlı değilse ya da imza tutmazsa başlık YOK SAYILIR (bugünkü davranış).
/// Anahtar iki uygulamada aynı ortam değişkeninde durur. Bu dosya web'e bağlı (csproj Compile Include) → iki taraf aynı kodu kullanır.</para>
/// </summary>
public static class IstemciAdresiImzasi
{
    public const string AdresBasligi = "X-DepoWise-Istemci-IP";
    public const string ImzaBasligi = "X-DepoWise-Istemci-Imza";
    public const string AnahtarDegiskeni = "DEPOWISE_ISTEMCI_IP_ANAHTARI";

    /// <summary>Ortam değişkenindeki anahtar; yoksa ya da 16 karakterden kısaysa null (özellik kapalı).</summary>
    public static string? Anahtar()
    {
        var k = Environment.GetEnvironmentVariable(AnahtarDegiskeni);
        return string.IsNullOrWhiteSpace(k) || k.Trim().Length < 16 ? null : k.Trim();
    }

    public static string Imzala(string anahtar, string adres)
        => Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(anahtar), System.Text.Encoding.UTF8.GetBytes(adres)));

    /// <summary>İmza tutuyor ve değer geçerli bir IP adresiyse adresi, değilse null döndürür (sabit süreli karşılaştırma).</summary>
    public static string? Dogrula(string? anahtar, string? adres, string? imza)
    {
        if (anahtar is null || string.IsNullOrWhiteSpace(adres) || string.IsNullOrWhiteSpace(imza)) return null;
        adres = adres.Trim();
        if (adres.Length > 64 || !System.Net.IPAddress.TryParse(adres, out _)) return null;
        var beklenen = System.Text.Encoding.ASCII.GetBytes(Imzala(anahtar, adres));
        var gelen = System.Text.Encoding.ASCII.GetBytes(imza.Trim().ToUpperInvariant());
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(beklenen, gelen) ? adres : null;
    }
}
