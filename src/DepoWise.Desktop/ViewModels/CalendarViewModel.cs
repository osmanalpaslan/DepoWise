using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Calendars;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Ay ızgarası hücresi (her Load'da yeniden kurulur — bağımsız durum taşımaz).</summary>
public sealed record CalendarDayCell(DateTime Date, bool InMonth, bool IsToday, bool IsSelected,
    IReadOnlyList<string> Preview, int MoreCount)
{
    public string DayNo => Date.Day.ToString(CultureInfo.InvariantCulture);
    public bool HasMore => MoreCount > 0;
    public string MoreText => $"+{MoreCount} daha…";
    public double CellOpacity => InMonth ? 1.0 : 0.4;
}

/// <summary>
/// ═══ TKV-01 (ADR-171, 2026-08-28) — TAKVİM (masaüstü) ═══
/// HİBRİT: yerel kaynaklar (el ile kayıt + iş emri planı + muayene/sigorta + gün-bazlı bakım) ÇEVRİMDIŞI
/// çalışır (DesktopServices.Calendar). Evrak+Proje SUNUCU-OTORİTELİDİR: çevrimiçiyse API'den eklenir,
/// çevrimdışıysa "çevrimiçi gerekli" notu görünür (Projeler ekranı emsali; veri uydurulmaz).
/// PK-H5: iş emri bağı yalnız gezinme — takvim hiçbir kaydın durumunu/iş mantığını değiştirmez.
/// </summary>
public sealed partial class CalendarViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, CalendarService.Module, PermissionAction.Create);
    public bool CanEditEvents => AccessControl.Can(_session, CalendarService.Module, PermissionAction.Edit);
    public bool CanDeleteEvents => AccessControl.Can(_session, CalendarService.Module, PermissionAction.Delete);
    public bool CanExport => AccessControl.Can(_session, "export", PermissionAction.View);

    public ObservableCollection<CalendarDayCell> DayCells { get; } = new();
    public ObservableCollection<CalendarItem> AgendaRows { get; } = new();
    public ObservableCollection<ProjectPick> BranchOptions { get; } = new();
    public ObservableCollection<ProjectPick> PersonnelOptions { get; } = new();
    public ObservableCollection<ProjectPick> WorkOrderOptions { get; } = new();
    public ObservableCollection<string> SourceOptions { get; } = new()
    { "Tümü", "Takvim Kaydı", "İş Emri", "Muayene/Sigorta", "Evrak Geçerlilik", "Proje", "Bakım Hedefi" };
    public ObservableCollection<string> DayNames { get; } = new() { "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt", "Paz" };

    [ObservableProperty] private string? _status;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filterSource = "Tümü";
    partial void OnFilterSourceChanged(string value) => _ = LoadAsync();
    [ObservableProperty] private ProjectPick? _filterBranch;
    partial void OnFilterBranchChanged(ProjectPick? value) => _ = LoadAsync();

    [ObservableProperty] private string _monthTitle = "";
    /// <summary>Sunucu-otoriteli kaynaklar (Evrak/Proje) çevrimdışıyken gösterilen not; çevrimiçiyse null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRemoteNote))]
    private string? _remoteNote;
    public bool HasRemoteNote => RemoteNote != null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasAgenda => AgendaRows.Count > 0;
    [ObservableProperty] private string _agendaTitle = "Ajanda — tüm ay";

    private DateTime _month = new(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
    private DateTime? _selectedDay;
    private List<CalendarItem> _items = new();

    // ── Form (el ile kayıt) ──
    [ObservableProperty] private bool _showAdd;
    [ObservableProperty] private string? _editId;
    [ObservableProperty] private string _formTitle2 = "";
    [ObservableProperty] private string _formNote = "";
    [ObservableProperty] private DateTimeOffset? _formStart = DateTimeOffset.UtcNow.Date;
    [ObservableProperty] private DateTimeOffset? _formEnd;
    [ObservableProperty] private ProjectPick? _formBranch;
    [ObservableProperty] private ProjectPick? _formResponsible;
    [ObservableProperty] private ProjectPick? _formWorkOrder;
    [ObservableProperty] private string? _formError;
    public string FormTitle => EditId is null ? "YENİ TAKVİM KAYDI" : "TAKVİM KAYDI DÜZENLE";
    private long? _editVersion;

    [ObservableProperty] private CalendarItem? _selectedAgenda;

    public CalendarViewModel(SessionContext session)
    {
        _session = session;
        LoadOptions();
        _ = LoadAsync();
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
        Doldur(WorkOrderOptions, () => DesktopServices.WorkOrders.List(_session).Select(w => (w.Id, $"{w.WoNo} — {w.Title}")));
    }

    private static string? SourceCode(string display) => display switch
    {
        "Takvim Kaydı" => "event", "İş Emri" => "work_order", "Muayene/Sigorta" => "inspection",
        "Evrak Geçerlilik" => "document", "Proje" => "project", "Bakım Hedefi" => "maintenance", _ => null,
    };

    private (long From, long To) Window()
    {
        var from = new DateTimeOffset(DateTime.SpecifyKind(_month, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var to = new DateTimeOffset(DateTime.SpecifyKind(_month.AddMonths(1), DateTimeKind.Utc)).ToUnixTimeMilliseconds() - 1;
        return (from, to);
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            LoadError = null;
            MonthTitle = _month.ToString("MMMM yyyy", new CultureInfo("tr-TR"));
            var (from, to) = Window();
            var source = SourceCode(FilterSource);
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

            // 1) YEREL katman: el ile kayıt + iş emri + muayene + gün-bazlı bakım (çevrimdışı tam çalışır).
            //    Evrak masaüstünde sunucu-otoriteli (documents=null) → yerel çağrıdan zaten gelmez.
            var list = DesktopServices.Calendar.Items(_session, from, to, source, FilterBranch?.Id, search).ToList();

            // 2) SUNUCU-OTORİTELİ kaynaklar (Evrak/Proje) — yalnız çevrimiçiyse eklenir (Projeler emsali).
            RemoteNote = null;
            var uzakKaynaklar = new[] { "document", "project" }.Where(k => source is null || source == k).ToList();
            if (uzakKaynaklar.Count > 0)
            {
                var kopuk = false;
                foreach (var k in uzakKaynaklar)
                {
                    var uzak = await OrgServerClient.ListCalendarAsync(from, to, k);
                    if (uzak is null) { kopuk = true; continue; }
                    foreach (var u in uzak)
                        list.Add(new CalendarItem(u.Source, "", u.Title, u.StartDate, u.EndDate,
                            null, u.BranchName == "—" ? null : u.BranchName,
                            u.ResponsibleName == "—" ? null : u.ResponsibleName, u.Detail, null, null, null, 0));
                }
                if (kopuk) RemoteNote = "Evrak geçerlilik ve Proje kaynakları çevrimiçi bağlantı gerektirir — şu an gösterilemiyor.";
                if (!string.IsNullOrWhiteSpace(search))
                    list = list.Where(i => i.Source is not ("document" or "project")
                        || i.Title.Contains(search!, StringComparison.OrdinalIgnoreCase)
                        || (i.Detail?.Contains(search!, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
                if (FilterBranch is not null)
                    list = list.Where(i => i.Source is not ("document" or "project") || i.BranchName == FilterBranch.Name).ToList();
            }

            _items = list.OrderBy(i => i.StartDate).ThenBy(i => i.Title, StringComparer.CurrentCulture).ToList();
            RebuildCells();
            RebuildAgenda();
        }
        catch (Exception ex) { LoadError = ex.Message; }
    }

    private List<CalendarItem> ItemsOfDay(DateTime day)
    {
        var dayStart = new DateTimeOffset(DateTime.SpecifyKind(day, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        var dayEnd = dayStart + 86_400_000 - 1;
        return _items.Where(i => i.StartDate <= dayEnd && (i.EndDate ?? i.StartDate) >= dayStart).ToList();
    }

    private void RebuildCells()
    {
        DayCells.Clear();
        var offset = ((int)_month.DayOfWeek + 6) % 7;   // Pazartesi=0
        var start = _month.AddDays(-offset);
        var last = _month.AddMonths(1).AddDays(-1);
        var end = last.AddDays(6 - ((int)last.DayOfWeek + 6) % 7);
        var today = DateTime.UtcNow.Date;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var gun = ItemsOfDay(d);
            DayCells.Add(new CalendarDayCell(d, d.Month == _month.Month, d == today, _selectedDay == d,
                gun.Take(3).Select(i => (i.IsEvent ? "★ " : "") + i.Title).ToList(), Math.Max(0, gun.Count - 3)));
        }
    }

    private void RebuildAgenda()
    {
        AgendaRows.Clear();
        foreach (var i in _selectedDay is { } d ? ItemsOfDay(d) : _items) AgendaRows.Add(i);
        AgendaTitle = _selectedDay is { } sd ? $"Ajanda — {sd:dd.MM.yyyy}" : "Ajanda — tüm ay";
        OnPropertyChanged(nameof(HasAgenda));
    }

    [RelayCommand]
    private void SelectDay(CalendarDayCell? cell)
    {
        if (cell is null) return;
        _selectedDay = _selectedDay == cell.Date ? null : cell.Date;
        RebuildCells();
        RebuildAgenda();
    }

    [RelayCommand] private Task PrevMonth() { _month = _month.AddMonths(-1); _selectedDay = null; return LoadAsync(); }
    [RelayCommand] private Task NextMonth() { _month = _month.AddMonths(1); _selectedDay = null; return LoadAsync(); }
    [RelayCommand] private Task GoToday() { _month = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1); _selectedDay = null; return LoadAsync(); }

    // ── Form ──

    [RelayCommand]
    private void NewEvent()
    {
        if (!CanWrite) { Status = "Yetki yok."; return; }
        EditId = null; _editVersion = null;
        FormTitle2 = ""; FormNote = ""; FormStart = DateTimeOffset.UtcNow.Date; FormEnd = null;
        FormBranch = null; FormResponsible = null; FormWorkOrder = null; FormError = null;
        LoadOptions();
        ShowAdd = true;
        OnPropertyChanged(nameof(FormTitle));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task BeginEdit()
    {
        if (SelectedAgenda is not { IsEvent: true } e) { Status = "Düzenlemek için el ile eklenmiş (★) bir takvim kaydı seçin."; return; }
        if (!CanEditEvents) { Status = "Yetki yok."; return; }
        // ⭐ FAZ 4.2: standart düzenleme onayı (kullanıcı isteği 2026-09-06).
        if (!await ConfirmService.ConfirmEditAsync()) return;
        EditId = e.Id; _editVersion = e.Version;
        FormTitle2 = e.Title; FormNote = e.Note ?? "";
        FormStart = DateTimeOffset.FromUnixTimeMilliseconds(e.StartDate);
        FormEnd = e.EndDate is { } ed ? DateTimeOffset.FromUnixTimeMilliseconds(ed) : null;
        FormBranch = BranchOptions.FirstOrDefault(b => b.Id == e.BranchId);
        FormResponsible = PersonnelOptions.FirstOrDefault(p => p.Id == e.ResponsiblePersonnelId);
        FormWorkOrder = WorkOrderOptions.FirstOrDefault(w => w.Id == e.WorkOrderId);
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
        if (string.IsNullOrWhiteSpace(FormTitle2) || FormStart is null)
        { FormError = "Başlık ve başlangıç tarihi zorunlu."; return; }
        var editing = EditId is not null;
        if (!await ConfirmService.AskAsync(editing ? "Takvim kaydı güncellensin mi?" : "Takvim kaydı oluşturulsun mu?", "Takvim")) return;
        try
        {
            var dto = new NewCalendarEvent(FormTitle2.Trim(), Ms(FormStart)!.Value, Ms(FormEnd),
                FormBranch?.Id, FormResponsible?.Id, FormWorkOrder?.Id,
                string.IsNullOrWhiteSpace(FormNote) ? null : FormNote.Trim());
            if (editing) DesktopServices.Calendar.Update(_session, EditId!, dto, _editVersion);
            else DesktopServices.Calendar.Create(_session, dto);
            Status = editing ? "Takvim kaydı güncellendi." : "Takvim kaydı oluşturuldu.";
            ShowAdd = false; EditId = null; _editVersion = null;
            await LoadAsync();
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteEvent()
    {
        if (SelectedAgenda is not { IsEvent: true } e) { Status = "Silmek için el ile eklenmiş (★) bir takvim kaydı seçin."; return; }
        if (!CanDeleteEvents) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync("Takvim kaydı silinsin mi? (Çöp Kutusu'ndan geri alınabilir.)", "Takvim")) return;
        try
        {
            DesktopServices.Calendar.Delete(_session, e.Id);
            Status = "Silindi — Çöp Kutusu'ndan geri alınabilir.";
            await LoadAsync();
        }
        catch (Exception ex) { Status = "Silinemedi: " + ex.Message; }
    }

    /// <summary>Liste kuralı 2: görünen ajanda kümesinin TAMAMI Excel'e (yerel — çevrimdışı da çalışır).</summary>
    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { Status = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            var hedef = await FilePickerService.SaveExcelAsync("Takvim.xlsx");
            if (hedef is null) return;
            await File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(CalendarService.ToTableModel(_items)));
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
            var hedef = await YazdirmaYardimcisi.YazdirAsync(CalendarService.ToTableModel(_items), _session);
            if (hedef is null) return;
            Status = $"PDF kaydedildi: {hedef}";
        }
        catch (Exception ex) { Status = "Yazdırılamadı: " + ex.Message; }
    }
}
