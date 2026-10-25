using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ═══ M-58 (AR-GE 2026-10-25, F-122) — KAYITLI RAPOR GÖRÜNÜMÜ + PDF ═══
/// Seçili rapor ve filtreleri bir adla kaydeder; listeden seçilince filtreler geri yüklenir ve rapor HEMEN
/// çalışır (tek tık). Model ve saklama biçimi ortaktır (<see cref="RaporGorunumleri"/>): yerel tercih + çevrimiçiyse
/// sunucu (web aynı kaydı okur). Tarih aralığı saklanmaz — raporun varsayılanı gelir. Yüklenen kimlikler YALNIZ
/// ekranda var olan seçeneklerde işaretlenir; kapsam yetkisi yine servis tarafında uygulanır.
/// PDF: Excel ile AYNI tablo ve AYNI dışa aktarma yetkisi (sunucudaki <c>?format=pdf</c> ile eşdeğer).
/// </summary>
public sealed partial class ReportsViewModel
{
    public ObservableCollection<RaporGorunumu> Gorunumler { get; } = new();
    [ObservableProperty] private RaporGorunumu? _seciliGorunum;
    [ObservableProperty] private string _gorunumAdi = "";
    public bool HasGorunum => Gorunumler.Count > 0;

    private void GorunumleriYukle()
    {
        try { Doldur(RaporGorunumleri.Coz(DesktopServices.ListPrefs.GetColumns(_session, RaporGorunumleri.PrefKey))); }
        catch { /* yerel tercih okunamadı → liste boş, ekran çalışır */ }
        _ = SunucudanKolonAynalaAsync(RaporGorunumleri.PrefKey, uzak =>
        {
            try { DesktopServices.ListPrefs.SaveColumns(_session, RaporGorunumleri.PrefKey, uzak); } catch { }
            Doldur(RaporGorunumleri.Coz(uzak));
        });
    }

    private void Doldur(IEnumerable<RaporGorunumu> liste)
    {
        Gorunumler.Clear();
        foreach (var g in liste) Gorunumler.Add(g);
        OnPropertyChanged(nameof(HasGorunum));
    }

    private void Sakla(List<RaporGorunumu> liste)
    {
        var yazi = RaporGorunumleri.Yaz(liste);
        DesktopServices.ListPrefs.SaveColumns(_session, RaporGorunumleri.PrefKey, yazi);
        _ = ServerListPrefsClient.SaveColumnsAsync(RaporGorunumleri.PrefKey, yazi);   // çevrimdışıysa sessizce atlanır
        Doldur(liste);
    }

    /// <summary>Ekrandaki rapor + filtre seçiminden görünüm üretir (yalnız o raporun kullandığı filtreler).</summary>
    internal RaporGorunumu MevcutGorunum(string ad)
    {
        var g = new RaporGorunumu { Ad = ad, Rapor = SelectedReport.Key };
        static IEnumerable<string> Isaretli(IEnumerable<BranchPick> l) => l.Where(x => x.IsChecked).Select(x => x.Id);
        if (ShowBranchSelect) g.Ekle(RaporGorunumleri.Alan.Branch, Isaretli(Branches));
        if (ShowVehicleSelect) g.Ekle(RaporGorunumleri.Alan.Vehicle, VehiclePicks.Where(p => p.IsSelected).Select(p => p.Id));
        if (ShowVehicleType) g.Ekle(RaporGorunumleri.Alan.VehicleType, Isaretli(VehicleTypes));
        if (ShowMaintenanceDef) g.Ekle(RaporGorunumleri.Alan.MaintenanceDef, Isaretli(MaintenanceDefs));
        if (ShowTechnician) g.Ekle(RaporGorunumleri.Alan.Technician, Isaretli(Technicians));
        if (ShowSupplier) g.Ekle(RaporGorunumleri.Alan.Supplier, Isaretli(Suppliers));
        if (ShowRequester) g.Ekle(RaporGorunumleri.Alan.Requester, Isaretli(Requesters));
        if (ShowStatus) g.Ekle(RaporGorunumleri.Alan.Status, Isaretli(Statuses));
        if (ShowRecipient) g.Ekle(RaporGorunumleri.Alan.Recipient, Isaretli(Recipients));
        if (ShowLocation) g.Ekle(RaporGorunumleri.Alan.Location, Isaretli(Locations));
        if (ShowMovementType) g.Ekle(RaporGorunumleri.Alan.MovementType, Isaretli(MovementTypes));
        if (ShowActivityType) g.Ekle(RaporGorunumleri.Alan.ActivityType, Isaretli(ActivityTypes));
        if (ShowMaterial)
        {
            g.Ekle(RaporGorunumleri.Alan.Material, PickedMaterials.Select(m => m.Id));
            foreach (var m in PickedMaterials) g.Etiket[m.Id] = m.Code + "\u001F" + m.Name;
        }
        if (ShowParty && PickedParty is not null)
        {
            g.Ekle(RaporGorunumleri.Alan.Party, new[] { PickedParty.Id });
            g.Etiket[PickedParty.Id] = PickedParty.Code + "\u001F" + PickedParty.Title;
        }
        if (ShowSearch && !string.IsNullOrWhiteSpace(SearchText)) g.Arama = SearchText.Trim();
        if (ShowSort && SelectedSort is not null) g.Siralama = SelectedSort.Id;
        return g;
    }

