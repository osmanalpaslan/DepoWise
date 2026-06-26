namespace DepoWise.Application.Common;

/// <summary>
/// İstek/işlem ilişkilendirme kimliği. Her API isteği ve masaüstü işlem zinciri bir
/// correlation_id taşır; loglar ve ApiError bu değerle eşlenir.
/// </summary>
public static class Correlation
{
    public static string New() => Guid.NewGuid().ToString("N");
}
