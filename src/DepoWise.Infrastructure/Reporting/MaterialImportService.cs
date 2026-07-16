using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Malzeme içe aktarımı — örnek başlık + ön kontrol + satır bazlı hata + dry-run (kesin onaydan önce).
/// Commit iş kurallarını ATLAMAZ (MaterialService.Create: tenant + permission + kod benzersiz + currency).
/// Politika: satır bazlı (bir hatalı satır diğerlerini bozmaz); dry-run hiç yazmaz.
/// </summary>
public sealed class MaterialImportService
{
    public const string ColCode = "Kod";
    public const string ColName = "Ad";
    public const string ColType = "Tür";
    public const string ColMinStock = "Min Stok";
    public const string ColPrice = "Birim Fiyat";
    public const string ColCurrency = "Para Birimi";

    private readonly MaterialService _materials;

    public MaterialImportService(MaterialService materials) => _materials = materials;

    /// <summary>İndirilebilir örnek dosya başlıkları (export ile aynı düzen).</summary>
    public IReadOnlyList<string> SampleHeaders() =>
        new[] { ColCode, ColName, ColType, ColMinStock, ColPrice, ColCurrency };

    /// <summary>Dry-run: yalnız doğrulama, DB'ye yazmaz.</summary>
    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "materials", PermissionAction.View);
        var errors = new List<ImportRowError>();
        int valid = 0;
        foreach (var row in rows)
        {
            if (Validate(row, out var err)) valid++;
            else if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
        }
        return new ImportResult(DryRun: true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    /// <summary>Commit: dry-run sonrası geçerli satırları uygular (satır bazlı try/catch).</summary>
    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "materials", PermissionAction.Create);
        var errors = new List<ImportRowError>();
        int added = 0, updated = 0, failed = 0;

        foreach (var row in rows)
        {
            if (!Validate(row, out var verr))
            {
                failed++;
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!));
                continue;
            }
            try
            {
                var code = Get(row, ColCode)!.Trim();
                if (!_materials.IsCodeUnique(s, code))
                {
                    updated++; // mevcut kod → idempotent (mevcut güncelleme akışı ileride)
                    continue;
                }
                _materials.Create(s, new NewMaterial(
                    Code: code,
                    Name: Get(row, ColName)!,
                    Type: Get(row, ColType),
                    MinStock: ParseDecimal(Get(row, ColMinStock)) ?? 0m,
                    UnitPrice: ParseDecimal(Get(row, ColPrice)) ?? 0m,
                    Currency: string.IsNullOrWhiteSpace(Get(row, ColCurrency)) ? "TRY" : Get(row, ColCurrency)!.Trim()));
                added++;
            }
            catch (Exception ex)
            {
                failed++;
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message));
            }
        }
        return new ImportResult(DryRun: false, rows.Count, added + updated, added, updated, failed, errors);
    }

    private static bool Validate(ImportRow row, out string? error)
    {
        if (string.IsNullOrWhiteSpace(Get(row, ColCode))) { error = "Kod zorunlu."; return false; }
        if (string.IsNullOrWhiteSpace(Get(row, ColName))) { error = "Ad zorunlu."; return false; }
        foreach (var col in new[] { ColPrice, ColMinStock })
        {
            var raw = Get(row, col);
            if (!string.IsNullOrWhiteSpace(raw) && ParseDecimal(raw) is null)
            { error = $"{col} sayısal olmalı: {raw}"; return false; }
        }
        var cur = Get(row, ColCurrency);
        if (!string.IsNullOrWhiteSpace(cur) && !Money.IsSupported(cur!.Trim()))
        { error = $"Desteklenmeyen para birimi: {cur}"; return false; }
        error = null;
        return true;
    }

    /// <summary>
    /// Excel'den gelen ondalık sayı. Türk Excel'i virgül yazar ("12,5") — nokta da kabul edilir.
    ///
    /// ⚠️ Money.Parse KULLANILMAZ: o, InvariantCulture + NumberStyles.Number ile çalışır ve virgülü BİNLİK
    /// AYIRICI sayar → "12,5" SESSİZCE 125 olur (10 kat hata). Money.Parse veritabanından okuma içindir
    /// (orada değerler daima nokta ile saklanır); kullanıcı Excel'i için uygun DEĞİLDİR.
    /// </summary>
    private static decimal? ParseDecimal(string? s)
        => decimal.TryParse(s?.Replace(',', '.').Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private static string? Get(ImportRow row, string col)
        => row.Values.TryGetValue(col, out var v) ? v : null;
}
