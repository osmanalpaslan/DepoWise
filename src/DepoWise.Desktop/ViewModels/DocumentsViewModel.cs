using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Files;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Belge liste satırı — ekrana hazır görünümler.</summary>
public sealed record DocumentItemRow(string Id, string EntityType, string EntityTypeDisplay, string EntityId,
    string EntityLabel, string Title, string? DocType, long? ValidFrom, long? ValidUntil,
    string? Description, string FileName, string? Mime, long? SizeBytes, long CreatedAt, long Version)
{
    public string BagliDisplay => EntityType == "company" ? "Genel (Firma)" : $"{EntityTypeDisplay}: {EntityLabel}";
    public string DocTypeDisplay => string.IsNullOrEmpty(DocType) ? "—" : DocType!;
    public string SizeDisplay => SizeBytes is { } b ? $"{b / 1024.0:0.#} KB" : "—";
    public string CreatedDisplay => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string ValidDisplay => ValidFrom is null && ValidUntil is null ? "—"
        : $"{Gun(ValidFrom)} – {Gun(ValidUntil)}";
    private static string Gun(long? ms) => ms is null ? "…"
        : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime.ToString("dd.MM.yyyy");
}

/// <summary>
/// ═══ EVR-01 (ADR-165, 2026-08-27) — EVRAK / BELGELER (masaüstü) ═══
///
/// SUNUCU-OTORİTELİ ekran: belgeler sunucuda tutulur (fotoğrafların aksine her makineden erişilir);
/// liste/yükleme/indirme/silme çevrimiçi API üzerinden. Çevrimdışıysa anlaşılır uyarı — yerele YAZILMAZ.
/// Yetki iki kapılı ve SUNUCUDADIR (files modülü + bağlı kaydın modülü + şube/proje kapsamı);
/// buradaki Can* yalnız görünürlüktür.
/// </summary>
public sealed partial class DocumentsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "files", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "files", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "files", PermissionAction.Delete);

    public ObservableCollection<DocumentItemRow> Items { get; } = new();
    public ObservableCollection<ProjectPick> TypeOptions { get; } = new();      // bağlı kayıt türleri
    public ObservableCollection<ProjectPick> EntityOptions { get; } = new();    // seçilen türün kayıtları
    public ObservableCollection<ProjectPick> FilterTypeOptions { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ProjectPick? _filterType;
    partial void OnFilterTypeChanged(ProjectPick? value) => _ = LoadAsync();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private DocumentItemRow? _selected;
    public bool HasSelection => Selected != null;

    // ── Form ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formTitle2 = "";
    [ObservableProperty] private string _formDocType = "";
    [ObservableProperty] private ProjectPick? _formEntityType;
    [ObservableProperty] private ProjectPick? _formEntity;
    [ObservableProperty] private DateTimeOffset? _formValidFrom;
    [ObservableProperty] private DateTimeOffset? _formValidUntil;
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string? _formFilePath;
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ BELGE" : "BELGE BİLGİLERİNİ DÜZENLE";
    public bool IsNew => EditId is null;
    public bool EntityPickVisible => IsNew && FormEntityType is { Id: not "company" };
    public string FormFileDisplay => string.IsNullOrEmpty(FormFilePath) ? "Dosya seçilmedi" : Path.GetFileName(FormFilePath);

    partial void OnFormEntityTypeChanged(ProjectPick? value)
    {
        OnPropertyChanged(nameof(EntityPickVisible));
        _ = LoadEntityOptionsAsync();
    }
    partial void OnFormFilePathChanged(string? value) => OnPropertyChanged(nameof(FormFileDisplay));
    partial void OnEditIdChanged(string? value) { OnPropertyChanged(nameof(FormTitle)); OnPropertyChanged(nameof(IsNew)); OnPropertyChanged(nameof(EntityPickVisible)); }

    public DocumentsViewModel(SessionContext session)
    {
        _session = session;
        foreach (var (key, label) in DepoWise.Infrastructure.Files.DocumentService.EntityTypes)
        {
            TypeOptions.Add(new ProjectPick(key, label));
            FilterTypeOptions.Add(new ProjectPick(key, label));
        }
        FilterTypeOptions.Insert(0, new ProjectPick("", "Tümü"));
        FilterType = FilterTypeOptions[0];
        FormEntityType = TypeOptions.FirstOrDefault(t => t.Id == "company");
        _ = LoadAsync();
    }

    /// <summary>Bağlı kayıt seçenekleri: mevcut sunucu uçlarından (çevrimdışıysa liste boş kalır, yazma zaten çevrimiçi ister).</summary>
    private async Task LoadEntityOptionsAsync()
    {
        EntityOptions.Clear();
        var tip = FormEntityType?.Id;
        if (string.IsNullOrEmpty(tip) || tip == "company") return;
        var yol = tip switch
        {
            "material" => ("/api/materials", "id", "name"),
            "vehicle" => ("/api/vehicles/options", "id", "name"),
            "equipment" => ("/api/equipment", "id", "name"),
            "personnel" => ("/api/personnel", "id", "fullName"),
            "branch" => ("/api/branches", "id", "name"),
            "project" => ("/api/projects", "id", "name"),
            _ => default,
        };
        if (yol == default) return;
        var list = await OrgServerClient.OptionListAsync(yol.Item1, yol.Item2, yol.Item3);
        if (list is null) { Status = "Kayıt listesi için sunucuya ulaşılamadı."; return; }
        foreach (var (id, name) in list) EntityOptions.Add(new ProjectPick(id, name));
    }

    [RelayCommand]
    private async Task Load() => await LoadAsync();

    private async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var list = await OrgServerClient.ListDocumentsAsync(
                string.IsNullOrEmpty(FilterType?.Id) ? null : FilterType!.Id,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText);
            Items.Clear();
            if (list is null)
            {
                LoadError = "Belgeler sunucuda tutulur; şu anda sunucuya ulaşılamıyor. İnternet bağlantısıyla tekrar deneyin.";
                OnPropertyChanged(nameof(HasRows));
                return;
            }
            foreach (var d in list)
                Items.Add(new DocumentItemRow(d.Id, d.EntityType, d.EntityTypeDisplay, d.EntityId, d.EntityLabel,
                    d.Title, d.DocType, d.ValidFrom, d.ValidUntil, d.Description, d.FileName, d.Mime,
                    d.SizeBytes, d.CreatedAt, d.Version));
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    [RelayCommand]
    private void NewDocument()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null;
        FormTitle2 = ""; FormDocType = ""; FormDescription = ""; FormFilePath = null;
        FormValidFrom = null; FormValidUntil = null; FormEntity = null;
        FormEntityType = TypeOptions.FirstOrDefault(t => t.Id == "company");
        FormError = null; ShowAdd = true;
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Belge seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        EditId = Selected.Id;
        FormTitle2 = Selected.Title; FormDocType = Selected.DocType ?? "";
        FormDescription = Selected.Description ?? ""; FormFilePath = null;
        FormValidFrom = Selected.ValidFrom is { } f ? DateTimeOffset.FromUnixTimeMilliseconds(f) : null;
        FormValidUntil = Selected.ValidUntil is { } u ? DateTimeOffset.FromUnixTimeMilliseconds(u) : null;
        FormEntityType = TypeOptions.FirstOrDefault(t => t.Id == Selected.EntityType);
        FormError = null; ShowAdd = true;
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; }

    [RelayCommand]
    private async Task PickFile()
    {
        var yol = await FilePickerService.PickFileAsync("Belge Seç",
            "Belgeler (PDF, Office, görsel)", "*.pdf", "*.jpg", "*.jpeg", "*.png", "*.docx", "*.xlsx", "*.doc", "*.xls");
        if (yol is null) return;
        // Sunucu zaten doğrular; burada erken doğrulama kullanıcıya ANINDA anlaşılır mesaj verir.
        var bytes = await File.ReadAllBytesAsync(yol);
        var v = DocumentValidation.Validate(Path.GetFileName(yol), null, bytes);
        if (!v.Ok) { FormError = v.Error; return; }
        FormError = null;
        FormFilePath = yol;
    }

    private static long? ToMs(DateTimeOffset? d) => d is null ? null
        : new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormTitle2)) { FormError = "Belge başlığı zorunlu."; return; }
        if (ToMs(FormValidFrom) is { } a && ToMs(FormValidUntil) is { } b && b < a)
        { FormError = "Geçerlilik bitişi başlangıçtan önce olamaz."; return; }
        var editing = EditId is not null;
        if (!editing)
        {
            if (string.IsNullOrEmpty(FormFilePath)) { FormError = "Dosya seçilmedi."; return; }
            if (FormEntityType is { Id: not "company" } && FormEntity is null) { FormError = "Bağlı kayıt seçilmedi."; return; }
        }
        if (!await ConfirmService.AskAsync(editing ? "Belge bilgileri güncellensin mi?" : "Belge yüklensin mi?", "Kaydet")) return;
        try
        {
            OrgServerClient.Result res;
            if (editing)
            {
                res = await OrgServerClient.UpdateDocumentMetaAsync(EditId!, new
                {
                    title = FormTitle2.Trim(),
                    docType = string.IsNullOrWhiteSpace(FormDocType) ? null : FormDocType.Trim(),
                    validFrom = ToMs(FormValidFrom),
                    validUntil = ToMs(FormValidUntil),
                    description = string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                });
            }
            else
            {
                var bytes = await File.ReadAllBytesAsync(FormFilePath!);
                res = await OrgServerClient.UploadDocumentAsync(Path.GetFileName(FormFilePath!), null, bytes,
                    new Dictionary<string, string?>
                    {
                        ["title"] = FormTitle2.Trim(),
                        ["docType"] = FormDocType,
                        ["description"] = FormDescription,
                        ["entityType"] = FormEntityType?.Id ?? "company",
                        ["entityId"] = FormEntity?.Id,
                        ["validFrom"] = ToMs(FormValidFrom)?.ToString(),
                        ["validUntil"] = ToMs(FormValidUntil)?.ToString(),
                    });
            }
            if (res.Offline) { FormError = "Belge işlemi çevrimiçi olmayı gerektirir (belgeler sunucuda tutulur). İnternet bağlantısıyla tekrar deneyin."; return; }
            if (!res.Ok) { FormError = res.Error ?? "Sunucu işlemi başarısız."; return; }
            ShowAdd = false; EditId = null;
            await LoadAsync();
            Status = editing ? "Belge bilgileri güncellendi." : "Belge yüklendi.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Download()
    {
        if (Selected is null) { Status = "Belge seçin."; return; }
        var hedef = await FilePickerService.SaveAnyAsync(Selected.FileName);
        if (hedef is null) return;
        var bytes = await OrgServerClient.DownloadDocumentAsync(Selected.Id);
        if (bytes is null) { Status = "Dosya indirilemedi (sunucuya ulaşılamıyor veya yetki yok)."; return; }
        try
        {
            await File.WriteAllBytesAsync(hedef, bytes);
            Status = $"İndirildi: {hedef}";
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) { Status = "Belge seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync($"'{Selected.Title}' silinsin mi?", "Belge Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            var res = await OrgServerClient.DeleteDocumentAsync(Selected.Id);
            if (res.Offline) { Status = "Belge silme çevrimiçi olmayı gerektirir. İnternet bağlantısıyla tekrar deneyin."; return; }
            if (!res.Ok) { Status = res.Error ?? "Silinemedi."; return; }
            await LoadAsync();
            Status = "Belge silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
