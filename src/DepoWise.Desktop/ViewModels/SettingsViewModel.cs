using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>2026-09-03 (kullanıcı isteği): ARAÇ MODELLERİ yönetimi — marka seç → modellerini yönet.
    /// Eksikti: formda "+" ile model eklenebiliyordu ama Tanımlar'dan yönetilemiyordu.</summary>
    public VehicleModelSectionViewModel VehicleModelSection { get; }

    public SettingsViewModel(SessionContext session)
    {
        _session = session;
        SubCategorySection = new SubCategorySectionViewModel(session);
        VehicleModelSection = new VehicleModelSectionViewModel(session);
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
        // ⭐ FAZ 4.5 (kullanıcı isteği 2026-09-06): Personel formunda "+" ile unvan eklenebiliyordu ama
        // bu tanım "Tanımlar" ekranında YOKTU — yani yanlış eklenen bir unvan hiçbir yerden düzeltilemiyordu.
        // Unvanlar ortak LookupService'te değil kendi servisindedir (yetki: personnel), bu yüzden
        // işlemler delege olarak geçilir. Kilit (sabit tanım) bu tabloda YOKTUR → düğme çizilmez.
        LookupSections.Add(new LookupSectionViewModel(_session, "Personel — Unvanlar", "personnel_titles",
            s => DesktopServices.PersonnelTitles.List(s)
                  .Select(t => new DepoWise.Infrastructure.Materials.LookupItem(t.Id, t.Name, false)).ToList(),
            (s, n) => DesktopServices.PersonnelTitles.Create(s, n),
            delete: (s, id) => DesktopServices.PersonnelTitles.Delete(s, id),
            // ⚠️ UNVAN YENİDEN ADLANDIRILMAZ: personel kaydı unvanı METİN olarak saklar (personnel.title),
            // yani tanımın adını değiştirmek mevcut personelin unvanını GÜNCELLEMEZ — sessiz tutarsızlık olurdu.
            // Doğru akış: yeni unvanı ekle, personeli güncelle, eskisini sil.
            rename: (s, id, ad) => throw new System.InvalidOperationException(
                "Unvan adı değiştirilemez (personel kayıtları unvanı metin olarak saklar). Yeni unvan ekleyip personeli güncelleyin."),
            kilitDestekli: false));

        // NOT: "Genel — Şube / Şantiye" girdisi KALDIRILDI (2026-08-09). Şube/Şantiye tanımları
        // admin-kısıtlı "branches" modülüne aittir ve yalnız Şube / Şantiye Tanımları ekranından
        // yönetilir; "Tanımlar" (definitions) yetkisiyle eklenip silinemez.
    }

}
