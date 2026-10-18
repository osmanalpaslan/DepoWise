using System.Net;
using System.Net.Http;

namespace DepoWise.Infrastructure.Net;

/// <summary>
/// ⭐ M-84 (AR-GE 2026-10-17) — SIKIŞTIRMALI HTTP İSTEMCİSİ.
///
/// <b>Bulgu.</b> API yanıt sıkıştırması (SNK-08, gzip/Brotli) sunucuda AÇIKTI ama hiçbir istemci <c>Accept-Encoding</c>
/// göndermiyordu: .NET'in düz <c>new HttpClient()</c>'ı sıkıştırma İSTEMEZ ve açmaz. Sonuç: masaüstünün eşitleme paketleri
/// (ilk kurulumda yüzlerce MB JSON) hep sıkıştırılmadan iniyordu. Bu istemci sıkıştırmayı ister ve kendiliğinden açar —
/// çağıran kod hiçbir şey değiştirmez (yanıt gövdesi yine düz metin okunur).
/// </summary>
public static class HttpIstemci
{
    /// <summary>Sıkıştırmayı isteyen/açan bağlantı işleyicisi (testte de doğrulanır).</summary>
    public static SocketsHttpHandler Isleyici() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        // Uzun ömürlü statik istemcilerde DNS değişikliği (Fly makine/IP değişimi) yakalansın.
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    };

    /// <summary>⭐ M-12 (AR-GE 2026-10-18): masaüstü açılışta kendi program sürümünü yazar; bundan sonra oluşturulan her istemci
    /// isteğe sürüm başlığını ekler → sunucu desteklenmeyen sürümün veri yazmasını durdurabilir. Null → başlık yok.</summary>
    public static string? UygulamaSurumu { get; set; }

    public static HttpClient Yeni(TimeSpan zamanAsimi)
    {
        var c = new HttpClient(Isleyici()) { Timeout = zamanAsimi };
        if (!string.IsNullOrWhiteSpace(UygulamaSurumu))
            c.DefaultRequestHeaders.TryAddWithoutValidation(DepoWise.Application.Update.GuncellemePolitikasi.SurumBasligi, UygulamaSurumu);
        return c;
    }
}
