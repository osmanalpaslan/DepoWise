using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;
using DepoWise.Infrastructure.Files;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Sunucu Yedekleri (yalnız Süper Admin). İki tarih arası sunucu yedeklerini listeler ve TOPLU siler.
/// Sunucu normalde hiçbir yedeği silmez; bu ekran kasıtlı temizlik içindir. Backend ayrı kurulur
/// (bkz. docs/SERVER_BACKUP_CONTRACT.md). Yapılandırma yoksa uyarı verir.
/// </summary>
public sealed partial class ServerBackupsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<ServerBackupRowVm> Items { get; } = new();

    [ObservableProperty] private DateTimeOffset? _from = DateTimeOffset.Now.AddDays(-30);
    [ObservableProperty] private DateTimeOffset? _to = DateTimeOffset.Now;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _busy;

    private string? ServerUrl => DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerUrl);
    private string? ServerToken => DesktopServices.Settings.Get(_session.CompanyId, SettingKeys.BackupServerToken);
    public bool ServerConfigured => !string.IsNullOrWhiteSpace(ServerUrl);
    public bool HasRows => Items.Count > 0;

    public ServerBackupsViewModel(SessionContext session)
    {
        _session = session;
        if (!ServerConfigured) Status = "Sunucu adresi tanımlı değil — Ayarlar › Sunucu Yedek.";
    }

    private (DateOnly From, DateOnly To)? Range()
    {
        if (From is null || To is null) { Status = "Başlangıç ve bitiş tarihi seçin."; return null; }
        var f = DateOnly.FromDateTime(From.Value.DateTime);
        var t = DateOnly.FromDateTime(To.Value.DateTime);
        if (f > t) { Status = "Başlangıç, bitişten sonra olamaz."; return null; }
        return (f, t);
    }

    [RelayCommand]
    private async Task Load()
    {
        if (!ServerConfigured) { Status = "Sunucu adresi tanımlı değil."; return; }
        var r = Range(); if (r is null) return;
        Busy = true;
        try
        {
            var res = await DesktopServices.BackupUpload.ListAsync(
                ServerUrl!, ServerToken, _session.CompanyId, r.Value.From, r.Value.To);
            Items.Clear();
            foreach (var it in res.Items) Items.Add(new ServerBackupRowVm(it));
            OnPropertyChanged(nameof(HasRows));
            Status = res.Ok ? $"{Items.Count} kayıt ({r.Value.From:dd.MM.yyyy} – {r.Value.To:dd.MM.yyyy})" : res.Message;
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task DeleteRange()
    {
        if (!ServerConfigured) { Status = "Sunucu adresi tanımlı değil."; return; }
        var r = Range(); if (r is null) return;
        if (!await ConfirmService.AskAsync(
                $"{r.Value.From:dd.MM.yyyy} – {r.Value.To:dd.MM.yyyy} arası TÜM sunucu yedekleri kalıcı olarak silinsin mi?\nBu işlem geri alınamaz.",
                "Sunucu Yedeklerini Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        Busy = true;
        try
        {
            var res = await DesktopServices.BackupUpload.DeleteRangeAsync(
                ServerUrl!, ServerToken, _session.CompanyId, r.Value.From, r.Value.To);
            Status = res.Message;
            if (res.Ok) await Load();
        }
        finally { Busy = false; }
    }
}

public sealed class ServerBackupRowVm
{
    public string Machine { get; }
    public string FileName { get; }
    public string DateText { get; }
    public string SizeText { get; }

    public ServerBackupRowVm(ServerBackupItem b)
    {
        Machine = b.Machine;
        FileName = b.FileName;
        DateText = b.Date == DateTimeOffset.MinValue ? "—" : b.Date.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        SizeText = $"{b.SizeBytes / 1024.0 / 1024.0:0.##} MB";
    }
}
