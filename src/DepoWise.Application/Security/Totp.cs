using System.Security.Cryptography;
using System.Text;

namespace DepoWise.Application.Security;

/// <summary>
/// ═══ M-01 (K-20, 2026-10-27) — DOĞRULAMA UYGULAMASI KODU (TOTP, RFC 6238) ═══
/// Google Authenticator / Microsoft Authenticator ile uyumlu: HMAC-SHA1, 6 hane, 30 saniyelik adım. Saat farkı için bir önceki
/// ve bir sonraki adım da kabul edilir. Aynı kodun ikinci kez kullanılmasını, ÇAĞIRAN son kullanılan adımı saklayarak engeller
/// (<see cref="Dogrula"/> yalnız <c>sonAdim</c>'dan BÜYÜK adımı kabul eder).
/// </summary>
public static class Totp
{
    public const int AdimSaniye = 30;
    public const int Hane = 6;
    /// <summary>Saat kayması toleransı (adım). ±1 = ±30 sn.</summary>
    public const int Pencere = 1;

    public static long Adim(DateTimeOffset an) => an.ToUnixTimeSeconds() / AdimSaniye;

    /// <summary>RFC 4226 HOTP: verilen adım için 6 haneli kod.</summary>
    public static string Uret(byte[] gizli, long adim)
    {
        Span<byte> sayac = stackalloc byte[8];
        for (int i = 7; i >= 0; i--) { sayac[i] = (byte)(adim & 0xFF); adim >>= 8; }
        var h = HMACSHA1.HashData(gizli, sayac);
        int o = h[^1] & 0x0F;
        int ikili = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (ikili % 1_000_000).ToString("D6");
    }

    /// <summary>Kod geçerliyse eşleşen ADIMI döndürür (çağıran bunu "son kullanılan" olarak saklar); değilse null.
    /// <paramref name="sonAdim"/> ve öncesindeki adımlar REDDEDİLİR (aynı kod ikinci kez kullanılamaz).</summary>
    public static long? Dogrula(byte[] gizli, string? kod, DateTimeOffset an, long sonAdim)
    {
        var k = Temizle(kod);
        if (k.Length != Hane || !k.All(char.IsAsciiDigit)) return null;
        var simdi = Adim(an);
        for (long a = simdi - Pencere; a <= simdi + Pencere; a++)
        {
            if (a <= sonAdim) continue;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Uret(gizli, a)), Encoding.ASCII.GetBytes(k)))
                return a;
        }
        return null;
    }

    /// <summary>Boşluk ve tireyi atar ("123 456" / "ABCD-EFGH" yazılabilsin).</summary>
    public static string Temizle(string? kod) => new((kod ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    /// <summary>Uygulamanın QR ile okuduğu adres (otpauth://totp/...).</summary>
    public static string OtpAuthAdresi(string yayinci, string hesap, string gizliBase32)
        => $"otpauth://totp/{Uri.EscapeDataString(yayinci)}:{Uri.EscapeDataString(hesap)}" +
           $"?secret={gizliBase32}&issuer={Uri.EscapeDataString(yayinci)}&algorithm=SHA1&digits={Hane}&period={AdimSaniye}";

    private const string Alfabe = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>RFC 4648 Base32 (dolgusuz) — uygulamalara elle girilen gizli anahtar biçimi.</summary>
    public static string Base32(byte[] veri)
    {
        var sb = new StringBuilder();
        int tampon = 0, bit = 0;
        foreach (var b in veri)
        {
            tampon = (tampon << 8) | b; bit += 8;
            while (bit >= 5) { sb.Append(Alfabe[(tampon >> (bit - 5)) & 31]); bit -= 5; }
        }
        if (bit > 0) sb.Append(Alfabe[(tampon << (5 - bit)) & 31]);
        return sb.ToString();
    }

    public static byte[] Base32Coz(string metin)
    {
        var t = Temizle(metin).TrimEnd('=').ToUpperInvariant();
        var cikti = new List<byte>();
        int tampon = 0, bit = 0;
        foreach (var c in t)
        {
            var i = Alfabe.IndexOf(c);
            if (i < 0) throw new FormatException("Geçersiz Base32 karakteri.");
            tampon = (tampon << 5) | i; bit += 5;
            if (bit >= 8) { cikti.Add((byte)((tampon >> (bit - 8)) & 0xFF)); bit -= 8; }
        }
        return cikti.ToArray();
    }
}
