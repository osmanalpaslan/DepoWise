using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Muayene/Sigorta içe aktarımı — araç İÇ KOD veya PLAKA ile eşlenir; belge tipi + tarihler.
/// Sütunlar yeni kayıt formuyla birebir aynıdır (kullanıcı kuralı 2026-07-16).
///
/// "Erteleme Tarihi" AYRI bir alan DEĞİLDİR: form, Sonuç="Ertelendi" olduğunda Sonraki Tarih yerine
/// erteleme tarihini yazar (tek DB alanı: next_date). İçe aktarım da AYNI kuralı uygular — böylece
/// dışa aktar → düzelt → geri aktar döngüsü tutarlı kalır.
/// </summary>
public sealed class InspectionImportService
{
    public const string ColVehicle = "Araç";           // İç Kod veya Plaka — ZORUNLU
    public const string ColDocType = "Belge Tipi";     // Muayene/Sigorta/Kasko/Kalibrasyon
    public const string ColLast = "Son Tarih";
    public const string ColNext = "Sonraki Tarih";
    public const string ColPlace = "Yer / Kurum";
    public const string ColResult = "Sonuç";           // yalnız Muayene: Geçti/Kaldı/Ertelendi
    public const string ColPostpone = "Erteleme Tarihi";
    public const string ColNote = "Açıklama";

    /// <summary>Sonuç "Ertelendi" ise sonraki tarih = erteleme tarihi (form ile aynı kural).</summary>
    private const string ResultPostponed = "Ertelendi";

    private readonly InspectionService _inspections;
    private readonly VehicleService _vehicles;
    public InspectionImportService(InspectionService inspections, VehicleService vehicles)
    { _inspections = inspections; _vehicles = vehicles; }

    public IReadOnlyList<string> SampleHeaders()
        => new[] { ColVehicle, ColDocType, ColLast, ColNext, ColPlace, ColResult, ColPostpone, ColNote };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "inspection", PermissionAction.View);
        var vmap = VehicleMap(s);
        var errors = new List<ImportRowError>(); int valid = 0;
        foreach (var row in rows)
            if (Validate(row, vmap, out var err)) valid++;
            else if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "inspection", PermissionAction.Create);
        var vmap = VehicleMap(s);
        var errors = new List<ImportRowError>(); int added = 0, failed = 0;
        foreach (var row in rows)
        {
            if (!Validate(row, vmap, out var verr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            try
            {
                var isInspection = DocCode(Get(row, ColDocType)) == "inspection";
                var result = isInspection ? Empty(Get(row, ColResult)) : null;
                // Ertelendi → sonraki tarih = erteleme tarihi (form ile aynı kural, tek DB alanı).
                var next = isInspection && string.Equals(result, ResultPostponed, StringComparison.OrdinalIgnoreCase)
                    ? ParseDate(Get(row, ColPostpone))
                    : ParseDate(Get(row, ColNext));

                _inspections.Save(s, new NewInspection(
                    VehicleId: vmap[VehKey(Get(row, ColVehicle)!)],
                    DocType: DocCode(Get(row, ColDocType)),
                    LastDate: ParseDate(Get(row, ColLast)),
                    NextDate: next,
                    Result: result,
                    Place: Empty(Get(row, ColPlace)),
                    Note: Empty(Get(row, ColNote))));
                added++;
            }
            catch (Exception ex)
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }
        return new ImportResult(false, rows.Count, added, added, 0, failed, errors);
    }

    /// <summary>Araçlar İÇ KOD ve PLAKA anahtarıyla haritalanır (Excel'de genelde plaka yazar).</summary>
    private Dictionary<string, string> VehicleMap(SessionContext s)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in _vehicles.List(s, null, int.MaxValue))   // 200 varsayılan sınırı AŞILIR: 2600 satırlık dosyada 201. araçtan sonrası "bulunamadı" derdi
        {
            map[VehKey(v.InternalCode)] = v.Id;
            if (!string.IsNullOrWhiteSpace(v.Plate)) map[VehKey(v.Plate!)] = v.Id;
        }
        return map;
    }

    /// <summary>Plaka/kod karşılaştırması boşluk-tire duyarsız ("34 ABC 123" = "34abc123").</summary>
    private static string VehKey(string s) => s.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    private static bool Validate(ImportRow row, IReadOnlyDictionary<string, string> vmap, out string? error)
    {
        var v = Get(row, ColVehicle);
        if (string.IsNullOrWhiteSpace(v)) { error = "Araç (İç Kod veya Plaka) zorunlu."; return false; }
        if (!vmap.ContainsKey(VehKey(v)))
        { error = $"Araç bulunamadı: {v} (araç önce sisteme tanımlı olmalı)"; return false; }
        if (string.IsNullOrWhiteSpace(Get(row, ColDocType))) { error = "Belge Tipi zorunlu."; return false; }

        foreach (var col in new[] { ColLast, ColNext, ColPostpone })
        {
            var d = Get(row, col);
            if (!string.IsNullOrWhiteSpace(d) && ParseDate(d) is null)
            { error = $"{col}: tarih gg.aa.yyyy olmalı ({d})"; return false; }
        }

        // Ertelendi denip erteleme tarihi yazılmamışsa kayıt anlamsız olur (form da bunu zorunlu tutar).
        var isInspection = DocCode(Get(row, ColDocType)) == "inspection";
        if (isInspection && string.Equals(Get(row, ColResult)?.Trim(), ResultPostponed, StringComparison.OrdinalIgnoreCase)
            && ParseDate(Get(row, ColPostpone)) is null)
        { error = "Sonuç 'Ertelendi' ise Erteleme Tarihi zorunlu."; return false; }

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
