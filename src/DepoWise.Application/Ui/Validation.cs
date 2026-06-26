using System.Globalization;

namespace DepoWise.Application.Ui;

public sealed record ValidationResult(bool Ok, string? Error = null)
{
    public static readonly ValidationResult Success = new(true);
    public static ValidationResult Fail(string error) => new(false, error);
}

/// <summary>
/// GG/AA/YYYY tarih doğrulama — yalnız maske değil GERÇEK takvim kontrolü
/// (31/02, 13. ay, 29/02 artık-yıl-dışı reddedilir). Web `parseDate` ile aynı kurallar.
/// </summary>
public static class DateInput
{
    public static bool TryParse(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        // Kesin biçim: gün/ay/yıl, gerçek takvim. AllowWhiteSpaces YOK.
        if (!DateOnly.TryParseExact(text.Trim(), "dd/MM/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return false;
        return date.Year is >= 1900 and <= 2200;
    }

    public static ValidationResult Validate(string? text)
        => TryParse(text, out _) ? ValidationResult.Success
            : ValidationResult.Fail("Geçersiz tarih (GG/AA/YYYY, gerçek takvim).");

    public static string Format(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// Numerik alan doğrulama — negatif ve sınır dışı fail-closed. Web `validateNumeric` ile aynı.
/// </summary>
public static class NumericInput
{
    public static ValidationResult Validate(decimal? value, decimal? min = null, decimal? max = null, bool allowNegative = false)
    {
        if (value is null) return ValidationResult.Fail("Değer zorunlu.");
        var v = value.Value;
        if (!allowNegative && v < 0) return ValidationResult.Fail("Negatif değer kabul edilmez.");
        if (min is not null && v < min) return ValidationResult.Fail($"En küçük değer {min}.");
        if (max is not null && v > max) return ValidationResult.Fail($"En büyük değer {max}.");
        return ValidationResult.Success;
    }
}
