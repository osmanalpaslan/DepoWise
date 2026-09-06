using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Açılır liste elemanı (LookupBox, ToString ile arar/gösterir).</summary>
public sealed record ProjectPick(string Id, string Name) { public override string ToString() => Name; }

/// <summary>Liste satırı — masaüstü görünümü (sunucudan gelen değerlerin ekrana hazır hâli).</summary>
public sealed record ProjectItemRow(string Id, string Name, string Status, string StatusDisplay,
    long? StartDate, long? EndDate, string? ManagerPersonnelId, string ManagerName,
    string? Location, string? Description, IReadOnlyList<string> BranchIds, string BranchDisplay, long Version)
{
    public string StartDisplay => Fmt(StartDate);
    public string EndDisplay => Fmt(EndDate);
    public string LocationDisplay => string.IsNullOrEmpty(Location) ? "—" : Location!;
    private static string Fmt(long? ms) => ms is null ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime.ToString("dd.MM.yyyy");
}

/// <summary>
/// ═══ PRJ-01 (ADR-164, 2026-08-27) — PROJELER (masaüstü) ═══
///
/// SUNUCU-OTORİTELİ ekran (şubeler gibi): liste ve CRUD çevrimiçi API üzerinden çalışır
/// (<see cref="OrgServerClient"/>); çevrimdışıysa anlaşılır uyarı gösterilir, yerele YAZILMAZ.
/// Yetki: <c>branches</c> modülü (PK-C4). Veri kapsamı SUNUCUDA BranchAccess ile uygulanır.
/// PK-C1: bağlı şantiye şimdilik TEK seçimdir; API sözleşmesi (branchIds listesi) çokluya hazırdır.
/// </summary>
public sealed partial class ProjectsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "branches", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "branches", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "branches", PermissionAction.Delete);

    public ObservableCollection<ProjectItemRow> Items { get; } = new();
    public ObservableCollection<ProjectPick> BranchOptions { get; } = new();
    public ObservableCollection<ProjectPick> PersonnelOptions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "Aktif", "Beklemede", "Tamamlandı" };
    public ObservableCollection<string> FilterStatusOptions { get; } = new() { "Tümü", "Aktif", "Beklemede", "Tamamlandı" };

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterStatus = "Tümü";
    partial void OnFilterStatusChanged(string value) => _ = LoadAsync();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ProjectItemRow? _selected;
    public bool HasSelection => Selected != null;

    // ── Form ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private string _formStatus = "Aktif";
    [ObservableProperty] private DateTimeOffset? _formStart;
    [ObservableProperty] private DateTimeOffset? _formEnd;
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private ProjectPick? _formManager;
    [ObservableProperty] private string _formLocation = "";
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ PROJE" : "PROJE DÜZENLE";
    private long? _editVersion;   // düzenleme kilidi jetonu (BranchesViewModel ile aynı desen)

    public ProjectsViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        _ = LoadAsync();
    }

    /// <summary>Şantiye + personel seçenekleri YERELDEN gelir (şube aynası + personel senkronlu) —
    /// açılır listeler çevrimdışıyken de dolu görünür; yazma yine sunucu ister.</summary>
    private void LoadOptions()
    {
        try
        {
            BranchOptions.Clear();
            foreach (var b in DesktopServices.Branches.List(_session))
                BranchOptions.Add(new ProjectPick(b.Id, b.Name));
        }
        catch { }
        try
        {
            PersonnelOptions.Clear();
            foreach (var (name, id) in DesktopServices.Personnel.AllNameToId(_session).OrderBy(x => x.Key, StringComparer.CurrentCulture))
                PersonnelOptions.Add(new ProjectPick(id, name));
        }
        catch { }
    }

    private static string StatusCode(string display) => display switch
    { "Beklemede" => "on_hold", "Tamamlandı" => "completed", _ => "active" };

    [RelayCommand]
    private async Task Load() => await LoadAsync();

    private async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var st = FilterStatus == "Tümü" ? null : StatusCode(FilterStatus);
            var list = await OrgServerClient.ListProjectsAsync(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText, st);
            Items.Clear();
            if (list is null)
            {
                LoadError = "Projeler sunucudan yüklenir; şu anda sunucuya ulaşılamıyor. İnternet bağlantısıyla tekrar deneyin.";
                OnPropertyChanged(nameof(HasRows));
                return;
            }
            foreach (var p in list)
                Items.Add(new ProjectItemRow(p.Id, p.Name, p.Status, p.StatusDisplay, p.StartDate, p.EndDate,
                    p.ManagerPersonnelId, p.ManagerName, p.Location, p.Description, p.BranchIds, p.BranchDisplay, p.Version));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    [RelayCommand]
    private void NewProject()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; _editVersion = null;
        FormName = ""; FormStatus = "Aktif"; FormStart = null; FormEnd = null;
        FormBranch = null; FormManager = null; FormLocation = ""; FormDescription = ""; FormError = null;
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task BeginEdit()
    {
        if (Selected is null) { Status = "Proje seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        // ⭐ FAZ 4.2: standart düzenleme onayı (kullanıcı isteği 2026-09-06).
        if (!await ConfirmService.ConfirmEditAsync()) return;
        EditId = Selected.Id; _editVersion = Selected.Version;
        FormName = Selected.Name;
        FormStatus = Selected.Status switch { "on_hold" => "Beklemede", "completed" => "Tamamlandı", _ => "Aktif" };
        FormStart = Selected.StartDate is { } sd ? DateTimeOffset.FromUnixTimeMilliseconds(sd) : null;
        FormEnd = Selected.EndDate is { } ed ? DateTimeOffset.FromUnixTimeMilliseconds(ed) : null;
        FormBranch = BranchOptions.FirstOrDefault(b => Selected.BranchIds.Contains(b.Id));   // PK-C1: tek şantiye
        FormManager = PersonnelOptions.FirstOrDefault(p => p.Id == Selected.ManagerPersonnelId);
        FormLocation = Selected.Location ?? ""; FormDescription = Selected.Description ?? "";
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; _editVersion = null; }

    /// <summary>Tarih = İŞ GÜNÜ anlamı (web ile aynı dönüşüm: günün UTC gece yarısı, Unix ms).</summary>
    private static long? ToMs(DateTimeOffset? d) => IsGunuTarihi.Ms(d);   // ADR-184: tek kaynak

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName)) { FormError = "Proje adı zorunlu."; return; }
        if (ToMs(FormStart) is { } s1 && ToMs(FormEnd) is { } s2 && s2 < s1)
        { FormError = "Bitiş tarihi başlangıçtan önce olamaz."; return; }
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Proje güncellensin mi?" : "Proje oluşturulsun mu?", "Kaydet")) return;
        try
        {
            var body = new
            {
                name = FormName.Trim(),
                status = StatusCode(FormStatus),
                startDate = ToMs(FormStart),
                endDate = ToMs(FormEnd),
                managerPersonnelId = FormManager?.Id,
                location = string.IsNullOrWhiteSpace(FormLocation) ? null : FormLocation.Trim(),
                description = string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                branchIds = FormBranch is null ? Array.Empty<string>() : new[] { FormBranch.Id },   // PK-C1: tek seçim
                version = editing ? _editVersion : null,
            };
            var res = editing
                ? await OrgServerClient.UpdateProjectAsync(EditId!, body)
                : await OrgServerClient.CreateProjectAsync(body);
            if (res.Offline) { FormError = "Proje işlemi çevrimiçi olmayı gerektirir (projeler sunucuda tutulur). İnternet bağlantısıyla tekrar deneyin."; return; }
            if (res.Status == 409)
            {
                // Düzenleme kilidi: proje biz formu açtıktan sonra değişti — yazılanlar kaybolmaz, karar kullanıcının.
                FormError = res.Error ?? "Kayıt değişti.";
                if (await ConfirmService.AskAsync(
                        FormError + "\n\nProjenin güncel hâlini yüklemek ister misiniz? " +
                        "(\"Formda kal\" derseniz yazdıklarınız durur, kopyalayıp tekrar uygulayabilirsiniz.)",
                        "Kayıt değişti", okText: "Kaydı yenile", cancelText: "Formda kal"))
                { ShowAdd = false; EditId = null; _editVersion = null; await LoadAsync(); }
                return;
            }
            if (!res.Ok) { FormError = res.Error ?? "Sunucu işlemi başarısız."; return; }
            ShowAdd = false; EditId = null; _editVersion = null;
            await LoadAsync();
            Status = editing ? "Proje güncellendi." : "Proje oluşturuldu.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) { Status = "Proje seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{Selected.Name}' silinsin mi?\n\nKayıt Çöp Kutusu'ndan geri alınabilir.",
                "Proje Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            var res = await OrgServerClient.DeleteProjectAsync(Selected.Id);
            if (res.Offline) { Status = "Proje silme çevrimiçi olmayı gerektirir. İnternet bağlantısıyla tekrar deneyin."; return; }
            if (!res.Ok) { Status = res.Error ?? "Silinemedi."; return; }
            await LoadAsync();
            Status = "Proje silindi (Çöp Kutusu'ndan geri alınabilir).";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
