using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Tanımlar/Ayarlar accordion'unda tek bir lookup bölümü (liste + ekle + sil). Tenant-izole; "definitions" yetkisi.
/// Load/Add delegeleri her tanım türüne göre (kategori/marka/birim...) enjekte edilir; Sil generic (tablo+id).
/// </summary>
public sealed partial class LookupSectionViewModel : ViewModelBase
{
    private readonly SessionContext _s;
    private readonly string _table;
    private readonly Func<SessionContext, IReadOnlyList<LookupItem>> _load;
    private readonly Action<SessionContext, string> _add;

    public string Title { get; }
    public ObservableCollection<LookupRowViewModel> Items { get; } = new();
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string? _error;

    public bool CanWrite => AccessControl.Can(_s, "definitions", PermissionAction.Create);
    public bool CanDelete => AccessControl.Can(_s, "definitions", PermissionAction.Delete);
    public bool CanEdit => AccessControl.Can(_s, "definitions", PermissionAction.Edit);

    public LookupSectionViewModel(SessionContext s, string title, string table,
        Func<SessionContext, IReadOnlyList<LookupItem>> load, Action<SessionContext, string> add)
    {
        _s = s; Title = title; _table = table; _load = load; _add = add;
        Reload();
    }

    private void Reload()
    {
        Items.Clear();
        try { foreach (var i in _load(_s)) Items.Add(new LookupRowViewModel(i.Id, i.Name)); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private void Add()
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(NewName)) { Error = "Ad girin."; return; }
        try { _add(_s, NewName.Trim()); NewName = ""; Reload(); }
        catch (Exception ex) { Error = ex.Message; }
    }

    [RelayCommand]
    private async Task Delete(LookupRowViewModel? item)
    {
        if (item is null) return;
        if (!await ConfirmService.AskAsync($"'{item.OriginalName}' silinsin mi?", "Tanım Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try { DesktopServices.Lookups.Delete(_s, _table, item.Id); Reload(); }
        catch (Exception ex) { Error = ex.Message; }
    }

    /// <summary>Tanımı yeniden adlandır (ID korunur). Kullanıcı isteği 2026-07-18. Başarısızsa eski ada döner.</summary>
    [RelayCommand]
    private void Rename(LookupRowViewModel? item)
    {
        if (item is null) return;
        Error = null;
        var newName = (item.Name ?? "").Trim();
        if (newName == item.OriginalName) return;   // değişmemiş → sessiz geç
        try { DesktopServices.Lookups.Rename(_s, _table, item.Id, newName); Reload(); }
        catch (Exception ex) { Error = ex.Message; item.Name = item.OriginalName; }
    }
}
