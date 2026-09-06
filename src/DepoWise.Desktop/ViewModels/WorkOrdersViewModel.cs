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
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.WorkOrders;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Durum geçiş düğmesi (detay panelinde matristen üretilir).</summary>
public sealed record WoStatusOption(string Code, string Label);

/// <summary>
/// ═══ EMR-01 (ADR-170, 2026-08-28) — İŞ EMİRLERİ (masaüstü) ═══
/// YEREL çalışır (çevrimdışı dahil); senkron taşır. Tüketim MEVCUT stok çıkışıyla (serviste, idempotent).
/// PK-F2: kapanan iş emri değiştirilemez — panel salt-okunura düşer.
/// </summary>
public sealed partial class WorkOrdersViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, WorkOrderService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, WorkOrderService.Module, PermissionAction.Edit);
    public bool CanCancel => AccessControl.Can(_session, WorkOrderService.Module, PermissionAction.Delete);
    public bool CanConsume => CanEdit && AccessControl.Can(_session, "stock", PermissionAction.Create);
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);
    public bool CanPickCostCenter => AccessControl.Can(_session, "cost_centers", PermissionAction.Edit);

    public ObservableCollection<WorkOrderRow> Items { get; } = new();
    public ObservableCollection<WorkOrderAssignmentRow> Assignments { get; } = new();
    public ObservableCollection<WorkOrderLinkRow> Links { get; } = new();
    public ObservableCollection<WorkOrderHistoryRow> HistoryRows { get; } = new();
    public ObservableCollection<DepoWise.Infrastructure.Accounting.CostCenterSummaryRow> Cost { get; } = new();
    public ObservableCollection<WoStatusOption> NextStatusOptions { get; } = new();

    public ObservableCollection<ProjectPick> BranchOptions { get; } = new();
    public ObservableCollection<ProjectPick> PersonnelOptions { get; } = new();
    public ObservableCollection<ProjectPick> CostCenterOptions { get; } = new();
    public ObservableCollection<ProjectPick> MaterialOptions { get; } = new();
    public ObservableCollection<ProjectPick> VehicleOptions { get; } = new();
    public ObservableCollection<ProjectPick> EquipmentOptions { get; } = new();
    public ObservableCollection<ProjectPick> AssignTargetOptions { get; } = new();
    public ObservableCollection<string> PriorityOptions { get; } = new() { "Normal", "Yüksek", "Acil", "Kritik" };
    public ObservableCollection<string> AssignTypeOptions { get; } = new() { "Personel", "Araç", "Ekipman" };
    public ObservableCollection<string> FilterStatusOptions { get; } = new()
    { "Tümü", "Taslak", "Atandı", "Devam Ediyor", "Beklemede", "Tamamlandı", "İptal" };

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
    [NotifyPropertyChangedFor(nameof(SelectedOpen))]
    private WorkOrderRow? _selected;
    public bool HasSelection => Selected != null;
    public bool SelectedOpen => Selected is { } w && w.Status is not ("completed" or "cancelled");
    public bool CanConsumeNow => CanConsume && SelectedOpen;
    public bool CanAssignNow => CanEdit && SelectedOpen;
    partial void OnSelectedChanged(WorkOrderRow? value)
    {
        LoadDetail();
        OnPropertyChanged(nameof(CanConsumeNow));
        OnPropertyChanged(nameof(CanAssignNow));
    }

    // ── Form ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formWoNo = "";
    [ObservableProperty] private string _formTitle2 = "";
    [ObservableProperty] private string _formDescription = "";
    [ObservableProperty] private string _formPriority = "Normal";
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private ProjectPick? _formAssignee;
    [ObservableProperty] private ProjectPick? _formCostCenter;
    [ObservableProperty] private DateTimeOffset? _formPlannedStart;
    [ObservableProperty] private DateTimeOffset? _formPlannedEnd;
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ İŞ EMRİ" : "İŞ EMRİ DÜZENLE";
    private long? _editVersion;

    // ── Detay panel girişleri ──
    [ObservableProperty] private string _assignType = "Personel";
    [ObservableProperty] private ProjectPick? _assignTarget;
    [ObservableProperty] private WorkOrderAssignmentRow? _selectedAssignment;
    [ObservableProperty] private ProjectPick? _consumeMaterial;
    [ObservableProperty] private decimal _consumeQty = 1m;
    partial void OnAssignTypeChanged(string value)
    {
        AssignTarget = null;
        AssignTargetOptions.Clear();
        var kaynak = value switch { "Araç" => VehicleOptions, "Ekipman" => EquipmentOptions, _ => PersonnelOptions };
        foreach (var o in kaynak) AssignTargetOptions.Add(o);
    }

    public WorkOrdersViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        OnAssignTypeChanged("Personel");
        Load();
    }

    private void LoadOptions()
    {
        void Doldur(ObservableCollection<ProjectPick> hedef, Func<IEnumerable<(string Id, string Name)>> kaynak)
        {
            try { hedef.Clear(); foreach (var (id, name) in kaynak()) hedef.Add(new ProjectPick(id, name)); }
            catch { }
        }
        Doldur(BranchOptions, () => DesktopServices.Branches.List(_session).Select(b => (b.Id, b.Name)));
        Doldur(PersonnelOptions, () => DesktopServices.Personnel.AllNameToId(_session)
            .OrderBy(x => x.Key, StringComparer.CurrentCulture).Select(x => (x.Value, x.Key)));
        Doldur(CostCenterOptions, () => DesktopServices.CostCenters.Options(_session));
        Doldur(MaterialOptions, () => DesktopServices.Materials
            .List(_session, new PageRequest { Limit = 1000 }).Items.Select(m => (m.Id, $"{m.Code} — {m.Name}")));
        Doldur(VehicleOptions, () => DesktopServices.Vehicles.List(_session).Select(v => (v.Id, v.InternalCode)));
        Doldur(EquipmentOptions, () => DesktopServices.Equipment.List(_session).Select(e => (e.Id, $"{e.Code} — {e.Name}")));
    }

    private static string StatusCode(string display) => display switch
    {
        "Taslak" => "draft", "Atandı" => "assigned", "Devam Ediyor" => "in_progress",
        "Beklemede" => "on_hold", "Tamamlandı" => "completed", "İptal" => "cancelled", _ => "",
    };
    private static string PriorityCode(string display) => display switch
    { "Yüksek" => "high", "Acil" => "urgent", "Kritik" => "critical", _ => "normal" };

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            var st = FilterStatus == "Tümü" ? null : StatusCode(FilterStatus);
            foreach (var w in DesktopServices.WorkOrders.List(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText, st))
                Items.Add(w);
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    private void LoadDetail()
    {
        Assignments.Clear(); Links.Clear(); HistoryRows.Clear(); Cost.Clear(); NextStatusOptions.Clear();
        if (Selected is null) return;
        try
        {
            foreach (var a in DesktopServices.WorkOrders.Assignments(_session, Selected.Id)) Assignments.Add(a);
            foreach (var l in DesktopServices.WorkOrders.Links(_session, Selected.Id)) Links.Add(l);
            foreach (var h in DesktopServices.WorkOrders.History(_session, Selected.Id)) HistoryRows.Add(h);
            foreach (var c in DesktopServices.WorkOrders.CostSummary(_session, Selected.Id)) Cost.Add(c);
            foreach (var ns in WorkOrderService.NextStates(Selected.Status))
                if (ns == "cancelled" ? CanCancel : CanEdit)
                    NextStatusOptions.Add(new WoStatusOption(ns,
                        ns == "completed" ? "Tamamla" : ns == "cancelled" ? "İptal Et" : WorkOrderService.StatusLabel(ns)));
        }
        catch { }
    }

    [RelayCommand]
    private void NewOrder()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; _editVersion = null;
        FormWoNo = ""; FormTitle2 = ""; FormDescription = ""; FormPriority = "Normal";
        FormBranch = null; FormAssignee = null; FormCostCenter = null;
        FormPlannedStart = null; FormPlannedEnd = null; FormError = null;
        LoadOptions();
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task BeginEdit()
    {
        if (Selected is null) { Status = "İş emri seçin."; return; }
        if (!CanEdit) { Status = "Yetki yok."; return; }
        if (!SelectedOpen) { Status = "Kapanmış iş emri düzenlenemez — yeni iş emri açın (PK-F2)."; return; }
        // ⭐ FAZ 4.2: standart düzenleme onayı (kullanıcı isteği 2026-09-06).
        if (!await ConfirmService.ConfirmEditAsync()) return;
        EditId = Selected.Id; _editVersion = Selected.Version;
        FormWoNo = Selected.WoNo; FormTitle2 = Selected.Title; FormDescription = Selected.Description ?? "";
        FormPriority = WorkOrderService.PriorityLabel(Selected.Priority);
        FormBranch = BranchOptions.FirstOrDefault(b => b.Id == Selected.BranchId);
        FormAssignee = PersonnelOptions.FirstOrDefault(p => p.Id == Selected.AssigneePersonnelId);
        FormCostCenter = CostCenterOptions.FirstOrDefault(c => c.Id == Selected.CostCenterId);
        FormPlannedStart = Selected.PlannedStart is { } a ? DateTimeOffset.FromUnixTimeMilliseconds(a) : null;
        FormPlannedEnd = Selected.PlannedEnd is { } b ? DateTimeOffset.FromUnixTimeMilliseconds(b) : null;
        FormError = null; ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private void CancelAdd() { ShowAdd = false; EditId = null; _editVersion = null; }

    private static long? Ms(DateTimeOffset? d) => IsGunuTarihi.Ms(d);   // ADR-184: tek kaynak

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (string.IsNullOrWhiteSpace(FormWoNo) || string.IsNullOrWhiteSpace(FormTitle2))
        { FormError = "No ve başlık zorunlu."; return; }
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "İş emri güncellensin mi?" : "İş emri oluşturulsun mu?", "İş Emri")) return;
        try
        {
            var dto = new NewWorkOrder(FormWoNo.Trim(), FormTitle2.Trim(),
                string.IsNullOrWhiteSpace(FormDescription) ? null : FormDescription.Trim(),
                PriorityCode(FormPriority), FormBranch?.Id, FormCostCenter?.Id, FormAssignee?.Id,
                Ms(FormPlannedStart), Ms(FormPlannedEnd));
            if (editing) DesktopServices.WorkOrders.UpdateMeta(_session, EditId!, dto, _editVersion);
            else DesktopServices.WorkOrders.Create(_session, dto);
            ShowAdd = false; EditId = null; _editVersion = null;
            Load();
            Status = editing ? "Güncellendi." : "İş emri oluşturuldu.";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task SetStatus(WoStatusOption option)
    {
        if (Selected is null) return;
        var uyari = option.Code == "completed" ? "\n\nTamamlanan iş emri YENİDEN AÇILAMAZ (PK-F2)." : "";
        if (!await ConfirmService.AskAsync($"Durum '{option.Label}' yapılsın mı?{uyari}", "Durum Değişikliği",
            "Evet", "Vazgeç", danger: option.Code == "cancelled")) return;
        try
        {
            DesktopServices.WorkOrders.SetStatus(_session, Selected.Id, option.Code);
            var id = Selected.Id;
            Load();
            Selected = Items.FirstOrDefault(x => x.Id == id);
            Status = $"Durum güncellendi: {WorkOrderService.StatusLabel(option.Code)}.";
        }
        catch (Exception ex) { Status = "Durum değiştirilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async Task AddAssignment()
    {
        if (Selected is null || AssignTarget is null) { Status = "Kaynak seçin."; return; }
        var tip = AssignType switch { "Araç" => "vehicle", "Ekipman" => "equipment", _ => "personnel" };
        try
        {
            DesktopServices.WorkOrders.AddAssignment(_session, Selected.Id, tip, AssignTarget.Id);
            AssignTarget = null;
            LoadDetail();
            Status = "Atandı.";
        }
        catch (Exception ex) { Status = "Atanamadı: " + ex.Message; }
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RemoveAssignment()
    {
        if (SelectedAssignment is null) { Status = "Kaldırılacak atamayı seçin."; return; }
        if (!await ConfirmService.AskAsync("Atama kaldırılsın mı?", "Atama", "Evet, Kaldır", "Vazgeç")) return;
        try
        {
            DesktopServices.WorkOrders.RemoveAssignment(_session, SelectedAssignment.Id);
            LoadDetail();
            Status = "Atama kaldırıldı.";
        }
        catch (Exception ex) { Status = "Kaldırılamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Consume()
    {
        if (Selected is null || ConsumeMaterial is null) { Status = "Malzeme seçin."; return; }
        if (ConsumeQty <= 0) { Status = "Miktar pozitif olmalı."; return; }
        if (!await ConfirmService.AskAsync(
                $"{ConsumeQty:0.####} birim tüketim kaydedilsin mi?\n\nŞantiye deposundan STOK ÇIKIŞI oluşur.",
                "Malzeme Tüketimi")) return;
        try
        {
            DesktopServices.WorkOrders.ConsumeMaterial(_session, Selected.Id,
                new[] { new StockLine(ConsumeMaterial.Id, ConsumeQty) },
                "desk-" + Guid.NewGuid().ToString("N"));   // idempotent: retry ikinci çıkış üretmez
            ConsumeMaterial = null; ConsumeQty = 1m;
            LoadDetail();
            Status = "Tüketim kaydedildi — stok çıkışı oluştu.";
        }
        catch (Exception ex) { Status = "Tüketim yapılamadı: " + ex.Message; }
    }

    /// <summary>Liste kuralı 2: filtrelenmiş TÜM liste Excel'e (yerel — çevrimdışı da çalışır).</summary>
    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            var st = FilterStatus == "Tümü" ? null : StatusCode(FilterStatus);
            var rows = DesktopServices.WorkOrders.List(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText, st);
            var hedef = await FilePickerService.SaveExcelAsync("IsEmirleri.xlsx");
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(WorkOrderService.ToTableModel(rows)));
            Status = $"Excel kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Excel aktarılamadı: " + ex.Message; }
    }
}
