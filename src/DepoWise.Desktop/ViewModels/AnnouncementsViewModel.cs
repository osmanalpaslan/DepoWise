using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Announcements;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Duyuru satırı görünümü (aktiflik anlık hesap — durum alanı yok).</summary>
public sealed record AnnouncementVm(AnnouncementRow Row, string StatusText)
{
    public string Title => Row.Title;
    public string? Body => Row.Body;
    public bool HasBody => !string.IsNullOrEmpty(Row.Body);
    public bool IsImportant => Row.IsImportant;
    public string ImportanceDisplay => Row.ImportanceDisplay;
    public string BranchDisplay => Row.BranchDisplay;
    public string PeriodDisplay => Row.PeriodDisplay;
    public string CreatedByName => Row.CreatedByName;
}

/// <summary>
/// ═══ DYR-01 (ADR-173, 2026-08-28) — DUYURULAR (masaüstü) ═══
/// YEREL çalışır (çevrimdışı okunur/yazılır; senkron taşır). PK-J1: okuma herkese — ekran menüde
/// herkese görünür; yazma announcements yetkisiyle. Yönetici tüm duyuruları (durum etiketiyle),
/// diğerleri yalnız YAYINDAKİLERİ görür.
/// </summary>
public sealed partial class AnnouncementsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanCreate => AccessControl.Can(_session, AnnouncementService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, AnnouncementService.Module, PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, AnnouncementService.Module, PermissionAction.Delete);
    public bool CanManage => CanCreate || CanEdit || CanDelete;
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);
    public string ListTitle => CanManage ? "Tüm Duyurular" : "Aktif Duyurular";

    public ObservableCollection<AnnouncementVm> Items { get; } = new();
    public ObservableCollection<ProjectPick> BranchOptions { get; } = new();
    public ObservableCollection<string> ImportanceOptions { get; } = new() { "Normal", "Önemli" };

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty] private AnnouncementVm? _selected;

    // ── Form ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formTitle2 = "";
    [ObservableProperty] private string _formBody = "";
    [ObservableProperty] private string _formImportance = "Normal";
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private DateTimeOffset? _formStart;
    [ObservableProperty] private DateTimeOffset? _formEnd;
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ DUYURU" : "DUYURU DÜZENLE";
    private long? _editVersion;

    public AnnouncementsViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        Load();
    }

    private void LoadOptions()
    {
        try
        {
            BranchOptions.Clear();
            foreach (var b in DesktopServices.Branches.List(_session))
                BranchOptions.Add(new ProjectPick(b.Id, b.Name));
        }
        catch { }
    }

    private long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var now = NowMs;
            foreach (var a in DesktopServices.Announcements.List(_session, includeInactive: CanManage,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText))
                Items.Add(new AnnouncementVm(a, a.StatusDisplay(now)));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    [RelayCommand]
    private void NewAnnouncement()
    {
        if (!CanCreate) { Status = "Yetki yok."; return; }
        EditId = null; _editVersion = null;
        FormTitle2 = ""; FormBody = ""; FormImportance = "Normal";
        FormBranch = null; FormStart = null; FormEnd = null; FormError = null;
        LoadOptions();
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Duyuru seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        var a = Selected.Row;
        EditId = a.Id; _editVersion = a.Version;
        FormTitle2 = a.Title; FormBody = a.Body ?? "";
        FormImportance = a.IsImportant ? "Önemli" : "Normal";
        FormBranch = BranchOptions.FirstOrDefault(b => b.Id == a.BranchId);
        FormStart = a.PublishStart is { } s ? DateTimeOffset.FromUnixTimeMilliseconds(s) : null;
        FormEnd = a.PublishEnd is { } e ? DateTimeOffset.FromUnixTimeMilliseconds(e) : null;
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; _editVersion = null; }

    private static long? Ms(DateTimeOffset? d) => d is null ? null
        : new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormTitle2)) { FormError = "Başlık zorunlu."; return; }
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing
            ? "Duyuru güncellensin mi? (Herkes için yeniden okunmamış olur.)" : "Duyuru yayınlansın mı?", "Duyuru")) return;
        try
        {
            var dto = new NewAnnouncement(FormTitle2.Trim(),
                string.IsNullOrWhiteSpace(FormBody) ? null : FormBody.Trim(),
                FormImportance == "Önemli" ? "important" : "normal",
                FormBranch?.Id, Ms(FormStart), Ms(FormEnd));
            if (editing) DesktopServices.Announcements.Update(_session, EditId!, dto, _editVersion);
            else DesktopServices.Announcements.Create(_session, dto);
            Status = editing ? "Duyuru güncellendi." : "Duyuru yayınlandı.";
            ShowAdd = false; EditId = null; _editVersion = null;
            Load();
            ShellViewModel.Current?.RefreshAlertBadge();   // duyuru bildirime düşer → çan tazelensin
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAnnouncement()
    {
        if (Selected is null) { Status = "Duyuru seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Duyuru silinsin mi? (Çöp Kutusu'ndan geri alınabilir.)", "Duyuru")) return;
        try
        {
            DesktopServices.Announcements.Delete(_session, Selected.Row.Id);
            Status = "Silindi — Çöp Kutusu'ndan geri alınabilir.";
            Load();
            ShellViewModel.Current?.RefreshAlertBadge();
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    /// <summary>Liste kuralı 2: filtrelenmiş TÜM liste Excel'e (yerel — çevrimdışı da çalışır).</summary>
    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            var rows = DesktopServices.Announcements.List(_session, includeInactive: CanManage,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText);
            var hedef = await FilePickerService.SaveExcelAsync("Duyurular.xlsx");
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(
                AnnouncementService.ToTableModel(rows, NowMs)));
            Status = $"Excel kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Excel aktarılamadı: " + ex.Message; }
    }
}
