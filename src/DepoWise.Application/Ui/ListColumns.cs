using System.Collections.Generic;
using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>Bir liste ekranındaki seçilebilir bir kolon: anahtar (filtre/tercih parametresi) + görünen ad.
/// IsNumeric=true ise filtre kutusu tam-sayı/karşılaştırma/aralık söz dizimi kabul eder (kullanıcı isteği
/// 2026-07-18: "stokta sadece 5 olanları listelemek istiyorum" — "içerir" araması 15/25/50'yi de yakalıyordu).
/// Bkz. Infrastructure/Database/GridQuery.cs.</summary>
public sealed record ListColumn(string Key, string Label, bool IsNumeric = false);

/// <summary>
/// Malzemeler listesi — seçilebilir kolonlar (kullanıcı isteği 2026-07-17): "yeni kayıt formunda fotoğraf
/// hariç olan her alanı liste tablosuna ekleyebilelim veya çıkarabilelim." Sıra, ekranda göründüğü sıradır.
///
/// "Açılış Stok" BİLİNÇLİ OLARAK YOK: kartta kalıcı bir alan değil, yalnız kayıt anındaki bir hareket
/// (bkz. OpeningStockService) — kartın "şu an"ki durumunu göstermez, listede yanıltıcı olurdu.
///
/// NOT: Web projesi tek başınadır (Application'a referansı yok) → aynı liste
/// <c>DepoWise.Web/Services/ListColumns.cs</c> içinde AYNADIR. İkisi birlikte güncellenmelidir.
/// </summary>
public static class MaterialListColumns
{
    public const string Code = "code";
    public const string Name = "name";
    public const string Type = "type";
    public const string Category = "category";
    public const string Unit = "unit";
    public const string Brand = "brand";
    public const string Supplier = "supplier";
    public const string UnitPrice = "unitPrice";
    public const string Currency = "currency";
    public const string MinStock = "minStock";
    public const string Stock = "stock";
    public const string Status = "status";
    public const string Description = "description";
    public const string CompatibleVehicles = "compatibleVehicles";
    public const string Equivalents = "equivalents";

    public static readonly IReadOnlyList<ListColumn> All = new[]
    {
        new ListColumn(Code, "Kod"),
        new ListColumn(Name, "Ad"),
        new ListColumn(Type, "Tür"),
        new ListColumn(Category, "Kategori"),
        new ListColumn(Unit, "Birim"),
        new ListColumn(Brand, "Marka"),
        new ListColumn(Supplier, "Tedarikçi"),
        new ListColumn(UnitPrice, "Birim Fiyat", IsNumeric: true),
        new ListColumn(Currency, "Para Birimi"),
        new ListColumn(MinStock, "Min Stok", IsNumeric: true),
        new ListColumn(Stock, "Stok", IsNumeric: true),
        new ListColumn(Status, "Durum"),
        new ListColumn(Description, "Açıklama"),
        new ListColumn(CompatibleVehicles, "Uyumlu Araçlar"),
        new ListColumn(Equivalents, "Muadil Malzeme"),
    };

    /// <summary>Kullanıcı hiç seçim yapmadıysa (ilk açılış) gösterilecek varsayılan kolonlar — eski sabit
    /// listeyle AYNI (davranış değişmesin).</summary>
    public static readonly IReadOnlyList<string> DefaultVisible = new[]
    {
        Code, Name, Type, UnitPrice, Currency, MinStock, Stock, Status,
    };

    /// <summary>Bilinmeyen/eski anahtarları eler; sıra <see cref="All"/>'a göre normalize edilir.</summary>
    public static IReadOnlyList<string> Sanitize(IEnumerable<string>? keys)
    {
        if (keys is null) return DefaultVisible;
        var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
        var result = All.Where(c => wanted.Contains(c.Key)).Select(c => c.Key).ToList();
        return result.Count > 0 ? result : DefaultVisible;
    }
}

/// <summary>
/// Araçlar listesi — seçilebilir kolonlar (kullanıcı isteği 2026-07-17). "Şablon" BİLİNÇLİ OLARAK YOK
/// (formda diğer alanları doldurma kolaylığıdır, kartın kalıcı bir alanı değil — malzeme içe aktarımındaki
/// "Şablon" istisnasıyla aynı gerekçe). "Bakım/Muayene" uyarı kolonu her zaman sabittir, bu listede değildir
/// (form alanı değil, hesaplanan bir göstergedir — kullanıcı tercihiyle kapatılamaz).
///
/// NOT: Web projesi tek başınadır (Application'a referansı yok) → aynı liste
/// <c>DepoWise.Web/Services/ListColumns.cs</c> içinde AYNADIR. İkisi birlikte güncellenmelidir.
/// </summary>
public static class VehicleListColumns
{
    public const string InternalCode = "internalCode";
    public const string Plate = "plate";
    public const string ProductionYear = "productionYear";
    public const string Meter = "meter";
    public const string Status = "status";
    public const string StatusNote = "statusNote";
    public const string VehicleType = "vehicleType";
    public const string Category = "category";
    public const string Brand = "brand";
    public const string Model = "model";
    public const string Branch = "branch";
    public const string Driver = "driver";
    public const string ChassisNo = "chassisNo";
    public const string EngineNo = "engineNo";

    public static readonly IReadOnlyList<ListColumn> All = new[]
    {
        new ListColumn(InternalCode, "İç Kod"),
        new ListColumn(Plate, "Plaka"),
        new ListColumn(ProductionYear, "Üretim Yılı", IsNumeric: true),
        new ListColumn(Meter, "Sayaç", IsNumeric: true),
        new ListColumn(Status, "Durum"),
        new ListColumn(StatusNote, "Durum Notu"),
        new ListColumn(VehicleType, "Araç Tipi"),
        new ListColumn(Category, "Kategori"),
        new ListColumn(Brand, "Marka"),
        new ListColumn(Model, "Model"),
        new ListColumn(Branch, "Şube/Şantiye"),
        new ListColumn(Driver, "Sürücü"),
        new ListColumn(ChassisNo, "Şase No"),
        new ListColumn(EngineNo, "Motor No"),
    };

    /// <summary>Eski sabit listeyle AYNI (davranış değişmesin).</summary>
    public static readonly IReadOnlyList<string> DefaultVisible = new[]
    {
        InternalCode, Plate, ProductionYear, Meter, Status,
    };

    public static IReadOnlyList<string> Sanitize(IEnumerable<string>? keys)
    {
        if (keys is null) return DefaultVisible;
        var wanted = new HashSet<string>(keys, StringComparer.Ordinal);
        var result = All.Where(c => wanted.Contains(c.Key)).Select(c => c.Key).ToList();
        return result.Count > 0 ? result : DefaultVisible;
    }
}
