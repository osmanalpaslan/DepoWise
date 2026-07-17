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
using DepoWise.Infrastructure.Vehicles;

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
        { "Malzemeler", "Araçlar", "Personel", "Muayene / Sigorta", "Bakım", "Talepler", "Yakıt Dağıtım", "Yakıt Depo Girişi" };
    public ObservableCollection<string> ImportItems { get; } = new()
        { "Malzemeler", "Araçlar", "Bakım", "Muayene / Sigorta", "Yakıt Dağıtım", "Yakıt Depo Girişi" };

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

            var dry = SelectedImport switch
            {
                "Araçlar" => DesktopServices.VehicleImport.DryRun(_session, rows),
                "Bakım" => DesktopServices.MaintenanceImport.DryRun(_session, rows),
                "Muayene / Sigorta" => DesktopServices.InspectionImport.DryRun(_session, rows),
                "Yakıt Dağıtım" => DesktopServices.FuelImport.DryRun(_session, rows),
                "Yakıt Depo Girişi" => DesktopServices.FuelDepotImport.DryRun(_session, rows),
                _ => DesktopServices.MaterialImport.DryRun(_session, rows),
            };
            // Ön kontrol hatalarını ONAY penceresinde göster: kullanıcı "depo yetersiz" / "araç bulunamadı"
            // gibi engelleri aktarımdan ÖNCE görsün (aksi halde satırlar tek tek patlar).
            var dryDetail = dry.Errors.Count > 0
                ? "\n\nÖn kontrol uyarıları:\n" + string.Join("\n", dry.Errors.Take(8).Select(e => e.RowNumber > 0 ? $"• Satır {e.RowNumber}: {e.Message}" : $"• {e.Message}"))
                  + (dry.Errors.Count > 8 ? $"\n… ve {dry.Errors.Count - 8} uyarı daha" : "")
                : "";
            if (!await ConfirmService.AskAsync(
                    $"{dry.Total} satır okundu, {dry.Valid} geçerli, {dry.Failed} hatalı.{dryDetail}\n\nİçe aktarılsın mı? (hatalı satırlar atlanır)",
                    "İçe Aktar"))
                return;
            // Tanım alanları isimle yazılır ve yoksa OTOMATİK oluşturulur (kullanıcı kuralı). Oluşan yeni
            // tanımlar raporlanır: "CATERPILLAR" ve "caterpiller" (yazım hatası) iki AYRI marka olur —
            // kullanıcı bu listeye bakıp hatayı görebilmeli.
            IReadOnlyList<string> createdLookups = System.Array.Empty<string>();
            ImportResult res;
            switch (SelectedImport)
            {
                case "Araçlar":
                    (res, createdLookups) = DesktopServices.VehicleImport.CommitWithLookups(_session, rows); break;
                case "Bakım":
                    (res, createdLookups) = DesktopServices.MaintenanceImport.CommitWithLookups(_session, rows); break;
                case "Muayene / Sigorta":
                    res = DesktopServices.InspectionImport.Commit(_session, rows); break;
                case "Yakıt Dağıtım":
                    res = DesktopServices.FuelImport.Commit(_session, rows); break;
                case "Yakıt Depo Girişi":
                    res = DesktopServices.FuelDepotImport.Commit(_session, rows); break;
                default:
                    (res, createdLookups) = DesktopServices.MaterialImport.CommitWithLookups(_session, rows); break;
            }
            // Yakıtta "Updated" = zaten vardı, atlandı (aynı dosya tekrar aktarıldı) — kullanıcıya böyle yaz.
            var isFuel = SelectedImport is "Yakıt Dağıtım" or "Yakıt Depo Girişi";
            ImportResult = isFuel
                ? $"İçe aktarım: toplam {res.Total}, eklenen {res.Added}, zaten vardı (atlandı) {res.Updated}, hatalı {res.Failed}."
                : $"İçe aktarım: toplam {res.Total}, eklenen {res.Added}, güncellenen {res.Updated}, hatalı {res.Failed}.";
            if (createdLookups.Count > 0)
            {
                ImportResult += $"\n\nOluşturulan yeni tanımlar ({createdLookups.Count}) — yazım hatası var mı diye kontrol edin:\n"
                    + string.Join("\n", createdLookups.Take(30).Select(x => "• " + x))
                    + (createdLookups.Count > 30 ? $"\n… ve {createdLookups.Count - 30} tanım daha (Tanımlar ekranından görün)" : "");
            }
            if (res.Errors.Count > 0)
                ImportResult += "\nHatalar:\n" + string.Join("\n", res.Errors.Select(e => e.RowNumber > 0 ? $"Satır {e.RowNumber}: {e.Message}" : e.Message));
            Status = "İçe aktarım tamamlandı.";
        }
        catch (Exception ex) { ImportResult = "İçe aktarılamadı: " + ex.Message; }
    }

    private static IReadOnlyList<string> TemplateHeaders(string entity) => entity switch
    {
        "Araçlar" => DesktopServices.VehicleImport.SampleHeaders(),
        "Bakım" => DesktopServices.MaintenanceImport.SampleHeaders(),
        "Muayene / Sigorta" => DesktopServices.InspectionImport.SampleHeaders(),
        "Yakıt Dağıtım" => DesktopServices.FuelImport.SampleHeaders(),
        "Yakıt Depo Girişi" => DesktopServices.FuelDepotImport.SampleHeaders(),
        _ => DesktopServices.MaterialImport.SampleHeaders(),
    };

    private TableModel BuildTable(string entity)
    {
        var rows = new List<IReadOnlyList<object?>>();
        switch (entity)
        {
            // Dışa aktarım sütunları İÇE AKTARIM ŞABLONUYLA BİREBİR aynı: dışa aktar → Excel'de düzelt →
            // geri içe aktar döngüsü çalışsın (sütun adı tutmazsa import satırı okuyamaz).
            case "Araçlar":
                foreach (var v in DesktopServices.Vehicles.List(_session))
                {
                    // Detay ayrı sorgu: liste satırında tanım ADLARI (marka/model/şube…) yok.
                    VehicleDetail? d = null;
                    try { d = DesktopServices.Vehicles.Get(_session, v.Id); } catch { }
                    rows.Add(new object?[]
                    {
                        v.InternalCode, v.Plate, v.ProductionYear,
                        DepoWise.Application.Ui.VehicleStatus.Label(v.Status), d?.StatusNote,
                        v.CurrentMeter, v.MeterUnit,
                        d?.VehicleTypeName, d?.CategoryName, d?.BrandName, d?.VehicleModelName,
                        d?.BranchName, d?.DriverName, d?.ChassisNo, d?.EngineNo,
                    });
                }
                return new TableModel("Araçlar", DesktopServices.VehicleImport.SampleHeaders().ToArray(), rows);

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

            // Yakıt dışa aktarımı, İÇE AKTARIM ŞABLONUYLA AYNI sütunlarda: dışa aktar → Excel'de düzelt →
            // geri içe aktar akışı çalışsın (sütun adları birebir eşleşmezse import satırı okuyamaz).
            case "Yakıt Dağıtım":
                foreach (var f in DesktopServices.Fuel.ListDistributions(_session, 5000))
                    rows.Add(new object?[] { f.VehicleCode,
                        DateTimeOffset.FromUnixTimeMilliseconds(f.DistributionDate).LocalDateTime.ToString("dd.MM.yyyy"),
                        f.Liters, f.CurrentMeter, f.UnitPrice, null, null });
                return new TableModel("Yakıt Dağıtım", DesktopServices.FuelImport.SampleHeaders().ToArray(), rows);

            case "Yakıt Depo Girişi":
                foreach (var d in DesktopServices.Fuel.ListDepotEntries(_session, 5000))
                    rows.Add(new object?[] {
                        DateTimeOffset.FromUnixTimeMilliseconds(d.EntryDate).LocalDateTime.ToString("dd.MM.yyyy"),
                        d.Liters, d.UnitPrice, null, d.InvoiceNo, null });
                return new TableModel("Yakıt Depo Girişi", DesktopServices.FuelDepotImport.SampleHeaders().ToArray(), rows);

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
