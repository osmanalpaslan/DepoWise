using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>Araç içe aktarımı — İç Kod benzersizse oluşturur (varsa atlar). Lookup'lar UI'dan tamamlanır.</summary>
public sealed class VehicleImportService
{
    public const string ColCode = "İç Kod";
    public const string ColPlate = "Plaka";
    public const string ColYear = "Yıl";
    public const string ColStatus = "Durum";

    private readonly VehicleService _vehicles;
    public VehicleImportService(VehicleService vehicles) => _vehicles = vehicles;

    public IReadOnlyList<string> SampleHeaders() => new[] { ColCode, ColPlate, ColYear, ColStatus };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "vehicles", PermissionAction.View);
        var errors = new List<ImportRowError>(); int valid = 0;
        foreach (var row in rows)
            if (Validate(row, out var err)) valid++;
            else if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "vehicles", PermissionAction.Create);
        var existing = _vehicles.List(s).Select(v => v.InternalCode).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportRowError>(); int added = 0, updated = 0, failed = 0;
        foreach (var row in rows)
        {
            if (!Validate(row, out var verr)) { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            try
            {
                var code = Get(row, ColCode)!.Trim();
                if (!existing.Add(code)) { updated++; continue; } // var → atla (idempotent)
                int? year = int.TryParse(Get(row, ColYear), out var y) ? y : null;
                _vehicles.Create(s, new NewVehicle(code,
                    Plate: Empty(Get(row, ColPlate)),
                    ProductionYear: year,
                    Status: StatusCode(Get(row, ColStatus))));
                added++;
            }
            catch (System.Exception ex) { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }
        return new ImportResult(false, rows.Count, added + updated, added, updated, failed, errors);
    }

    private static bool Validate(ImportRow row, out string? error)
    {
        if (string.IsNullOrWhiteSpace(Get(row, ColCode))) { error = "İç Kod zorunlu."; return false; }
        var yr = Get(row, ColYear);
        if (!string.IsNullOrWhiteSpace(yr) && !int.TryParse(yr, out _)) { error = "Yıl sayısal olmalı."; return false; }
        error = null; return true;
    }

    private static string StatusCode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "pasif" or "passive" => "passive", "bakımda" or "bakimda" or "maintenance" => "maintenance", _ => "active"
    };

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Get(ImportRow row, string col) => row.Values.TryGetValue(col, out var v) ? v : null;
}
