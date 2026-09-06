using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ═══ MLY-01 (ADR-168, 2026-08-28) — MALİYET MERKEZLERİ (masaüstü) ═══
/// Tek ekran: tanım CRUD + tarih aralıklı maliyet özeti (Sorgula ile — ağır rapor kuralı).
/// YEREL çalışır; tanımlar senkronla taşınır. Özet mevcut hesapları DEĞİŞTİRMEZ (yalnız okur).
/// </summary>
public sealed partial class CostCentersViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, CostCenterService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, CostCenterService.Module, PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, CostCenterService.Module, PermissionAction.Delete);
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);

    public ObservableCollection<CostCenterRow> Items { get; } = new();
    public ObservableCollection<CostCenterSummaryRow> Summary { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new() { "Aktif", "Pasif" };

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private DateTimeOffset? _fromDate = new DateTimeOffset(DateTime.Today.AddDays(-30));
    [ObservableProperty] private DateTimeOffset? _toDate = new DateTimeOffset(DateTime.Today);
    [ObservableProperty] private bool _summaryLoaded;
    public bool SummaryEmpty => SummaryLoaded && Summary.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private CostCenterRow? _selected;
    public bool HasSelection => Selected != null;

    // ── Form ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formCode = "";
    [ObservableProperty] private string _formName = "";
    [ObservableProperty] private string _formStatus = "Aktif";
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ MALİYET MERKEZİ" : "MALİYET MERKEZİ DÜZENLE";
    private long? _editVersion;

    public CostCentersViewModel(SessionContext session)
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
            foreach (var x in DesktopServices.CostCenters.List(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText))
                Items.Add(x);
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    private (long From, long To) Aralik()
    {
        long Ms(DateTimeOffset? d, bool son) => new DateTimeOffset(DateTime.SpecifyKind(
            (d?.Date ?? DateTime.Today).AddDays(son ? 1 : 0).AddMilliseconds(son ? -1 : 0), DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    // NOT (ADR-184): burada gün SONU hesabı da var; ortak kaynak karşılığı IsGunuTarihi.Ms / GunSonuMs'tir.
        return (Ms(FromDate, false), Ms(ToDate, true));
    }

    /// <summary>Ağır rapor kuralı: özet yalnız Sorgula ile hesaplanır.</summary>
    [RelayCommand]
    private void LoadSummary()
    {
        try
        {
            Summary.Clear();
            var (f, t) = Aralik();
            foreach (var x in DesktopServices.CostCenters.Summary(_session, f, t)) Summary.Add(x);
            SummaryLoaded = true;
            OnPropertyChanged(nameof(SummaryEmpty));
            Status = Summary.Count == 0 ? "Bu aralıkta merkeze bağlı maliyet kaydı yok." : $"{Summary.Count} özet satırı.";
        }
        catch (Exception ex) { Status = "Özet hesaplanamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        if (!SummaryLoaded || Summary.Count == 0) { Status = "Önce Sorgula ile özet hesaplayın."; return; }
        try
        {
            var hedef = await FilePickerService.SaveExcelAsync("MaliyetMerkezi.xlsx");
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(CostCenterService.SummaryTable(Summary.ToList())));
            Status = $"Excel kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Excel aktarılamadı: " + ex.Message; }
    }

    /// <summary>
    /// ⭐ YAZDIR (PDF) — kullanıcı isteği 2026-09-06.
    ///
    /// Excel çıktısıyla AYNI TableModel kullanılır: iki çıktı asla ayrışmaz (aynı kolonlar,
    /// aynı satırlar, aynı sıra ve aynı filtre kümesi). Sayfa başlığına firma, şube, kullanıcı
    /// ve tarih; sayısal kolonlara toplam satırı otomatik eklenir (bkz. TablePdfService).
    /// </summary>
    [RelayCommand]
    private async Task Yazdir()
    {
        if (!CanExport) { Status = "Yazdırma yetkiniz yok (dışa aktarım yetkisi gerekir)."; return; }
        if (!SummaryLoaded || Summary.Count == 0) { Status = "Önce Sorgula ile özet hesaplayın."; return; }
        try
        {
            var hedef = await YazdirmaYardimcisi.YazdirAsync(CostCenterService.SummaryTable(Summary.ToList()), _session);
            if (hedef is null) return;
            Status = $"PDF kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Yazdırılamadı: " + ex.Message; }
    }

    [RelayCommand]
    private void NewCenter()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; _editVersion = null;
        FormCode = ""; FormName = ""; FormStatus = "Aktif"; FormDescription = ""; FormError = null;
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task BeginEdit()
    {
        if (Selected is null) { Status = "Maliyet merkezi seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        // ⭐ FAZ 4.2: standart düzenleme onayı (kullanıcı isteği 2026-09-06).
        if (!await ConfirmService.ConfirmEditAsync()) return;
        EditId = Selected.Id; _editVersion = Selected.Version;
        FormCode = Selected.Code ?? ""; FormName = Selected.Name;
        FormStatus = Selected.Status == "passive" ? "Pasif" : "Aktif";
        FormDescription = Selected.Description ?? "";
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; _editVersion = null; }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormName)) { FormError = "Ad zorunlu."; return; }
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Maliyet merkezi güncellensin mi?" : "Maliyet merkezi oluşturulsun mu?", "Kaydet")) return;
        try
        {
            var dto = new NewCostCenter(FormName.Trim(), FormCode, FormStatus == "Pasif" ? "passive" : "active", FormDescription);
            if (editing) DesktopServices.CostCenters.Update(_session, EditId!, dto, _editVersion);
            else DesktopServices.CostCenters.Create(_session, dto);
            ShowAdd = false; EditId = null; _editVersion = null;
            Load();
            Status = editing ? "Güncellendi." : "Oluşturuldu.";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null) { Status = "Maliyet merkezi seçin."; return; }
        if (!CanDelete) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{Selected.Name}' silinsin mi?\n\nÇöp Kutusu'ndan geri alınabilir; bağlı kayıtların maliyeti özetten düşer.",
                "Maliyet Merkezi Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.CostCenters.Delete(_session, Selected.Id);
            Load();
            Status = "Silindi (Çöp Kutusu'ndan geri alınabilir).";
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }
}
