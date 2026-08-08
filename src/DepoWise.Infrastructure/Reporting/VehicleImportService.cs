using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Araç içe aktarımı — İç Kod benzersizse oluşturur (varsa ATLAR, idempotent).
///
/// Sütunlar YENİ KAYIT FORMUYLA BİREBİR aynıdır (kullanıcı kuralı 2026-07-16: "içeri alma şablonlarında
/// yeni kayıt formunda bulunan her alan olmalı — fotoğraf hariç"). Tek istisna: "Şablon" alanı — o, formda
/// diğer alanları DOLDURMAK için bir kolaylıktır; Excel'de zaten her alan tek tek yazılıdır.
///
/// Tanım alanları (Makine Tipi/Kategori/Marka/Model/Şantiye/Sürücü) İSİMLE yazılır ve yoksa OTOMATİK
/// OLUŞTURULUR (<see cref="ImportLookupResolver"/>) — kullanıcı isteği. Oluşan yeni tanımlar raporlanır.
/// </summary>
public sealed class VehicleImportService
{
    public const string ColCode = "İç Kod";            // ZORUNLU, benzersiz
    public const string ColPlate = "Plaka";
    public const string ColYear = "Üretim Yılı";
    public const string ColStatus = "Durum";           // Aktif / Pasif / Bakımda / Arızalı
    public const string ColStatusNote = "Durum Açıklaması";
    public const string ColMeter = "Sayaç";
    public const string ColMeterUnit = "Birim";        // km | saat
    public const string ColType = "Makine Tipi";
    public const string ColCategory = "Kategori";
    public const string ColBrand = "Marka";
    public const string ColModel = "Model";
    public const string ColBranch = "Şantiye / Şube";
    public const string ColDriver = "Sürücü";
    public const string ColChassis = "Şasi No";
    public const string ColEngine = "Motor No";

    private readonly VehicleService _vehicles;
    private readonly LookupService _lookups;

    public VehicleImportService(VehicleService vehicles, LookupService lookups)
    { _vehicles = vehicles; _lookups = lookups; }

