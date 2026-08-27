using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Tanımlar — tanım listeleri (kategori/birim/marka/tedarikçi/şube/araç) accordion.
/// Geliştirici Modu artık Ayarlar menüsünde ayrı sekmede; bağlantı/marka setup'ta otomatik.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    [ObservableProperty] private string? _status;

    /// <summary>Tanım listeleri (accordion bölümleri): kategori/birim/marka/tedarikçi/şube/araç tanımları.</summary>
    public System.Collections.ObjectModel.ObservableCollection<LookupSectionViewModel> LookupSections { get; } = new();

    /// <summary>Malzeme ALT KATEGORİ yönetimi (madde 10): kategori seç → alt kategorilerini yönet.</summary>
    public SubCategorySectionViewModel SubCategorySection { get; }

    public SettingsViewModel(SessionContext session)
    {
        _session = session;
        SubCategorySection = new SubCategorySectionViewModel(session);
        BuildLookupSections();
    }

    private void BuildLookupSections()
    {
        var L = DesktopServices.Lookups;
        void Add(string title, string table,
            Func<SessionContext, IReadOnlyList<DepoWise.Infrastructure.Materials.LookupItem>> load,
            Action<SessionContext, string> add)
            => LookupSections.Add(new LookupSectionViewModel(_session, title, table, load, add));

        // Ekran-bazlı gruplama (kullanıcı isteği 2026-08-05, madde 10): başlık öneki + sıralama ile
        // hangi ekrana ait tanım olduğu net. (Alt kategori yönetimi ayrı/daha büyük iş — bkz. DEVAM.md.)
        // ── MALZEME tanımları ──
        Add("Malzeme — Kategoriler", "material_categories", s => L.ListCategories(s), (s, n) => L.AddCategory(s, n));
        Add("Malzeme — Birimler", "units", s => L.List(s, "units"), (s, n) => L.AddUnit(s, n));
        Add("Malzeme — Markalar", "brands", s => L.ListBrands(s, "material"), (s, n) => L.AddBrand(s, n, "material"));
        Add("Malzeme — Tedarikçiler", "suppliers", s => L.List(s, "suppliers"), (s, n) => L.AddSupplier(s, n));
        // ── ARAÇ tanımları ──
        Add("Araç — Tipler", "vehicle_types", s => L.List(s, "vehicle_types"), (s, n) => L.AddVehicleType(s, n));
        Add("Ekipman — Türler", "equipment_types", s => L.List(s, "equipment_types"), (s, n) => L.AddEquipmentType(s, n));   // EKP-01
        Add("Araç — Kategoriler", "vehicle_categories", s => L.List(s, "vehicle_categories"), (s, n) => L.AddVehicleCategory(s, n));
        Add("Araç — Markalar", "brands", s => L.ListBrands(s, "vehicle"), (s, n) => L.AddVehicleBrand(s, n));
        // NOT: "Genel — Şube / Şantiye" girdisi KALDIRILDI (2026-08-09). Şube/Şantiye tanımları
        // admin-kısıtlı "branches" modülüne aittir ve yalnız Şube / Şantiye Tanımları ekranından
        // yönetilir; "Tanımlar" (definitions) yetkisiyle eklenip silinemez.
    }

}
