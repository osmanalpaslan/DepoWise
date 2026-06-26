using System.Security.Cryptography;

namespace DepoWise.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 parola hash. Biçim: pbkdf2$sha256$&lt;iter&gt;$&lt;saltB64&gt;$&lt;hashB64&gt;.
/// Web tarafı (lib/security/password.ts) AYNI biçim/algoritmayı kullanır → fonksiyonel parite.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2$sha256";

    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Parola boş olamaz.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256") return false;
        if (!int.TryParse(parts[2], out var iter) || iter < 1) return false;
        byte[] salt, expected;
        try { salt = Convert.FromBase64String(parts[3]); expected = Convert.FromBase64String(parts[4]); }
        catch { return false; }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
