using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Şube / Şantiye Tanım — liste + yeni/düzenle (ad, tür, üst şube) + detayda şubeye atanmış kullanıcılar
/// (otomatik listelenir). CRUD BranchService'te (tenant + yetki). Kullanıcı atama "Kullanıcılar" ekranından.
/// </summary>
public sealed partial class BranchesViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    /// <summary>#5 — Şube kodu + şifresi yalnız Admin / Süper Admin'e görünür/düzenlenebilir.</summary>
    public bool IsAdmin => AccessControl.IsAdmin(_session);
    public bool CanWrite => AccessControl.Can(_session, "branches", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "branches", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "branches", PermissionAction.Delete);

    public ObservableCollection<BranchRow> Items { get; } = new();
    public ObservableCollection<BranchRow> ParentOptions { get; } = new();
    public ObservableCollection<BranchUserRow> BranchUsers { get; } = new();
    public ObservableCollection<string> KindOptions { get; } = new() { "Şube", "Şantiye", "Saha" };

    /// <summary>Firma seçici — YALNIZ süper adminde görünür; şube seçilen firmaya bağlı açılır.
    /// Süper-admin-altı roller kendi firmasına kilitlidir (BranchService fail-closed zorlar).</summary>
    public bool IsSuperAdmin => _session.IsSuperAdmin;
    public ObservableCollection<CompanyPick> Companies { get; } = new();
    [ObservableProperty] private CompanyPick? _selectedCompany;

    /// <summary>Listelenen/oluşturulan şubelerin firması: süper admin seçtiyse o, aksi halde oturumun firması.</summary>
    private string TargetCompanyId => IsSuperAdmin && SelectedCompany is not null ? SelectedCompany.Id : _session.CompanyId;

    /// <summary>Firma değişti: liste + form o firmaya göre yenilenir (şube yanlış firmaya açılmasın).</summary>
    partial void OnSelectedCompanyChanged(CompanyPick? value)
    {
        if (_loading) return;
        CancelAdd();
        Load();
    }
    private bool _loading;

    [ObservableProperty] private string? _status;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private BranchRow? _selected;
    public bool HasSelection => Selected != null;

    // Form
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private string _formKind = "Şube";
    [ObservableProperty] private BranchRow? _formParent;
    [ObservableProperty] private string _formCode = "";
    [ObservableProperty] private string _formPassword = "";
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ ŞUBE / ŞANTİYE" : "ŞUBE DÜZENLE";
    public string PasswordLabel => EditId is null ? "Şube Şifresi" : "Yeni Şifre (boş = değişmez)";

    public BranchesViewModel(SessionContext session)
    {
        _session = session;
        // Firma seçici (yalnız süper admin) — varsayılan KENDİ firması, alfabetik ilk firma değil.
        if (_session.IsSuperAdmin)
        {
            _loading = true;
            try { foreach (var (id, name) in DesktopServices.Companies.Selectable(_session)) Companies.Add(new CompanyPick(id, name)); } catch { }
            SelectedCompany = Companies.FirstOrDefault(c => c.Id == _session.CompanyId) ?? Companies.FirstOrDefault();
            _loading = false;
        }
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            ParentOptions.Clear();
            foreach (var b in DesktopServices.Branches.List(_session, TargetCompanyId)) { Items.Add(b); ParentOptions.Add(b); }
            Status = $"{Items.Count} şube / şantiye";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        Selected = null; BranchUsers.Clear();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }

    partial void OnSelectedChanged(BranchRow? value)
    {
        BranchUsers.Clear();
        if (value is null) return;
        try { foreach (var u in DesktopServices.Branches.GetUsers(_session, value.Id)) BranchUsers.Add(u); }
        catch (Exception ex) { Status = "Kullanıcılar yüklenemedi: " + ex.Message; }
    }

    private static string KindCode(string display) => display switch { "Şantiye" => "site", "Saha" => "field", _ => "branch" };

    [RelayCommand]
    private void NewBranch()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; _editVersion = null;   // düzenleme kilidi: yeni kayıtta sürüm yok
        FormName = ""; FormKind = "Şube"; FormParent = null; FormCode = ""; FormPassword = ""; FormError = null;
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle)); OnPropertyChanged(nameof(PasswordLabel));
    }

    [RelayCommand]
    private void BeginEdit()
    {
        if (Selected is null) { Status = "Şube seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        EditId = Selected.Id; _editVersion = Selected.Version;   // düzenleme kilidi
        FormName = Selected.Name;
        FormKind = Selected.Kind switch { "site" => "Şantiye", "field" => "Saha", _ => "Şube" };
        FormParent = ParentOptions.FirstOrDefault(p => p.Id == Selected.ParentId);
        FormCode = Selected.Code ?? ""; FormPassword = "";
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle)); OnPropertyChanged(nameof(PasswordLabel));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; _editVersion = null; }

    /// <summary>DÜZENLEME KİLİDİ: formun açıldığı andaki şube sürümü (bkz. <see cref="BeginEdit"/>).
    /// Kaydederken sunucuya geri gönderilir; şube arada değiştiyse sunucu 409 döner ve üzerine yazılmaz.</summary>
    private long? _editVersion;

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName)) { FormError = "Ad zorunlu."; return; }
        // Kendini üst şube seçmeyi engelle
        var parentId = FormParent?.Id == EditId ? null : FormParent?.Id;
        var kind = KindCode(FormKind);
        var code = string.IsNullOrWhiteSpace(FormCode) ? null : FormCode.Trim();
        var pass = string.IsNullOrWhiteSpace(FormPassword) ? null : FormPassword;
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Şube güncellensin mi?" : "Şube oluşturulsun mu?", "Kaydet")) return;
        try
        {
            // ŞUBELER SUNUCU-OTORİTELİ (2026-07-25): çevrimiçiyken SUNUCUYA yaz. Yalnız yerele yazarsak sonraki
            // girişte aynalama (BranchMirror) sunucuda olmayan yerel şubeyi siler → kayıt kaybolur. Çevrimdışı → uyar.
            var res = editing
                ? await OrgServerClient.UpdateBranchAsync(EditId!, FormName.Trim(), kind, parentId, code, pass, TargetCompanyId, _editVersion)
                : await OrgServerClient.CreateBranchAsync(FormName.Trim(), kind, parentId, code, pass, TargetCompanyId);
            if (res.Offline) { FormError = "Şube işlemi çevrimiçi olmayı gerektirir (şubeler sunucuda tutulur). İnternet bağlantısıyla tekrar deneyin."; return; }
            if (res.Status == 409)
            {
                // DÜZENLEME KİLİDİ: şube biz formu açtıktan sonra değişti. Yazdıklarını KAYBETME: karar kullanıcının.
                FormError = res.Error ?? "Kayıt değişti.";
                if (await ConfirmService.AskAsync(
                        FormError + "\n\nŞubenin güncel hâlini yüklemek ister misiniz? " +
                        "(\"Formda kal\" derseniz yazdıklarınız durur, kopyalayıp tekrar uygulayabilirsiniz.)",
                        "Kayıt değişti", okText: "Kaydı yenile", cancelText: "Formda kal"))
                {
                    ShowAdd = false; EditId = null; _editVersion = null;
                    await BranchMirror.RefreshAsync(TargetCompanyId);
                    Load();
                }
                return;
            }
            if (!res.Ok) { FormError = res.Error ?? "Sunucu işlemi başarısız."; return; }
            await BranchMirror.RefreshAsync(TargetCompanyId);   // sunucu yazdı → yerel kopyayı anında aynala
            ShowAdd = false; EditId = null; _editVersion = null;
            Load();
            Status = editing ? "Şube güncellendi." : "Şube oluşturuldu.";
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) { Status = "Şube seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{Selected.Name}' silinsin mi?\n\nBu şubeye atanmış kullanıcıların şubesi boşaltılır.",
                "Şube Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            // Sunucu-otoriteli: silme de sunucuda yapılmalı (yalnız yerelde silsek aynalama geri getirirdi).
            var res = await OrgServerClient.DeleteBranchAsync(Selected.Id);
            if (res.Offline) { Status = "Şube silme çevrimiçi olmayı gerektirir. İnternet bağlantısıyla tekrar deneyin."; return; }
            if (!res.Ok) { Status = res.Error ?? "Silinemedi."; return; }
            await BranchMirror.RefreshAsync(TargetCompanyId);
            Load();
            Status = "Şube silindi.";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
