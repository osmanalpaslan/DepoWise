using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// YAKIT DEPO GİRİŞİ içe aktarımı (yakıt satın alma) — dağıtımların kaynağı.
///
/// Neden ayrı: <see cref="FuelService.Distribute"/> depoda yeterli yakıt yoksa kaydı REDDEDER (negatif
/// bakiye yasak — CLAUDE.md §4). Elde yalnız "araca şu kadar yakıt verdim" Excel'i varsa, önce buradan
/// depoya giriş aktarılmalıdır; aksi halde dağıtım aktarımı "depo yetersiz" der.
///
/// Zorunlu: Litre + Birim Fiyat (fiyat, dağıtımlarda snapshot olarak kullanılır — bilinmiyorsa 0 yazılamaz,
/// servis pozitif ister). Tedarikçi/Fatura/Tarih/Açıklama opsiyoneldir.
///
/// Tekrar aktarım koruması <see cref="FuelImportService"/> ile aynı ilkededir: operation_id satır
/// içeriğinden deterministik üretilir → aynı dosya ikinci kez aktarılırsa kayıt tekrarlanmaz.
/// </summary>
public sealed class FuelDepotImportService
{
    public const string ColDate = "Tarih";           // gg.aa.yyyy (boş = bugün)
    public const string ColLiters = "Litre";         // ZORUNLU
    public const string ColPrice = "Birim Fiyat";    // ZORUNLU
    public const string ColSupplier = "Tedarikçi";   // boş = yok
    public const string ColInvoice = "Fatura No";
    public const string ColNote = "Açıklama";

    private readonly FuelService _fuel;
    private readonly LookupService _lookups;

    public FuelDepotImportService(FuelService fuel, LookupService lookups)
    { _fuel = fuel; _lookups = lookups; }

    public IReadOnlyList<string> SampleHeaders()
        => new[] { ColDate, ColLiters, ColPrice, ColSupplier, ColInvoice, ColNote };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "fuel", PermissionAction.View);
        var errors = new List<ImportRowError>(); int valid = 0;
        foreach (var row in rows)
            if (Validate(row, out var err)) valid++;
            else if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "fuel", PermissionAction.Create);
        var smap = _lookups.List(s, "suppliers").ToDictionary(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportRowError>();
        int added = 0, skipped = 0, failed = 0;

        // Tarih sırası: depo fiyatı "son giriş" olduğu için sıra fiyat geçmişini doğru kurar.
        var ordered = rows
            .Select(r => (Row: r, Date: ParseDate(Get(r, ColDate))))
            .OrderBy(x => x.Date ?? long.MaxValue)
            .ToList();

        foreach (var (row, date) in ordered)
        {
            if (!Validate(row, out var verr))
            {
                failed++;
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!));
                continue;
            }
            try
            {
                var liters = ParseDecimal(Get(row, ColLiters))!.Value;
                var price = ParseDecimal(Get(row, ColPrice))!.Value;
                var opId = OperationId(s.CompanyId, row.RowNumber, date, liters, price);

                if (_fuel.OperationApplied(s, opId, depotEntry: true)) { skipped++; continue; }

                _fuel.AddDepotEntry(s, new NewDepotEntry(
                    Liters: liters,
                    UnitPrice: price,
                    SupplierId: LookupId(smap, Get(row, ColSupplier)),
                    InvoiceNo: Empty(Get(row, ColInvoice)),
                    Note: Empty(Get(row, ColNote)),
                    EntryDate: date), opId);
                added++;
            }
            catch (Exception ex)
            {
                failed++;
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message));
            }
        }
        return new ImportResult(false, rows.Count, added, added, skipped, failed, errors);
    }

    private static bool Validate(ImportRow row, out string? error)
    {
        foreach (var col in new[] { ColLiters, ColPrice })
        {
            var raw = Get(row, col);
            if (string.IsNullOrWhiteSpace(raw)) { error = $"{col} zorunlu."; return false; }
            var val = ParseDecimal(raw);
            if (val is null || val <= 0) { error = $"{col} pozitif bir sayı olmalı: {raw}"; return false; }
        }
        var d = Get(row, ColDate);
        if (!string.IsNullOrWhiteSpace(d) && ParseDate(d) is null)
        { error = $"Tarih gg.aa.yyyy olmalı: {d}"; return false; }
        error = null; return true;
    }

    private static string OperationId(string companyId, int rowNumber, long? date, decimal liters, decimal price)
    {
        var raw = $"fuel-depot-import|{companyId}|{rowNumber}|{date?.ToString() ?? "-"}|{liters:0.####}|{price:0.####}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32].ToLowerInvariant();
    }

    private static string? LookupId(IReadOnlyDictionary<string, string> map, string? name)
        => !string.IsNullOrWhiteSpace(name) && map.TryGetValue(name.Trim(), out var id) ? id : null;

    private static long? ParseDate(string? s)
        => DateTimeOffset.TryParseExact(s?.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToUnixTimeMilliseconds() : (long?)null;

    /// <summary>Türk Excel'i virgüllü ondalık yazar ("12,5") — nokta da kabul edilir.</summary>
    private static decimal? ParseDecimal(string? s)
        => decimal.TryParse(s?.Replace(',', '.').Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Get(ImportRow row, string col) => row.Values.TryGetValue(col, out var v) ? v : null;
}
