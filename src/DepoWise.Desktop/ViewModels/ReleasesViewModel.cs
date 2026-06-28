using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Update;
using DepoWise.Infrastructure.Update;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Güncelleme Yönetimi (YALNIZ Süper Admin) — paket yayınla (dosyadan checksum/boyut otomatik) + yayınlananları
/// listele + seçili paketi % ilerlemeyle KUR (UpdateService: checksum doğrula, bozuksa kurma, hata→rollback).
/// Web Güncelleme sunucusu hazır olunca indirme bu akışa bağlanır (ApplyUpdate aynı kalır).
/// </summary>
public sealed partial class ReleasesViewModel : ViewModelBase
{
    private readonly SessionContext _session;
    private byte[]? _pickedContent;

    public ObservableCollection<ReleaseRow> Items { get; } = new();

    [ObservableProperty] private string _currentVersion = "—";
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;

    // Paket seçimi
    [ObservableProperty] private string? _pickedFileName;
    [ObservableProperty] private string _fileChecksum = "";
    [ObservableProperty] private long _fileSize;

    // Yayın formu
    [ObservableProperty] private string _formVersion = "";
    [ObservableProperty] private string _formMinVersion = "0.0.0";
    [ObservableProperty] private string _formNotes = "";
    [ObservableProperty] private bool _formSigned;
    [ObservableProperty] private string? _formError;

    // Kurulum ilerlemesi
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isUpdating;
    [ObservableProperty] private int _updateProgress;
    public bool CanInstall => _pickedContent != null && !IsUpdating;

    public ReleasesViewModel(SessionContext session)
    {
        _session = session;
        try { CurrentVersion = DesktopServices.Update.CurrentVersion(); } catch { }
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var r in DesktopServices.Releases.List(_session)) Items.Add(r);
            Status = $"{Items.Count} yayın";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasRows));
    }

    [RelayCommand]
    private async Task PickFile()
    {
        var path = await FilePickerService.PickFileAsync("Güncelleme Paketi Seç", "Paket Dosyası",
            "*.zip", "*.depowiseupdate", "*.bin", "*.*");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            _pickedContent = bytes;
            PickedFileName = Path.GetFileName(path);
            FileChecksum = Convert.ToHexString(SHA256.HashData(bytes));
            FileSize = bytes.Length;
            Status = $"Paket seçildi: {PickedFileName}";
            OnPropertyChanged(nameof(CanInstall));
        }
        catch (Exception ex) { FormError = "Dosya okunamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Publish()
    {
        FormError = null;
        if (_pickedContent is null) { FormError = "Önce paket dosyası seçin."; return; }
        if (string.IsNullOrWhiteSpace(FormVersion)) { FormError = "Sürüm zorunlu (X.Y.Z)."; return; }
        if (!await ConfirmService.AskAsync($"{FormVersion} sürümü yayınlansın mı?", "Paket Yayınla")) return;
        try
        {
            DesktopServices.Releases.Publish(_session, new NewRelease(
                FormVersion.Trim(), FileChecksum, FileSize, FormMinVersion.Trim(),
                string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(), FormSigned));
            Load();
            Status = $"{FormVersion} yayınlandı.";
        }
        catch (Exception ex) { FormError = "Yayınlanamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Install()
    {
        if (_pickedContent is null) { Status = "Önce paket dosyası seçin."; return; }
        if (string.IsNullOrWhiteSpace(FormVersion)) { Status = "Kurulacak paketin sürümünü girin."; return; }
        if (!await ConfirmService.AskAsync(
                $"{FormVersion} sürümü KURULSUN mu? (hata olursa önceki sürüme dönülür)", "Güncellemeyi Kur")) return;

        IsUpdating = true; UpdateProgress = 0; FormError = null;
        var pkg = new UpdatePackage(FormVersion.Trim(), FileChecksum, FileSize, FormMinVersion.Trim(),
            string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(), FormSigned);
        var content = _pickedContent;
        try
        {
            await Task.Run(() => DesktopServices.Update.ApplyUpdate(pkg, content,
                progress: p => Dispatcher.UIThread.Post(() => UpdateProgress = p)));
            CurrentVersion = DesktopServices.Update.CurrentVersion();
            UpdateProgress = 100;
            Status = $"Güncelleme tamamlandı: sürüm {CurrentVersion}.";
        }
        catch (UpdateFailedException ex) { Status = "Güncelleme başarısız (önceki sürüme dönüldü): " + ex.Message; }
        catch (Exception ex) { Status = "Güncelleme hatası: " + ex.Message; }
        finally { IsUpdating = false; }
    }
}
