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

/// <summary>
/// ═══ EVR-01 (ADR-165, 2026-08-27) — BELGE (EVRAK) DOĞRULAMASI ═══
///
/// Fotoğraf doğrulamasının (<see cref="FileValidation"/>) kardeşi; onu DEĞİŞTİRMEZ. Belgelerde de aynı
/// güvenlik ilkesi: boyut sınırı + MAGIC-BYTE (dosyanın gerçek içeriği) kontrolü — uzantıya/bildirilen
/// MIME'a güvenilmez. PDF ve Office (docx/xlsx/doc/xls) ile görsel (jpg/png) belgeler kabul edilir.
///
/// Office ayrımı NOTU: docx/xlsx aslında ZIP, doc/xls ise OLE kapsayıcısıdır — magic-byte kapsayıcıyı
/// doğrular, alt türü (Word mü Excel mi) UZANTI belirler. Ham ".zip" BİLEREK kabul edilmez: kapsayıcı
/// doğru olsa da uzantısı izinli listede değilse dosya reddedilir.
/// </summary>
public static class DocumentValidation
{
    /// <summary>Fotoğrafla AYNI sınır (7 MB) — ikinci bir boyut kuralı İCAT EDİLMEDİ; sınır değişecekse
    /// ürün kararıyla tek yerden değişir.</summary>
    public const int MaxBytes = FileValidation.MaxBytes;

    /// <summary>uzantı → beklenen MIME. Tek doğru kaynak; UI "kabul edilen türler" listesini de buradan alır.</summary>
    public static readonly IReadOnlyDictionary<string, string> AllowedExtMime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"] = "application/pdf",
        ["jpg"] = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["png"] = "image/png",
        ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["doc"] = "application/msword",
        ["xls"] = "application/vnd.ms-excel",
    };

    private static bool PdfMagic(ReadOnlySpan<byte> b)
        => b.Length >= 4 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46;   // %PDF
    private static bool ZipMagic(ReadOnlySpan<byte> b)
        => b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && (b[2] is 0x03 or 0x05 or 0x07); // PK..
    private static bool OleMagic(ReadOnlySpan<byte> b)
        => b.Length >= 8 && b[0] == 0xD0 && b[1] == 0xCF && b[2] == 0x11 && b[3] == 0xE0
        && b[4] == 0xA1 && b[5] == 0xB1 && b[6] == 0x1A && b[7] == 0xE1;                    // OLE2 (doc/xls)

    public static FileValidationResult Validate(string? fileName, string? declaredMime, byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return new(false, "Boş dosya.", null, null);
        if (bytes.Length > MaxBytes) return new(false, $"Dosya 7 MB sınırını aşıyor ({bytes.Length} bayt).", null, null);

        var ext = (Path.GetExtension(fileName ?? "") ?? "").TrimStart('.').ToLowerInvariant();
        if (!AllowedExtMime.TryGetValue(ext, out var mime))
            return new(false, "İzin verilmeyen dosya türü. Kabul edilenler: PDF, JPG, PNG, DOCX, XLSX, DOC, XLS.", null, null);

        // İçerik (magic-byte) uzantının vaadiyle uyuşmalı — sahte uzantı reddedilir.
        bool icerikUyar = ext switch
        {
            "pdf" => PdfMagic(bytes),
            "jpg" or "jpeg" or "png" => FileValidation.DetectImage(bytes) is { } g
                                        && string.Equals(g.Mime, mime, StringComparison.OrdinalIgnoreCase),
            "docx" or "xlsx" => ZipMagic(bytes),
            "doc" or "xls" => OleMagic(bytes),
            _ => false,
        };
        if (!icerikUyar) return new(false, "Dosya içeriği uzantısıyla uyuşmuyor (sahte/bozuk dosya).", null, null);

        if (!string.IsNullOrWhiteSpace(declaredMime)
            && !string.Equals(declaredMime, mime, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(declaredMime, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return new(false, "Bildirilen MIME içerikle uyuşmuyor.", null, null);

        return new(true, null, mime, ext == "jpeg" ? "jpg" : ext);
    }
}
