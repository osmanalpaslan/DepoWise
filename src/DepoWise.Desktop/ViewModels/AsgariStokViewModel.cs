using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Seçim kutusu satırı (LookupBox varsayılan olarak Name gösterir).</summary>
public sealed record AsgariSecim(string? Id, string Name);

/// <summary>Öneri satırı: seçim + kullanıcının değiştirebileceği talep miktarı (öneri yalnız başlangıç değeri).</summary>
public sealed partial class AsgariOneriSatiri : ObservableObject
{
    public AsgariOneri Oneri { get; }
    [ObservableProperty] private bool _secili;
    [ObservableProperty] private decimal _talepMiktari;
    public AsgariOneriSatiri(AsgariOneri o) { Oneri = o; _talepMiktari = o.Onerilen; }
    public string MalzemeMetni => $"{Oneri.Code} — {Oneri.Name}";
    public string MevcutMetni => $"{Oneri.Mevcut:0.##} {Oneri.Unit}".Trim();
    public string AsgariMetni => $"{Oneri.Asgari:0.##}";
    public string OnerilenMetni => $"{Oneri.Onerilen:0.##}";
}

/// <summary>
/// ⭐ M-31 (AR-GE 2026-10-26, F-114) — ASGARİ STOK ÖNERİLERİ (web: /stock/min-suggestions, aynı servis).
/// Depo seçilir → asgarinin altına düşen malzemeler; kullanıcı seçip miktarı düzeltir, TEK depo için talep oluşturur (sistem sipariş
/// vermez). Alt panel: depoya özel asgari tanımla / kaldır (yoksa malzeme kartının genel asgarisi geçerli).
/// </summary>
public sealed partial class AsgariStokViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<AsgariSecim> Depolar { get; } = new();
    public ObservableCollection<AsgariSecim> Malzemeler { get; } = new();
    public ObservableCollection<AsgariOneriSatiri> Items { get; } = new();
    public ObservableCollection<DepoAsgari> Tanimlar { get; } = new();

    [ObservableProperty] private AsgariSecim? _seciliDepo;
    [ObservableProperty] private AsgariSecim? _tanimMalzeme;
    [ObservableProperty] private AsgariSecim? _tanimDepo;
    [ObservableProperty] private decimal _tanimAsgari;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _loadError;
    [ObservableProperty] private bool _isExporting;
    public bool HasError => LoadError != null;
    public bool HasRows => Items.Count > 0;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool CanWrite => AccessControl.Can(_session, "materials", PermissionAction.Edit);
    public bool CanRequest => AccessControl.Can(_session, "requests", PermissionAction.Create);

    public AsgariStokViewModel(SessionContext session)
    {
        _session = session;
        try
        {
            if (string.IsNullOrEmpty(session.OperatingBranchId)) Depolar.Add(new AsgariSecim(null, "Tüm depolarım"));
            foreach (var b in DesktopServices.Branches.List(session).Where(b => BranchAccess.CanAccess(session, b.Id)))
                Depolar.Add(new AsgariSecim(b.Id, b.Name));
            foreach (var m in DesktopServices.Materials.SearchGridAll(session, new MaterialGridFilter(), "code", false, false))
                Malzemeler.Add(new AsgariSecim(m.Id, $"{m.Code} — {m.Name}"));
        }
        catch (Exception ex) { LoadError = ex.Message; }
        SeciliDepo = Depolar.FirstOrDefault(d => d.Id == session.OperatingBranchId) ?? Depolar.FirstOrDefault();
        TanimDepo = Depolar.FirstOrDefault(d => d.Id is not null && (session.OperatingBranchId is null || d.Id == session.OperatingBranchId));
        Load();
    }

    partial void OnSeciliDepoChanged(AsgariSecim? value) => Load();
    partial void OnTanimMalzemeChanged(AsgariSecim? value) => TanimlariYukle();

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var o in DesktopServices.AsgariStok.Oneriler(_session, SeciliDepo?.Id)) Items.Add(new AsgariOneriSatiri(o));
            Status = Items.Count == 0 ? "Asgarinin altında malzeme yok." : $"{Items.Count} öneri — talep için seçip miktarı gözden geçirin.";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    [RelayCommand]
    private async Task TalepOlustur()
    {
        var secilen = Items.Where(x => x.Secili).ToList();
        if (secilen.Count == 0) { Status = "Talep için en az bir öneri seçin."; return; }
        var depolar = secilen.Select(x => x.Oneri.LocationId).Distinct().ToList();
        if (depolar.Count > 1) { Status = "Talep tek depo için oluşturulur — aynı depodaki önerileri seçin (ya da üstten depo seçin)."; return; }
        if (secilen.Any(x => x.TalepMiktari <= 0)) { Status = "Talep miktarı sıfırdan büyük olmalı."; return; }
        if (!await ConfirmService.AskAsync($"{secilen[0].Oneri.LocationName} için {secilen.Count} malzemelik talep oluşturulsun mu?\n\n" +
                string.Join("\n", secilen.Select(x => $"• {x.MalzemeMetni}: {x.TalepMiktari:0.##}")), "Talep Oluştur", "Evet, Oluştur")) return;
        try
        {
            var h = DesktopServices.AsgariStok.TalepOlustur(_session, depolar[0],
                secilen.Select(x => new RequestItemInput(x.Oneri.MaterialId, x.TalepMiktari)).ToList());
            Status = $"Talep oluşturuldu: {h.DocNo} (Talepler ekranından onaya gönderebilirsiniz).";
            foreach (var x in secilen) x.Secili = false;
        }
        catch (Exception ex) { Status = "Talep oluşturulamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!AccessControl.Can(_session, "export", PermissionAction.View)) { Status = "Dışa aktarım (export) yetkiniz yok."; return; }
        if (IsExporting) return;
        IsExporting = true;
        try
        {
            var path = await FilePickerService.SaveExcelAsync("Asgari Stok Onerileri.xlsx");
            if (path is null) return;
            var bytes = DesktopServices.Excel.Export(AsgariStokService.ToTableModel(Items.Select(x => x.Oneri).ToList()));
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            Status = $"Excel'e aktarıldı ({Items.Count} satır).";
        }
        catch (Exception ex) { Status = "Aktarılamadı: " + ex.Message; }
        finally { IsExporting = false; }
    }

    private void TanimlariYukle()
    {
        Tanimlar.Clear();
        if (TanimMalzeme?.Id is null) return;
        try { foreach (var t in DesktopServices.AsgariStok.Liste(_session, TanimMalzeme.Id)) Tanimlar.Add(t); }
        catch (Exception ex) { Status = "Tanımlar okunamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task TanimKaydet()
    {
        if (TanimMalzeme?.Id is null || TanimDepo?.Id is null) { Status = "Malzeme ve depo seçin."; return; }
        if (TanimAsgari <= 0) { Status = "Asgari miktar sıfırdan büyük olmalı (kaldırmak için 'Depoya Özeli Kaldır')."; return; }
        if (!await ConfirmService.AskAsync($"{TanimMalzeme.Name} için {TanimDepo.Name} deposunda asgari stok {TanimAsgari:0.##} olsun mu?", "Depo Asgarisi", "Evet, Kaydet")) return;
        Yaz(TanimAsgari, "kaydedildi");
    }

    [RelayCommand]
    private async Task TanimKaldir()
    {
        if (TanimMalzeme?.Id is null || TanimDepo?.Id is null) { Status = "Malzeme ve depo seçin."; return; }
        if (!await ConfirmService.AskAsync($"{TanimMalzeme.Name} için {TanimDepo.Name} deposuna özel asgari kaldırılsın mı? (malzeme kartının genel asgarisi geçerli olur)", "Depo Asgarisi", "Evet, Kaldır")) return;
        Yaz(null, "kaldırıldı");
    }

    private void Yaz(decimal? deger, string ne)
    {
        try
        {
            DesktopServices.AsgariStok.Ayarla(_session, TanimMalzeme!.Id!, TanimDepo!.Id!, deger);
            Status = $"Depoya özel asgari {ne}.";
            TanimlariYukle();
            Load();
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }
}