    [RelayCommand]
    private void GorunumKaydet()
    {
        try
        {
            var g = MevcutGorunum(GorunumAdi);
            Sakla(RaporGorunumleri.Kaydet(Gorunumler, g));
            SeciliGorunum = null;
            Status = $"Görünüm kaydedildi: {g.Ad}";
            GorunumAdi = "";
        }
        catch (ArgumentException ex) { Status = ex.Message; }
        catch (Exception ex) { Status = "Görünüm kaydedilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SilGorunum()
    {
        if (SeciliGorunum is not { } g) { Status = "Silmek için listeden bir görünüm seçin."; return; }
        if (!await ConfirmService.ConfirmDeleteAsync($"«{g.Ad}» görünümü")) return;
        try
        {
            Sakla(RaporGorunumleri.Sil(Gorunumler, g.Ad));
            SeciliGorunum = null;
            Status = $"Görünüm silindi: {g.Ad}";
        }
        catch (Exception ex) { Status = "Görünüm silinemedi: " + ex.Message; }
    }

    partial void OnSeciliGorunumChanged(RaporGorunumu? value)
    {
        if (value is not null) GorunumUygula(value, calistir: true);
    }

    /// <summary>Görünümü ekrana uygular. Rapor bu ekranda yoksa (ör. yönetici raporu operasyon ekranında) uyarır.</summary>
    internal bool GorunumUygula(RaporGorunumu g, bool calistir)
    {
        var rapor = ReportItems.FirstOrDefault(r => r.Key == g.Rapor);
        if (rapor is null) { Status = $"«{g.Ad}» görünümünün raporu bu ekranda yok ya da yetkiniz kalmadı."; return false; }
        SelectedReport = rapor;
        static void Isaretle(IEnumerable<BranchPick> l, IReadOnlyList<string> ids)
        {
            var set = new HashSet<string>(ids, StringComparer.Ordinal);
            foreach (var x in l) x.IsChecked = set.Contains(x.Id);
        }
        Isaretle(Branches, g.Kimlikler(RaporGorunumleri.Alan.Branch));
        var araclar = new HashSet<string>(g.Kimlikler(RaporGorunumleri.Alan.Vehicle), StringComparer.Ordinal);
        foreach (var p in VehiclePicks) p.IsSelected = araclar.Contains(p.Id);
        Isaretle(VehicleTypes, g.Kimlikler(RaporGorunumleri.Alan.VehicleType));
        Isaretle(MaintenanceDefs, g.Kimlikler(RaporGorunumleri.Alan.MaintenanceDef));
        Isaretle(Technicians, g.Kimlikler(RaporGorunumleri.Alan.Technician));
        Isaretle(Suppliers, g.Kimlikler(RaporGorunumleri.Alan.Supplier));
        Isaretle(Requesters, g.Kimlikler(RaporGorunumleri.Alan.Requester));
        Isaretle(Statuses, g.Kimlikler(RaporGorunumleri.Alan.Status));
        Isaretle(Recipients, g.Kimlikler(RaporGorunumleri.Alan.Recipient));
        Isaretle(Locations, g.Kimlikler(RaporGorunumleri.Alan.Location));
        Isaretle(MovementTypes, g.Kimlikler(RaporGorunumleri.Alan.MovementType));
        Isaretle(ActivityTypes, g.Kimlikler(RaporGorunumleri.Alan.ActivityType));
        PickedMaterials.Clear();
        foreach (var id in g.Kimlikler(RaporGorunumleri.Alan.Material))
        {
            var (kod, ad) = EtiketCoz(g, id);
            PickedMaterials.Add(new MaterialRefRow(id, kod, ad));
        }
        var cari = g.Kimlikler(RaporGorunumleri.Alan.Party).FirstOrDefault();
        PickedParty = cari is null ? null : EtiketCoz(g, cari) is var (ck, ct) ? new PartyRefRow(cari, ck, ct) : null;
        SearchText = g.Arama ?? "";
        if (g.Siralama is { } sk && SortOptions.FirstOrDefault(o => o.Id == sk) is { } so) SelectedSort = so;
        if (calistir) RunCommand.Execute(null);
        return true;
    }

    private static (string Kod, string Ad) EtiketCoz(RaporGorunumu g, string id)
    {
        if (!g.Etiket.TryGetValue(id, out var e)) return ("", id);
        var i = e.IndexOf('\u001F');
        return i < 0 ? ("", e) : (e[..i], e[(i + 1)..]);
    }

    /// <summary>Seçili raporu PDF'e aktarır — Excel ile AYNI tablo ve AYNI özel buton yetkisi.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ExportPdfAsync()
    {
        var button = SelectedReport.IsManager ? SpecialButtons.ExportManagerReports : SpecialButtons.ExportReports;
        if (!AccessControl.CanUseButton(_session, button))
        {
            Status = "Dışa aktarma (PDF) yetkiniz yok. Yetki için firma yöneticinize başvurun.";
            return;
        }
        try
        {
            var table = BuildTable();
            var hedef = await YazdirmaYardimcisi.YazdirAsync(table, _session, dosyaAdi: SafeFileName(table.Title) + ".pdf");
            if (hedef is null) return;   // kaydetme penceresi iptal edildi — hata değil
            DepoWise.Desktop.FilePickerService.OpenFile(hedef);
            Status = $"PDF kaydedildi: {table.Title}";
        }
        catch (Exception ex) { Status = "PDF üretilemedi: " + ex.Message; }
    }
}
