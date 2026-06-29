using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>Muayene/Sigorta içe aktarımı — araç İç Kod ile eşlenir; belge tipi + tarihler.</summary>
public sealed class InspectionImportService
{
    public const string ColVehicle = "Araç";       // İç Kod
    public const string ColDocType = "Belge Tipi"; // Muayene/Sigorta/Kasko/Kalibrasyon
    public const string ColLast = "Son Tarih";
    public const string ColNext = "Sonraki Tarih";
    public const string ColPlace = "Yer";
    public const string ColResult = "Sonuç";

    private readonly InspectionService _inspections;
    private readonly VehicleService _vehicles;
    public InspectionImportService(InspectionService inspections, VehicleService vehicles)
    { _inspections = inspections; _vehicles = vehicles; }

    public IReadOnlyList<string> SampleHeaders() => new[] { ColVehicle, ColDocType, ColLast, ColNext, ColPlace, ColResult };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "inspection", PermissionAction.View);
        var codes = VehCodes(s);
        var errors = new List<ImportRowError>(); int valid = 0;
        foreach (var row in rows)
            if (Validate(row, codes, out var err)) valid++;
            else if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "inspection", PermissionAction.Create);
        var map = _vehicles.List(s).ToDictionary(v => v.InternalCode, v => v.Id, System.StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportRowError>(); int added = 0, failed = 0;
        foreach (var row in rows)
        {
            if (!Validate(row, map.Keys.ToHashSet(System.StringComparer.OrdinalIgnoreCase), out var verr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            try
            {
                _inspections.Save(s, new NewInspection(
                    VehicleId: map[Get(row, ColVehicle)!.Trim()],
                    DocType: DocCode(Get(row, ColDocType)),
                    LastDate: ParseDate(Get(row, ColLast)),
                    NextDate: ParseDate(Get(row, ColNext)),
                    Result: Empty(Get(row, ColResult)),
                    Place: Empty(Get(row, ColPlace))));
                added++;
            }
            catch (System.Exception ex) { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }
        return new ImportResult(false, rows.Count, added, added, 0, failed, errors);
    }

    private HashSet<string> VehCodes(SessionContext s) => _vehicles.List(s).Select(v => v.InternalCode).ToHashSet(System.StringComparer.OrdinalIgnoreCase);

    private static bool Validate(ImportRow row, HashSet<string> codes, out string? error)
    {
        var v = Get(row, ColVehicle);
        if (string.IsNullOrWhiteSpace(v)) { error = "Araç (İç Kod) zorunlu."; return false; }
        if (!codes.Contains(v.Trim())) { error = $"Araç bulunamadı: {v}"; return false; }
        if (string.IsNullOrWhiteSpace(Get(row, ColDocType))) { error = "Belge Tipi zorunlu."; return false; }
        foreach (var col in new[] { ColLast, ColNext })
        {
            var d = Get(row, col);
            if (!string.IsNullOrWhiteSpace(d) && ParseDate(d) is null) { error = $"{col}: tarih gg.aa.yyyy olmalı."; return false; }
        }
        error = null; return true;
    }

    private static string DocCode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "sigorta" => "insurance", "kasko" => "kasko", "kalibrasyon" => "calibration", _ => "inspection"
    };

    private static long? ParseDate(string? s)
        => DateTimeOffset.TryParseExact(s?.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToUnixTimeMilliseconds() : (long?)null;

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Get(ImportRow row, string col) => row.Values.TryGetValue(col, out var v) ? v : null;
}
