using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Bakım içe aktarımı — araç İç Kod ile eşlenir; bakım tanımı ada göre bulunur, yoksa oluşturulur.
/// Malzeme satırları içe aktarımda YOK (stok düşümü için bakım ekranı kullanılır); km/saat/tarih + açıklama.
/// </summary>
public sealed class MaintenanceImportService
{
    public const string ColVehicle = "Araç";       // İç Kod
    public const string ColDef = "Bakım Tanımı";
    public const string ColKm = "Yapılma KM";
    public const string ColHour = "Yapılma Saat";
    public const string ColDate = "Tarih";
    public const string ColNote = "Açıklama";

    private readonly MaintenanceService _maint;
    private readonly MaintenanceDefinitionService _defs;
    private readonly VehicleService _vehicles;
    public MaintenanceImportService(MaintenanceService maint, MaintenanceDefinitionService defs, VehicleService vehicles)
    { _maint = maint; _defs = defs; _vehicles = vehicles; }

    public IReadOnlyList<string> SampleHeaders() => new[] { ColVehicle, ColDef, ColKm, ColHour, ColDate, ColNote };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "maintenance", PermissionAction.View);
        var codes = _vehicles.List(s).Select(v => v.InternalCode).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportRowError>(); int valid = 0;
        foreach (var row in rows)
            if (Validate(row, codes, out var err)) valid++;
            else if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "maintenance", PermissionAction.Create);
        var vmap = _vehicles.List(s).ToDictionary(v => v.InternalCode, v => v.Id, System.StringComparer.OrdinalIgnoreCase);
        var dmap = _defs.List(s).ToDictionary(d => d.Name, d => d.Id, System.StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportRowError>(); int added = 0, failed = 0;
        foreach (var row in rows)
        {
            if (!Validate(row, vmap.Keys.ToHashSet(System.StringComparer.OrdinalIgnoreCase), out var verr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            try
            {
                var defName = Get(row, ColDef)!.Trim();
                if (!dmap.TryGetValue(defName, out var defId))
                {
                    defId = _defs.Create(s, new NewMaintenanceDefinition(defName, 0m, "km"));
                    dmap[defName] = defId;
                }
                _maint.Save(s, new NewMaintenance(
                    VehicleId: vmap[Get(row, ColVehicle)!.Trim()],
                    DefinitionId: defId,
                    Description: Empty(Get(row, ColNote)),
                    PerformedKm: ParseDec(Get(row, ColKm)),
                    PerformedHour: ParseDec(Get(row, ColHour)),
                    PerformedDate: ParseDate(Get(row, ColDate)),
                    Materials: System.Array.Empty<MaintenanceMaterialLine>()), System.Guid.NewGuid().ToString("N"));
                added++;
            }
            catch (System.Exception ex) { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }
        return new ImportResult(false, rows.Count, added, added, 0, failed, errors);
    }

    private static bool Validate(ImportRow row, HashSet<string> codes, out string? error)
    {
        var v = Get(row, ColVehicle);
        if (string.IsNullOrWhiteSpace(v)) { error = "Araç (İç Kod) zorunlu."; return false; }
        if (!codes.Contains(v.Trim())) { error = $"Araç bulunamadı: {v}"; return false; }
        if (string.IsNullOrWhiteSpace(Get(row, ColDef))) { error = "Bakım Tanımı zorunlu."; return false; }
        var d = Get(row, ColDate);
        if (!string.IsNullOrWhiteSpace(d) && ParseDate(d) is null) { error = "Tarih gg.aa.yyyy olmalı."; return false; }
        error = null; return true;
    }

    private static decimal? ParseDec(string? s)
        => decimal.TryParse(s?.Replace(',', '.').Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : (decimal?)null;

    private static long? ParseDate(string? s)
        => DateTimeOffset.TryParseExact(s?.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToUnixTimeMilliseconds() : (long?)null;

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Get(ImportRow row, string col) => row.Values.TryGetValue(col, out var v) ? v : null;
}
