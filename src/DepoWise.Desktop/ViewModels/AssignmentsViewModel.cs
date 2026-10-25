using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Assignments;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Toplu zimmet teslim formundaki bir kalem (2026-10-11).</summary>
public sealed partial class ZimmetKalemi : ObservableObject
{
    public ZimmetKalemi(string assetType, string assetId, string label, decimal quantity)
    { AssetType = assetType; AssetId = assetId; Label = label; _quantity = quantity; }
    public string AssetType { get; }
    public string AssetId { get; }
    public string Label { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantityDisplay))]
    private decimal _quantity;
    public string TypeDisplay => AssetType == "equipment" ? "Ekipman" : "Malzeme";
    public string QuantityDisplay => Quantity.ToString("0.####", System.Globalization.CultureInfo.CurrentCulture);
}

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
    /// <summary>⭐ M-63: ekran "Yeni işlem" sekmesiyle açılır; yazma yetkisi yoksa doğrudan "Kimde ne var".</summary>
    public int BaslangicSekmesi => CanWrite ? 0 : 1;
    /// <summary>Denetim B5+B6 (M-46): iade tarihi vermek zimmet DÜZENLEME yetkisi ister (servis kapısı + web ile aynı).</summary>
    public bool CanEdit => AccessControl.Can(_session, AssignmentService.Module, PermissionAction.Edit);
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

    /// <summary>⭐ 2026-10-11 (kullanıcı isteği): TOPLU TESLİM kalemleri. "Listeye Ekle" ile birikir, "Kaydet" hepsini
    /// TEK işlemde teslim eder (ya hepsi ya hiçbiri — serviste <see cref="AssignmentService.IssueMany"/>).
    /// Liste boşsa eski tekli akış AYNEN çalışır.</summary>
    public ObservableCollection<ZimmetKalemi> Lines { get; } = new();
    public bool HasLines => Lines.Count > 0;
    public bool IsIssue => FormOp == "Teslim";
    public bool ShowLines => IsIssue && HasLines;

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

    // ═══ M-46 (AR-GE 2026-10-19, F-081) — BEKLENEN İADE · GECİKENLER ═══
    public ObservableCollection<DepoWise.Infrastructure.Assignments.ZimmetIadesi> Gecikenler { get; } = new();
    public bool HasGeciken => Gecikenler.Count > 0;
    public string GecikenOzeti => $"⚠ {Gecikenler.Count} zimmetin iade tarihi geçti";
    [ObservableProperty] private DateTimeOffset? _iadeTarihi;

    private void GecikenleriYukle()
    {
        Gecikenler.Clear();
        try { foreach (var g in DesktopServices.Assignments.IadeDurumu(_session, yalnizGeciken: true)) Gecikenler.Add(g); } catch { }
        OnPropertyChanged(nameof(HasGeciken));
        OnPropertyChanged(nameof(GecikenOzeti));
    }

    [RelayCommand]
    private void IadeTarihiVer()
    {
        if (Selected is null) { Status = "Önce listeden bir zimmet satırı seçin."; return; }
        try
        {
            DesktopServices.Assignments.IadeTarihiAyarla(_session, Selected.PersonnelId, Selected.AssetType, Selected.AssetId,
                DepoWise.Application.Common.IsGunuTarihi.Ms(IadeTarihi));
            Status = IadeTarihi is null ? "Beklenen iade tarihi kaldırıldı." : $"Beklenen iade tarihi kaydedildi: {IadeTarihi:dd.MM.yyyy}.";
            GecikenleriYukle();
        }
        catch (Exception ex) { Status = "Kaydedilemedi: " + ex.Message; }
    }

    // ── Form ──
    [ObservableProperty] private string _formOp = "Teslim";
    [ObservableProperty] private string _formAssetType = "Malzeme";
    [ObservableProperty] private ProjectPick? _formAsset;

    // ⭐ M-49 (AR-GE 2026-10-19, F-139): QR / barkod okutma — TAM eşleşen malzeme/ekipman seçilir; Teslimde listeye eklenir.
    [ObservableProperty] private string? _okunanKod;

    [RelayCommand]
    private void KodOkut()
    {
        if (string.IsNullOrWhiteSpace(OkunanKod)) return;
        var kod = OkunanKod.Trim();
        OkunanKod = "";
        FormError = null;
        try
        {
            var tur = IsMaterial ? DepoWise.Infrastructure.Reporting.KodOkutmaService.Malzeme : DepoWise.Infrastructure.Reporting.KodOkutmaService.Ekipman;
            var r = DesktopServices.KodOkutma.Bul(_session, tur, kod);
            var a = r is null ? null : AssetOptions.FirstOrDefault(x => x.Id == r.Id);
            if (a is null) { FormError = $"'{kod}' koduna ait {(IsMaterial ? "malzeme" : "ekipman")} bulunamadı (kod tam eşleşmeli; varlık türünü kontrol edin)."; return; }
            FormAsset = a;
            if (IsIssue) AddLine();   // teslimde okutulan her kalem listeye eklenir (art arda okutma)
            if (FormError is null) Status = $"Okutuldu: {a.Name}";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }
    [ObservableProperty] private ProjectPick? _formPersonnel;
    [ObservableProperty] private ProjectPick? _formToPersonnel;
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private decimal _formQuantity = 1m;
    [ObservableProperty] private DateTimeOffset? _formDocDate = new DateTimeOffset(DateTime.Today);
    [ObservableProperty] private string _formNote = "";
    [ObservableProperty] private string? _formError;

    public bool IsTransfer => FormOp == "Devir";
    public bool IsMaterial => FormAssetType == "Malzeme";
    partial void OnFormOpChanged(string value)
    {
        OnPropertyChanged(nameof(IsTransfer));
        OnPropertyChanged(nameof(IsIssue));
        if (!IsIssue && Lines.Count > 0) { Lines.Clear(); LinesChanged(); }   // kalem listesi yalnız Teslim içindir
        OnPropertyChanged(nameof(ShowLines));
    }

    private void LinesChanged()
    {
        OnPropertyChanged(nameof(HasLines));
        OnPropertyChanged(nameof(ShowLines));
    }

    /// <summary>Seçili varlığı teslim listesine ekler. Aynı malzeme tekrar eklenirse miktarı ARTAR; aynı ekipman
    /// ikinci kez eklenemez (ekipman tek kişide ve tek adettir).</summary>
    [RelayCommand]
    private void AddLine()
    {
        FormError = null;
        if (FormAsset is null) { FormError = "Listeye eklemek için önce bir malzeme/ekipman seçin."; return; }
        var tip = IsMaterial ? "material" : "equipment";
        var qty = IsMaterial ? FormQuantity : 1m;
        if (qty <= 0) { FormError = "Miktar sıfırdan büyük olmalı."; return; }
        var var_ = Lines.FirstOrDefault(l => l.AssetType == tip && l.AssetId == FormAsset.Id);
        if (var_ is not null)
        {
            if (tip == "equipment") { FormError = "Bu ekipman listede zaten var."; return; }
            var_.Quantity += qty;
        }
        else
        {
            if (Lines.Count >= AssignmentService.MaxTopluKalem) { FormError = $"Bir seferde en fazla {AssignmentService.MaxTopluKalem} kalem."; return; }
            Lines.Add(new ZimmetKalemi(tip, FormAsset.Id, FormAsset.Name, qty));
        }
        FormAsset = null;
        FormQuantity = 1m;
        LinesChanged();
    }

    [RelayCommand]
    private void RemoveLine(ZimmetKalemi? k)
    {
        if (k is null) return;
        Lines.Remove(k);
        LinesChanged();
    }
    partial void OnFormAssetTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsMaterial));
        if (!IsMaterial) FormQuantity = 1m;
        LoadAssetOptions();
    }

    // ════════════ ⭐ M-50 (AR-GE 2026-10-19, F-083) — ZİMMET KİT (SET) ŞABLONU ════════════
    // Kit yalnız şablondur: seçilen kit teslim listesine yüklenir, teslim her zamanki "Kaydet" (tek işlem) ile yapılır.
    public ObservableCollection<DepoWise.Infrastructure.Assignments.ZimmetKiti> Kitler { get; } = new();
    [ObservableProperty] private DepoWise.Infrastructure.Assignments.ZimmetKiti? _seciliKit;
    [ObservableProperty] private string _kitAdi = "";

    private void KitleriYukle()
    {
        var secili = SeciliKit?.Id;
        Kitler.Clear();
        try { foreach (var k in DesktopServices.ZimmetKit.Liste(_session)) Kitler.Add(k); } catch { }
        SeciliKit = Kitler.FirstOrDefault(k => k.Id == secili);
    }

    [RelayCommand]
    private void KitYukle()
    {
        FormError = null;
        if (SeciliKit is null) { FormError = "Önce bir kit seçin."; return; }
        if (!IsIssue) FormOp = "Teslim";
        int eklenen = 0, atlanan = 0;
        foreach (var k in SeciliKit.Kalemler)
        {
            if (!k.Mevcut) { atlanan++; continue; }
            var var_ = Lines.FirstOrDefault(l => l.AssetType == k.AssetType && l.AssetId == k.AssetId);
            if (var_ is not null) { if (k.AssetType == "material") var_.Quantity += k.Quantity; else atlanan++; continue; }
            if (Lines.Count >= AssignmentService.MaxTopluKalem) { atlanan++; continue; }
            Lines.Add(new ZimmetKalemi(k.AssetType, k.AssetId, k.Label, k.Quantity));
            eklenen++;
        }
        LinesChanged();
        Status = $"'{SeciliKit.Ad}' kiti listeye yüklendi: {eklenen} kalem" + (atlanan > 0 ? $" ({atlanan} kalem atlandı: silinmiş ya da listede zaten var)." : ".") +
                 " Personeli seçip Kaydet'e basın.";
    }

    [RelayCommand]
    private async Task KitKaydet()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (Lines.Count == 0) { FormError = "Önce teslim listesine kalem ekleyin (Listeye Ekle), sonra kit olarak kaydedin."; return; }
        var ad = string.IsNullOrWhiteSpace(KitAdi) ? SeciliKit?.Ad : KitAdi.Trim();
        if (string.IsNullOrWhiteSpace(ad)) { FormError = "Kit adı yazın (ya da güncellemek için bir kit seçin)."; return; }
        var ayni = Kitler.FirstOrDefault(k => string.Equals(k.Ad, ad, StringComparison.OrdinalIgnoreCase));
        if (!await ConfirmService.AskAsync(ayni is null ? $"Listedeki {Lines.Count} kalem '{ad}' adıyla kit olarak kaydedilsin mi?"
                                                        : $"'{ad}' kiti listedeki {Lines.Count} kalemle GÜNCELLENSİN mi?", "Zimmet Kiti")) return;
        try
        {
            var id = DesktopServices.ZimmetKit.Kaydet(_session, ayni?.Id, ad,
                Lines.Select(l => new AssignmentLine(l.AssetType, l.AssetId, l.Quantity)).ToList());
            KitAdi = "";
            KitleriYukle();
            SeciliKit = Kitler.FirstOrDefault(k => k.Id == id);
            Status = $"'{ad}' kiti kaydedildi.";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task KitSil()
    {
        FormError = null;
        if (SeciliKit is null) { FormError = "Silinecek kiti seçin."; return; }
        if (!await ConfirmService.AskAsync($"'{SeciliKit.Ad}' kiti silinsin mi? (Yapılmış zimmet teslimleri etkilenmez.)", "Zimmet Kiti")) return;
        try
        {
            var ad = SeciliKit.Ad;
            DesktopServices.ZimmetKit.Sil(_session, SeciliKit.Id);
            SeciliKit = null;
            KitleriYukle();
            Status = $"'{ad}' kiti silindi.";
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    public AssignmentsViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        LoadAssetOptions();
        KitleriYukle();   // ⭐ M-50
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
        GecikenleriYukle();   // ⭐ M-46
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

    private long? DocDateMs => IsGunuTarihi.Ms(FormDocDate);   // ADR-184: tek kaynak

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        // ⭐ 2026-10-11: TOPLU TESLİM — listede kalem varsa (seçili ama eklenmemiş varlık da listeye katılır).
        if (IsIssue && Lines.Count > 0)
        {
            if (FormPersonnel is null) { FormError = "Teslim alacak personeli seçin."; return; }
            if (FormAsset is not null) AddLine();
            if (FormError is not null) return;
            var kalemler = Lines.Select(l => new AssignmentLine(l.AssetType, l.AssetId, l.Quantity)).ToList();
            if (!await ConfirmService.AskAsync($"{FormPersonnel.Name} kişisine {kalemler.Count} kalem teslim kaydedilsin mi?", "Zimmet")) return;
            try
            {
                DesktopServices.Assignments.IssueMany(_session, FormPersonnel.Id, kalemler, FormBranch?.Id, DocDateMs, FormNote,
                    "desk-" + Guid.NewGuid().ToString("N"));
                Lines.Clear(); LinesChanged();
                FormNote = "";
                Load();
                LoadHistory();
                Status = $"{kalemler.Count} kalem teslim kaydedildi.";
            }
            catch (Exception ex) { FormError = ex.Message; }
            return;
        }
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
        try
        {
            var rows = DesktopServices.Assignments.Holdings(_session,
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText);
            var hedef = await YazdirmaYardimcisi.YazdirAsync(AssignmentService.ToTableModel(rows), _session);
            if (hedef is null) return;
            Status = $"PDF kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Yazdırılamadı: " + ex.Message; }
    }
}
