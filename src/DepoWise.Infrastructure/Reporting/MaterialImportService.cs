using System.Globalization;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Malzeme içe aktarımı — Kod benzersizse oluşturur (varsa ATLAR, idempotent).
/// Commit iş kurallarını ATLAMAZ (MaterialService.Create: tenant + permission + kod benzersiz + currency).
/// Politika: satır bazlı (bir hatalı satır diğerlerini bozmaz); dry-run hiç yazmaz.
///
/// Sütunlar YENİ KAYIT FORMUYLA BİREBİR aynıdır (kullanıcı kuralı 2026-07-16: "içeri alma şablonlarında
/// yeni kayıt formunda bulunan her alan olmalı — fotoğraf hariç"). Tek istisna: "Şablon" alanı (formda
/// diğer alanları doldurma kolaylığı; Excel'de her alan zaten yazılı).
///
/// Tanım alanları (Kategori/Alt Kategori/Birim/Marka/Tedarikçi) İSİMLE yazılır, yoksa OTOMATİK OLUŞTURULUR.
/// </summary>
public sealed class MaterialImportService
{
    public const string ColCode = "Kod";               // ZORUNLU, benzersiz
    public const string ColName = "Ad";                // ZORUNLU
    public const string ColType = "Tür";
    public const string ColCategory = "Kategori";
    public const string ColSubCategory = "Alt Kategori";
    public const string ColUnit = "Birim";
    public const string ColBrand = "Marka";
    public const string ColSupplier = "Tedarikçi";
    public const string ColVehicles = "Uyumlu Araçlar";     // araç iç kod/plaka, virgülle ayrılmış
    public const string ColEquivalents = "Muadil Malzeme";  // malzeme kodu, virgülle ayrılmış
    public const string ColPrice = "Birim Fiyat";
    public const string ColMinStock = "Min Stok";
    public const string ColOpening = "Açılış Stok";
    public const string ColDescription = "Açıklama";
    public const string ColCurrency = "Para Birimi";

    private readonly MaterialService _materials;
    private readonly LookupService _lookups;
    private readonly OpeningStockService? _opening;
    private readonly VehicleService? _vehicles;

    /// <summary>Açılış stoğu ve uyumlu araçlar OPSİYONEL bağımlılıktır: verilmezse o sütunlar yok sayılır
    /// (eski çağrı yerleri bozulmasın diye — servisler null geçilebilir).</summary>
    public MaterialImportService(MaterialService materials, LookupService lookups,
        OpeningStockService? opening = null, VehicleService? vehicles = null)
    { _materials = materials; _lookups = lookups; _opening = opening; _vehicles = vehicles; }

