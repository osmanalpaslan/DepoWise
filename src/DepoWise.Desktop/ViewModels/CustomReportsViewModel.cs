using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Tasarımcıda bir kolon satırı: seçili mi, hangi filtre metni girildi.</summary>
public sealed partial class CustomReportColumnRow : ObservableObject
{
    public CustomReportColumnRow(ListColumn column) { Column = column; }

    public ListColumn Column { get; }
    public string Key => Column.Key;
    public string Label => Column.Label;

    [ObservableProperty] private bool _selected;
    /// <summary>Kullanıcının bu kolona yazdığı ARAMA METNİ. ⚠️ SQL değildir; parametre olarak geçer.</summary>
    [ObservableProperty] private string _filter = "";
}

/// <summary>
/// ═══ ARA İŞ 4 (ADR-186) — CUSTOM RAPOR TASARIMCISI (masaüstü) ═══
///
/// Kullanıcı kendi raporunu <b>seçerek</b> tanımlar: kaynak → kolonlar → filtreler → sırala → kaydet.
///
/// <b>Kullanıcı SQL YAZAMAZ (PK-CR-01/05=A):</b> ekranda serbest metin yalnız (a) rapor adı ve
/// (b) her kolonun ARAMA DEĞERİ içindir. Tablo adı · kolon adı · SQL ifadesi · JOIN · ORDER BY ·
/// aggregate girilebilecek HİÇBİR alan yoktur; kaynak ve kolonlar listeden SEÇİLİR
/// (<see cref="CustomReportSources"/> beyaz listesi).
///
/// <b>Çevrimdışı:</b> tanımlar yerel SQLite'ta durur ve senkronla taşınır → internet olmadan da
/// tanımlanır, listelenir ve çalıştırılır.
///
/// Çalıştırma bu ekranda YAPILMAZ: kaydedilen rapor mevcut <b>Raporlar</b> ekranının listesinde
/// belirir ve oradaki ortak motorla (dört güvenlik kapısı) çalışır — ikinci motor yoktur.
/// </summary>
public sealed partial class CustomReportsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public CustomReportsViewModel(SessionContext session)
    {
        _session = session;
        Sources = new ObservableCollection<CustomReportSource>(CustomReportSources.All);
        _selectedSource = Sources[0];
        Columns = new ObservableCollection<CustomReportColumnRow>();
        Definitions = new ObservableCollection<CustomReportDefinition>();
        KolonlariKur();
        Yenile();
    }

    // ── Yetki (UI kapıları; asıl kapılar serviste) ──
    public bool CanCreate => AccessControl.Can(_session, "reports", PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, "reports", PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, "reports", PermissionAction.Delete);

    public ObservableCollection<CustomReportSource> Sources { get; }
    public ObservableCollection<CustomReportColumnRow> Columns { get; }
    public ObservableCollection<CustomReportDefinition> Definitions { get; }

    [ObservableProperty] private CustomReportSource _selectedSource;
    [ObservableProperty] private CustomReportDefinition? _selectedDefinition;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _editingId;
    [ObservableProperty] private string _message = "";

    /// <summary>Seçili kaynak OLAY verisi mi (tarih aralığı zorunlu)?</summary>
    public bool RequiresDate => SelectedSource?.RequiresDate ?? false;
    /// <summary>Seçili kaynak ANA veri mi (en az bir filtre zorunlu)?</summary>
    public bool RequiresFilter => SelectedSource?.RequiresFilter ?? false;

    /// <summary>Kullanıcıya gösterilen kural açıklaması — neden tarih ya da filtre istendiğini anlatır.</summary>
    public string RuleHint => RequiresDate
        ? "Bu kaynak bir OLAY kaydıdır: raporu çalıştırırken tarih aralığı seçmeniz zorunludur."
        : "Bu kaynak ANA VERİDİR (tarih alanı yoktur): çok büyük sonuç oluşmaması için en az bir filtre girmelisiniz.";

    partial void OnSelectedSourceChanged(CustomReportSource value)
    {
        KolonlariKur();
        OnPropertyChanged(nameof(RequiresDate));
        OnPropertyChanged(nameof(RequiresFilter));
        OnPropertyChanged(nameof(RuleHint));
    }

    partial void OnSelectedDefinitionChanged(CustomReportDefinition? value)
    {
        if (value is null) return;
        Duzenle(value);
    }

    private void KolonlariKur()
    {
        Columns.Clear();
        foreach (var c in SelectedSource.Columns) Columns.Add(new CustomReportColumnRow(c));
    }

    [RelayCommand]
    private void Yenile()
    {
        Definitions.Clear();
        try
        {
            foreach (var d in DesktopServices.CustomReports.List(_session, includeInactive: true))
                Definitions.Add(d);
        }
        catch (System.Exception ex) { Message = "Liste yüklenemedi: " + ex.Message; }
    }

    /// <summary>Formu boşaltır (yeni rapor).</summary>
    [RelayCommand]
    private void Temizle()
    {
        EditingId = null;
        Name = "";
        SelectedDefinition = null;
        KolonlariKur();
        Message = "";
    }

    private async System.Threading.Tasks.Task Duzenle(CustomReportDefinition def)
    {
        var src = CustomReportSources.ByKey(def.SourceKey);
        if (src is null) { Message = "Bu tanımın kaynağı tanınmıyor."; return; }
        // ⭐ FAZ 4.2: standart düzenleme onayı (kullanıcı isteği 2026-09-06).
        if (!await ConfirmService.ConfirmEditAsync()) return;
        if (SelectedSource.Key != src.Key) SelectedSource = src;   // kolonları da yeniler
        EditingId = def.Id;
        Name = def.Name;
        foreach (var row in Columns)
        {
            row.Selected = def.Columns.Contains(row.Key);
            row.Filter = def.Filters.FirstOrDefault(f => f.ColumnKey == row.Key)?.Value ?? "";
        }
        Message = "";
    }

    /// <summary>Kaydeder. Doğrulama SERVİSTE de tekrar yapılır (ekran tek başına güvenlik sayılmaz).</summary>
    [RelayCommand]
    private void Kaydet()
    {
        try
        {
            // Kolon SIRASI kullanıcının listedeki sırasıdır (rapor başlıkları bu sırayla çıkar).
            var kolonlar = Columns.Where(c => c.Selected).Select(c => c.Key).ToList();
            var filtreler = Columns
                .Where(c => !string.IsNullOrWhiteSpace(c.Filter))
                .Select(c => new CustomReportFilter(c.Key, c.Filter.Trim()))
                .ToList();

            if (EditingId is { } id)
            {
                DesktopServices.CustomReports.Update(_session, id, Name, SelectedSource.Key,
                    kolonlar, filtreler, sortColumn: null, sortDesc: false, isActive: true);
                Message = "Rapor güncellendi.";
            }
            else
            {
                var yeni = DesktopServices.CustomReports.Create(_session, Name, SelectedSource.Key,
                    kolonlar, filtreler, sortColumn: null, sortDesc: false);
                EditingId = yeni;
                Message = "Rapor kaydedildi. «Raporlar» ekranında görünmesi için ilgili yetkinin verilmiş olması gerekir.";
            }
            Yenile();
        }
        catch (System.Exception ex) { Message = ex.Message; }
    }

    /// <summary>Yumuşak siler (kayıt fiziksel olarak durur — proje standardı).</summary>
    /// <remarks>⭐ FAZ 4.2 (kullanıcı isteği 2026-09-06): silme ONAY SORMADAN çalışıyordu —
    /// yanlış tıklama kaydı sessizce siliyordu. Artık önce onay istenir.</remarks>
    [RelayCommand]
    private async System.Threading.Tasks.Task Sil()
    {
        if (EditingId is not { } id) { Message = "Önce listeden bir rapor seçin."; return; }
        if (!await ConfirmService.AskAsync("Kaydı silmek istediğinize emin misiniz?", "Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.CustomReports.Delete(_session, id);
            Message = "Rapor silindi.";
            Temizle();
            Yenile();
        }
        catch (System.Exception ex) { Message = ex.Message; }
    }
}