    public IReadOnlyList<string> SampleHeaders() => new[]
    {
        ColCode, ColPlate, ColYear, ColStatus, ColStatusNote, ColMeter, ColMeterUnit,
        ColType, ColCategory, ColBrand, ColModel, ColBranch, ColDriver, ColChassis, ColEngine,
    };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "vehicles", PermissionAction.View);
        var res = new ImportLookupResolver(_lookups, s);
        var errors = new List<ImportRowError>(); int valid = 0;
        // Dosya İÇİNDE tekrar eden iç kodu da yakala (DB'de yok ama aynı dosyada iki kez geçiyor olabilir).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!Validate(row, out var err))
            {
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
                continue;
            }
            if (!BranchKnown(s, res, row, out var berr))
            {
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, berr!));
                continue;
            }
            var code = Get(row, ColCode)!.Trim();
            if (!seen.Add(code))
            {
                if (errors.Count < ImportResult.MaxReportedErrors)
                    errors.Add(new ImportRowError(row.RowNumber, $"Bu iç kod dosyada birden çok kez geçiyor: {code}"));
                continue;
            }
            valid++;
        }
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    /// <summary>Commit + bu aktarımda OLUŞAN yeni tanımlar (kullanıcı yazım hatalarını görebilsin).</summary>
    public (ImportResult Result, IReadOnlyList<string> CreatedLookups) CommitWithLookups(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "vehicles", PermissionAction.Create);
        var res = new ImportLookupResolver(_lookups, s);
        // ⚠️ int.MaxValue ŞART: List varsayılanı 200'dür → 200'den fazla aracı olan firmada mükerrer
        // kontrolü 201. araçtan sonrasını "yok" sanıp KOPYA oluştururdu.
        var existing = _vehicles.List(s, null, int.MaxValue).Select(v => v.InternalCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportRowError>(); int added = 0, skipped = 0, failed = 0;

        foreach (var row in rows)
        {
            if (!Validate(row, out var verr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            if (!BranchKnown(s, res, row, out var berr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, berr!)); continue; }
            try
            {
                var code = Get(row, ColCode)!.Trim();
                if (!existing.Add(code)) { skipped++; continue; }   // zaten var → atla (idempotent)

                // Marka ÖNCE çözülür: model markaya bağlıdır (markasız model oluşturulamaz).
                var brandId = res.VehicleBrand(Get(row, ColBrand));

                _vehicles.Create(s, new NewVehicle(
                    InternalCode: code,
                    Plate: Empty(Get(row, ColPlate)),
                    ProductionYear: ParseInt(Get(row, ColYear)),
                    CurrentMeter: ParseDecimal(Get(row, ColMeter)) ?? 0m,
                    MeterUnit: MeterUnitCode(Get(row, ColMeterUnit)),
                    // Satırda "Şube" boşsa içe aktarım ekranında seçilen şubeye (oturum) düşer (2026-07-26).
                    BranchId: res.Branch(Get(row, ColBranch)) ?? s.OperatingBranchId,
                    DriverPersonnelId: res.Personnel(Get(row, ColDriver)),
                    ChassisNo: Empty(Get(row, ColChassis)),
                    EngineNo: Empty(Get(row, ColEngine)),
                    Status: VehicleStatus.Parse(Get(row, ColStatus))!,   // Validate geçerliliği doğruladı
                    // Not yalnız Bakımda/Arızalı'da saklanır — bu kuralı servis uygular.
                    StatusNote: Empty(Get(row, ColStatusNote)),
                    VehicleTypeId: res.VehicleType(Get(row, ColType)),
                    CategoryId: res.VehicleCategory(Get(row, ColCategory)),
                    BrandId: brandId,
                    VehicleModelId: res.VehicleModel(brandId, Get(row, ColModel))));
                added++;
            }
            catch (Exception ex)
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }
        return (new ImportResult(false, rows.Count, added, added, skipped, failed, errors), res.CreatedNames);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows) => CommitWithLookups(s, rows).Result;

    private static bool Validate(ImportRow row, out string? error)
    {
        if (string.IsNullOrWhiteSpace(Get(row, ColCode))) { error = "İç Kod zorunlu."; return false; }

        var yr = Get(row, ColYear);
        if (!string.IsNullOrWhiteSpace(yr))
        {
            var y = ParseInt(yr);
            if (y is null) { error = $"Üretim Yılı sayısal olmalı: {yr}"; return false; }
            if (!FieldChecks.YearInRange(y))
            { error = $"Üretim Yılı {FieldChecks.MinVehicleYear}–{FieldChecks.MaxVehicleYear} aralığında olmalı: {yr}"; return false; }
        }

        var meter = Get(row, ColMeter);
        if (!string.IsNullOrWhiteSpace(meter))
        {
            var m = ParseDecimal(meter);
            if (m is null) { error = $"Sayaç sayısal olmalı: {meter}"; return false; }
            if (m < 0) { error = $"Sayaç negatif olamaz: {meter}"; return false; }
        }

        // Tanınmayan durum SESSİZCE "aktif" yazılmaz — satır reddedilir (yanlış durum = yanlış veri).
        if (VehicleStatus.Parse(Get(row, ColStatus)) is null)
        { error = $"Geçersiz Durum: {Get(row, ColStatus)} (Aktif / Pasif / Bakımda / Arızalı)"; return false; }

        var mu = Get(row, ColMeterUnit);
        if (!string.IsNullOrWhiteSpace(mu) && MeterUnitCodeOrNull(mu) is null)
        { error = $"Geçersiz Birim: {mu} (km / saat)"; return false; }

        error = null; return true;
    }

    private static string MeterUnitCode(string? s) => MeterUnitCodeOrNull(s) ?? "km";

    private static string? MeterUnitCodeOrNull(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "" => "km",
        "km" or "kilometre" => "km",
        "hour" or "saat" or "sa" or "h" => "hour",
        _ => null,
    };

    private static int? ParseInt(string? s)
        => int.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int?)null;

    /// <summary>Türk Excel'i virgüllü ondalık yazar ("12,5"). Money.Parse KULLANILMAZ — virgülü binlik
    /// ayırıcı sayıp 10 kat bozardı (bkz. MaterialImportService açıklaması).</summary>
    private static decimal? ParseDecimal(string? s)
        => decimal.TryParse(s?.Replace(',', '.').Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Get(ImportRow row, string col) => row.Values.TryGetValue(col, out var v) ? v : null;

    /// <summary>
    /// Şube / Şantiye adı verilmişse TANIMLI olmalı (kullanıcı kararı 2026-08-09).
    /// İçe aktarma artık Şube/Şantiye OLUŞTURMAZ; tanınmayan ad satır hatası üretir ve kullanıcı bunu
    /// ÖNİZLEMEDE (DryRun) görür. Boş bırakılan alanın davranışı DEĞİŞMEDİ (kullanıcının kendi şubesi).
    /// Tanımları okuma yetkisi yoksa kontrol atlanır (yeni yetki şartı getirilmez).
    /// </summary>
    private static bool BranchKnown(SessionContext s, ImportLookupResolver res, ImportRow row, out string? error)
    {
        error = null;
        var name = Get(row, ColBranch);
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (!AccessControl.Can(s, "definitions", PermissionAction.View)) return true;
        if (res.Branch(name) is not null) return true;
        error = $"Şube / Şantiye bulunamadı: '{name.Trim()}'. Lütfen Şube / Şantiye Tanımları ekranından ekleyin ya da adı düzeltin.";
        return false;
    }
}
