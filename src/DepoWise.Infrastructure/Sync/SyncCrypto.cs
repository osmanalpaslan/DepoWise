using System.Security.Cryptography;
using System.Text;

namespace DepoWise.Infrastructure.Sync;

public static class SyncCrypto
{
    public static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Kriptografik rastgele anahtar (tek-kullanımlık enrollment / cihaz token).</summary>
    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
}
