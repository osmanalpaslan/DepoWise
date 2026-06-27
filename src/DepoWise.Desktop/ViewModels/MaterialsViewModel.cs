using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Malzemeler ekranı — liste + arama + yeni kayıt. MaterialService üzerine (SQLite).</summary>
public sealed partial class MaterialsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<MaterialRow> Items { get; } = new();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _status;

    // Yeni kayıt alanları
    [ObservableProperty] private string _newCode = "";
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private decimal _newUnitPrice;
    [ObservableProperty] private decimal _newMinStock;

    public bool CanWrite => AccessControl.Can(_session, "materials", PermissionAction.Create);

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
        catch (Exception ex) { Status = "Hata: " + ex.Message; }
    }

    [RelayCommand]
    private void Add()
    {
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
            Load();
            Status = "Malzeme eklendi.";
        }
        catch (Exception ex) { Status = "Eklenemedi: " + ex.Message; }
    }
}

public sealed record MaterialRow(string Code, string Name, string? Type, decimal UnitPrice, string Currency, decimal MinStock, decimal Stock);
