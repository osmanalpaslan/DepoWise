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

    // ⭐ FAZ 4.5 (kullanıcı isteği 2026-09-06): "+" ile eklenebilen HER tanım "Tanımlar" ekranında da
    // yönetilebilmeli. Bazı tanımlar (ör. personel unvanı) ortak LookupService'te DEĞİL, kendi
    // servisindedir. Bu üç işlem dışarıdan verilebilir; verilmezse davranış eskisiyle BİREBİR aynıdır.
    private readonly Action<SessionContext, string>? _delete;      // (oturum, id)
    private readonly Action<SessionContext, string, string>? _rename;   // (oturum, id, yeniAd)
    private readonly bool _kilitDestekli;

    /// <summary>Kilit (sabit tanım) bu bölümde destekleniyor mu — desteklenmiyorsa düğme çizilmez.</summary>
    public bool SupportsLock => _kilitDestekli;

    public string Title { get; }
    public ObservableCollection<LookupRowViewModel> Items { get; } = new();
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string? _error;

    public bool CanWrite => AccessControl.Can(_s, "definitions", PermissionAction.Create);
    public bool CanDelete => AccessControl.Can(_s, "definitions", PermissionAction.Delete);
    public bool CanEdit => AccessControl.Can(_s, "definitions", PermissionAction.Edit);
    /// <summary>Kilit aç/kapa (sabit tanım) yalnız admin (kullanıcı isteği 2026-07-19).</summary>
    public bool CanToggleLock => AccessControl.IsAdmin(_s) && _kilitDestekli;

    public LookupSectionViewModel(SessionContext s, string title, string table,
        Func<SessionContext, IReadOnlyList<LookupItem>> load, Action<SessionContext, string> add,
        Action<SessionContext, string>? delete = null,
        Action<SessionContext, string, string>? rename = null,
        bool kilitDestekli = true)
    {
        _s = s; Title = title; _table = table; _load = load; _add = add;
        _delete = delete; _rename = rename; _kilitDestekli = kilitDestekli;
        Reload();
    }

    private void Reload()
    {
        Items.Clear();
        try { foreach (var i in _load(_s)) Items.Add(new LookupRowViewModel(i.Id, i.Name, i.IsLocked)); }
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
        try { if (_delete is not null) _delete(_s, item.Id); else DesktopServices.Lookups.Delete(_s, _table, item.Id); Reload(); }
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
        try { if (_rename is not null) _rename(_s, item.Id, newName); else DesktopServices.Lookups.Rename(_s, _table, item.Id, newName); Reload(); }
        catch (Exception ex) { Error = ex.Message; item.Name = item.OriginalName; }
    }

    /// <summary>Kilit aç/kapa (sabit tanım — kullanıcı isteği 2026-07-19). Yalnız admin.</summary>
    [RelayCommand]
    private void ToggleLock(LookupRowViewModel? item)
    {
        if (item is null) return;
        Error = null;
        try { if (!_kilitDestekli) return; DesktopServices.Lookups.SetLocked(_s, _table, item.Id, !item.IsLocked); Reload(); }
        catch (Exception ex) { Error = ex.Message; }
    }
}
