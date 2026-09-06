using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;   // BKM-04: BranchRow (malzemenin çekildiği depo)
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Bakım Takibi — sekmeli: (1) Bakım Tanımları (tanım CRUD + ilişkili araç + alt bakım), (2) Uyarılar (GetAlerts).
/// "Araç Bakımları" sekmesi sonraki fazda. İş kuralları servis katmanında.
/// </summary>
public sealed partial class MaintenanceViewModel : ViewModelBase, IDeepLinkTarget, IKayitLoguKaynagi
{

    // ⭐ FAZ 4.3 (kullanıcı isteği 2026-09-06) — "her kaydın kendine ait bir log ekranı olmalı".
    // Kabuktaki "Seçili Kaydın Geçmişi" menüsü bu üç bilgiyi okur; log okuma/yetki tek yerdedir
    // (AuditLogService.ForEntity + btn-screen-log). Seçim yoksa null → kullanıcıya "kayıt seçin" denir.
    public string? LogEntityType => "vehicle_maintenance";
    public string? LogEntityId => SelectedMaint?.Id;
    public string? LogKayitAdi => SelectedMaint is null ? null : SelectedMaint.VehicleCode + " · " + SelectedMaint.DefinitionName;

    private readonly SessionContext _session;

    [ObservableProperty] private string? _status;
    /// <summary>Açılışta seçili sekme (menüden alt-bağlantıyla gelince ayarlanır). 0=Tanımlar,1=Araç Bakımları,2=Uyarılar.</summary>
    [ObservableProperty] private int _selectedTab;


    // ── MLY-01 (ADR-168): opsiyonel maliyet merkezi seçimi ───────────────────────────────────────
    /// <summary>Alan yalnız cost_centers Edit yetkisi olana görünür (bağ yazmak veri değiştirir).</summary>
    public bool CanPickCostCenter => AccessControl.Can(_session, "cost_centers", PermissionAction.Edit);
    public System.Collections.ObjectModel.ObservableCollection<ProjectPick> CostCenterOptions { get; } = new();
    [ObservableProperty] private ProjectPick? _mntCostCenter;
    /// <summary>⭐ MUH-01a: EKİPMAN bakımının maliyet merkezi — araç sekmesindekinden AYRI tutulur.
    /// Ortak alan kullanmak, araç için seçilen merkezin ekipman kaydına sessizce yapışması demekti;
    /// depo seçiminde (MntLocation) bu paylaşım bilinçli ve ekranda YAZILI, merkez için değil.</summary>
    [ObservableProperty] private ProjectPick? _eqmCostCenter;

    /// <summary>⭐ MUH-01b (2026-09-04): ARAÇ bakımının dış servis faturası / servis fişi numarası.</summary>
    [ObservableProperty] private string _mntInvoice = "";
    /// <summary>⭐ MUH-01b: EKİPMAN bakımının belge numarası — maliyet merkezinde olduğu gibi AYRI alan.</summary>
    [ObservableProperty] private string _eqmInvoice = "";

    // ── LST-01 (2026-09-04): BAKIM LİSTESİ SAYFALAMA ─────────────────────────────────────────────
    // Liste sabit 200 tavanıyla okunuyordu; 200. kaydın ötesindeki bakımlar SESSİZCE düşüyordu.
    // Desen ARA İŞ 6'daki yakıt ekranından alındı (yeni bir yol icat edilmedi).
    [ObservableProperty] private int _bakimSayfa = 1;
    [ObservableProperty] private int _bakimSayfaBoyutu = 50;
    [ObservableProperty] private int _bakimToplam;
    [ObservableProperty] private int _bakimToplamSayfa = 1;
    public IReadOnlyList<int> BakimSayfaBoyutlari { get; } = new[] { 25, 50, 100, 200 };
    public bool BakimOncekiVar => BakimSayfa > 1;
    public bool BakimSonrakiVar => BakimSayfa < BakimToplamSayfa;
    /// <summary>Kullanıcı kaç kaydın VAR olduğunu görür — sessiz kesilme bir daha olmaz.</summary>
    public string BakimDurumu => BakimToplam == 0
        ? "Bakım kaydı yok"
        : $"{BakimToplam} bakım — sayfa {BakimSayfa} / {BakimToplamSayfa}";
    private void BakimSayfalamaTazele()
    {
        OnPropertyChanged(nameof(BakimDurumu));
        OnPropertyChanged(nameof(BakimOncekiVar));
        OnPropertyChanged(nameof(BakimSonrakiVar));
    }
    [RelayCommand] private void OncekiBakimSayfasi() { if (BakimOncekiVar) { BakimSayfa--; LoadMaint(); } }
    [RelayCommand] private void SonrakiBakimSayfasi() { if (BakimSonrakiVar) { BakimSayfa++; LoadMaint(); } }
    partial void OnBakimSayfaBoyutuChanged(int value) { BakimSayfa = 1; LoadMaint(); }

    // ── MUH-01c (2026-09-04): DIŞ SERVİS SAĞLAYICISI (cari) ────────────────────────────────────
    // Bakım dışarıda yapıldıysa kime borçlanıldığı buraya yazılır. Kendi atölyesinde yapılan
    // bakımda boş kalır — alan OPSİYONELDİR ve mevcut akışı zorunlu hâle GETİRMEZ.
    /// <summary>Cari listesi yalnız parties View yetkisi olana yüklenir.</summary>
    public bool CanPickParty => AccessControl.Can(_session, "parties", PermissionAction.View);
    public ObservableCollection<ProjectPick> PartyOptions { get; } = new();
    [ObservableProperty] private ProjectPick? _mntParty;
    [ObservableProperty] private ProjectPick? _eqmParty;
    private void LoadPartyOptions()
    {
        if (!CanPickParty) return;
        try
        {
            PartyOptions.Clear();
            foreach (var p in DesktopServices.Parties.List(_session, pageSize: 500).Items)
                PartyOptions.Add(new ProjectPick(p.Party.Id, p.Party.Title));
        }
        catch { }
    }
    private void LoadCostCenterOptions()
    {
        try
        {
            CostCenterOptions.Clear();
            foreach (var (id, name) in DesktopServices.CostCenters.Options(_session))
                CostCenterOptions.Add(new ProjectPick(id, name));
        }
        catch { }
    }
    /// <summary>Kayıt SONRASI bağ — işlem zinciri değişmedi; bağ yazılamazsa kayıt "merkezsiz" kalır.</summary>
    private void BaglaMaliyetMerkezi(string entityType, string entityId, ProjectPick? merkez)
    {
        if (merkez is null) return;
        try { DesktopServices.CostCenters.Link(_session, entityType, entityId, merkez.Id); }
        catch (System.Exception ex) { Status = "Kayıt alındı; maliyet merkezi bağlanamadı: " + ex.Message; }
    }

