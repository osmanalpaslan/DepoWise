using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ═══ ZMT-01 (ADR-167, 2026-08-28) — ZİMMET (masaüstü) ═══
///
/// YEREL çalışır (çevrimdışı dahil — stok gibi): işlemler yerel SQLite'a yazılır, senkron taşır.
/// Malzeme teslim/iadesi stok defterini MEVCUT kapılarla oynatır (aynı transaction, serviste).
/// Tek ekran: işlem formu + "Kimde Ne Var" + seçili varlığın geçmiş zinciri.
/// </summary>
public sealed partial class AssignmentsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, AssignmentService.Module, PermissionAction.Create);
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);
    /// <summary>TRH-01: tarih alanı yetki yoksa bugüne kilitli (servis de normalleştirir).</summary>
    public bool CanBackDate => DepoWise.Application.Security.DateEntryPolicy.Serbest(_session);

    public ObservableCollection<HoldingRow> Items { get; } = new();
    public ObservableCollection<AssignmentMovementRow> History { get; } = new();
    public ObservableCollection<ProjectPick> AssetOptions { get; } = new();
    public ObservableCollection<ProjectPick> PersonnelOptions { get; } = new();
    public ObservableCollection<ProjectPick> BranchOptions { get; } = new();
    public ObservableCollection<string> OpOptions { get; } = new() { "Teslim", "İade", "Hasarlı İade", "Devir", "Kayıp" };
    public ObservableCollection<string> AssetTypeOptions { get; } = new() { "Malzeme", "Ekipman" };

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool IsEmpty => !HasError && Items.Count == 0;
    public bool HasRows => Items.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private HoldingRow? _selected;
    public bool HasSelection => Selected != null;
    partial void OnSelectedChanged(HoldingRow? value) => LoadHistory();

    // ── Form ──
    [ObservableProperty] private string _formOp = "Teslim";
    [ObservableProperty] private string _formAssetType = "Malzeme";
    [ObservableProperty] private ProjectPick? _formAsset;
    [ObservableProperty] private ProjectPick? _formPersonnel;
    [ObservableProperty] private ProjectPick? _formToPersonnel;
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private decimal _formQuantity = 1m;
    [ObservableProperty] private DateTimeOffset? _formDocDate = new DateTimeOffset(DateTime.Today);
    [ObservableProperty] private string _formNote = "";
    [ObservableProperty] private string? _formError;

    public bool IsTransfer => FormOp == "Devir";
    public bool IsMaterial => FormAssetType == "Malzeme";
    partial void OnFormOpChanged(string value) => OnPropertyChanged(nameof(IsTransfer));
    partial void OnFormAssetTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsMaterial));
        if (!IsMaterial) FormQuantity = 1m;
        LoadAssetOptions();
    }

    public AssignmentsViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        LoadAssetOptions();
        Load();
    }

    private void LoadOptions()
    {
        try
        {
            PersonnelOptions.Clear();
            foreach (var (name, id) in DesktopServices.Personnel.AllNameToId(_session).OrderBy(x => x.Key, StringComparer.CurrentCulture))
                PersonnelOptions.Add(new ProjectPick(id, name));
        }
        catch { }
        try
        {
            BranchOptions.Clear();
            foreach (var b in DesktopServices.Branches.List(_session)) BranchOptions.Add(new ProjectPick(b.Id, b.Name));
        }
        catch { }
    }

    private void LoadAssetOptions()
    {
        AssetOptions.Clear();
        FormAsset = null;
        try
        {
            if (IsMaterial)
                foreach (var m in DesktopServices.Materials.List(_session,
                    new DepoWise.Application.Common.PageRequest { Limit = 1000 }).Items)
                    AssetOptions.Add(new ProjectPick(m.Id, $"{m.Code} — {m.Name}"));
            else
                foreach (var e in DesktopServices.Equipment.List(_session))
                    AssetOptions.Add(new ProjectPick(e.Id, $"{e.Code} — {e.Name}"));
        }
        catch { }
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Items.Clear();
            foreach (var h in DesktopServices.Assignments.Holdings(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText))
                Items.Add(h);
            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(IsEmpty));
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    private void LoadHistory()
    {
        History.Clear();
        if (Selected is null) return;
        try
        {
            foreach (var m in DesktopServices.Assignments.History(_session, Selected.AssetType, Selected.AssetId))
                History.Add(m);
        }
        catch { }
    }

    private static string OpCode(string display) => display switch
    { "İade" => "return", "Hasarlı İade" => "damaged", "Devir" => "transfer", "Kayıp" => "lost", _ => "issue" };

    private long? DocDateMs => FormDocDate is null ? null
        : new DateTimeOffset(DateTime.SpecifyKind(FormDocDate.Value.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (FormAsset is null || FormPersonnel is null) { FormError = "Varlık ve personel seçin."; return; }
        var op = OpCode(FormOp);
        if (op == "transfer" && FormToPersonnel is null) { FormError = "Devir için alan personeli seçin."; return; }
        if (!await ConfirmService.AskAsync($"{FormOp} kaydedilsin mi?", "Zimmet")) return;
        try
        {
            var tip = IsMaterial ? "material" : "equipment";
            var qty = IsMaterial ? FormQuantity : 1m;
            var opId = "desk-" + Guid.NewGuid().ToString("N");   // idempotent: retry ikinci hareket/stok üretmez
            var svc = DesktopServices.Assignments;
            switch (op)
            {
                case "return": svc.Return(_session, tip, FormAsset.Id, FormPersonnel.Id, qty, FormBranch?.Id, DocDateMs, FormNote, opId); break;
                case "damaged": svc.Return(_session, tip, FormAsset.Id, FormPersonnel.Id, qty, FormBranch?.Id, DocDateMs, FormNote, opId, damaged: true); break;
                case "lost": svc.Lost(_session, tip, FormAsset.Id, FormPersonnel.Id, qty, FormBranch?.Id, DocDateMs, FormNote, opId); break;
                case "transfer": svc.Transfer(_session, tip, FormAsset.Id, FormPersonnel.Id, FormToPersonnel!.Id, qty, FormBranch?.Id, DocDateMs, FormNote, opId); break;
                default: svc.Issue(_session, tip, FormAsset.Id, FormPersonnel.Id, qty, FormBranch?.Id, DocDateMs, FormNote, opId); break;
            }
            FormNote = "";
            Load();
            LoadHistory();
            Status = FormOp + " kaydedildi.";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    /// <summary>Liste kuralı 2: filtrelenmiş TÜM "kimde ne var" kümesi Excel'e (yerel — çevrimdışı da çalışır).</summary>
    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            var rows = DesktopServices.Assignments.Holdings(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText);
            var hedef = await FilePickerService.SaveExcelAsync("Zimmet.xlsx");
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(AssignmentService.ToTableModel(rows)));
            Status = $"Excel kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Excel aktarılamadı: " + ex.Message; }
    }
}
