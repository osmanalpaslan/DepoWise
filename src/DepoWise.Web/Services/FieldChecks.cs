using System.Text.RegularExpressions;

namespace DepoWise.Web.Services;

/// <summary>
/// Web arayüzü alan kontrolleri. Web projesi bağımsız (backend'e yalnız HTTP ile bağlanır) olduğundan
/// DepoWise.Application.Ui.FieldChecks buraya küçük bir kopya olarak taşındı. Kesin kurallar sunucuda da
/// zorlanır; buradakiler kullanıcıya erken/nazik uyarı içindir (yumuşak kontroller kullanıcı yine geçebilir).
/// </summary>
public static class FieldChecks
{
    /// <summary>"+" seçim standardı (kullanıcı isteği 2026-08-06, madde 5): Türkçe karakter-doğru karşılaştırma
    /// (İ/I/ı/i, Ç/Ğ/Ö/Ş/Ü). StringComparison.OrdinalIgnoreCase Türkçe büyük/küçük harf kurallarını (İ↔i, I↔ı)
    /// DOĞRU eşlemez — arama/tekrar-kontrolü yapan TÜM ekranlar bu TEK kaynağı kullanır.</summary>
    public static readonly System.Globalization.CompareInfo TrCompare = new System.Globalization.CultureInfo("tr-TR").CompareInfo;

    public const int MinVehicleYear = 1950;
    public static int MaxVehicleYear => DateTimeOffset.UtcNow.Year + 1;
    public static bool YearInRange(int? year) => year is null || (year >= MinVehicleYear && year <= MaxVehicleYear);

    /// <summary>Türk plaka biçimi (34 ABC 123 vb.). Boş plaka bu kontrolü tetiklemez. İş makinesi/plakasız
    /// araçlar uymayabilir → çağıran YALNIZ uyarı verir, engellemez.</summary>
    public static bool PlateLooksTurkish(string? plate)
    {
        if (string.IsNullOrWhiteSpace(plate)) return true;
        var p = plate.Replace(" ", "").Replace("-", "").ToUpperInvariant();
        return Regex.IsMatch(p, "^[0-9]{2}[A-ZÇĞİÖŞÜ]{1,4}[0-9]{2,5}$");
    }

    /// <summary>Telefon "makul" mü (yalnız uyarı için): 7-15 hane + geçerli karakterler.</summary>
    public static bool PhoneLooksValid(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return true;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 7 || digits.Length > 15) return false;
        return Regex.IsMatch(phone.Trim(), @"^[0-9+\-()\s]+$");
    }

    public const decimal LargeValueThreshold = 1_000_000m;
    public static bool IsSuspiciouslyLarge(decimal value) => value >= LargeValueThreshold;
}
