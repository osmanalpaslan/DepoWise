namespace DepoWise.Application.Files;

public sealed record FileValidationResult(bool Ok, string? Error, string? DetectedMime, string? DetectedExt);

/// <summary>
/// Dosya/fotoğraf güvenlik doğrulaması (analiz §6.16/§9): boyut ≤7MB, izinli MIME, MAGIC-BYTE eşleşmesi
/// (uzantı doğru olsa bile sahte içerik reddedilir), güvenli dosya adı. Web `fileValidation.ts` ile aynı.
/// </summary>
public static class FileValidation
{
    public const int MaxBytes = 7 * 1024 * 1024; // 7 MB

    private static readonly HashSet<string> AllowedMimes = new(StringComparer.OrdinalIgnoreCase)
    { "image/jpeg", "image/png" };

    /// <summary>Magic-byte ile gerçek tipi tespit eder (uzantıya/declared MIME'a güvenmez).</summary>
    public static (string Mime, string Ext)? DetectImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("image/jpeg", "jpg");
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return ("image/png", "png");
        return null;
    }

    public static FileValidationResult ValidateImage(string? fileName, string? declaredMime, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return new(false, "Boş dosya.", null, null);
        if (bytes.Length > MaxBytes) return new(false, $"Dosya 7 MB sınırını aşıyor ({bytes.Length} bayt).", null, null);

        var detected = DetectImage(bytes);
        if (detected is null) return new(false, "Geçersiz veya sahte görsel (magic-byte eşleşmedi).", null, null);

        if (!string.IsNullOrWhiteSpace(declaredMime) && !AllowedMimes.Contains(declaredMime))
            return new(false, $"İzin verilmeyen MIME: {declaredMime}", null, null);

        // Bildirilen MIME ile gerçek içerik uyuşmuyorsa reddet (jpg uzantı + png içerik gibi)
        if (!string.IsNullOrWhiteSpace(declaredMime) &&
            !string.Equals(declaredMime, detected.Value.Mime, StringComparison.OrdinalIgnoreCase))
            return new(false, "Bildirilen MIME içerikle uyuşmuyor.", null, null);

        return new(true, null, detected.Value.Mime, detected.Value.Ext);
    }

    /// <summary>Path traversal/geçersiz karakterleri temizler; uzantıyı tespit edilen tipe sabitler.</summary>
    public static string SafeFileName(string? original, string detectedExt)
    {
        var baseName = Path.GetFileNameWithoutExtension(original ?? "dosya");
        var clean = new string(baseName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrWhiteSpace(clean)) clean = "dosya";
        if (clean.Length > 64) clean = clean[..64];
        return $"{clean}.{detectedExt}";
    }
}
