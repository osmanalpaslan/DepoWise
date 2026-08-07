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
/// Ortak seçim alanı davranışı (madde 3, kullanıcı isteği 2026-08-06): kullanıcı bir seçim alanına
/// tıkladığında (arama yapmadan önce) mevcut kayıtlar listelenir; kayıt sayısı <see cref="MaxUnfiltered"/>'dan
/// fazlaysa İLK ETAPTA en fazla bu kadarı gösterilir. Kullanıcı arama yazmaya başlayınca sınır KALKAR, arama
/// sonucundaki TÜM uygun kayıtlar listelenir. Türkçe karakter-doğru arama (İ/I/ı/i, Ç/Ğ/Ö/Ş/Ü) —
/// <c>StringComparison.OrdinalIgnoreCase</c> bunu YANLIŞ eşler (kanıt: web LookupSelect düzeltmesi, 2026-08-06).
/// Saf/çerçeve-bağımsız mantık — masaüstü (AsyncPopulator) ve testler bu TEK kaynağı kullanır.
/// </summary>
public static class SelectionSearch
{
    public const int MaxUnfiltered = 25;
    private static readonly System.Globalization.CompareInfo TrCompare =
        new System.Globalization.CultureInfo("tr-TR").CompareInfo;

    public static bool Contains(string? haystack, string needle)
        => TrCompare.IndexOf(haystack ?? "", needle, System.Globalization.CompareOptions.IgnoreCase) >= 0;

    /// <summary>Arama boşsa ilk <see cref="MaxUnfiltered"/> kayıt (mevcut sıra korunur); doluysa TÜM eşleşenler.</summary>
    public static IEnumerable<T> Apply<T>(IEnumerable<T> items, string? search, Func<T, string?> text)
    {
        if (string.IsNullOrWhiteSpace(search)) return items.Take(MaxUnfiltered);
        var q = search.Trim();
        return items.Where(x => Contains(text(x), q));
    }
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
