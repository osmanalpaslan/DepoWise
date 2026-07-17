using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// İçe aktarımda "isimden tanım bul, yoksa OLUŞTUR" (kullanıcı kuralı 2026-07-16: "hiçbir alana tanım
/// ekleme, ben içeri aldığımda tanımlar oluşacak").
///
/// ⚠️ PERFORMANS — bu sınıfın VAROLUŞ SEBEBİ: babanın dosyası ~2600 satır ve her satırda ~8 tanım alanı var
/// (tip/kategori/marka/model/şube/sürücü/birim/tedarikçi). Her satırda DB'ye sorulsaydı ~20.000 sorgu olurdu.
/// Burada tüm tanımlar BİR KEZ belleğe alınır; yeni oluşturulanlar da önbelleğe eklenir → satır başına
/// DB erişimi YOK (yalnız gerçekten yeni tanım oluşurken 1 INSERT).
///
/// ⚠️ EŞLEME: büyük/küçük harf + baş/son boşluk duyarsızdır ("CATERPILLAR" = "caterpillar " = "Caterpillar").
/// Bu, aynı markanın tekrar tekrar oluşmasını engeller. Ama GERÇEK yazım hatasını ("caterpiller") sistem
/// ayırt EDEMEZ → ayrı tanım olur. Bu yüzden <see cref="CreatedNames"/> raporlanır: kullanıcı aktarım
/// sonrası "hangi yeni tanımlar oluştu?" diye bakıp yazım hatalarını görebilsin.
/// </summary>
public sealed class ImportLookupResolver
{
    private readonly LookupService _lookups;
    private readonly SessionContext _s;

    /// <summary>Bu aktarımda OLUŞTURULAN yeni tanımlar: "Marka: Caterpillar" gibi. Kullanıcıya raporlanır.</summary>
    private readonly List<string> _created = new();
    public IReadOnlyList<string> CreatedNames => _created;
    public int CreatedCount => _created.Count;

    // kind → (normalize edilmiş ad → id). Tembel yüklenir (kullanılmayan tanım tablosu hiç okunmaz).
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.Ordinal);

    public ImportLookupResolver(LookupService lookups, SessionContext s)
    { _lookups = lookups; _s = s; }

    /// <summary>Eşleme anahtarı: boşluk kırpılır, büyük/küçük harf yok sayılır.</summary>
    private static string Key(string name) => name.Trim().ToUpperInvariant();

    /// <summary>
    /// Ortak yol: önbellekten bul; yoksa <paramref name="create"/> ile OLUŞTUR ve önbelleğe ekle.
    /// Boş isim → null (alan boş bırakılmış, tanım oluşturulmaz).
    /// </summary>
    private string? Resolve(string kind, string? name, Func<IReadOnlyList<LookupItem>> list, Func<string, string> create, string label)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (!_cache.TryGetValue(kind, out var map))
        {
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            try { foreach (var i in list()) map[Key(i.Name)] = i.Id; } catch { }
            _cache[kind] = map;
        }
        var k = Key(name);
        if (map.TryGetValue(k, out var id)) return id;

        var newId = create(name.Trim());
        map[k] = newId;
        _created.Add($"{label}: {name.Trim()}");
        return newId;
    }

    // ── Araç tanımları ──────────────────────────────────────────────────────────────────────
    public string? VehicleType(string? name)
        => Resolve("vehicle_types", name, () => _lookups.List(_s, "vehicle_types"),
            n => _lookups.AddVehicleType(_s, n), "Makine Tipi");

    public string? VehicleCategory(string? name)
        => Resolve("vehicle_categories", name, () => _lookups.List(_s, "vehicle_categories"),
            n => _lookups.AddVehicleCategory(_s, n), "Araç Kategorisi");

    public string? VehicleBrand(string? name)
        => Resolve("vehicle_brands", name, () => _lookups.ListBrands(_s, "vehicle"),
            n => _lookups.AddVehicleBrand(_s, n), "Araç Markası");

    /// <summary>Model MARKAYA bağlıdır → önbellek anahtarı markayı içerir. Marka yoksa model oluşturulamaz
    /// (modelin ebeveyni zorunlu) → null döner, satır modelsiz geçer.</summary>
    public string? VehicleModel(string? brandId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(brandId)) return null;
        return Resolve("vehicle_models:" + brandId, name, () => _lookups.ListVehicleModels(_s, brandId!),
            n => _lookups.AddVehicleModel(_s, brandId!, n), "Araç Modeli");
    }

    /// <summary>Şube/şantiye. Yeni oluşanlar "şantiye" (site) türünde açılır.</summary>
    public string? Branch(string? name)
        => Resolve("branches", name, () => _lookups.List(_s, "branches"),
            n => _lookups.AddBranch(_s, n), "Şube/Şantiye");

    /// <summary>Personel (sürücü/teknisyen/yakıtı veren).</summary>
    public string? Personnel(string? name)
        => Resolve("personnel", name, () => _lookups.ListPersonnel(_s),
            n => _lookups.AddPersonnel(_s, n), "Personel");

    // ── Malzeme tanımları ───────────────────────────────────────────────────────────────────
    public string? Unit(string? name)
        => Resolve("units", name, () => _lookups.List(_s, "units"),
            n => _lookups.AddUnit(_s, n), "Birim");

    public string? Supplier(string? name)
        => Resolve("suppliers", name, () => _lookups.List(_s, "suppliers"),
            n => _lookups.AddSupplier(_s, n), "Tedarikçi");

    public string? MaterialBrand(string? name)
        => Resolve("material_brands", name, () => _lookups.ListBrands(_s, "material"),
            n => _lookups.AddBrand(_s, n, "material"), "Malzeme Markası");

    /// <summary>Üst (ana) malzeme kategorisi.</summary>
    public string? MaterialCategory(string? name)
        => Resolve("material_categories", name, () => _lookups.ListCategories(_s, null),
            n => _lookups.AddCategory(_s, n), "Malzeme Kategorisi");

    /// <summary>Alt kategori — ÜST kategoriye bağlıdır (ebeveynsiz alt kategori olmaz → null).</summary>
    public string? MaterialSubCategory(string? parentId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parentId)) return null;
        return Resolve("material_subcategories:" + parentId, name, () => _lookups.ListCategories(_s, parentId),
            n => _lookups.AddCategory(_s, n, parentId), "Alt Kategori");
    }
}
