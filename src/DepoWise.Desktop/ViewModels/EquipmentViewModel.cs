using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Equipment;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ═══ EKP-01 (ADR-166, 2026-08-28) — EKİPMAN (masaüstü) ═══
///
/// Araçlar gibi YEREL çalışır (çevrimdışı dahil): CRUD yerel SQLite'a yazılır, senkron taşır.
/// Yetki: equipment modülü + BranchAccess kapsamı (serviste). Bakım/yakıt entegrasyonu kapsam dışı (PK-E2/E3).
/// </summary>
public sealed partial class EquipmentViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, EquipmentService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, EquipmentService.Module, PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, EquipmentService.Module, PermissionAction.Delete);
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);

    public ObservableCollection<EquipmentRow> Items { get; } = new();
    public ObservableCollection<ProjectPick> TypeOptions { get; } = new();
    public ObservableCollection<ProjectPick> BranchOptions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "Aktif", "Pasif", "Bakımda" };
    public ObservableCollection<string> FilterStatusOptions { get; } = new() { "Tümü", "Aktif", "Pasif", "Bakımda" };

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterStatus = "Tümü";
    partial void OnFilterStatusChanged(string value) => Load();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private EquipmentRow? _selected;
    public bool HasSelection => Selected != null;

    // ── Form ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formCode = "";
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private ProjectPick? _formType;
    [ObservableProperty] private string _formStatus = "Aktif";
    [ObservableProperty] private string _formStatusNote = "";
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private string _formSerialNo = "";
    [ObservableProperty] private string _formLocation = "";
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ EKİPMAN" : "EKİPMAN DÜZENLE";
    private long? _editVersion;   // düzenleme kilidi

    public EquipmentViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        Load();
    }

    private void LoadOptions()
    {
        try
        {
            TypeOptions.Clear();
            foreach (var t in DesktopServices.Lookups.List(_session, "equipment_types"))
                TypeOptions.Add(new ProjectPick(t.Id, t.Name));
        }
        catch { }
        try
        {
            BranchOptions.Clear();
            foreach (var b in DesktopServices.Branches.List(_session))
                BranchOptions.Add(new ProjectPick(b.Id, b.Name));
        }
        catch { }
    }

    private static string StatusCode(string display) => display switch
    { "Pasif" => "passive", "Bakımda" => "maintenance", _ => "active" };

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            var st = FilterStatus == "Tümü" ? null : StatusCode(FilterStatus);
            Items.Clear();
            foreach (var e in DesktopServices.Equipment.List(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText, status: st))
                Items.Add(e);
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    /// <summary>Liste kuralı 2: filtrelenmiş TÜM sonucu Excel'e aktarır (yereldir — çevrimdışı da çalışır).</summary>
    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            var st = FilterStatus == "Tümü" ? null : StatusCode(FilterStatus);
            var rows = DesktopServices.Equipment.List(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText, status: st);
            var hedef = await FilePickerService.SaveExcelAsync("Ekipman.xlsx");
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(EquipmentService.ToTableModel(rows)));
            Status = $"Excel kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Excel aktarılamadı: " + ex.Message; }
    }

    /// <summary>BAR-01 (ADR-177): seçili ekipmanın KODUNU içeren yazdırılabilir QR etiketi (PNG).
    /// SALT-OKUNUR — kayda/DB'ye yazmaz; QR'a yalnız kod girer. Yerel üretim → çevrimdışı da çalışır.</summary>
    [RelayCommand]
    private async Task QrLabel()
    {
        if (Selected is null) { Status = "Önce listeden bir ekipman seçin."; return; }
        try
        {
            var bytes = DepoWise.Infrastructure.Reporting.QrLabelService.Png(Selected.Code);
            var hedef = await FilePickerService.SavePngAsync(DepoWise.Infrastructure.Reporting.QrLabelService.FileName(Selected.Code));
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, bytes);
            FilePickerService.OpenFile(hedef);
            Status = "QR etiketi kaydedildi: " + hedef;
        }
        catch (Exception ex) { Status = "QR üretilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void NewEquipment()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; _editVersion = null;
        FormCode = ""; FormName = ""; FormType = null; FormStatus = "Aktif"; FormStatusNote = "";
        FormBranch = null; FormSerialNo = ""; FormLocation = ""; FormDescription = ""; FormError = null;
        LoadOptions();   // "+" ile başka ekranda eklenen tür/şube de görünsün
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Ekipman seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        EditId = Selected.Id; _editVersion = Selected.Version;
        FormCode = Selected.Code; FormName = Selected.Name;
        FormType = TypeOptions.FirstOrDefault(t => t.Id == Selected.TypeId);
        FormStatus = Selected.Status switch { "passive" => "Pasif", "maintenance" => "Bakımda", _ => "Aktif" };
        FormStatusNote = Selected.StatusNote ?? "";
        FormBranch = BranchOptions.FirstOrDefault(b => b.Id == Selected.BranchId);
        FormSerialNo = Selected.SerialNo ?? ""; FormLocation = Selected.Location ?? "";
        FormDescription = Selected.Description ?? "";
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; _editVersion = null; }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormCode) || string.IsNullOrWhiteSpace(FormName))
        { FormError = "Kod ve ad zorunlu."; return; }
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Ekipman güncellensin mi?" : "Ekipman oluşturulsun mu?", "Kaydet")) return;
        try
        {
            var dto = new NewEquipment(FormCode.Trim(), FormName.Trim(), FormType?.Id, StatusCode(FormStatus),
                FormStatusNote, FormBranch?.Id, FormSerialNo, FormLocation, FormDescription);
            if (editing) DesktopServices.Equipment.Update(_session, EditId!, dto, _editVersion);
            else DesktopServices.Equipment.Create(_session, dto);
            ShowAdd = false; EditId = null; _editVersion = null;
            Load();
            Status = editing ? "Ekipman güncellendi." : "Ekipman oluşturuldu.";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) { Status = "Ekipman seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{Selected.Name}' silinsin mi?\n\nKayıt Çöp Kutusu'ndan geri alınabilir.",
                "Ekipman Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Equipment.Delete(_session, Selected.Id);
            Load();
            Status = "Ekipman silindi (Çöp Kutusu'ndan geri alınabilir).";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
