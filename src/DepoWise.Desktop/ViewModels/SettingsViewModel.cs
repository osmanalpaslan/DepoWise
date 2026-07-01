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

    public SettingsViewModel(SessionContext session)
    {
        _session = session;
        BuildLookupSections();
    }

    private void BuildLookupSections()
    {
        var L = DesktopServices.Lookups;
        void Add(string title, string table,
            Func<SessionContext, IReadOnlyList<DepoWise.Infrastructure.Materials.LookupItem>> load,
            Action<SessionContext, string> add)
            => LookupSections.Add(new LookupSectionViewModel(_session, title, table, load, add));

        Add("Malzeme Kategorileri", "material_categories", s => L.ListCategories(s), (s, n) => L.AddCategory(s, n));
        Add("Birimler", "units", s => L.List(s, "units"), (s, n) => L.AddUnit(s, n));
        Add("Markalar (Malzeme)", "brands", s => L.ListBrands(s, "material"), (s, n) => L.AddBrand(s, n, "material"));
        Add("Tedarikçiler", "suppliers", s => L.List(s, "suppliers"), (s, n) => L.AddSupplier(s, n));
        Add("Şube / Şantiye", "branches", s => L.List(s, "branches"), (s, n) => L.AddBranch(s, n));
        Add("Araç Tipleri", "vehicle_types", s => L.List(s, "vehicle_types"), (s, n) => L.AddVehicleType(s, n));
        Add("Araç Kategorileri", "vehicle_categories", s => L.List(s, "vehicle_categories"), (s, n) => L.AddVehicleCategory(s, n));
        Add("Markalar (Araç)", "brands", s => L.ListBrands(s, "vehicle"), (s, n) => L.AddVehicleBrand(s, n));
    }

}
