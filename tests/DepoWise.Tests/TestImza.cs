using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DepoWise.Application.Update;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ M-01 (2026-10-27) — testler için paket imza anahtarı. ÜRETİM gizli anahtarı testlerde YOKTUR (yalnız yayın bilgisayarında);
/// testler kendi anahtarını üretir ve açık anahtarını <see cref="PaketImzasi.EkAnahtarlar"/>'a (yalnız test derlemesinin
/// erişebildiği iç liste) ekler. Ayrıca Node imzalayıcısıyla (scripts/lib/yayin_ortak.mjs) üretilmiş sabit bir örneğin açık
/// anahtarı eklenir — Node'un imzası ile C# doğrulamasının uyumu böylece her koşuda sınanır.
/// </summary>
internal static class TestImza
{
    private static readonly ECDsa Anahtar = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>Node ile üretilmiş örnek (test anahtarı): imzaMetni("1.2.3", "ab"×32).</summary>
    public const string NodeAcikAnahtar =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEg34mVcZ/AR6eJN4XHTP1Snglh0tUQj2XG3Kb49+903tv0ClaPaRL65tFXAvWnFGdlbOoAoDwXRh7t5He9gAGKA==";
    public const string NodeImza =
        "MEQCIEbQ2sQ7pozs0h51A+Py9fOZZFtJ+rnyLlB7HGHS1LLSAiBMUMRtbzQQTtDhRQDR0NBlhD1DpbzERIa+isMxsAm6dw==";

    [ModuleInitializer]
    internal static void Kaydet()
    {
        lock (PaketImzasi.EkAnahtarlar)
        {
            PaketImzasi.EkAnahtarlar.Add(Convert.ToBase64String(Anahtar.ExportSubjectPublicKeyInfo()));
            PaketImzasi.EkAnahtarlar.Add(NodeAcikAnahtar);
        }
    }

    /// <summary>Sürüm + özet için test anahtarıyla imza (base64, DER) — üretim imzalayıcısıyla aynı biçim.</summary>
    public static string Imzala(string surum, string sha256Hex)
        => Convert.ToBase64String(Anahtar.SignData(Encoding.UTF8.GetBytes(PaketImzasi.ImzaMetni(surum, sha256Hex)),
            HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

    /// <summary>İmzalı test paketi tanımı (özet içerikten hesaplanır).</summary>
    public static UpdatePackage Paket(string surum, byte[] icerik, string min = "0.0.0", string? notlar = null)
    {
        var sha = Convert.ToHexString(SHA256.HashData(icerik));
        return new UpdatePackage(surum, sha, icerik.Length, min, notlar, true, "/x", Imzala(surum, sha));
    }

    /// <summary>İçeriği önemsiz politika testleri için: sabit özetli imzalı paket.</summary>
    public static UpdatePackage Paket(string surum, string min = "0.0.0")
    {
        var sha = new string('A', 64);
        return new UpdatePackage(surum, sha, 1, min, null, true, "/x", Imzala(surum, sha));
    }
}