    public IReadOnlyList<string> SampleHeaders() => new[]
    {
        ColCode, ColName, ColType, ColCategory, ColSubCategory, ColUnit, ColBrand, ColSupplier,
        ColVehicles, ColEquivalents, ColPrice, ColMinStock, ColOpening, ColDescription, ColCurrency,
    };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "materials", PermissionAction.View);
        var errors = new List<ImportRowError>();
        int valid = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!Validate(row, out var err))
            {
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
                continue;
            }
            var code = Get(row, ColCode)!.Trim();
            if (!seen.Add(code))
            {
                if (errors.Count < ImportResult.MaxReportedErrors)
                    errors.Add(new ImportRowError(row.RowNumber, $"Bu kod dosyada birden çok kez geçiyor: {code}"));
                continue;
            }
            valid++;
        }
        return new ImportResult(DryRun: true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    /// <summary>Commit + bu aktarımda OLUŞAN yeni tanımlar (kullanıcı yazım hatalarını görebilsin).</summary>
    public (ImportResult Result, IReadOnlyList<string> CreatedLookups) CommitWithLookups(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "materials", PermissionAction.Create);
        var res = new ImportLookupResolver(_lookups, s);
        var errors = new List<ImportRowError>();
        int added = 0, skipped = 0, failed = 0;

        // Araç eşlemesi (Uyumlu Araçlar) — 2600 satır için TEK sefer yüklenir, satır başına sorgu YOK.
        var vehMap = BuildVehicleMap(s);
        // Kod → id (mükerrer kontrolü + muadil bağlama). İçe aktarım sırasında eklenenler de buraya girer.
        // ⚠️ List(PageRequest) KULLANILMAZ: PageRequest.MaxLimit = 200'dür → Limit=100000 verilse bile
        // 200 satır döner ve 201. malzemeden sonrası "yok" sanılıp KOPYA oluşurdu. AllCodeToId sayfalamasızdır.
        var matMap = _materials.AllCodeToId(s);

        // Muadil bağlantısı, HER İKİ malzeme de var olduktan sonra kurulmalı (ileri referans: A'nın muadili
        // B ama B dosyada A'dan SONRA geliyor) → önce tüm malzemeler eklenir, muadiller 2. turda bağlanır.
        var pendingEquivalents = new List<(int RowNumber, string MaterialId, string[] Codes)>();

        foreach (var row in rows)
        {
            if (!Validate(row, out var verr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            try
            {
                var code = Get(row, ColCode)!.Trim();
                if (matMap.ContainsKey(code.ToUpperInvariant())) { skipped++; continue; }   // zaten var → atla

                var catId = res.MaterialCategory(Get(row, ColCategory));
                // Alt kategori seçiliyse malzemenin kategorisi ALT kategoridir (web formuyla aynı kural).
                var subId = res.MaterialSubCategory(catId, Get(row, ColSubCategory));

                var id = _materials.Create(s, new NewMaterial(
                    Code: code,
                    Name: Get(row, ColName)!.Trim(),
                    Type: Empty(Get(row, ColType)),
                    CategoryId: subId ?? catId,
                    UnitId: res.Unit(Get(row, ColUnit)),
                    BrandId: res.MaterialBrand(Get(row, ColBrand)),
                    SupplierId: res.Supplier(Get(row, ColSupplier)),
                    MinStock: ParseDecimal(Get(row, ColMinStock)) ?? 0m,
                    UnitPrice: ParseDecimal(Get(row, ColPrice)) ?? 0m,
                    Currency: string.IsNullOrWhiteSpace(Get(row, ColCurrency)) ? "TRY" : Get(row, ColCurrency)!.Trim(),
                    Description: Empty(Get(row, ColDescription))));
                matMap[code.ToUpperInvariant()] = id;

                // Uyumlu araçlar: iç kod VEYA plaka ile eşlenir; bulunmayanlar sessizce atlanır
                // (araç henüz aktarılmamış olabilir — malzeme kaydını bu yüzden reddetmek doğru olmaz).
                var vehIds = SplitList(Get(row, ColVehicles))
                    .Select(v => vehMap.TryGetValue(VehKey(v), out var vid) ? vid : null)
                    .Where(v => v is not null).Select(v => v!).Distinct().ToList();
                if (vehIds.Count > 0) _materials.SetCompatibleVehicles(s, id, vehIds);

                var eq = SplitList(Get(row, ColEquivalents));
                if (eq.Length > 0) pendingEquivalents.Add((row.RowNumber, id, eq));

                // Açılış stoğu 0 dışında ise stok hareketi (web formuyla aynı davranış). ADR-086: negatif de olur.
                var opening = ParseDecimal(Get(row, ColOpening)) ?? 0m;
                if (opening != 0 && _opening is not null)
                {
                    var price = ParseDecimal(Get(row, ColPrice));
                    _opening.RecordOpening(s, id, opening, Guid.NewGuid().ToString("N"), price > 0 ? price : null);
                }
                added++;
            }
            catch (Exception ex)
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }

        // 2. TUR — muadiller (artık tüm malzemeler mevcut, ileri referans çalışır).
        foreach (var (rowNumber, materialId, codes) in pendingEquivalents)
        {
            foreach (var c in codes)
            {
                if (!matMap.TryGetValue(c.Trim().ToUpperInvariant(), out var eqId) || eqId == materialId) continue;
                try { _materials.AddEquivalent(s, materialId, eqId); }
                catch (Exception ex)
                {
                    if (errors.Count < ImportResult.MaxReportedErrors)
                        errors.Add(new ImportRowError(rowNumber, $"Muadil bağlanamadı ({c}): {ex.Message}"));
                }
            }
        }

        return (new ImportResult(DryRun: false, rows.Count, added, added, skipped, failed, errors), res.CreatedNames);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows) => CommitWithLookups(s, rows).Result;

    /// <summary>Araçlar iç kod VE plaka anahtarıyla haritalanır (Excel'de ikisi de yazılabilir).</summary>
    private Dictionary<string, string> BuildVehicleMap(SessionContext s)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_vehicles is null) return map;
        try
        {
            foreach (var v in _vehicles.List(s, null, int.MaxValue))   // 200 varsayılan sınırı AŞILIR: 2600 satırlık dosyada 201. araçtan sonrası "bulunamadı" derdi
            {
                map[VehKey(v.InternalCode)] = v.Id;
                if (!string.IsNullOrWhiteSpace(v.Plate)) map[VehKey(v.Plate!)] = v.Id;
            }
        }
        catch { }
        return map;
    }

    /// <summary>Plaka/kod karşılaştırması boşluk-tire-harf duyarsız ("34 ABC 123" = "34abc123").</summary>
    private static string VehKey(string s) => s.Replace(" ", "").Replace("-", "").Trim().ToUpperInvariant();

    /// <summary>"A, B , C" → ["A","B","C"]. Noktalı virgül de ayırıcı sayılır (Excel'de ikisi de yaygın).</summary>
    private static string[] SplitList(string? s)
        => string.IsNullOrWhiteSpace(s)
            ? Array.Empty<string>()
            : s.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Validate(ImportRow row, out string? error)
    {
        if (string.IsNullOrWhiteSpace(Get(row, ColCode))) { error = "Kod zorunlu."; return false; }
        if (string.IsNullOrWhiteSpace(Get(row, ColName))) { error = "Ad zorunlu."; return false; }
        foreach (var col in new[] { ColPrice, ColMinStock, ColOpening })
        {
            var raw = Get(row, col);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var v = ParseDecimal(raw);
            if (v is null) { error = $"{col} sayısal olmalı: {raw}"; return false; }
            // ADR-086: Açılış Stok NEGATİF olabilir (firma devralırken mevcut/eksik stoğunu girer).
            // Fiyat ve Min Stok negatif OLAMAZ (anlamsız — eşik/tutar eksi olmaz).
            if (col != ColOpening && v < 0) { error = $"{col} negatif olamaz: {raw}"; return false; }
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
        => decimal.TryParse(s?.Replace(',', '.').Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : (decimal?)null;

    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static string? Get(ImportRow row, string col)
        => row.Values.TryGetValue(col, out var v) ? v : null;
}
