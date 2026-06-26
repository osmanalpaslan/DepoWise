using System.Globalization;

namespace DepoWise.Application.Common;

/// <summary>
/// Para = decimal tutar + currency_code. Float KULLANILMAZ. Varsayılan TRY; USD/EUR desteklenir.
/// İşlem anındaki kur snapshot olarak ayrı saklanır (bu tip kuru taşımaz).
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public const string BaseCurrency = "TRY";
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "TRY", "USD", "EUR" };

    public static Money Try(decimal amount) => new(amount, BaseCurrency);

    public static bool IsSupported(string? currency) => currency is not null && Allowed.Contains(currency);

    public static string Serialize(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static decimal Parse(string? text)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    public string SerializeAmount() => Serialize(Amount);
}
