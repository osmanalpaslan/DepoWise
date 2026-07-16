using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// YAKIT DAĞITIM içe aktarımı (araca yakıt verme) — Excel'de tutulan geçmiş kayıtları sisteme alır.
///
/// Gerçek dünya uyumu (kullanıcı isteği 2026-07-16): elde tutulan Excel'lerde alanlar eksik olur, bu yüzden
/// yalnız ARAÇ ve LİTRE zorunludur. Sayaç/fiyat/personel/tarih boş bırakılabilir:
///   • Sayaç boş → aracın MEVCUT sayacı yazılır (sayaç ileri gitmez, geçmiş kayıt sayacı bozmaz).
///   • Fiyat boş → servis o anki depo fiyatını (son depo girişi) kullanır.
///   • Personel boş → boş geçilir (canlı ekranda zorunlu; içe aktarımda geçmiş veri için değil).
///   • Tarih boş → bugün.
/// Araç hem İÇ KOD hem PLAKA ile eşlenir (Excel'de genelde plaka yazar, sistemin iç kodu değil).
///
/// ⚠️ DEPO BAKİYESİ: FuelService.Distribute, depoda yeterli yakıt yoksa kaydı REDDEDER (negatif bakiye
/// yasak — CLAUDE.md §4). Bu yüzden DryRun, dosyadaki toplam litreyi mevcut depo bakiyesiyle kıyaslar ve
/// yetersizse ÖNCEDEN uyarır: kullanıcı önce "Yakıt Depo Girişi" aktarmalıdır.
///
/// ⚠️ SIRA: Commit satırları TARİHE GÖRE sıralayarak işler. Sıralanmazsa prev_meter (önceki sayaç) yanlış
/// kaydedilir ve sayaç geçmişi tutarsız olur (MeterRule yalnız ileri gider).
///
/// ⚠️ TEKRAR AKTARIM: operation_id satır içeriğinden DETERMİNİSTİK üretilir → aynı dosya ikinci kez
/// aktarılırsa kayıt TEKRARLANMAZ ("zaten vardı" olarak atlanır). Satır numarası da karışıma girer, böylece
/// aynı araca aynı gün aynı litre iki kez verildiyse (meşru tekrar) ikisi de korunur.
/// </summary>
public sealed class FuelImportService
{
    public const string ColVehicle = "Araç";        // İç Kod veya Plaka
    public const string ColDate = "Tarih";          // gg.aa.yyyy (boş = bugün)
    public const string ColLiters = "Litre";        // ZORUNLU
    public const string ColMeter = "Sayaç";         // boş = aracın mevcut sayacı
    public const string ColPrice = "Birim Fiyat";   // boş = güncel depo fiyatı
    public const string ColPersonnel = "Personel";  // boş = yok
    public const string ColNote = "Açıklama";

    private readonly FuelService _fuel;
    private readonly VehicleService _vehicles;
    private readonly LookupService _lookups;

    public FuelImportService(FuelService fuel, VehicleService vehicles, LookupService lookups)
    { _fuel = fuel; _vehicles = vehicles; _lookups = lookups; }

