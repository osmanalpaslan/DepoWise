namespace DepoWise.Web.Services;

/// <summary>Bir liste ekranındaki seçilebilir bir kolon: anahtar (filtre/tercih parametresi) + görünen ad.</summary>
public sealed record ListColumn(string Key, string Label);

/// <summary>
/// Malzemeler listesi — seçilebilir kolonlar (kullanıcı isteği 2026-07-17). "Açılış Stok" BİLİNÇLİ OLARAK
/// YOK: kartta kalıcı bir alan değil, yalnız kayıt anındaki bir hareket.
///
/// NOT: Bu dosya, masaüstü/sunucu tarafındaki <c>DepoWise.Application/Ui/ListColumns.cs</c>'in AYNASIDIR
/// (web projesinin Application'a referansı yoktur). İkisi BİRLİKTE güncellenmelidir.
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
        new ListColumn(UnitPrice, "Birim Fiyat"),
        new ListColumn(Currency, "Para Birimi"),
        new ListColumn(MinStock, "Min Stok"),
        new ListColumn(Stock, "Stok"),
        new ListColumn(Status, "Durum"),
        new ListColumn(Description, "Açıklama"),
        new ListColumn(CompatibleVehicles, "Uyumlu Araçlar"),
        new ListColumn(Equivalents, "Muadil Malzeme"),
    };

    public static readonly IReadOnlyList<string> DefaultVisible = new[]
    {
        Code, Name, Type, UnitPrice, Currency, MinStock, Stock, Status,
    };
}

/// <summary>
/// Araçlar listesi — seçilebilir kolonlar (kullanıcı isteği 2026-07-17). "Şablon" ve "Bakım/Muayene"
/// BİLİNÇLİ OLARAK YOK (bkz. Application tarafındaki ayna dosyanın açıklaması).
///
/// NOT: Bu dosya, masaüstü/sunucu tarafındaki <c>DepoWise.Application/Ui/ListColumns.cs</c>'in AYNASIDIR
/// (web projesinin Application'a referansı yoktur). İkisi BİRLİKTE güncellenmelidir.
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
        new ListColumn(ProductionYear, "Üretim Yılı"),
        new ListColumn(Meter, "Sayaç"),
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

    public static readonly IReadOnlyList<string> DefaultVisible = new[]
    {
        InternalCode, Plate, ProductionYear, Meter, Status,
    };
}
