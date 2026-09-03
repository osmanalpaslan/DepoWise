using System;

namespace DepoWise.Application.Setup;

/// <summary>
/// ═══ İNDİRME ADRESİ KAPISI (2026-09-04) ═══
///
/// Kurulum aracı, indirme adresini SUNUCUNUN yanıtından alıyor (<c>downloadUrl</c>). Eskiden bu adres
/// hiç denetlenmiyordu: göreli değilse olduğu gibi kullanılıyordu. Bugün sunucu göreli yol döndürüyor,
/// ama savunma katmanı yoktu — sunucu yanıtı bozulursa/değişirse kurulum aracı başka bir host'tan
/// (hatta düz HTTP ile) paket indirebilirdi.
///
/// Kural: <b>yalnız HTTPS</b> ve <b>yalnız kurulum aracına gömülü sunucunun host'u</b>.
/// Bu, <see cref="SetupPackageVerifier"/> ile birlikte iki katmanlı koruma sağlar: yanlış yerden
/// indirilemez (bu sınıf) ve yanlış içerik kurulamaz (checksum kapısı).
/// </summary>
public static class SetupUrlPolicy
{
    /// <summary>
    /// Sunucudan gelen adresi mutlak, güvenli bir adrese çevirir.
    /// Göreli ("/api/...") ise sunucu köküne eklenir. Mutlaksa host ve şema DOĞRULANIR.
    /// </summary>
    /// <param name="serverBaseUrl">Kurulum aracına derleme zamanında gömülen sunucu adresi.</param>
    /// <param name="urlFromServer">Sunucunun bildirdiği indirme adresi (göreli ya da mutlak).</param>
    /// <exception cref="SetupVerificationException">Adres güvenli değilse.</exception>
    public static Uri ResolveDownloadUrl(string serverBaseUrl, string? urlFromServer)
    {
        if (string.IsNullOrWhiteSpace(urlFromServer))
            throw new SetupVerificationException("ADRES_YOK",
                "Sunucu paket indirme adresini bildirmedi. Kurulum iptal edildi.");

        if (!Uri.TryCreate(serverBaseUrl?.TrimEnd('/'), UriKind.Absolute, out var baseUri))
            throw new SetupVerificationException("SUNUCU_ADRESI_GECERSIZ",
                "Kurulum aracına tanımlı sunucu adresi geçersiz. Kurulum iptal edildi.");

        var raw = urlFromServer.Trim();

        // Göreli adres → sunucu köküne ekle (bugünkü normal durum: "/api/releases/1.0.171/download")
        if (raw.StartsWith('/'))
            return Guvenli(new Uri(baseUri, raw), baseUri);

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var abs))
            throw new SetupVerificationException("ADRES_GECERSIZ",
                "Sunucunun bildirdiği indirme adresi geçersiz. Kurulum iptal edildi.");

        return Guvenli(abs, baseUri);
    }

    private static Uri Guvenli(Uri candidate, Uri baseUri)
    {
        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new SetupVerificationException("SEMA_GUVENSIZ",
                "Kurulum paketi yalnız güvenli bağlantı (HTTPS) üzerinden indirilebilir. Kurulum iptal edildi.");

        if (!string.Equals(candidate.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            throw new SetupVerificationException("HOST_IZINSIZ",
                "Kurulum paketi beklenmeyen bir adresten indirilmek istendi. " +
                "Güvenlik gereği kurulum iptal edildi.");

        return candidate;
    }
}
