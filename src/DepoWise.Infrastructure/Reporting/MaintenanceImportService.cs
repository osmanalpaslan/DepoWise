using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Bakım içe aktarımı — araç İÇ KOD veya PLAKA ile eşlenir; bakım tanımı / alt bakım / teknisyen ada göre
/// bulunur, yoksa OTOMATİK OLUŞTURULUR (kullanıcı kuralı 2026-07-16).
///
/// Sütunlar yeni kayıt formuyla aynıdır — İKİ bilinçli istisna:
///  • MALZEME satırları: bakıma malzeme eklemek STOKTAN DÜŞER; Excel'den toplu stok hareketi üretilmesi
///    istenmiyor (stok düşümü için Bakım ekranı kullanılır).
///  • ARAÇ DURUMU: kullanıcı bunu bakım EKRANINA istedi, şablona İSTEMEDİ (2026-07-16 kararı).
///    Toplu durum değişikliği Araç içe aktarımının "Durum" sütunuyla yapılır.
/// </summary>
public sealed class MaintenanceImportService
{
    public const string ColVehicle = "Araç";        // İç Kod veya Plaka — ZORUNLU
    public const string ColDef = "Bakım Tanımı";    // ZORUNLU (yoksa oluşturulur)
    public const string ColSubDef = "Alt Bakım";
    public const string ColTechnician = "Teknisyen";
    public const string ColKm = "Yapılma KM";
    public const string ColHour = "Yapılma Saat";
    public const string ColDate = "Tarih";
    public const string ColNote = "Açıklama";

    private readonly MaintenanceService _maint;
    private readonly MaintenanceDefinitionService _defs;
    private readonly VehicleService _vehicles;
    private readonly LookupService _lookups;

    public MaintenanceImportService(MaintenanceService maint, MaintenanceDefinitionService defs,
        VehicleService vehicles, LookupService lookups)
    { _maint = maint; _defs = defs; _vehicles = vehicles; _lookups = lookups; }

    public IReadOnlyList<string> SampleHeaders()
        => new[] { ColVehicle, ColDef, ColSubDef, ColTechnician, ColKm, ColHour, ColDate, ColNote };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "maintenance", PermissionAction.View);
        var vmap = VehicleMap(s);
        var errors = new List<ImportRowError>(); int valid = 0;
        foreach (var row in rows)
            if (Validate(row, vmap, out var err)) valid++;
            else if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    /// <summary>Commit + bu aktarımda OLUŞAN yeni tanımlar (bakım tanımı / alt bakım / teknisyen).</summary>
    public (ImportResult Result, IReadOnlyList<string> CreatedLookups) CommitWithLookups(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "maintenance", PermissionAction.Create);
        var res = new ImportLookupResolver(_lookups, s);
        var vmap = VehicleMap(s);
        var created = new List<string>();

        // Bakım tanımları önbelleği — 2600 satır için satır başına sorgu YOK.
        var dmap = _defs.List(s).ToDictionary(d => Key(d.Name), d => d.Id, StringComparer.Ordinal);
        // Alt bakım önbelleği: ebeveyn tanım id'si → (alt ad → id). Alt bakım ebeveyne bağlıdır.
        var subCache = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        var errors = new List<ImportRowError>(); int added = 0, failed = 0;
        foreach (var row in rows)
        {
            if (!Validate(row, vmap, out var verr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            try
            {
                var defName = Get(row, ColDef)!.Trim();
                if (!dmap.TryGetValue(Key(defName), out var defId))
                {
                    defId = _defs.Create(s, new NewMaintenanceDefinition(defName, 0m, "km"));
                    dmap[Key(defName)] = defId;
                    created.Add($"Bakım Tanımı: {defName}");
                }

                string? subId = null;
                var subName = Get(row, ColSubDef);
                if (!string.IsNullOrWhiteSpace(subName))
                {
                    if (!subCache.TryGetValue(defId, out var subs))
                    {
                        subs = new Dictionary<string, string>(StringComparer.Ordinal);
                        try { foreach (var sd in _defs.List(s, defId)) subs[Key(sd.Name)] = sd.Id; } catch { }
                        subCache[defId] = subs;
                    }
                    var sk = Key(subName!);
                    if (!subs.TryGetValue(sk, out var sid))
                    {
                        sid = _defs.Create(s, new NewMaintenanceDefinition(subName!.Trim(), 0m, "km", defId));
                        subs[sk] = sid;
                        created.Add($"Alt Bakım: {subName!.Trim()}");
                    }
                    subId = sid;
                }

                _maint.Save(s, new NewMaintenance(
                    VehicleId: vmap[VehKey(Get(row, ColVehicle)!)],
                    DefinitionId: defId,
                    SubDefinitionId: subId,
                    TechnicianId: res.Personnel(Get(row, ColTechnician)),
                    Description: Empty(Get(row, ColNote)),
                    PerformedKm: ParseDec(Get(row, ColKm)),
                    PerformedHour: ParseDec(Get(row, ColHour)),
                    PerformedDate: ParseDate(Get(row, ColDate)),
                    Materials: Array.Empty<MaintenanceMaterialLine>()), Guid.NewGuid().ToString("N"));
                added++;
            }
            catch (Exception ex)
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }
        created.AddRange(res.CreatedNames);
        return (new ImportResult(false, rows.Count, added, added, 0, failed, errors), created);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows) => CommitWithLookups(s, rows).Result;

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

    private static string Key(string s) => s.Trim().ToUpperInvariant();
    /// <summary>Plaka/kod karşılaştırması boşluk-tire duyarsız ("34 ABC 123" = "34abc123").</summary>
    private static string VehKey(string s) => s.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    private static bool Validate(ImportRow row, IReadOnlyDictionary<string, string> vmap, out string? error)
    {
        var v = Get(row, ColVehicle);
        if (string.IsNullOrWhiteSpace(v)) { error = "Araç (İç Kod veya Plaka) zorunlu."; return false; }
        if (!vmap.ContainsKey(VehKey(v)))
        { error = $"Araç bulunamadı: {v} (araç önce sisteme tanımlı olmalı)"; return false; }
        if (string.IsNullOrWhiteSpace(Get(row, ColDef))) { error = "Bakım Tanımı zorunlu."; return false; }

        foreach (var col in new[] { ColKm, ColHour })
        {
            var raw = Get(row, col);
            if (!string.IsNullOrWhiteSpace(raw) && ParseDecRaw(raw) is null)
            { error = $"{col} sayısal olmalı: {raw}"; return false; }
        }
        var d = Get(row, ColDate);
        if (!string.IsNullOrWhiteSpace(d) && ParseDate(d) is null)
        { error = $"Tarih gg.aa.yyyy olmalı: {d}"; return false; }
        error = null; return true;
    }

    /// <summary>Bakım km/saat: yalnız POZİTİF değer anlamlıdır (0/boş = bilinmiyor → null).</summary>
    private static decimal? ParseDec(string? s)
    {
        var v = ParseDecRaw(s);
        return v > 0 ? v : null;
    }

    /// <summary>Türk Excel'i virgüllü ondalık yazar ("12,5") — nokta da kabul edilir.</summary>
    private static decimal? ParseDecRaw(string? s)
        => decimal.TryParse(s?.Replace(',', '.').Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private static long? ParseDate(string? s)
        => DateTimeOffset.TryParseExact(s?.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToUnixTimeMilliseconds() : (long?)null;

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Get(ImportRow row, string col) => row.Values.TryGetValue(col, out var v) ? v : null;
}
