using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Files;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Çöp Kutusu — silinen (is_deleted=1) kayıtları listeler; "Geri Yükle" ile kurtarır (RestoreTrash yetkisi).
///
/// G6-04 (2026-08-11): ekran KİLİTLİ açılır. Önceden <c>reauthenticated: true</c> SABİT geçiliyordu; yani
/// <c>TrashService.RequireAccess</c>'in ikinci kapısı (yeniden kimlik doğrulama) masaüstünde hiç işlemiyordu.
/// Web bunu zaten istiyordu (Trash.razor → /api/trash parola ile). Artık masaüstü de parola sorar; doğrulama
/// <c>AuthService.VerifyUserPassword</c> ile YEREL olarak yapılır (çevrimdışı da çalışsın diye) ve bayrak
/// ancak gerçekten doğrulandıysa <c>true</c> olur. Yetki kontrolü (RestoreTrash) AYRICA yerinde durur.
/// </summary>
public sealed partial class TrashViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    /// <summary>Parola doğrulandı mı? Servise BU değer geçilir — asla sabit true değil.</summary>
    private bool _reauthenticated;

    public bool CanRestore => AccessControl.CanUseButton(_session, SpecialButtons.RestoreTrash);

    public ObservableCollection<TrashRowVm> Items { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    /// <summary>Kilit ekranı görünür mü (parola henüz doğrulanmadı).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnlocked))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLocked = true;
    [ObservableProperty] private string? _unlockError;
    public bool IsUnlocked => !IsLocked;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => IsUnlocked && !HasError && Items.Count == 0;

    public TrashViewModel(SessionContext session)
    {
        _session = session;
        Status = "Çöp kutusunu açmak için parolanızı doğrulayın.";
    }

    /// <summary>Kilidi açar: parola sorar → yerel olarak doğrular → ancak başarılıysa listeyi yükler.</summary>
    [RelayCommand]
    private async Task Unlock()
    {
        UnlockError = null;
        // Yetki kapısı parola kapısından ÖNCE: yetkisi olmayana parola bile sorulmaz (fail-closed).
        if (!CanRestore) { UnlockError = "Bu ekran için yetkiniz yok."; return; }

        var password = await ConfirmService.AskPasswordAsync(
            "Çöp Kutusu silinmiş kayıtları gösterir. Güvenlik için parolanızı yeniden girin.",
            "Çöp Kutusu", "Parolanız", "Çöp Kutusunu Aç");
        if (password is null) return;   // vazgeçildi → kilit açılmaz

        if (!DesktopServices.Auth.VerifyUserPassword(_session.UserId, password))
        {
            UnlockError = "Parola hatalı.";
            return;
        }

        _reauthenticated = true;
        IsLocked = false;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        if (!_reauthenticated) { Status = "Çöp kutusunu açmak için parolanızı doğrulayın."; return; }
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var t in DesktopServices.Trash.List(_session, _reauthenticated)) Items.Add(new TrashRowVm(t));
            Status = $"{Items.Count} silinmiş kayıt";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private async Task Restore(TrashRowVm? row)
    {
        if (row is null) return;
        if (!CanRestore) { Status = "Yetki yok."; return; }
        if (!_reauthenticated) { Status = "Önce parolanızı doğrulayın."; return; }
        if (!await ConfirmService.AskAsync($"'{row.Label}' geri yüklensin mi?", "Geri Yükle")) return;
        try
        {
            DesktopServices.Trash.Restore(_session, row.Table, row.Id, _reauthenticated);
            Load();
            Status = "Kayıt geri yüklendi.";
        }
        catch (Exception ex) { Status = "Geri yüklenemedi: " + ex.Message; }
    }
}

public sealed class TrashRowVm
{
    public string Table { get; }
    public string Id { get; }
    public string Label { get; }
    public string DateText { get; }
    public string TableText { get; }

    public TrashRowVm(TrashItem t)
    {
        Table = t.Table; Id = t.Id; Label = string.IsNullOrWhiteSpace(t.Label) ? "(adsız)" : t.Label;
        DateText = DateTimeOffset.FromUnixTimeMilliseconds(t.UpdatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        TableText = t.Table switch
        {
            "materials" => "Malzeme", "vehicles" => "Araç", "personnel" => "Personel", "branches" => "Şube",
            "users" => "Kullanıcı",   // G6-03
            "suppliers" => "Tedarikçi", "brands" => "Marka", "units" => "Birim",
            "material_categories" => "Kategori", "vehicle_templates" => "Araç Şablonu",
            "vehicle_types" => "Makine Tipi", "vehicle_categories" => "Araç Kategorisi",
            "equipment" => "Ekipman", "equipment_types" => "Ekipman Türü",   // EKP-01
            "cost_centers" => "Maliyet Merkezi",   // MLY-01
            "vehicle_models" => "Model", "maintenance_definitions" => "Bakım Tanımı", _ => t.Table
        };
    }
}
