using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Stok Sayım — malzeme seç (sistem stoğu gösterilir) + sayılan miktar + gerekçe → fark kadar 'adjustment'
/// stok hareketi (StockService.Count). Altta son sayım/düzeltme hareketleri.
/// </summary>
public sealed partial class StockCountViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "stock", PermissionAction.Create);

    public ObservableCollection<MaterialRefRow> MaterialResults { get; } = new();
    public ObservableCollection<StockMovementRow> Adjustments { get; } = new();

    [ObservableProperty] private string _materialSearch = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaterial))]
    private MaterialRefRow? _selectedMaterial;
    public bool HasMaterial => SelectedMaterial != null;
    [ObservableProperty] private decimal _systemBalance;
    [ObservableProperty] private decimal _countedQty;
    [ObservableProperty] private string _reason = "Sayım";
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private string? _status;

    public bool HasRows => Adjustments.Count > 0;
    public bool IsEmpty => Adjustments.Count == 0;
    public string DiffText => HasMaterial ? $"Fark: {(CountedQty - SystemBalance):0.##}" : "";

    // ══════════════ G1-02: ÇOK MALZEMELİ SAYIM SEPETİ (2026-08-10) ══════════════
    // Eskiden bir sayım belgesinde YALNIZ BİR malzeme olabiliyordu; 200 kalemlik sayım yapan depocu
    // 200 ayrı belge açmak zorundaydı. Servis (StockService.Count) ZATEN çok satırlıydı ve hepsini
    // TEK transaction + TEK belge + TEK operationId ile işliyor → yalnız ekran sınırıydı.
    // Web'deki desenle (ve StockEntryViewModel.ExitLines sepetiyle) aynı yaklaşım.
    //
    // ⚠️ ÇIKIŞ SEPETİNDEN FARKI: çıkışta aynı malzeme tekrar eklenirse miktarlar TOPLANIR; sayımda
    // TOPLANMAZ — sayılan miktar mutlak bir değerdir, son girilen değer geçerlidir (üzerine yazılır).

    /// <param name="SystemQty">Ekleme anındaki sistem stoğu (bilgi amaçlı; gerçek fark sunucuda yeniden hesaplanır).</param>
    public sealed record CountLineVm(string MaterialId, string Code, string Name, decimal SystemQty, decimal CountedQty)
    {
        public string Display => $"{Code} — {Name}";
        public decimal Diff => CountedQty - SystemQty;
        public string SystemText => SystemQty.ToString("0.##");
        public string CountedText => CountedQty.ToString("0.##");
        public string DiffText => Diff == 0 ? "0" : $"{Diff:+0.##;-0.##}";
    }

    public ObservableCollection<CountLineVm> CountLines { get; } = new();
    public bool HasCountLines => CountLines.Count > 0;
    public string CountLinesSummary => CountLines.Count == 0
        ? "Listeye malzeme eklemeden tek malzeme de sayabilirsiniz."
        : $"{CountLines.Count} malzeme listede — Kaydet'e basınca hepsi TEK sayım belgesinde işlenir.";

    [RelayCommand]
    private void AddCountLine()
    {
        FormError = null;
        if (SelectedMaterial is null) { FormError = "Önce malzeme seçin."; return; }
        // Aynı malzeme tekrar eklenirse SAYILAN MİKTAR GÜNCELLENİR (toplanmaz) — iki ayrı sayım satırı olmaz.
        var existing = CountLines.FirstOrDefault(l => l.MaterialId == SelectedMaterial.Id);
        if (existing is not null)
            CountLines[CountLines.IndexOf(existing)] = existing with { SystemQty = SystemBalance, CountedQty = CountedQty };
        else
            CountLines.Add(new CountLineVm(SelectedMaterial.Id, SelectedMaterial.Code, SelectedMaterial.Name, SystemBalance, CountedQty));

        SelectedMaterial = null; MaterialSearch = ""; SystemBalance = 0; CountedQty = 0;
        OnPropertyChanged(nameof(DiffText));
        NotifyCountLines();
        RefreshMaterials();
    }

    [RelayCommand]
    private void RemoveCountLine(CountLineVm? line)
    {
        if (line is null) return;
        CountLines.Remove(line);
        NotifyCountLines();
    }

    private void NotifyCountLines()
    {
        OnPropertyChanged(nameof(HasCountLines));
        OnPropertyChanged(nameof(CountLinesSummary));
    }

    /// <summary>Kaydedilecek satırlar: sepet + (varsa) formda duran seçim — kullanıcı "Listeye Ekle"ye
    /// basmayı unutursa seçimi kaybetmeyelim (ExitLines'taki aynı koruma).
    /// G1-03: fark=0 satırlar da GÖNDERİLİR; servis onları sayım satırı olarak yazar, hareket üretmez.</summary>
    private List<CountLine> BuildCountLines()
    {
        var lines = CountLines.Select(l => new CountLine(l.MaterialId, l.CountedQty)).ToList();
        if (SelectedMaterial is not null)
        {
            var i = lines.FindIndex(l => l.MaterialId == SelectedMaterial.Id);
            if (i >= 0) lines[i] = lines[i] with { CountedQuantity = CountedQty };   // sayımda üzerine yazılır
            else lines.Add(new CountLine(SelectedMaterial.Id, CountedQty));
        }
        return lines;
    }

    public StockCountViewModel(SessionContext session)
    {
        _session = session;
        Load();
        RefreshMaterials();
    }

    [RelayCommand]
    private void Load()
    {
        try
        {
            Adjustments.Clear();
            foreach (var m in DesktopServices.Stock.RecentMovements(_session).Where(x => x.MovementType == "adjustment"))
                Adjustments.Add(m);
            Status = $"{Adjustments.Count} sayım düzeltmesi";
        }
        catch (Exception ex) { Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnMaterialSearchChanged(string value) => RefreshMaterials();
    partial void OnCountedQtyChanged(decimal value) => OnPropertyChanged(nameof(DiffText));

    private void RefreshMaterials()
    {
        MaterialResults.Clear();
        var term = MaterialSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 30 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items) MaterialResults.Add(new MaterialRefRow(m.Id, m.Code, m.Name));
        }
        catch { }
    }

    [RelayCommand]
    private void PickMaterial(MaterialRefRow? m)
    {
        if (m is null) return;
        SelectedMaterial = m;
        MaterialSearch = $"{m.Code} - {m.Name}";
        MaterialResults.Clear();
        try { SystemBalance = DesktopServices.Stock.GetBalance(_session, m.Id); } catch { SystemBalance = 0; }
        CountedQty = SystemBalance;
        OnPropertyChanged(nameof(DiffText));
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (!await BranchGuard.RequireBranchAsync(_session, "Stok Sayım")) return;   // "Tüm Şubeler" modunda işlem yok
        if (string.IsNullOrWhiteSpace(Reason)) { FormError = "Gerekçe zorunlu."; return; }
        // G1-02: sepet + (varsa) formdaki seçim birlikte TEK belgede işlenir. Sepet boşsa eski tek-malzeme
        // davranışı aynen sürer (formdaki seçim tek satır olur).
        var lines = BuildCountLines();
        if (lines.Count == 0) { FormError = "Malzeme seçin (ya da listeye ekleyin)."; return; }

        string confirmMsg;
        if (lines.Count == 1 && SelectedMaterial is not null && CountLines.Count == 0)
        {
            // Tek malzeme: mevcut (bilinen) onay metni korunur.
            var diff = CountedQty - SystemBalance;
            var diffNote = diff == 0 ? "Fark yok — kayıt raporda görünür." : $"Fark: {diff:0.##} (stoğa yansır).";
            confirmMsg = $"Sayım kaydedilsin mi?\nSistem: {SystemBalance:0.##}  Sayılan: {CountedQty:0.##}\n{diffNote}";
        }
        else
        {
            var diffCount = CountLines.Count(l => l.Diff != 0);
            confirmMsg = $"{lines.Count} malzeme sayıldı ({diffCount} malzemede fark var).\n" +
                         "Hepsi TEK sayım belgesinde kaydedilsin mi? (fark kadar düzeltme hareketi oluşur)";
        }
        if (!await ConfirmService.AskAsync(confirmMsg, "Stok Sayım")) return;

        try
        {
            // TEK çağrı → TEK transaction → TEK belge → TEK operationId (satır başına ayrı belge YOK).
            DesktopServices.Stock.Count(_session, lines, Reason.Trim(), Guid.NewGuid().ToString("N"));
            Status = lines.Count == 1
                ? "Sayım kaydedildi (fark stoğa yansıdı)."
                : $"Sayım kaydedildi ({lines.Count} malzeme, tek belge).";
            CountLines.Clear(); NotifyCountLines();
            SelectedMaterial = null; MaterialSearch = ""; SystemBalance = 0; CountedQty = 0; Reason = "Sayım";
            OnPropertyChanged(nameof(DiffText));
            Load(); RefreshMaterials();
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }
}
