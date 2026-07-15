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
/// Ortak alan kontrolleri (web + masaüstü + sunucu). Kesin kurallar (yıl aralığı) servis katmanında
/// zorlanır; "yumuşak" kontroller (plaka/telefon biçimi, çok büyük değer) UI'da UYARI için kullanılır
/// (kullanıcı yine de devam edebilir).
/// </summary>
public static class FieldChecks
{
    // Üretim yılı makul aralığı (iş makineleri eski olabilir → alt sınır düşük).
    public const int MinVehicleYear = 1950;
    public static int MaxVehicleYear => System.DateTimeOffset.UtcNow.Year + 1;
    public static bool YearInRange(int? year) => year is null || (year >= MinVehicleYear && year <= MaxVehicleYear);

    /// <summary>Türk plaka biçimi (34 ABC 123 vb.): 2 rakam + 1-3 harf + 2-4 rakam, ya da 2 rakam + 1-2 harf +
    /// 4-5 rakam. Boşluklar serbest. İş makinesi/plakasız araçlar buna uymayabilir → çağıran YALNIZ uyarı verir.</summary>
    public static bool PlateLooksTurkish(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return true; // boş plaka bu kontrolü tetiklemez
        var p = plate.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        return System.Text.RegularExpressions.Regex.IsMatch(p, "^[0-9]{2}[A-ZÇĞİÖŞÜ]{1,4}[0-9]{2,5}$");
    }

    /// <summary>Telefon "makul" mü: yalnız uyarı için. Sadece rakam/boşluk/+/-/parantez ve 7-15 hane.</summary>
    public static bool PhoneLooksValid(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return true; // boş telefon uyarı vermez
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 7 || digits.Length > 15) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(phone.Trim(), @"^[0-9+\-()\s]+$");
    }

    /// <summary>Yanlışlıkla girilmiş olabilecek "çok büyük" sayı eşiği (yalnız uyarı için).</summary>
    public const decimal LargeValueThreshold = 1_000_000m;
    public static bool IsSuspiciouslyLarge(decimal value) => value >= LargeValueThreshold;
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