    public MaintenanceViewModel(SessionContext session, int initialTab = 0)
    {
        _session = session;
        SelectedTab = initialTab;
        LoadCostCenterOptions();   // MLY-01
        LoadPartyOptions();        // MUH-01c
        LoadDefs();
        LoadMaint();
        LoadAlerts();
    }

    public bool CanWrite => AccessControl.Can(_session, "maintenance", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "maintenance", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "maintenance", PermissionAction.Delete);

    // ════════════════════ TAB 1 — BAKIM TANIMLARI ════════════════════
    public ObservableCollection<MaintenanceDefinitionRow> Defs { get; } = new();
    public ObservableCollection<MaintenanceDefinitionRow> SubDefs { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDefSelection))]
    private MaintenanceDefinitionRow? _selectedDef;
    public bool HasDefSelection => SelectedDef != null;

    [ObservableProperty] private string? _defsError;
    public bool HasDefsError => DefsError != null;
    public bool DefsEmpty => !HasDefsError && Defs.Count == 0;
    public bool HasDefs => Defs.Count > 0;

    // Yeni/düzenle tanım formu
    [ObservableProperty] private bool _showDefAdd;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefIsEditMode))]
    [NotifyPropertyChangedFor(nameof(DefFormTitle))]
    private string? _defEditId;
    /// <summary>B-1: düzenlemeye başlanan andaki <c>version</c>. Kaydederken servise geri verilir; kayıt bu
    /// arada başkası tarafından değiştiyse servis <c>ConcurrencyException</c> atar (sessiz üzerine yazma olmaz).
    /// 0 = sürüm bilinmiyor → kontrol yapılmaz.</summary>
    private long _defEditVersion;
    public bool DefIsEditMode => DefEditId != null;
    public string DefFormTitle => DefIsEditMode ? "BAKIM TANIMI DÜZENLE" : "YENİ BAKIM TANIMI";
    public string? AddDefButtonText => CanWrite ? "Yeni Tanım" : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefNameError))]
    [NotifyPropertyChangedFor(nameof(HasDefNameError))]
    private string _defName = "";
    [ObservableProperty] private string _defDescription = "";
    [ObservableProperty] private decimal _defIntervalValue;
    [ObservableProperty] private string _defUnitDisplay = "km";
    public ObservableCollection<string> UnitOptions { get; } = new() { "km", "saat", "gün" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DefNameError))]
    [NotifyPropertyChangedFor(nameof(HasDefNameError))]
    private bool _triedDefSave;
    public string? DefNameError => TriedDefSave && string.IsNullOrWhiteSpace(DefName) ? "Tanım adı zorunlu." : null;
    public bool HasDefNameError => DefNameError != null;

    // İlişkili araçlar (periyodik takip)
    public ObservableCollection<VehiclePick> VehiclePicks { get; } = new();
    public ObservableCollection<VehiclePick> FilteredVehicles { get; } = new();
    [ObservableProperty] private string _vehicleSearch = "";
    private bool _vehiclesLoaded;

    partial void OnVehicleSearchChanged(string value) => RebuildFilteredVehicles();

    // Alt bakım ekleme
    [ObservableProperty] private string _newSubDefName = "";

    private static string UnitCode(string display) => display switch { "saat" => "hour", "gün" => "day", _ => "km" };
    private static string UnitDisplay(string code) => code switch { "hour" => "saat", "day" => "gün", _ => "km" };

    [RelayCommand]
    private void LoadDefs()
    {
        try
        {
            DefsError = null;
            Defs.Clear();
            foreach (var d in DesktopServices.MaintenanceDefs.List(_session)) Defs.Add(d);
            Status = $"{Defs.Count} bakım tanımı";
        }
        catch (Exception ex) { DefsError = ex.Message; Status = "Hata: " + ex.Message; }
        SelectedDef = null;
        OnPropertyChanged(nameof(DefsEmpty));
        OnPropertyChanged(nameof(HasDefs));
        OnPropertyChanged(nameof(HasDefsError));
    }

    partial void OnSelectedDefChanged(MaintenanceDefinitionRow? value)
    {
        SubDefs.Clear();
        if (value is null) return;
        try { foreach (var sub in DesktopServices.MaintenanceDefs.List(_session, value.Id)) SubDefs.Add(sub); }
        catch { }
    }

    [RelayCommand]
    private void ToggleDefAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowDefAdd = !ShowDefAdd;
        if (ShowDefAdd) LoadVehiclePicks();
    }

    [RelayCommand]
    private void ClearDef()
    {
        DefName = ""; DefDescription = ""; DefIntervalValue = 0; DefUnitDisplay = "km";
        foreach (var p in VehiclePicks) p.IsSelected = false;
        VehicleSearch = ""; DefEditId = null; _defEditVersion = 0; TriedDefSave = false; ShowDefAdd = false;
    }

    [RelayCommand]
    private async Task AddDef()
    {
        TriedDefSave = true;
        bool editing = DefIsEditMode;
        if (editing ? !CanEdit : !CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(DefName)) { Status = "Tanım adı zorunlu."; return; }
        if (!editing && !await ConfirmService.AskAsync("Yeni bakım tanımı kaydedilsin mi?", "Kaydet")) return;
        if (editing && SelectedDef is { } od)
        {
            var sum = new ChangeSummary();
            sum.Add("Tanım Adı", od.Name, DefName.Trim());
            sum.Add("Periyot", od.IntervalValue, DefIntervalValue);
            sum.Add("Birim", UnitDisplay(od.IntervalUnit), DefUnitDisplay);
            sum.Add("Açıklama", od.Description, string.IsNullOrWhiteSpace(DefDescription) ? null : DefDescription.Trim());
            if (!await ConfirmService.AskAsync(sum.Build("Bakım tanımı güncellensin mi?"), "Kaydet")) return;
        }

        var dto = new NewMaintenanceDefinition(
            Name: DefName.Trim(), IntervalValue: DefIntervalValue, IntervalUnit: UnitCode(DefUnitDisplay),
            Description: string.IsNullOrWhiteSpace(DefDescription) ? null : DefDescription.Trim());
        var vehIds = VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        try
        {
            if (editing)
            {
                DesktopServices.MaintenanceDefs.Update(_session, DefEditId!, dto,
                    _defEditVersion > 0 ? _defEditVersion : null); // B-1: düzenleme kilidi
                DesktopServices.MaintenanceDefs.SetVehicles(_session, DefEditId!, vehIds);
                Status = "Bakım tanımı güncellendi.";
            }
            else
            {
                DesktopServices.MaintenanceDefs.Create(_session, dto, vehIds);
                Status = "Bakım tanımı eklendi.";
            }
            ClearDef(); LoadDefs();
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task BeginEditDef()
    {
        if (SelectedDef is null) return;
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Bu bakım tanımını düzenlemek istiyor musunuz?", "Düzenle")) return;
        LoadVehiclePicks();
        var d = SelectedDef;
        DefEditId = d.Id; _defEditVersion = d.Version; // B-1: kilit jetonunu forma al
        DefName = d.Name; DefDescription = d.Description ?? "";
        DefIntervalValue = d.IntervalValue; DefUnitDisplay = UnitDisplay(d.IntervalUnit);
        var ids = DesktopServices.MaintenanceDefs.GetVehicleIds(_session, d.Id).ToHashSet();
        foreach (var p in VehiclePicks) p.IsSelected = ids.Contains(p.Id);
        RebuildFilteredVehicles();
        TriedDefSave = false; ShowDefAdd = true;
    }

    [RelayCommand]
    private async Task RequestDeleteDef()
    {
        if (SelectedDef is null) return;
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{SelectedDef.Name}' bakım tanımı silinsin mi?", "Tanım Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.MaintenanceDefs.Delete(_session, SelectedDef.Id); LoadDefs(); Status = "Tanım silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    // Alt bakım tanımı (parent = SelectedDef)
    [RelayCommand]
    private void AddSubDef()
    {
        if (SelectedDef is null) { Status = "Önce ana tanım seçin."; return; }
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewSubDefName)) return;
        try
        {
            DesktopServices.MaintenanceDefs.Create(_session, new NewMaintenanceDefinition(
                Name: NewSubDefName.Trim(), IntervalValue: 0, IntervalUnit: UnitCode(DefUnitDisplay),
                ParentDefId: SelectedDef.Id));
            NewSubDefName = "";
            OnSelectedDefChanged(SelectedDef); // alt listeyi yenile
            Status = "Alt bakım eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteSubDef(MaintenanceDefinitionRow? sub)
    {
        if (sub is null || !CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{sub.Name}' alt bakımı silinsin mi?", "Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.MaintenanceDefs.Delete(_session, sub.Id); OnSelectedDefChanged(SelectedDef); Status = "Alt bakım silindi."; }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    private void LoadVehiclePicks()
    {
        if (_vehiclesLoaded) { RebuildFilteredVehicles(); return; }
        VehiclePicks.Clear();
        try { foreach (var v in DesktopServices.Vehicles.List(_session)) VehiclePicks.Add(new VehiclePick(v.Id, v.InternalCode, v.Plate ?? "")); }
        catch { }
        _vehiclesLoaded = true;
        RebuildFilteredVehicles();
    }

    private void RebuildFilteredVehicles()
    {
        FilteredVehicles.Clear();
        var t = VehicleSearch?.Trim();
        foreach (var p in VehiclePicks)
            if (string.IsNullOrEmpty(t) || p.Code.Contains(t, StringComparison.OrdinalIgnoreCase) || p.Plate.Contains(t, StringComparison.OrdinalIgnoreCase))
                FilteredVehicles.Add(p);
    }

    [RelayCommand] private void SelectAllVehicles() { foreach (var p in FilteredVehicles) p.IsSelected = true; }
    [RelayCommand] private void ClearVehicles() { foreach (var p in FilteredVehicles) p.IsSelected = false; }

    // ════════════════════ TAB 2 — ARAÇ BAKIMLARI ════════════════════
    public ObservableCollection<MaintenanceRow> Maintenances { get; } = new();
    public ObservableCollection<MaintenanceMaterialRow> MaintMaterials { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintSelection))]
    [NotifyPropertyChangedFor(nameof(CanCancelSelected))]
    private MaintenanceRow? _selectedMaint;
    public bool HasMaintSelection => SelectedMaint != null;
    public bool CanCancelSelected => SelectedMaint is { IsCancelled: false } && CanEdit;

    // ── İş #5: bakım kaydının YAN ETKİSİZ alanları (açıklama/alt not/teknisyen) ────────────────
    // Malzeme ve sayaç alanları BİLİNÇLİ olarak düzenlenmez; onlar için "İptal Et + yeniden gir".
    [ObservableProperty] private bool _showMetaEdit;
    [ObservableProperty] private string _metaDescription = "";
    [ObservableProperty] private string _metaSubNote = "";
    [ObservableProperty] private LookupItem? _metaTechnician;
    [ObservableProperty] private string? _metaError;
    private long _metaVersion;   // düzenleme kilidi: formu açtığımız andaki sürüm

    /// <summary>Seçili bakımın metadata düzenleme formunu açar (mevcut değerlerle, servis katmanından).</summary>
    [RelayCommand]
    private void BeginMetaEdit()
    {
        if (SelectedMaint is null || !CanCancelSelected) return;
        LoadMntPickers();
        MetaDescription = SelectedMaint.Description ?? "";
        MetaSubNote = SelectedMaint.SubDefinitionNote ?? "";
        MetaTechnician = Technicians.FirstOrDefault(t => t.Id == SelectedMaint.TechnicianId);
        _metaVersion = SelectedMaint.Version;   // düzenleme kilidi: formu açtığımız andaki sürüm
        MetaError = null;
        ShowMetaEdit = true;
    }

    [RelayCommand]
    private void CancelMetaEdit() { ShowMetaEdit = false; MetaError = null; }

    /// <summary>Kaydet — servis katmanı yetki, firma izolasyonu ve düzenleme kilidini uygular.</summary>
    [RelayCommand]
    private async Task SaveMetaEdit()
    {
        if (SelectedMaint is null) return;
        if (!await ConfirmService.AskAsync("Bakım kaydının açıklama/teknisyen bilgileri güncellensin mi?", "Kaydet")) return;
        try
        {
            DesktopServices.Maintenance.UpdateMetadata(_session, SelectedMaint.Id,
                MetaDescription, MetaSubNote, MetaTechnician?.Id, _metaVersion);
            ShowMetaEdit = false; MetaError = null;
            Status = "Bakım kaydı güncellendi.";
            LoadMaint();
        }
        catch (Exception ex) { MetaError = ex.Message; }
    }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintError))]
    [NotifyPropertyChangedFor(nameof(MaintEmpty))]
    private string? _maintError;
    public bool HasMaintError => MaintError != null;
    public bool MaintEmpty => !HasMaintError && Maintenances.Count == 0;
    public bool HasMaint => Maintenances.Count > 0;

    // Yeni kayıt formu
    [ObservableProperty] private bool _showMntAdd;
    public ObservableCollection<VehicleListRow> MntVehicles { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> MntVehiclePopulator => SearchPopulator.For(() => MntVehicles, v => v.Display);
    public ObservableCollection<MaintenanceDefinitionRow> MntDefs { get; } = new();
    public ObservableCollection<MaintenanceDefinitionRow> MntSubDefs { get; } = new();
    public ObservableCollection<LookupItem> Technicians { get; } = new();
    public Func<string, CancellationToken, Task<IEnumerable<object>>> TechnicianPopulator => SearchPopulator.For(() => Technicians, t => t.Name);
    public ObservableCollection<MntMaterialLine> MntLines { get; } = new();
    public ObservableCollection<MaterialRefRow> MntMaterialResults { get; } = new();
    private bool _mntPickersLoaded;

    // ── BKM-04 / KARAR-9: MALZEMENİN ÇEKİLDİĞİ DEPO ──────────────────────────────────────────────
    /// <summary>Seçilebilir depolar — YEREL veritabanından (çevrimdışı çalışır, API çağrısı YOK).
    /// "Atanmamış" bilinçli olarak listede YOKTUR: yeni stok yazma hedefi olamaz.</summary>
    public ObservableCollection<BranchRow> MntLocations { get; } = new();
    /// <summary>Kullanıcının seçtiği depo. Varsayılan oturum şubesidir ama kullanıcı değiştirebilir;
    /// değiştirdiği değer olduğu gibi servise gider (sessizce oturum şubesine ÇEVRİLMEZ).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MntLocationText))]
    private BranchRow? _mntLocation;
    private BranchRow? _mntLocationDefault;
    public bool MntHasNoLocation => MntLocations.Count == 0;
    /// <summary>Onay penceresinde ve ekranda gösterilen açık metin — kullanıcı stoğun nereden
    /// düşeceğini tahmin etmek zorunda kalmasın.</summary>
    public string MntLocationText => MntLocation?.Name ?? "Atanmamış (depo seçilmedi)";

    [ObservableProperty] private VehicleListRow? _mntVehicle;
    [ObservableProperty] private MaintenanceDefinitionRow? _mntDef;
    [ObservableProperty] private MaintenanceDefinitionRow? _mntSubDef;
    [ObservableProperty] private LookupItem? _mntTechnician;
    [ObservableProperty] private decimal _mntKm;
    [ObservableProperty] private decimal _mntHour;
    [ObservableProperty] private DateTimeOffset? _mntDate;
    [ObservableProperty] private string _mntDescription = "";

    // ── Araç durumu (kullanıcı isteği 2026-07-16): bakım kaydı açarken aracı "Arızalı" vb. işaretle.
    //    BOŞ bırakılırsa aracın durumuna DOKUNULMAZ.
    public ObservableCollection<StatusPick> VehicleStatusOptions { get; } =
        new(DepoWise.Application.Ui.VehicleStatus.All.Select(x => new StatusPick(x.Code, x.Label)));
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVehStatusNote))]
    private StatusPick? _mntVehStatus;
    [ObservableProperty] private string _mntVehStatusNote = "";
    /// <summary>Durum açıklaması yalnız Bakımda / Arızalı seçilince anlamlıdır.</summary>
    public bool ShowVehStatusNote => MntVehStatus is not null
        && DepoWise.Application.Ui.VehicleStatus.NeedsNote(MntVehStatus.Code);
    [ObservableProperty] private string _mntMaterialSearch = "";
    [ObservableProperty] private string _cancelReason = "";
    [ObservableProperty] private bool _isAddingMntSub;
    [ObservableProperty] private string _newMntSubName = "";

    // Teknisyen yanına "+" Personel ekleme (madde 5.2, kullanıcı isteği 2026-08-06): eklenen kişi otomatik
    // "Saha Personeli" işaretlenir (Personeller modülündeki IsFieldStaff — aynı alan yeniden kullanılıyor).
    public bool CanAddTechnician => AccessControl.Can(_session, "personnel", PermissionAction.Create);
    [ObservableProperty] private bool _isAddingTechnician;
    [ObservableProperty] private string _newTechnicianName = "";

    [RelayCommand] private void StartAddTechnician() { IsAddingTechnician = true; NewTechnicianName = ""; }
    [RelayCommand] private void CancelAddTechnician() { IsAddingTechnician = false; NewTechnicianName = ""; }
    [RelayCommand]
    private void ConfirmAddTechnician()
    {
        if (string.IsNullOrWhiteSpace(NewTechnicianName)) return;
        try
        {
            var name = NewTechnicianName.Trim();
            var id = DesktopServices.Personnel.Create(_session, new NewPersonnel(name, null, null, null, true, IsFieldStaff: true));
            var item = new LookupItem(id, name);
            Technicians.Add(item); MntTechnician = item;
            IsAddingTechnician = false; NewTechnicianName = "";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    [RelayCommand] private void StartAddMntSub() { if (MntDef is null) { Status = "Önce bakım tanımı seçin."; return; } IsAddingMntSub = true; NewMntSubName = ""; }
    [RelayCommand] private void CancelAddMntSub() { IsAddingMntSub = false; NewMntSubName = ""; }
    [RelayCommand]
    private void ConfirmAddMntSub()
    {
        if (MntDef is null || string.IsNullOrWhiteSpace(NewMntSubName)) return;
        try
        {
            var id = DesktopServices.MaintenanceDefs.Create(_session, new NewMaintenanceDefinition(
                NewMntSubName.Trim(), 0m, "km", ParentDefId: MntDef.Id));
            var row = new MaintenanceDefinitionRow(id, NewMntSubName.Trim(), 0m, "km", null, MntDef.Id);
            MntSubDefs.Add(row); MntSubDef = row;
            IsAddingMntSub = false; NewMntSubName = "";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    public string? AddMntButtonText => CanWrite ? "Yeni Kayıt" : null;

    partial void OnMntDefChanged(MaintenanceDefinitionRow? value)
    {
        MntSubDef = null; MntSubDefs.Clear();
        if (value is null) return;
        try { foreach (var sub in DesktopServices.MaintenanceDefs.List(_session, value.Id)) MntSubDefs.Add(sub); }
        catch { }
    }

    partial void OnMntVehicleChanged(VehicleListRow? value)
    {
        if (value is null) return;
        if (MntKm < value.CurrentMeter && value.MeterUnit != "hour") MntKm = value.CurrentMeter;
        if (MntHour < value.CurrentMeter && value.MeterUnit == "hour") MntHour = value.CurrentMeter;
    }

    partial void OnMntMaterialSearchChanged(string value) => RefreshMntMaterials();

    private void RefreshMntMaterials()
    {
        MntMaterialResults.Clear();
        var term = MntMaterialSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 50 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items)
            {
                if (MntLines.Any(l => l.MaterialId == m.Id)) continue;
                MntMaterialResults.Add(new MaterialRefRow(m.Id, m.Code, m.Name));
            }
        }
        catch { }
    }

    [RelayCommand]
    private void LoadMaint()
    {
        try
        {
            MaintError = null;
            Maintenances.Clear();
            // ⭐ LST-01: sabit 200 tavanı KALDIRILDI (ötesi sessizce düşüyordu).
            var grid = DesktopServices.Maintenance.SearchMaintenancesGrid(_session, BakimSayfa, BakimSayfaBoyutu,
                freeText: BakimAramaAktif, fromMs: BakimBasMs, toMs: BakimBitMs);
            foreach (var r in grid.Items) Maintenances.Add(r);
            BakimToplam = grid.TotalCount; BakimToplamSayfa = grid.TotalPages;
            BakimSayfalamaTazele();
        }
        catch (Exception ex) { MaintError = ex.Message; }
        SelectedMaint = null; MaintMaterials.Clear();
        OnPropertyChanged(nameof(MaintEmpty));
        OnPropertyChanged(nameof(HasMaint));
        OnPropertyChanged(nameof(HasMaintError));
    }

    // ═══ FAZ 4.8 (kullanıcı isteği 2026-09-06) — BAKIM LİSTESİ SORGULAMA ════════════════════════
    // Bulunan eksik: servis TARİH ARALIĞI ve SERBEST METİN (araç kodu · plaka · bakım adı · açıklama ·
    // belge no) süzmesini zaten destekliyordu, ama ekranda ne alan ne buton vardı — kullanıcı
    // sorgulayamıyordu. Yeni sorgu altyapısı KURULMADI; mevcut SearchMaintenancesGrid'e bağlanıldı.

    /// <summary>Arama kutusundaki metin (Filtrele'ye basılana kadar sorguya girmez).</summary>
    [ObservableProperty] private string _bakimArama = "";

    /// <summary>Filtrele ile onaylanmış arama metni — ağır sorgu her tuşta çalışmaz (CLAUDE.md §5).</summary>
    private string? _bakimAramaAktif;
    public string? BakimAramaAktif => _bakimAramaAktif;

    [ObservableProperty] private System.DateTimeOffset? _bakimBaslangic;
    [ObservableProperty] private System.DateTimeOffset? _bakimBitis;

    private long? _bakimBasMs, _bakimBitMs;
    public long? BakimBasMs => _bakimBasMs;
    public long? BakimBitMs => _bakimBitMs;

    /// <summary>Aktif bir süzgeç var mı (arayüzde "Temizle" bunu gösterir).</summary>
    public bool BakimSuzgecVar => _bakimAramaAktif is not null || _bakimBasMs is not null || _bakimBitMs is not null;

    /// <summary>Filtrele — girilen ölçütleri uygular ve ilk sayfaya döner.</summary>
    [RelayCommand]
    private void BakimFiltrele()
    {
        var q = (BakimArama ?? "").Trim();
        _bakimAramaAktif = q.Length == 0 ? null : q;
        // Tarih aralığı GÜN sınırlarına çekilir: bitiş günü dahil olmalı (kullanıcı 1–5 diyorsa 5 dahildir).
        _bakimBasMs = BakimBaslangic is { } b ? new System.DateTimeOffset(b.Date, System.TimeSpan.Zero).ToUnixTimeMilliseconds() : null;
        _bakimBitMs = BakimBitis is { } t ? new System.DateTimeOffset(t.Date, System.TimeSpan.Zero).ToUnixTimeMilliseconds() + 86_399_999 : null;
        BakimSayfa = 1;
        OnPropertyChanged(nameof(BakimSuzgecVar));
        LoadMaint();
    }

    /// <summary>Süzgeçleri temizler ve tüm listeye döner.</summary>
    [RelayCommand]
    private void BakimTemizle()
    {
        BakimArama = ""; BakimBaslangic = null; BakimBitis = null;
        _bakimAramaAktif = null; _bakimBasMs = null; _bakimBitMs = null;
        BakimSayfa = 1;
        OnPropertyChanged(nameof(BakimSuzgecVar));
        LoadMaint();
    }

    partial void OnSelectedMaintChanged(MaintenanceRow? value)
    {
        MaintMaterials.Clear(); CancelReason = "";
        if (value is null) return;
        try { foreach (var mm in DesktopServices.Maintenance.GetMaintenanceMaterials(_session, value.Id)) MaintMaterials.Add(mm); }
        catch { }
    }

    [RelayCommand]
    private void ToggleMntAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowMntAdd = !ShowMntAdd;
        if (ShowMntAdd) LoadMntPickers();
    }

    /// <summary>Köprü: Araç Bakımları sekmesinde uyarıya sebep olan aracın EN SON bakım kaydını seçer (salt detay).</summary>
    public void OpenEntity(string vehicleId)
    {
        SelectedTab = 1;                       // Araç Bakımları
        if (Maintenances.Count == 0) LoadMaint();
        SelectedMaint = Maintenances.FirstOrDefault(m => m.VehicleId == vehicleId); // en yeni (DESC) → detay paneli açılır
    }

    // ── Uyarı → bakım kaydı köprüsü (kullanıcı isteği 2026-09-02) ──────────────────────────────
    // Kullanıcı uyarı listesinde "hangi araç, hangi bakım" sorusunu cevaplayamıyordu. Satıra
    // tıklayınca Araç Bakımları sekmesine geçilir ve ilgili kayıt İNCELEME (detay) panelinde açılır.

    /// <summary>Uyarı listesinde tıklanan satır — köprüyü tetikler, sonra tekrar tıklanabilsin diye boşalır.</summary>
    [ObservableProperty] private MaintenanceAlertRow? _selectedAlert;

    partial void OnSelectedAlertChanged(MaintenanceAlertRow? value)
    {
        if (value is null) return;
        OpenAlert(value);
        SelectedAlert = null;   // aynı satıra ikinci kez tıklanabilsin (seçim takılı kalmasın)
    }

    /// <summary>
    /// Uyarıdan bakım kaydına köprü — <b>KİMLİKLE</b> eşleşir.
    ///
    /// ⚠️ 2026-09-02 DÜZELTMESİ (kullanıcı bildirimi: "10.000 bakıma tıklıyorum, 100.000'lik başka bir
    /// bakıma yönlendiriyor"). Önceki sürüm (araç + bakım ADI) ile arıyor, bulamazsa <b>yalnız araca</b>
    /// düşüp o aracın EN YENİ bakımını açıyordu. "Hiç yapılmamış" uyarılarda eşleşme zaten YOKTUR →
    /// her seferinde alakasız bir kayıt açılıyordu (canlıda 75 "hiç yapılmamış" uyarının 23'ü, başka
    /// bakım kaydı olan araçlarda — yani bu yol gerçekten tetikleniyordu).
    ///
    /// Artık uyarı, dayandığı bakım kaydının KİMLİĞİNİ taşır (<see cref="MaintenanceAlertRow.MaintenanceId"/>):
    /// kimlik yoksa <b>hiçbir kayıt açılmaz</b>, kullanıcıya "ilk bakım bekliyor" denir. Kimlik varsa
    /// kayıt listede yoksa (liste sınırı) o aracın kayıtları getirilip yeniden aranır — sessiz yanlış
    /// eşleşme mümkün değildir.
    /// </summary>
    public void OpenAlert(MaintenanceAlertRow row)
    {
        SelectedTab = 1;                       // Araç Bakımları
        if (Maintenances.Count == 0) LoadMaint();

        if (string.IsNullOrEmpty(row.MaintenanceId))
        {
            // İlk bakım bekleyen uyarı: açılacak kayıt YOK. Yanlış kayıt açmak yerine listeyi o araca
            // daraltıp durumu söylüyoruz — kullanıcı aracın mevcut bakımlarını yine de görebilir.
            AracaDaralt(row.VehicleId);
            SelectedMaint = null;
            Status = $"{row.VehicleCode} · \"{row.Definition}\" için henüz bakım kaydı yok (ilk bakım bekliyor). " +
                     "Liste bu aracın kayıtlarına daraltıldı.";
            return;
        }

        var hit = Maintenances.FirstOrDefault(m => m.Id == row.MaintenanceId);
        if (hit is null)
        {
            AracaDaralt(row.VehicleId);        // liste sınırı dışında kalmış olabilir
            hit = Maintenances.FirstOrDefault(m => m.Id == row.MaintenanceId);
        }

        SelectedMaint = hit;
        Status = hit is null
            ? $"{row.VehicleCode} · \"{row.Definition}\" kaydı bulunamadı (silinmiş veya yetki dışı olabilir)."
            : $"{row.VehicleCode} · \"{row.Definition}\" bakım kaydı açıldı.";
    }

    /// <summary>Bakım listesini TEK araca daraltır. "Yenile" (LoadMaint) tam listeye geri döner.</summary>
    private void AracaDaralt(string vehicleId)
    {
        try
        {
            Maintenances.Clear();
            // ⭐ LST-01: araç süzmeli liste de sayfalanır (aynı servis metodu).
            var grid = DesktopServices.Maintenance.SearchMaintenancesGrid(_session, BakimSayfa, BakimSayfaBoyutu, vehicleId);
            foreach (var r in grid.Items) Maintenances.Add(r);
            BakimToplam = grid.TotalCount; BakimToplamSayfa = grid.TotalPages;
            BakimSayfalamaTazele();
            OnPropertyChanged(nameof(MaintEmpty));
            OnPropertyChanged(nameof(HasMaint));
        }
        catch (Exception ex) { MaintError = ex.Message; }
    }

    private void LoadMntPickers()
    {
        if (!_mntPickersLoaded)
        {
            try { foreach (var v in DesktopServices.Vehicles.List(_session)) MntVehicles.Add(v); } catch { }
            try { foreach (var d in DesktopServices.MaintenanceDefs.List(_session)) MntDefs.Add(d); } catch { }
            try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Technicians.Add(p); } catch { }
            // BKM-04: depo listesi + varsayılan (oturum şubesi). Yerelden okunur → çevrimdışı çalışır.
            var (locs, def) = StockLocationPicker.Load(_session);
            foreach (var b in locs) MntLocations.Add(b);
            _mntLocationDefault = def;
            MntLocation ??= def;
            OnPropertyChanged(nameof(MntHasNoLocation));
            _mntPickersLoaded = true;
        }
        RefreshMntMaterials();
    }

    [RelayCommand]
    private void ClearMnt()
    {
        MntVehicle = null; MntDef = null; MntSubDef = null; MntTechnician = null;
        MntKm = 0; MntHour = 0; MntDate = null; MntDescription = ""; MntMaterialSearch = "";
        MntInvoice = ""; MntParty = null;   // ⭐ MUH-01b/c: belge no ve cari sonraki kayda taşınmasın
        MntVehStatus = null; MntVehStatusNote = "";
        IsAddingMntSub = false; NewMntSubName = "";
        MntLines.Clear(); MntMaterialResults.Clear(); ShowMntAdd = false;
        MntLocation = _mntLocationDefault;   // BKM-04: YENİ kayıt için varsayılana dön (kaydedilen seçim taşınmaz)
    }

    [RelayCommand]
    private void AddMntMaterial(MaterialRefRow? m)
    {
        if (m is null) return;
        if (!MntLines.Any(l => l.MaterialId == m.Id)) MntLines.Add(new MntMaterialLine(m.Id, m.Code, m.Name));
        RefreshMntMaterials();
    }

    [RelayCommand]
    private void RemoveMntLine(MntMaterialLine? l)
    {
        if (l is not null) MntLines.Remove(l);
        RefreshMntMaterials();
    }

    [RelayCommand]
    private async Task SaveMnt()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (!await BranchGuard.RequireBranchAsync(_session, "Bakım Takibi")) return;   // "Tüm Şubeler" modunda işlem yok
        if (MntVehicle is null) { Status = "Araç seçin."; return; }
        if (MntDef is null) { Status = "Bakım tanımı seçin."; return; }
        if (MntLines.Any(l => l.Quantity <= 0)) { Status = "Malzeme miktarı pozitif olmalı."; return; }

        // madde 5.3: yetersiz stok ENGELLENMEZ. Eksik varsa uyarı + opsiyonel "Taslak Talep Oluştur";
        // her iki durumda da bakım kaydı DEVAM eder (iş akışı kesilmez).
        // BKM-04: uyarı SEÇİLEN DEPONUN stoğuna bakar — firma geneline değil. Aksi hâlde "15 var" deyip
        // o depodan eksiye düşerdik (STK-04/05'te düzeltilen aynı hata sınıfı).
        var checkLocation = MntLocation?.Id ?? StockBalanceWriter.Unassigned;
        var shortfalls = new List<(string MaterialId, string Label, decimal Shortfall)>();
        foreach (var l in MntLines)
        {
            if (l.FromTeamStock) continue;   // ekip stoğu merkez depodan düşmez → eksik uyarısı anlamsız
            decimal bal;
            try { bal = DesktopServices.Stock.GetBalanceAt(_session, l.MaterialId, checkLocation); } catch { bal = 0m; }
            if (l.Quantity > bal) shortfalls.Add((l.MaterialId, $"{l.Code} — {l.Name}", l.Quantity - bal));
        }

        if (shortfalls.Count > 0)
        {
            var canRequest = AccessControl.Can(_session, "requests", PermissionAction.Create);
            var detail = string.Join("\n", shortfalls.Select(x => $"• {x.Label}: eksik {x.Shortfall:0.##}"));
            var baseMsg = "İlgili malzeme için yeterli stok bulunmamaktadır. İşleme devam edebilirsiniz." +
                (canRequest ? " İsterseniz bu işlem için otomatik bir malzeme talebi oluşturabilirsiniz." : "") +
                "\n\n" + detail;
            if (canRequest)
            {
                // İki yol da bakım kaydını SÜRDÜRÜR (madde 5.3 — engellenmez); tek fark taslak talep oluşturulsun mu.
                var makeDraft = await ConfirmService.AskAsync(baseMsg, "Yetersiz Stok",
                    okText: "Taslak Talep Oluştur ve Devam Et", cancelText: "Talepsiz Devam Et");
                if (makeDraft)
                {
                    try
                    {
                        DesktopServices.Requests.Create(_session, new NewRequest(
                            shortfalls.Select(x => new RequestItemInput(x.MaterialId, x.Shortfall)).ToList()));
                        Status = "Talep taslak olarak oluşturuldu. Talepler ekranından düzenleyerek gönderebilirsiniz.";
                    }
                    catch (Exception rex) { Status = "Taslak talep oluşturulamadı: " + rex.Message + " (bakım kaydı yine de sürüyor)"; }
                }
            }
            else if (!await ConfirmService.AskAsync(baseMsg, "Yetersiz Stok", okText: "Devam Et", cancelText: "Vazgeç", danger: true))
                return;   // talep yetkisi olmayan kullanıcıya bilgilendirme + geri çıkış imkânı
        }
        else if (!await ConfirmService.AskAsync(
                     MntLines.Count == 0 ? "Bakım kaydı eklensin mi?"
                     : $"Bakım kaydı eklensin mi?\n\nMalzemeler şu depodan düşülecek: {MntLocationText}",
                     "Kaydet")) return;
        try
        {
            var materials = MntLines.Select(l => new MaintenanceMaterialLine(l.MaterialId, l.Quantity, l.FromTeamStock)).ToList();
            var mntId = DesktopServices.Maintenance.Save(_session, new NewMaintenance(
                VehicleId: MntVehicle.Id, DefinitionId: MntDef.Id, SubDefinitionId: MntSubDef?.Id,
                TechnicianId: MntTechnician?.Id,
                Description: string.IsNullOrWhiteSpace(MntDescription) ? null : MntDescription.Trim(),
                PerformedKm: MntKm > 0 ? MntKm : (decimal?)null,
                PerformedHour: MntHour > 0 ? MntHour : (decimal?)null,
                PerformedDate: IsGunuTarihi.Ms(MntDate),   // ADR-184: takvim tarihi → UTC gün başı
                Materials: materials,
                // BKM-04: KULLANICININ SEÇTİĞİ depo — olduğu gibi gider. Depo yoksa null → ATANMAMIŞ
                // (bakım stok yüzünden engellenmez, KARAR-9 md. 8).
                StockLocationId: MntLocation?.Id,
                // ⭐ MUH-01b: dış servis faturası / servis fişi no (opsiyonel)
                InvoiceNo: MntInvoice,
                // ⭐ MUH-01c: dış servis sağlayıcısı (cari) — opsiyonel
                PartyId: MntParty?.Id), Guid.NewGuid().ToString("N"));
            BaglaMaliyetMerkezi("vehicle_maintenance", mntId, MntCostCenter);   // MLY-01

            // Araç durumu seçildiyse aracı da güncelle. Bakım kaydı BAŞARILI oldu; durum güncellenemezse
            // bakım GERİ ALINMAZ (ayrı işlem) — kullanıcıya açıkça söylenir.
            var vehStatus = MntVehStatus;
            if (vehStatus is not null)
            {
                try
                {
                    DesktopServices.Vehicles.SetStatus(_session, MntVehicle.Id, vehStatus.Code,
                        string.IsNullOrWhiteSpace(MntVehStatusNote) ? null : MntVehStatusNote.Trim());
                }
                catch (Exception vex)
                {
                    ClearMnt(); LoadMaint(); LoadAlerts();
                    Status = $"Bakım kaydedildi ANCAK araç durumu güncellenemedi: {vex.Message} (Araçlar ekranından elle değiştirin.)";
                    return;
                }
            }
            ClearMnt(); LoadMaint(); LoadAlerts();
            Status = vehStatus is null
                ? "Bakım kaydı eklendi."
                : $"Bakım kaydı eklendi; araç durumu '{vehStatus.Label}' olarak güncellendi.";
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task CancelMaint()
    {
        if (SelectedMaint is null || !CanEdit) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(CancelReason)) { Status = "İptal gerekçesi zorunlu."; return; }
        if (!await ConfirmService.AskAsync("Bu bakım kaydı iptal edilsin mi? (malzeme stoğu geri eklenir)", "Bakım İptal", "Evet, İptal Et", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Maintenance.Cancel(_session, SelectedMaint.Id, CancelReason.Trim());
            LoadMaint(); LoadAlerts();
            Status = "Bakım kaydı iptal edildi.";
        }
        catch (Exception ex) { Status = "İptal edilemedi: " + ex.Message; }
    }

    // ════════════════════ TAB 3 — UYARILAR ════════════════════
    public ObservableCollection<MaintenanceAlertRow> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [RelayCommand]
    private void LoadAlerts()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var codes = new Dictionary<string, string>();
            var plates = new Dictionary<string, string?>();
            try
            {
                foreach (var v in DesktopServices.Vehicles.List(_session))
                {
                    codes[v.Id] = v.InternalCode;
                    plates[v.Id] = v.Plate;   // kullanıcı isteği: uyarı listesinde plaka da görünsün
                }
            }
            catch { }
            foreach (var a in DesktopServices.Maintenance.GetAlerts(_session)
                         .OrderByDescending(x => (int)x.Level).ThenByDescending(x => x.Progress))
            {
                var code = codes.TryGetValue(a.VehicleId, out var c) ? c : a.VehicleId;
                plates.TryGetValue(a.VehicleId, out var plate);
                Items.Add(new MaintenanceAlertRow(code, a.DefinitionName, a.Level, a.Progress, a.Consumed,
                    a.Interval, a.VehicleId, plate, a.MaintenanceId));
            }
        }
        catch (Exception ex) { LoadError = ex.Message; }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    // ═══════════ 7b — EKİPMAN BAKIMLARI SEKMESİ (PK-F9, ADR-191) ═══════════════════════════════
    //
    // ⚠️ Yukarıdaki ARAÇ bakım akışı HİÇ DEĞİŞTİRİLMEDİ. Bu bölüm onun EKİPMAN ikizidir ve
    // ayrı servis/tablolar üzerinden çalışır (EquipmentMaintenanceService).
    //
    // Hedef seçimi ekranın MEVCUT sekme yapısıyla yapılır (yeni AppScreen açılmadı):
    //   "Bakım Tanımları" · "Araç Bakımları" · "Ekipman Bakımları" · "Uyarılar"
    //
    // ⚠️ Bilinçli KAPSAM: ekipmanda SAYAÇ YOKTUR (PK-F8) → araç tarafındaki sayaç ilerletme ve
    // araç DURUM değişikliği burada YOKTUR. Yetersiz stok uyarısı/taslak talep akışı da v1'de
    // araç sekmesine özgü kalır; ekipman kaydı stok kuralları açısından servis tarafında araçla
    // AYNI davranır (negatif stok engellenmez, ekip stoğu merkezden düşmez).

    public ObservableCollection<EquipmentPick> EqmEquipment { get; } = new();
    public ObservableCollection<EquipmentMaintenanceRow> EqmRows { get; } = new();
    public ObservableCollection<MntMaterialLine> EqmLines { get; } = new();

    [ObservableProperty] private EquipmentPick? _eqmSelected;
    [ObservableProperty] private MaintenanceDefinitionRow? _eqmDef;
    [ObservableProperty] private string _eqmDescription = "";
    [ObservableProperty] private long? _eqmPerformedDate;
    [ObservableProperty] private EquipmentMaintenanceRow? _eqmSelectedRow;
    [ObservableProperty] private string _eqmCancelReason = "";

    /// <summary>Ekipman sekmesi verisini yükler — ekipman listesi + mevcut bakım kayıtları.</summary>
    [RelayCommand]
    public void LoadEquipmentTab()
    {
        try
        {
            EqmEquipment.Clear();
            foreach (var e in DesktopServices.Equipment.List(_session))
                EqmEquipment.Add(new EquipmentPick(e.Id, string.IsNullOrWhiteSpace(e.Name) ? e.Code : $"{e.Code} — {e.Name}"));
            RefreshEquipmentRows();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void RefreshEquipmentRows()
    {
        EqmRows.Clear();
        try
        {
            foreach (var r in DesktopServices.EquipmentMaintenance.List(_session,
                         EqmSelected is null ? null : EqmSelected.Id))
                EqmRows.Add(r);
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    partial void OnEqmSelectedChanged(EquipmentPick? value) => RefreshEquipmentRows();

    [RelayCommand]
    private async Task SaveEquipmentMaintenance()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (!await BranchGuard.RequireBranchAsync(_session, "Bakım Takibi")) return;
        if (EqmSelected is null) { Status = "Ekipman seçin."; return; }
        if (EqmDef is null) { Status = "Bakım tanımı seçin."; return; }
        if (EqmLines.Any(l => l.Quantity <= 0)) { Status = "Malzeme miktarı pozitif olmalı."; return; }

        try
        {
            var mats = EqmLines
                .Select(l => new DepoWise.Infrastructure.Maintenance.MaintenanceMaterialLine(l.MaterialId, l.Quantity, l.FromTeamStock))
                .ToList();
            var eqmId = DesktopServices.EquipmentMaintenance.Save(_session,
                new DepoWise.Infrastructure.Maintenance.NewEquipmentMaintenance(
                    EquipmentId: EqmSelected.Id, DefinitionId: EqmDef.Id,
                    Description: string.IsNullOrWhiteSpace(EqmDescription) ? null : EqmDescription.Trim(),
                    PerformedDate: EqmPerformedDate, Materials: mats,
                    StockLocationId: MntLocation?.Id,          // araç sekmesiyle AYNI depo seçimi kullanılır
                    InvoiceNo: EqmInvoice,                     // ⭐ MUH-01b: belge no (araç sekmesinden AYRI)
                    PartyId: EqmParty?.Id),                    // ⭐ MUH-01c: dış servis sağlayıcısı
                Guid.NewGuid().ToString("N"));
            // ⭐ MUH-01a: ekipman bakımı da maliyet merkezine bağlanır — araç bakımıyla AYNI alan,
            // AYNI yardımcı (kayıt SONRASI bağ; bağ yazılamazsa kayıt "merkezsiz" kalır).
            // Ekipman bakım hattı (7b) açıldığında bu unutulmuştu: sunucu ucu bağı yazmaya
            // çalışıyor ama hiçbir arayüz göndermiyordu.
            BaglaMaliyetMerkezi("equipment_maintenance", eqmId, EqmCostCenter);
            EqmLines.Clear();
            EqmDescription = "";
            EqmInvoice = ""; EqmParty = null;   // ⭐ MUH-01b/c
            RefreshEquipmentRows();
            Status = "Ekipman bakımı kaydedildi.";
        }
        catch (Exception ex) { Status = "İşlem başarısız: " + ex.Message; }
    }

    [RelayCommand]
    private async Task CancelEquipmentMaintenance()
    {
        if (EqmSelectedRow is null) { Status = "Kayıt seçin."; return; }
        if (string.IsNullOrWhiteSpace(EqmCancelReason)) { Status = "İptal gerekçesi zorunlu."; return; }
        // ⭐ FAZ 4.2: standart iptal onayı (kullanıcı isteği 2026-09-06).
        if (!await ConfirmService.ConfirmCancelAsync(
                "Ekipman bakımı iptal edilecek; kullanılan malzemeler stoğa geri döner.")) return;
        try
        {
            DesktopServices.EquipmentMaintenance.Cancel(_session, EqmSelectedRow.Id, EqmCancelReason.Trim());
            EqmCancelReason = "";
            RefreshEquipmentRows();
            Status = "Ekipman bakımı iptal edildi.";
        }
        catch (Exception ex) { Status = "İşlem başarısız: " + ex.Message; }
    }
}

/// <summary>Ekipman seçici satırı (7b) — araç tarafındaki <c>VehiclePick</c> ile aynı rol.</summary>
public sealed record EquipmentPick(string Id, string Display)
{
    public override string ToString() => Display;
}

/// <summary>Bakım/Günlük Faaliyet formundaki malzeme satırı — İKİ EKRAN da bu sınıfı paylaşır (ortak davranış).</summary>
public sealed partial class MntMaterialLine : ObservableObject
{
    public string MaterialId { get; }
    public string Code { get; }
    public string Name { get; }
    [ObservableProperty] private decimal _quantity = 1;
    /// <summary>"Bakım Ekibi Stoğundan Kullanıldı" (kullanıcı isteği 2026-08-08): işaretliyse malzeme kayda
    /// girer ve maliyete dâhil olur, ancak merkez depo stoğundan düşülmez. Varsayılan false = eski davranış.</summary>
    [ObservableProperty] private bool _fromTeamStock;
    /// <summary>Eklenen satırda da KOD — AD gösterilir (kullanıcı isteği 2026-09-04):
    /// listeye eklendikten sonra da doğru parçanın seçildiği doğrulanabilsin.</summary>
    public string Display => string.IsNullOrWhiteSpace(Code) ? Name : $"{Code} — {Name}";

    public MntMaterialLine(string materialId, string code, string name) { MaterialId = materialId; Code = code; Name = name; }
}

public sealed record MaintenanceAlertRow(string VehicleCode, string Definition, AlertLevel Level,
    double Progress, decimal Consumed, decimal Interval, string VehicleId = "", string? Plate = null,
    // Uyarının dayandığı bakım kaydının kimliği; "hiç yapılmamış" uyarıda null (bkz. OpenAlert).
    string? MaintenanceId = null)
{
    /// <summary>Plakası olmayan araçta boş kutu yerine tire gösterilir (kolon hizası bozulmasın).</summary>
    public string PlateDisplay => string.IsNullOrWhiteSpace(Plate) ? "—" : Plate!;
    public string ProgressText => $"%{Progress * 100:0}";
    public string ConsumedText => $"{Consumed:0.##} / {Interval:0.##}";
    public string LevelText => Level switch
    {
        AlertLevel.Overdue => "Gecikti",
        AlertLevel.Critical => "Kritik",
        AlertLevel.Approaching => "Yaklaşıyor",
        _ => "Güncel",
    };
    public BadgeKind LevelKind => Level switch
    {
        AlertLevel.Overdue => BadgeKind.Danger,
        AlertLevel.Critical => BadgeKind.Warning,
        AlertLevel.Approaching => BadgeKind.Warning,
        _ => BadgeKind.Success,
    };
}
