using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Sistem Logu — audit_logs salt-okuma. Loglar SİLİNEMEZ (yalnız görüntüleme).</summary>
public sealed partial class AuditLogViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<AuditLogRow> Items { get; } = new();

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;

    public AuditLogViewModel(SessionContext session)
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
            foreach (var a in DesktopServices.Audit.List(_session)) Items.Add(a);
            Status = $"{Items.Count} kayıt (loglar silinemez)";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }
}
