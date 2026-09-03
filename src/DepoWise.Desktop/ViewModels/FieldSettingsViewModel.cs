using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Alan Ayarları satırı — kutu değişince ANINDA kaydedilir (tek satırlık ayar; toplu form yok).</summary>
public sealed partial class FieldSettingRow : ObservableObject
{
    private readonly FieldSettingsViewModel _sahip;
    public string ScreenKey { get; }
    public string FieldKey { get; }
    public string Label { get; }
    public bool SystemRequired { get; }
    public string StatusText { get; }

    [ObservableProperty] private bool _required;

    public FieldSettingRow(FieldSettingsViewModel sahip, FieldRequirementRow r)
    {
        _sahip = sahip;
        ScreenKey = r.ScreenKey; FieldKey = r.FieldKey; Label = r.Label;
        SystemRequired = r.SystemRequired; StatusText = r.StatusText;
        _required = r.Required;
    }

    partial void OnRequiredChanged(bool value) => _sahip.Kaydet(this, value);
}

/// <summary>Ekran grubu (Araçlar / Malzemeler / …) — kategorize liste (kullanıcı isteği).</summary>
public sealed class FieldSettingGroup
{
    public string Title { get; }
    public ObservableCollection<FieldSettingRow> Items { get; } = new();
    public FieldSettingGroup(string title) { Title = title; }
}

/// <summary>
/// ═══ ALAN AYARLARI (kullanıcı isteği 2026-09-03) ═══
///
/// Firma yöneticisi, formlardaki OPSİYONEL alanları firma bazında ZORUNLU yapar (veya geri alır).
/// Sistem zorunluları kilitli gösterilir. Ayar sunucu otoritelidir; masaüstüne tanım senkronuyla
/// iner ve formlar kayıt sırasında <see cref="FieldRequirementService.RequiredFieldsFor"/> ile uygular.
/// Yeni ekran/alan eklendiğinde FieldCatalog'a satır eklenir → burada kendiliğinden görünür (kalıcı kural).
/// </summary>
public sealed partial class FieldSettingsViewModel : ViewModelBase
{
    private readonly SessionContext _session;
    private bool _yukleniyor;

    public ObservableCollection<FieldSettingGroup> Groups { get; } = new();
    [ObservableProperty] private string? _status;

    public bool CanEdit => AccessControl.Can(_session, FieldRequirementService.Module, PermissionAction.Edit);

    public FieldSettingsViewModel(SessionContext session)
    {
        _session = session;
        Load();
    }

    [RelayCommand]
    private void Load()
    {
        _yukleniyor = true;
        try
        {
            Groups.Clear();
            foreach (var ekran in DesktopServices.FieldRequirements.List(_session).GroupBy(r => r.ScreenLabel))
            {
                var grup = new FieldSettingGroup(ekran.Key);
                foreach (var r in ekran) grup.Items.Add(new FieldSettingRow(this, r));
                Groups.Add(grup);
            }
            Status = "Zorunlu yaptığınız alanlar bu firmadaki TÜM kullanıcılar için geçerli olur; diğer firmalar etkilenmez.";
        }
        catch (Exception ex) { Status = "Yüklenemedi: " + ex.Message; }
        finally { _yukleniyor = false; }
    }

    /// <summary>Kutudaki değişikliği anında yazar; hata olursa kutuyu GERİ alır (sessiz tutarsızlık yok).</summary>
    internal void Kaydet(FieldSettingRow satir, bool value)
    {
        if (_yukleniyor) return;
        try
        {
            DesktopServices.FieldRequirements.Set(_session, satir.ScreenKey, satir.FieldKey, value);
            Status = $"«{satir.Label}» {(value ? "ZORUNLU yapıldı" : "opsiyonele döndürüldü")}.";
        }
        catch (Exception ex)
        {
            Status = "Kaydedilemedi: " + ex.Message;
            _yukleniyor = true;             // geri alma ikinci bir kaydetme tetiklemesin
            satir.Required = !value;
            _yukleniyor = false;
        }
    }
}
