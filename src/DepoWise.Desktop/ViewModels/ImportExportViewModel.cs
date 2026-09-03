using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Reporting;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// EXL-01 — Excel Merkezi: merkezi Excel dışa aktarım (15 kaynak) + örnek şablon indirme + içe aktarım
/// (7 set). Kaynak listesi ve kolonlar web/API ile ORTAK <see cref="ExcelCenterService"/>'ten gelir.
/// Tüm girdi/çıktı .xlsx.
/// </summary>
public sealed partial class ImportExportViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public ObservableCollection<string> ExportItems { get; } =
        new(ExcelCenterService.Sources.Select(x => x.Label));
    public ObservableCollection<string> ImportItems { get; } = new()
        { "Malzemeler", "Araçlar", "Personel", "Bakım", "Muayene / Sigorta", "Yakıt Dağıtım", "Yakıt Depo Girişi" };

    [ObservableProperty] private string _selectedExport = "Malzemeler";
    [ObservableProperty] private string _selectedImport = "Malzemeler";
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _importResult;

    /// <summary>İçe aktarım hedef şubesi (ZORUNLU). Id=null → "Tüm Şubeler" (firma geneli).
    /// SelectedImportBranch == null → henüz seçilmedi (import engellenir).</summary>
    public sealed record BranchOption(string? Id, string Name);
    public ObservableCollection<BranchOption> ImportBranches { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowImportBranchPassword))]
    private BranchOption? _selectedImportBranch;

    // ═══ ŞUBE ŞİFRESİ (kullanıcı isteği 2026-09-03) ══════════════════════════════════════════════
    // Oturumun çalışma şubesinden FARKLI gerçek bir şube seçilirse o şubenin ŞİFRESİ istenir
    // (girişteki L1/L2 kuralının aynısı). Kendi şubesinde ve "Tüm Şubeler"de alan GÖRÜNMEZ.
    // Şifresi tanımlı olmayan şube serbesttir (VerifyBranchPassword şifresizde true döner).

    [ObservableProperty] private string _importBranchPassword = "";
    public bool ShowImportBranchPassword =>
        SelectedImportBranch is { Id: not null } b && b.Id != _session.OperatingBranchId;

    /// <summary>Dışa aktarım şubesi (kullanıcı isteği 2026-09-03). Varsayılan = oturumun çalışma şubesi
    /// → seçime dokunulmazsa davranış BİREBİR eskisi gibidir.</summary>
    public ObservableCollection<BranchOption> ExportBranches { get; } = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExportBranchPassword))]
    private BranchOption? _selectedExportBranch;
    [ObservableProperty] private string _exportBranchPassword = "";
    public bool ShowExportBranchPassword =>
        SelectedExportBranch is { Id: not null } b && b.Id != _session.OperatingBranchId;

    public ImportExportViewModel(SessionContext session)
    {
        _session = session;
        // Zorunlu şube seçimi: "Tüm Şubeler" (firma geneli) + firmanın şubeleri. Varsayılan SEÇİLMEZ → kullanıcı bilinçli seçer.
        ImportBranches.Add(new BranchOption(null, "Tüm Şubeler"));
        try { foreach (var b in DesktopServices.Branches.List(_session)) ImportBranches.Add(new BranchOption(b.Id, b.Name)); } catch { }
        // Dışa aktarım şubeleri aynı listedir; varsayılan OTURUMUN şubesi (davranış değişmesin diye).
        foreach (var b in ImportBranches) ExportBranches.Add(b);
        SelectedExportBranch = ExportBranches.FirstOrDefault(x => x.Id == _session.OperatingBranchId) ?? ExportBranches[0];
    }

    /// <summary>Farklı şube seçildiyse ŞİFRE doğrulaması (yerel, çevrimdışı çalışır). Sorun yoksa null döner.</summary>
    private string? SubeSifreKontrol(BranchOption? secim, string sifre)
    {
        if (secim is not { Id: not null } b || b.Id == _session.OperatingBranchId) return null;   // kendi şubesi/Tüm Şubeler
        return DesktopServices.Branches.VerifyBranchPassword(_session.CompanyId, b.Id!, sifre)
            ? null
            : $"«{b.Name}» şube şifresi hatalı. Farklı bir şubeyle çalışmak için o şubenin şifresi gerekir.";
    }

    /// <summary>Oturumun ÇALIŞMA şubesi adı (uyarı metni için).</summary>
    private string CurrentBranchDisplay =>
        _session.OperatingBranchId is null ? "Tüm Şubeler"
        : ImportBranches.FirstOrDefault(x => x.Id == _session.OperatingBranchId)?.Name ?? "mevcut şube";

    /// <summary>İçe aktarımın seçilen hedef şubeyle çalışması için oturum kopyası (OperatingBranchId override).</summary>
    private SessionContext ImportSession(string? branchId) =>
        new(_session.UserId, _session.CompanyId, _session.RoleKeys, _session.Permissions, _session.CanViewAllBranches)
        {
            OperatingBranchId = branchId,
            BlockedModules = _session.BlockedModules,
            // ⚠️ ŞB-04 turunda görüldü: bu kopya ŞUBE KAPSAMINI taşımıyordu → içe aktarım yolunda
            // BranchAccess kullanıcıyı kısıtsız sayıyor, kapsam dışı şubeye kayıt basılabiliyordu.
            // (Web'deki aynı kopyada da aynı eksik vardı; ikisi birlikte kapatıldı.)
            ScopeBranchIds = _session.ScopeBranchIds,
            HomeBranchId = _session.HomeBranchId,
            BranchDescendants = _session.BranchDescendants,
        };

    [RelayCommand]
    private async Task Export()
    {
        // Deny-by-default: dışa aktarım ayrı yetki (2026-07-26).
        if (!AccessControl.Can(_session, "export", PermissionAction.View))
        { Status = "Dışa aktarım (export) yetkiniz yok."; return; }
        try
        {
            // Kaynak listesi/kolonlar ORTAK servisten (EXL-01) — kaynak modül yetkisi/tenant/BranchAccess
            // servis içinde uygulanır; yetkisiz kaynakta buradaki catch kullanıcıya nedeni gösterir.
            // 2026-09-03 (kullanıcı isteği): dışa aktarımda da şube seçilir; farklı şubede ŞİFRE doğrulanır.
            if (SubeSifreKontrol(SelectedExportBranch, ExportBranchPassword) is { } sifreHatasi)
            { Status = sifreHatasi; return; }
            var exportSession = SelectedExportBranch is null ? _session : ImportSession(SelectedExportBranch.Id);

            var src = ExcelCenterService.Sources.First(x => x.Label == SelectedExport);
            var table = DesktopServices.ExcelCenter.Build(exportSession, src.Key);
            var bytes = DesktopServices.Excel.Export(table);
            var path = await FilePickerService.SaveExcelAsync(src.FileName);
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
        // Deny-by-default: içe aktarım yetkisi (2026-07-26 — export'tan ayrı).
        if (!AccessControl.Can(_session, "import_export", PermissionAction.View))
        { ImportResult = "İçe aktarım (import) yetkiniz yok."; return; }
        try
        {
            // ZORUNLU şube seçimi: seçilmeden içe aktarım yapılamaz (kullanıcı isteği 2026-07-26).
            if (SelectedImportBranch is null)
            {
                ImportResult = "Lütfen önce içe aktarılacak ŞUBEYİ seçin (zorunlu). Tüm şubelerde görünmesi için 'Tüm Şubeler' seçin.";
                return;
            }
            var chosenBranchId = SelectedImportBranch.Id;   // null = Tüm Şubeler (firma geneli)

            // 2026-09-03 (kullanıcı isteği): farklı şubeye aktarım o şubenin ŞİFRESİYLE doğrulanır.
            if (SubeSifreKontrol(SelectedImportBranch, ImportBranchPassword) is { } sifreHatasi)
            { ImportResult = sifreHatasi; return; }

            // Farklı şube uyarısı: seçilen hedef, oturumun çalışma şubesinden farklıysa kullanıcı bilinçli onaylasın.
            if (chosenBranchId != _session.OperatingBranchId)
            {
                if (!await ConfirmService.AskAsync(
                        $"Çalışma şubeniz: «{CurrentBranchDisplay}».\nİçe aktarım «{SelectedImportBranch.Name}» şubesine yapılacak.\n\nFarklı bir şubeye aktarıyorsunuz — devam edilsin mi?",
                        "Farklı Şubeye İçe Aktarım", "Evet, Devam Et", "Vazgeç", danger: true))
                    return;
            }

            var path = await FilePickerService.PickFileAsync("İçe Aktarılacak Excel", "Excel", "*.xlsx");
            if (string.IsNullOrEmpty(path)) return;
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            var rows = DesktopServices.Excel.ReadRows(bytes);
            if (rows.Count == 0) { ImportResult = "Dosyada veri satırı bulunamadı."; return; }

            // Seçilen şubeyle oturum kopyası: işlem kayıtları (stok/yakıt/bakım/günlük) bu şubeyle etiketlenir;
            // araç/personel için satırda "Şube" boşsa bu şubeye düşer. "Tüm Şubeler" → şubesiz (firma geneli).
            var s = ImportSession(chosenBranchId);

            var dry = SelectedImport switch
            {
                "Araçlar" => DesktopServices.VehicleImport.DryRun(s, rows),
                "Bakım" => DesktopServices.MaintenanceImport.DryRun(s, rows),
                "Personel" => DesktopServices.PersonnelImport.DryRun(s, rows),
                "Muayene / Sigorta" => DesktopServices.InspectionImport.DryRun(s, rows),
                "Yakıt Dağıtım" => DesktopServices.FuelImport.DryRun(s, rows),
                "Yakıt Depo Girişi" => DesktopServices.FuelDepotImport.DryRun(s, rows),
                _ => DesktopServices.MaterialImport.DryRun(s, rows),
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
                    (res, createdLookups) = DesktopServices.VehicleImport.CommitWithLookups(s, rows); break;
                case "Bakım":
                    (res, createdLookups) = DesktopServices.MaintenanceImport.CommitWithLookups(s, rows); break;
                case "Personel":
                    (res, createdLookups) = DesktopServices.PersonnelImport.CommitWithLookups(s, rows); break;
                case "Muayene / Sigorta":
                    res = DesktopServices.InspectionImport.Commit(s, rows); break;
                case "Yakıt Dağıtım":
                    res = DesktopServices.FuelImport.Commit(s, rows); break;
                case "Yakıt Depo Girişi":
                    res = DesktopServices.FuelDepotImport.Commit(s, rows); break;
                default:
                    (res, createdLookups) = DesktopServices.MaterialImport.CommitWithLookups(s, rows); break;
            }
            // EXL-01 (PK-M5): HİÇBİR import mevcut kaydı GÜNCELLEMEZ — tüm servislerde "zaten var → atla".
            // "Updated" alanı atlanan sayıyı taşır; eski "güncellenen" etiketi yanıltıcıydı (R17), düzeltildi.
            ImportResult = $"İçe aktarım: toplam {res.Total}, eklenen {res.Added}, zaten mevcut (atlandı) {res.Updated}, hatalı {res.Failed}.";
            if (createdLookups.Count > 0)
            {
                ImportResult += $"\n\nOluşturulan yeni tanımlar ({createdLookups.Count}) — yazım hatası var mı diye kontrol edin:\n"
                    + string.Join("\n", createdLookups.Take(30).Select(x => "• " + x))
                    + (createdLookups.Count > 30 ? $"\n… ve {createdLookups.Count - 30} tanım daha (Tanımlar ekranından görün)" : "");
            }
            if (res.Errors.Count > 0)
                ImportResult += "\nHatalar:\n" + string.Join("\n", res.Errors.Select(e => e.RowNumber > 0 ? $"Satır {e.RowNumber}: {e.Message}" : e.Message));

            // İçe aktarılan veriyi HEMEN sunucuya gönder (kullanıcı bulgusu 2026-07-19: "içeri aldığım kayıtlar
            // aynı şubede başka makinede eşitlenmedi"). KÖK NEDEN: içe aktarım yerele yazıyordu ama push yalnız
            // periyodik (~3dk) / "Eşitle" / girişte oluyordu → kullanıcı makineyi kapatıp diğerine geçince veri
            // sunucuya HİÇ ulaşmıyordu (canlı doğrulandı: sunucuda 0 malzeme). Artık içe aktarım biter bitmez
            // push edilir ve sonuç KULLANICIYA gösterilir (sessiz başarısızlık olmaz).
            if (res.Added > 0 || res.Updated > 0)
            {
                Status = "İçe aktarıldı — veriler sunucuya gönderiliyor…";
                await BusinessSyncPushService.PushAsync();
                ImportResult += BusinessSyncPushService.LastPushFailed
                    ? "\n\n⚠️ Veriler sunucuya GÖNDERİLEMEDİ (çevrimdışı ya da zaman aşımı). İnternet bağlanınca " +
                      "üst bardaki “Eşitle”ye basın — yoksa aynı firmadaki başka makine/kullanıcı bu kayıtları göremez."
                    : "\n\n✔ Veriler sunucuya gönderildi. Aynı firmadaki başka makine/kullanıcı, girişte veya " +
                      "“Eşitle” ile bu kayıtları görebilir.";
            }
            Status = "İçe aktarım tamamlandı.";
        }
        catch (Exception ex) { ImportResult = "İçe aktarılamadı: " + ex.Message; }
    }

    private static IReadOnlyList<string> TemplateHeaders(string entity) => entity switch
    {
        "Araçlar" => DesktopServices.VehicleImport.SampleHeaders(),
        "Bakım" => DesktopServices.MaintenanceImport.SampleHeaders(),
        "Personel" => DesktopServices.PersonnelImport.SampleHeaders(),
        "Muayene / Sigorta" => DesktopServices.InspectionImport.SampleHeaders(),
        "Yakıt Dağıtım" => DesktopServices.FuelImport.SampleHeaders(),
        "Yakıt Depo Girişi" => DesktopServices.FuelDepotImport.SampleHeaders(),
        _ => DesktopServices.MaterialImport.SampleHeaders(),
    };

}
