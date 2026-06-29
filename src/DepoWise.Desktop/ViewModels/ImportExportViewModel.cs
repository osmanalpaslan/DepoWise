using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// İmport / Export — tüm ana ekranlardan Excel'e dışa aktarım + örnek şablon indirme + Excel'den içe aktarım.
/// İçe aktarım şu an Malzemeler için (servis hazır); Araç/Bakım/Muayene import sonraki adımlarda eklenecek.
/// Tüm girdi/çıktı .xlsx.
/// </summary>
public sealed partial class ImportExportViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<string> ExportItems { get; } = new()
        { "Malzemeler", "Araçlar", "Personel", "Muayene / Sigorta", "Bakım", "Talepler" };
    public ObservableCollection<string> ImportItems { get; } = new() { "Malzemeler" };

    [ObservableProperty] private string _selectedExport = "Malzemeler";
    [ObservableProperty] private string _selectedImport = "Malzemeler";
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _importResult;

    public ImportExportViewModel(SessionContext session) => _session = session;

    [RelayCommand]
    private async Task Export()
    {
        try
        {
            var table = BuildTable(SelectedExport);
            var bytes = DesktopServices.Excel.Export(table);
            var path = await FilePickerService.SaveExcelAsync(SelectedExport.Replace(" / ", "_").Replace(" ", "_"));
            if (string.IsNullOrEmpty(path)) return;
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            FilePickerService.OpenFile(path);
            Status = $"Dışa aktarıldı ({table.Rows.Count} satır): {path}";
        }
        catch (Exception ex) { Status = "Dışa aktarılamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task DownloadTemplate()
    {
        try
        {
            var headers = TemplateHeaders(SelectedImport);
            var bytes = DesktopServices.Excel.Template(SelectedImport + " Şablon", headers);
            var path = await FilePickerService.SaveExcelAsync(SelectedImport.Replace(" ", "_") + "_sablon");
            if (string.IsNullOrEmpty(path)) return;
            await System.IO.File.WriteAllBytesAsync(path, bytes);
            FilePickerService.OpenFile(path);
            Status = "Örnek şablon indirildi: " + path;
        }
        catch (Exception ex) { Status = "Şablon oluşturulamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Import()
    {
        ImportResult = null;
        try
        {
            var path = await FilePickerService.PickFileAsync("İçe Aktarılacak Excel", "Excel", "*.xlsx");
            if (string.IsNullOrEmpty(path)) return;
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            var rows = DesktopServices.Excel.ReadRows(bytes);
            if (rows.Count == 0) { ImportResult = "Dosyada veri satırı bulunamadı."; return; }

            if (SelectedImport != "Malzemeler") { ImportResult = "Bu ekran için içe aktarım henüz desteklenmiyor."; return; }

            var dry = DesktopServices.MaterialImport.DryRun(_session, rows);
            if (!await ConfirmService.AskAsync(
                    $"{dry.Total} satır okundu, {dry.Valid} geçerli, {dry.Failed} hatalı.\nİçe aktarılsın mı?", "İçe Aktar"))
                return;
            var res = DesktopServices.MaterialImport.Commit(_session, rows);
            ImportResult = $"İçe aktarım: toplam {res.Total}, eklenen {res.Added}, güncellenen {res.Updated}, hatalı {res.Failed}."
                + (res.Errors.Count > 0 ? "\nHatalar:\n" + string.Join("\n", res.Errors.Select(e => $"Satır {e.RowNumber}: {e.Message}")) : "");
            Status = "İçe aktarım tamamlandı.";
        }
        catch (Exception ex) { ImportResult = "İçe aktarılamadı: " + ex.Message; }
    }

    private static IReadOnlyList<string> TemplateHeaders(string entity) => entity switch
    {
        _ => new[] { "Kod", "Ad", "Tür", "Min Stok", "Birim Fiyat", "Para Birimi" } // Malzemeler
    };

    private TableModel BuildTable(string entity)
    {
        var rows = new List<IReadOnlyList<object?>>();
        switch (entity)
        {
            case "Araçlar":
                foreach (var v in DesktopServices.Vehicles.List(_session))
                    rows.Add(new object?[] { v.InternalCode, v.Plate, v.Status, v.CurrentMeter, v.MeterUnit, v.ProductionYear });
                return new TableModel("Araçlar", new[] { "İç Kod", "Plaka", "Durum", "Sayaç", "Birim", "Yıl" }, rows);

            case "Personel":
                foreach (var p in DesktopServices.Personnel.List(_session, new PageRequest { Limit = 5000 }).Items)
                    rows.Add(new object?[] { p.FullName, p.Title, p.Phone, p.IsActive ? "Aktif" : "Pasif" });
                return new TableModel("Personel", new[] { "Ad Soyad", "Unvan", "Telefon", "Durum" }, rows);

            case "Muayene / Sigorta":
                foreach (var i in DesktopServices.Inspection.List(_session))
                    rows.Add(new object?[] { i.VehicleText, i.DocTypeText, i.LastText, i.NextText, i.Place, i.Result, i.StatusText });
                return new TableModel("Muayene Sigorta", new[] { "Araç", "Belge", "Son", "Sonraki", "Yer", "Sonuç", "Durum" }, rows);

            case "Bakım":
                foreach (var m in DesktopServices.Maintenance.ListMaintenances(_session))
                    rows.Add(new object?[] { m.VehicleCode, m.DefinitionName, m.SubDisplay, m.PerformedDisplay, m.NextDueDisplay, m.StatusText });
                return new TableModel("Bakım", new[] { "Araç", "Bakım", "Alt Bakım", "Yapılma", "Sonraki", "Durum" }, rows);

            case "Talepler":
                foreach (var r in DesktopServices.Requests.List(_session))
                    rows.Add(new object?[] { r.DocNo, DateTimeOffset.FromUnixTimeMilliseconds(r.RequestDate).LocalDateTime.ToString("dd.MM.yyyy"),
                        RequestRow.StatusLabel(r.Status), r.ItemCount });
                return new TableModel("Talepler", new[] { "Belge No", "Tarih", "Durum", "Kalem" }, rows);

            default: // Malzemeler
                foreach (var m in DesktopServices.Materials.List(_session, new PageRequest { Limit = 5000 }).Items)
                    rows.Add(new object?[] { m.Code, m.Name, m.Type, m.MinStock, m.UnitPrice, m.Currency });
                return new TableModel("Malzemeler", new[] { "Kod", "Ad", "Tür", "Min Stok", "Birim Fiyat", "Para Birimi" }, rows);
        }
    }
}
