using System.Text.RegularExpressions;

namespace DepoWise.Application.Security;

/// <summary>
/// Log redaction — ham secret/PII loglanmaz (analiz §9). password/token/secret/authorization/
/// connection string/session değerlerini ve Bearer token'ları maskeler. Web `redact.ts` ile aynı.
/// </summary>
public static partial class LogRedactor
{
    private const string Mask = "***";

    // key=value  veya  "key":"value"  (anahtar hassas listede)
    [GeneratedRegex(@"(?i)(password|passwd|pwd|token|secret|authorization|auth|api[_-]?key|connection[_-]?string|conn[_-]?str|session|cookie)(""?\s*[:=]\s*""?)([^""'\s,;}]+)", RegexOptions.None)]
    private static partial Regex KeyValueRegex();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerRegex();

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        // Önce Bearer token'ları (key-value, boşlukta durduğu için token'ı kaçırmasın), sonra key=value
        var s = BearerRegex().Replace(input, "Bearer " + Mask);
        s = KeyValueRegex().Replace(s, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Mask}");
        return s;
    }

    /// <summary>Bir değerin hassas anahtar için olup olmadığını kontrol eder (alan bazlı redaction).</summary>
    public static bool IsSensitiveKey(string key)
        => Regex.IsMatch(key, @"(?i)^(password|passwd|pwd|token|secret|authorization|auth|api[_-]?key|connection[_-]?string|conn[_-]?str|session|cookie)$");
}
