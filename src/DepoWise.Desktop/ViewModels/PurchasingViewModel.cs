using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Purchasing;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Yeni sipariş formu satırı (masaüstü).</summary>
public sealed partial class PurchaseLineVm : ObservableObject
{
    [ObservableProperty] private ProjectPick? _material;
    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _unitPrice;
}

/// <summary>Mal kabul satırı (masaüstü) — kalan miktarla önerilir, kısmi kabul için azaltılır.</summary>
public sealed partial class ReceiveLineVm : ObservableObject
{
    public string LineId { get; init; } = "";
    public string MaterialName { get; init; } = "";
    public decimal Ordered { get; init; }
    public decimal Received { get; init; }
    public decimal Remaining => Ordered - Received;
    [ObservableProperty] private decimal _receiveNow;
    public string Info => $"Sipariş: {Ordered:0.####} · Kabul: {Received:0.####} · Kalan: {Remaining:0.####}";
}

/// <summary>
/// ═══ STN-01 (ADR-169, 2026-08-28) — SATIN ALMA (masaüstü) ═══
/// YEREL çalışır (çevrimdışı dahil): sipariş + mal kabul yerel SQLite'a yazılır, senkron taşır.
/// Mal kabul MEVCUT stok girişini kullanır (serviste, tek transaction, idempotent).
/// </summary>
public sealed partial class PurchasingViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, PurchaseOrderService.Module, PermissionAction.Create);
    public bool CanCancel => AccessControl.Can(_session, PurchaseOrderService.Module, PermissionAction.Delete);
    /// <summary>Mal kabul = sipariş Edit + STOK Create (stok yan kapısı yok — servis de zorlar).</summary>
    public bool CanReceive => AccessControl.Can(_session, PurchaseOrderService.Module, PermissionAction.Edit)
                           && AccessControl.Can(_session, "stock", PermissionAction.Create);
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);
    public bool CanBackDate => DateEntryPolicy.Serbest(_session);
    public bool CanPickCostCenter => AccessControl.Can(_session, "cost_centers", PermissionAction.Edit);

    public ObservableCollection<PurchaseOrderRow> Items { get; } = new();
    public ObservableCollection<PurchaseLineVm> FormLines { get; } = new();
    public ObservableCollection<ReceiveLineVm> DetailLines { get; } = new();
    public ObservableCollection<ProjectPick> SupplierOptions { get; } = new();
    public ObservableCollection<ProjectPick> MaterialOptions { get; } = new();
    public ObservableCollection<ProjectPick> BranchOptions { get; } = new();
    public ObservableCollection<ProjectPick> CostCenterOptions { get; } = new();
    public ObservableCollection<string> FilterStatusOptions { get; } = new() { "Tümü", "Açık", "Tamamlandı", "İptal" };

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterStatus = "Tümü";
    partial void OnFilterStatusChanged(string value) => Load();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private PurchaseOrderRow? _selected;
    public bool HasSelection => Selected != null;
    public bool SelectedOpen => Selected?.Status == "open";
    partial void OnSelectedChanged(PurchaseOrderRow? value)
    {
        LoadDetail();
        OnPropertyChanged(nameof(SelectedOpen));
        OnPropertyChanged(nameof(CanReceiveNow));
    }
    public bool CanReceiveNow => CanReceive && SelectedOpen && DetailLines.Any(l => l.Remaining > 0);

    // ── Form ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string _formOrderNo = "";
    [ObservableProperty] private ProjectPick? _formSupplier;
    [ObservableProperty] private ProjectPick? _formRequest;
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private ProjectPick? _formCostCenter;
    [ObservableProperty] private DateTimeOffset? _formOrderDate = new DateTimeOffset(DateTime.Today);
    [ObservableProperty] private string _formNote = "";
    [ObservableProperty] private string? _formError;
    public ObservableCollection<ProjectPick> RequestOptions { get; } = new();

    public PurchasingViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        Load();
    }

    private void LoadOptions()
    {
        void Doldur(ObservableCollection<ProjectPick> hedef, Func<IEnumerable<(string Id, string Name)>> kaynak)
        {
            try { hedef.Clear(); foreach (var (id, name) in kaynak()) hedef.Add(new ProjectPick(id, name)); }
            catch { }
        }
        Doldur(SupplierOptions, () => DesktopServices.Lookups.List(_session, "suppliers").Select(x => (x.Id, x.Name)));
        Doldur(BranchOptions, () => DesktopServices.Branches.List(_session).Select(b => (b.Id, b.Name)));
        Doldur(CostCenterOptions, () => DesktopServices.CostCenters.Options(_session));
        Doldur(MaterialOptions, () => DesktopServices.Materials
            .List(_session, new PageRequest { Limit = 1000 }).Items.Select(m => (m.Id, $"{m.Code} — {m.Name}")));
        Doldur(RequestOptions, () => DesktopServices.Requests.List(_session, DepoWise.Application.Requests.RequestStatus.Approved, null, 200)
            .Select(r => (r.Id, r.DocNo)));
    }

    private static string StatusCode(string display) => display switch
    { "Açık" => "open", "Tamamlandı" => "closed", "İptal" => "cancelled", _ => "" };

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var st = FilterStatus == "Tümü" ? null : StatusCode(FilterStatus);
            foreach (var o in DesktopServices.Purchasing.List(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText, st))
                Items.Add(o);
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    private void LoadDetail()
    {
        DetailLines.Clear();
        if (Selected is null) return;
        try
        {
            foreach (var l in DesktopServices.Purchasing.Lines(_session, Selected.Id))
                DetailLines.Add(new ReceiveLineVm
                {
                    LineId = l.Id, MaterialName = l.MaterialName,
                    Ordered = l.Quantity, Received = l.ReceivedQty,
                    ReceiveNow = l.RemainingQty,   // varsayılan: kalanın tamamı
                });
        }
        catch { }
        OnPropertyChanged(nameof(CanReceiveNow));
    }

    [RelayCommand]
    private void NewOrder()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        FormOrderNo = ""; FormSupplier = null; FormRequest = null; FormBranch = null; FormCostCenter = null;
        FormOrderDate = new DateTimeOffset(DateTime.Today); FormNote = ""; FormError = null;
        FormLines.Clear(); FormLines.Add(new PurchaseLineVm());
        LoadOptions();
        ShowAdd = true;
    }

    [RelayCommand] private void AddLine() => FormLines.Add(new PurchaseLineVm());
    [RelayCommand] private void RemoveLine(PurchaseLineVm line) { if (FormLines.Count > 1) FormLines.Remove(line); }
    [RelayCommand] private void CancelAdd() => ShowAdd = false;

    /// <summary>Talep seçilince satırlar talepten KOPYALANIR (öneri — düzenlenebilir).</summary>
    partial void OnFormRequestChanged(ProjectPick? value)
    {
        if (value is null) return;
        try
        {
            var items = DesktopServices.Requests.GetItems(_session, value.Id);
            if (items.Count == 0) return;
            var byCode = MaterialOptions.ToDictionary(
                m => m.Name.Split(" — ")[0], m => m, StringComparer.Ordinal);
            FormLines.Clear();
            foreach (var it in items)
                if (byCode.TryGetValue(it.MaterialCode, out var pick))
                    FormLines.Add(new PurchaseLineVm { Material = pick, Quantity = it.Quantity });
            if (FormLines.Count == 0) FormLines.Add(new PurchaseLineVm());
        }
        catch { }
    }

    private long? DocDateMs(DateTimeOffset? d) => IsGunuTarihi.Ms(d);   // ADR-184: tek kaynak

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormOrderNo)) { FormError = "Sipariş no zorunlu."; return; }
        var satirlar = FormLines.Where(l => l.Material is not null && l.Quantity > 0)
            .Select(l => new NewPurchaseOrderLine(l.Material!.Id, l.Quantity,
                l.UnitPrice > 0 ? l.UnitPrice : null)).ToList();
        if (satirlar.Count == 0) { FormError = "En az bir satır girin."; return; }
        if (!await ConfirmService.AskAsync("Sipariş oluşturulsun mu?", "Satın Alma")) return;
        try
        {
            DesktopServices.Purchasing.Create(_session, new NewPurchaseOrder(
                FormOrderNo.Trim(), FormSupplier?.Id, FormRequest?.Id, FormBranch?.Id, FormCostCenter?.Id,
                DocDateMs(FormOrderDate), FormNote, satirlar));
            ShowAdd = false;
            Load();
            Status = "Sipariş oluşturuldu.";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task Receive()
    {
        if (Selected is null) { Status = "Sipariş seçin."; return; }
        var secilen = DetailLines.Where(l => l.ReceiveNow > 0)
            .Select(l => new ReceiveLine(l.LineId, l.ReceiveNow)).ToList();
        if (secilen.Count == 0) { Status = "Kabul miktarı girin."; return; }
        if (!await ConfirmService.AskAsync(
                "Mal kabul kaydedilsin mi?\n\nSeçilen miktarlar teslim deposuna STOK GİRİŞİ olarak işlenir.",
                "Mal Kabul")) return;
        try
        {
            DesktopServices.Purchasing.Receive(_session, Selected.Id, secilen,
                "desk-" + Guid.NewGuid().ToString("N"));   // idempotent: retry ikinci stok girişi üretmez
            Load();
            LoadDetail();
            Status = "Mal kabul kaydedildi — stok girişi oluştu.";
        }
        catch (Exception ex) { Status = "Mal kabul yapılamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Cancel()
    {
        if (Selected is null) { Status = "Sipariş seçin."; return; }
        if (!CanCancel) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                $"'{Selected.OrderNo}' iptal edilsin mi?\n\nYapılmış mal kabulleri stok defterinde kalır.",
                "Sipariş İptali", "Evet, İptal Et", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Purchasing.Cancel(_session, Selected.Id);
            Load();
            Status = "Sipariş iptal edildi.";
        }
        catch (Exception ex) { Status = "İptal edilemedi: " + ex.Message; }
    }

    /// <summary>Liste kuralı 2: filtrelenmiş TÜM sipariş listesi Excel'e (yerel — çevrimdışı da çalışır).</summary>
    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            var st = FilterStatus == "Tümü" ? null : StatusCode(FilterStatus);
            var rows = DesktopServices.Purchasing.List(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText, st);
            var hedef = await FilePickerService.SaveExcelAsync("SatinAlma.xlsx");
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(PurchaseOrderService.ToTableModel(rows)));
            Status = $"Excel kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Excel aktarılamadı: " + ex.Message; }
    }
}
