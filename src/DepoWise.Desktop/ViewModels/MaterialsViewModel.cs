using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Desktop.Controls;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Malzemeler ekranı — liste + arama + yeni kayıt. MaterialService üzerine (SQLite).</summary>
public sealed partial class MaterialsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<MaterialRow> Items { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _status;

    // Liste durumları (Faz 7a — boş/hata; yükleme senkron olduğundan kalıcı değil)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;

    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    // Yeni kayıt formu görünürlüğü + alanları
    [ObservableProperty] private bool _showAdd;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    private string _newCode = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private string _newName = "";

    [ObservableProperty] private decimal _newUnitPrice;
    [ObservableProperty] private decimal _newMinStock;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CodeError))]
    [NotifyPropertyChangedFor(nameof(HasCodeError))]
    [NotifyPropertyChangedFor(nameof(NameError))]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private bool _triedSave;

    // Alan-bazlı doğrulama (mevcut iş kuralının görsel yansıması: kod+ad zorunlu)
    public string? CodeError => TriedSave && string.IsNullOrWhiteSpace(NewCode) ? "Kod zorunlu." : null;
    public bool HasCodeError => CodeError != null;
    public string? NameError => TriedSave && string.IsNullOrWhiteSpace(NewName) ? "Ad zorunlu." : null;
    public bool HasNameError => NameError != null;

    public bool CanWrite => AccessControl.Can(_session, "materials", PermissionAction.Create);
    public string? AddButtonText => CanWrite ? "Yeni Malzeme" : null;

    public MaterialsViewModel(SessionContext session)
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
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 200 },
                string.IsNullOrWhiteSpace(Search) ? null : Search.Trim());
            foreach (var m in page.Items)
            {
                var stock = DesktopServices.OpeningStock.GetBalance(_session, m.Id);
                Items.Add(new MaterialRow(m.Code, m.Name, m.Type, m.UnitPrice, m.Currency, m.MinStock, stock));
            }
            Status = $"{Items.Count} kayıt";
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            Status = "Hata: " + ex.Message;
        }
        NotifyListState();
    }

    [RelayCommand]
    private void Add()
    {
        TriedSave = true;
        if (!CanWrite) { Status = "Yetki yok."; return; }
        if (string.IsNullOrWhiteSpace(NewCode) || string.IsNullOrWhiteSpace(NewName))
        {
            Status = "Kod ve ad zorunlu."; return;
        }
        try
        {
            DesktopServices.Materials.Create(_session, new NewMaterial(
                Code: NewCode.Trim(), Name: NewName.Trim(),
                UnitPrice: NewUnitPrice, MinStock: NewMinStock, Currency: "TRY"));
            NewCode = ""; NewName = ""; NewUnitPrice = 0; NewMinStock = 0;
            TriedSave = false; ShowAdd = false;
            Load();
            Status = "Malzeme eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }

    /// <summary>Yeni kayıt formunu aç/kapat (sunum durumu).</summary>
    [RelayCommand]
    private void ToggleAdd()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        ShowAdd = !ShowAdd;
    }

    /// <summary>İptal — form alanlarını temizler ve kapatır (sunum durumu; iş mantığı yok).</summary>
    [RelayCommand]
    private void Clear()
    {
        NewCode = ""; NewName = ""; NewUnitPrice = 0; NewMinStock = 0;
        TriedSave = false; ShowAdd = false;
    }

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRows));
    }
}

public sealed record MaterialRow(string Code, string Name, string? Type, decimal UnitPrice, string Currency, decimal MinStock, decimal Stock)
{
    // Sunum türevleri (mevcut veriden hesap; iş mantığı değişmez)
    public bool IsLowStock => Stock <= MinStock;
    public string StockText => IsLowStock ? "Düşük" : "Yeterli";
    public BadgeKind StockKind => IsLowStock ? BadgeKind.Warning : BadgeKind.Success;
    public string TypeDisplay => string.IsNullOrWhiteSpace(Type) ? "—" : Type!;
}
