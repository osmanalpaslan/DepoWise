using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// YALNIZCA GELİŞTİRME amaçlı bileşen galerisi (Faz 5). Üretim navigasyonuna EKLENMEZ
/// (ShellViewModel.BuildGroups'ta yer almaz). Ortak bileşenlerin canlı referansı + XAML derleme doğrulaması.
/// Sahte veri yalnız demo amaçlıdır; hiçbir iş servisine bağlı değildir.
/// </summary>
public sealed partial class ComponentGalleryViewModel : ViewModelBase
{
    [ObservableProperty] private string? _searchText;

    public ObservableCollection<GalleryRow> Rows { get; } = new()
    {
        new GalleryRow("MTR-001", "Hidrolik Yağ", "Aktif", 42),
        new GalleryRow("MTR-002", "Filtre", "Düşük", 3),
        new GalleryRow("MTR-003", "Conta", "Aktif", 120),
    };

    [RelayCommand] private void DemoAction() { /* demo; iş mantığı yok */ }
}

public sealed record GalleryRow(string Code, string Name, string Status, int Stock);