    public IReadOnlyList<string> SampleHeaders()
        => new[] { ColVehicle, ColDate, ColLiters, ColMeter, ColPrice, ColPersonnel, ColNote };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "fuel", PermissionAction.View);
        var vmap = VehicleMap(s);
        var errors = new List<ImportRowError>();
        int valid = 0; decimal totalLiters = 0m;

        foreach (var row in rows)
        {
            if (Validate(row, vmap, out var err))
            {
                valid++;
                totalLiters += ParseDecimal(Get(row, ColLiters)) ?? 0m;
            }
            else if (errors.Count < ImportResult.MaxReportedErrors)
                errors.Add(new ImportRowError(row.RowNumber, err!));
        }

        // Depo yeterli mi? Yetersizse Commit satır satır patlar; kullanıcıya ŞİMDİ söyle.
        var depot = _fuel.GetDepotBalance(s);
        if (valid > 0 && totalLiters > depot && errors.Count < ImportResult.MaxReportedErrors)
            errors.Add(new ImportRowError(0,
                $"DEPO YETERSİZ: dosyadaki toplam {totalLiters:0.##} L, depoda mevcut {depot:0.##} L. " +
                $"Önce 'Yakıt Depo Girişi' aktarın ({totalLiters - depot:0.##} L eksik), sonra dağıtımları aktarın."));

        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "fuel", PermissionAction.Create);
        var vmap = VehicleMap(s);
        var pmap = _lookups.ListPersonnel(s).ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);
        var errors = new List<ImportRowError>();
        int added = 0, skipped = 0, failed = 0;

        // TARİH SIRASI: prev_meter doğru kaydedilsin (MeterRule yalnız ileri gider). Tarihsiz satırlar sona.
        var ordered = rows
            .Select(r => (Row: r, Date: ParseDate(Get(r, ColDate))))
            .OrderBy(x => x.Date ?? long.MaxValue)
            .ToList();

        foreach (var (row, date) in ordered)
        {
            if (!Validate(row, vmap, out var verr))
            {
                failed++;
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!));
                continue;
            }
            try
            {
                var veh = vmap[Key(Get(row, ColVehicle)!)];
                var liters = ParseDecimal(Get(row, ColLiters))!.Value;
                // Sayaç boşsa aracın mevcut sayacı → ShouldAdvance false → sayaç DEĞİŞMEZ (geçmiş kaydı bozmaz).
                var meter = ParseDecimal(Get(row, ColMeter)) ?? veh.CurrentMeter;
                var opId = OperationId(s.CompanyId, row.RowNumber, veh.Id, date, liters, meter);

                if (_fuel.OperationApplied(s, opId, depotEntry: false)) { skipped++; continue; }

                _fuel.Distribute(s, new NewDistribution(
                    VehicleId: veh.Id,
                    Liters: liters,
                    CurrentMeter: meter,
                    UnitPrice: ParseDecimal(Get(row, ColPrice)),        // null → servis güncel depo fiyatını kullanır
                    PersonnelId: LookupId(pmap, Get(row, ColPersonnel)),
                    DistributionDate: date,                              // null → servis "şimdi" yazar
                    Note: Empty(Get(row, ColNote))), opId);
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

    // ── Doğrulama ──────────────────────────────────────────────────────────────────────────
    private static bool Validate(ImportRow row, IReadOnlyDictionary<string, VehicleListRow> vmap, out string? error)
    {
        var v = Get(row, ColVehicle);
        if (string.IsNullOrWhiteSpace(v)) { error = "Araç (İç Kod veya Plaka) zorunlu."; return false; }
        if (!vmap.ContainsKey(Key(v)))
        { error = $"Araç bulunamadı: {v} (İç Kod ya da Plaka yazın; araç önce sisteme tanımlı olmalı)"; return false; }

        var lt = Get(row, ColLiters);
        if (string.IsNullOrWhiteSpace(lt)) { error = "Litre zorunlu."; return false; }
        var liters = ParseDecimal(lt);
        if (liters is null || liters <= 0) { error = $"Litre pozitif bir sayı olmalı: {lt}"; return false; }

        foreach (var (col, val) in new[] { (ColMeter, Get(row, ColMeter)), (ColPrice, Get(row, ColPrice)) })
            if (!string.IsNullOrWhiteSpace(val) && ParseDecimal(val) is null)
            { error = $"{col}: sayı olmalı ({val})"; return false; }

        var d = Get(row, ColDate);
        if (!string.IsNullOrWhiteSpace(d) && ParseDate(d) is null)
        { error = $"Tarih gg.aa.yyyy olmalı: {d}"; return false; }

        error = null; return true;
    }

    /// <summary>Araçlar hem İÇ KOD hem PLAKA anahtarıyla haritalanır (Excel'de plaka yazılı olur).</summary>
    private Dictionary<string, VehicleListRow> VehicleMap(SessionContext s)
    {
        var map = new Dictionary<string, VehicleListRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in _vehicles.List(s))
        {
            map[Key(v.InternalCode)] = v;
            if (!string.IsNullOrWhiteSpace(v.Plate)) map[Key(v.Plate!)] = v;   // iç kod çakışırsa iç kod kazanır değil — son yazan
        }
        return map;
    }

    /// <summary>Plaka karşılaştırması boşluk/büyük-küçük harf duyarsız ("34 ABC 123" = "34abc123").</summary>
    private static string Key(string s) => s.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    /// <summary>Satır içeriğinden DETERMİNİSTİK operation_id — aynı dosya tekrar aktarılırsa kayıt tekrarlanmaz.
    /// Satır numarası da karışımda: aynı araca aynı gün aynı litre iki AYRI satırda meşru olabilir, ikisi de korunur.</summary>
    private static string OperationId(string companyId, int rowNumber, string vehicleId, long? date, decimal liters, decimal meter)
    {
        var raw = $"fuel-import|{companyId}|{rowNumber}|{vehicleId}|{date?.ToString() ?? "-"}|{liters:0.####}|{meter:0.####}";
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
