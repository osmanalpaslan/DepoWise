using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Files;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Yedek Yönetimi — yedek al (VACUUM INTO), listele, geri yükle (yeniden başlat gerekir), klasörü aç.
/// Günlük otomatik yedek + 30 gün saklama servis tarafında.
/// </summary>
public sealed partial class BackupViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "backup", PermissionAction.Create);

    public ObservableCollection<BackupRowVm> Items { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    [ObservableProperty] private BackupRowVm? _selected;

    public BackupViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var b in DesktopServices.Backup.ListBackups()) Items.Add(new BackupRowVm(b));
            Status = $"{Items.Count} yedek — klasör: {DesktopServices.Backup.GetBackupFolder()}";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private async Task Backup()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Şimdi yedek alınsın mı?", "Yedek Al")) return;
        try
        {
            var path = DesktopServices.Backup.Backup();
            Load();
            Status = "Yedek alındı: " + path;
        }
        catch (Exception ex) { Status = "Yedek alınamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Restore(BackupRowVm? row)
    {
        row ??= Selected;
        if (row is null) { Status = "Yedek seçin."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{row.FileName}' yedeği GERİ YÜKLENSİN mi?\nMevcut veriler bu yedekle değiştirilir; uygulama yeniden başlatılmalı.",
                "Yedek Geri Yükle", "Evet, Geri Yükle", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Backup.Restore(_session, row.Path, reauthenticated: true);
            Status = "Geri yüklendi. Lütfen uygulamayı kapatıp yeniden açın.";
        }
        catch (Exception ex) { Status = "Geri yüklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try { FilePickerService.OpenFile(DesktopServices.Backup.GetBackupFolder()); }
        catch (Exception ex) { Status = "Klasör açılamadı: " + ex.Message; }
    }
}

public sealed class BackupRowVm
{
    public string Path { get; }
    public string FileName { get; }
    public string SizeText { get; }
    public string DateText { get; }

    public BackupRowVm(BackupInfo b)
    {
        Path = b.Path;
        FileName = System.IO.Path.GetFileName(b.Path);
        SizeText = $"{b.SizeBytes / 1024.0 / 1024.0:0.##} MB";
        DateText = DateTimeOffset.FromUnixTimeMilliseconds(b.CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    }
}
