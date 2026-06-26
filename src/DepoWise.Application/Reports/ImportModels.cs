namespace DepoWise.Application.Reports;

/// <summary>İçe aktarım satırı: başlık→değer + satır numarası.</summary>
public sealed record ImportRow(int RowNumber, IReadOnlyDictionary<string, string?> Values);

public sealed record ImportRowError(int RowNumber, string Message);

/// <summary>İçe aktarım sonucu. dry-run'da yalnız doğrulama; commit'te uygulanan sayılar.</summary>
public sealed record ImportResult(
    bool DryRun, int Total, int Valid, int Added, int Updated, int Failed,
    IReadOnlyList<ImportRowError> Errors)
{
    public const int MaxReportedErrors = 15;
}
