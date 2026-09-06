using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.Views;

/// <summary>
/// Çift-tık ile açılan araç "hızlı düzenle" penceresi (kullanıcı isteği 2026-07-19): Düzelt / Kaydet / Sil.
/// Kod-arkası (DataContext bağlaması YOK) — ColumnPickerWindow ile aynı düşük-riskli desen. İç kod ve sayaç
/// bu pencerede DEĞİŞTİRİLMEZ (sayaç zaten Update ile güncellenmez — geri gitme koruması). Close: "saved"/"deleted"/null.
/// </summary>
public partial class VehicleQuickEditWindow : Window
{
    private sealed class Opt
    {
        public string Id { get; }
        public string Name { get; }
        public Opt(string id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    public VehicleQuickEditWindow() => InitializeComponent();

    public VehicleQuickEditWindow(SessionContext session, string vehicleId)
    {
        InitializeComponent();

        var canEdit = AccessControl.Can(session, "vehicles", PermissionAction.Edit);
        var canDelete = AccessControl.Can(session, "vehicles", PermissionAction.Delete);

        var plate = this.FindControl<TextBox>("PlateBox")!;
        var statusBox = this.FindControl<ComboBox>("StatusBox")!;
        var yearBox = this.FindControl<NumericUpDown>("YearBox")!;
        var noteBox = this.FindControl<TextBox>("NoteBox")!;
        // İş #9: sabit tanım (lookup) alanları ana ekranlarla AYNI ortak bileşene geçirildi (aranabilir +
        // sayfalı). "Durum" 3 sabit değerdir → ComboBox olarak KALIR (lookup değil, enum).
        var typeBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("TypeBox")!;
        var catBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("CatBox")!;
        var brandBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("BrandBox")!;
        var modelBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("ModelBox")!;
        var branchBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("BranchBox")!;
        var driverBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("DriverBox")!;
        var chassisBox = this.FindControl<TextBox>("ChassisBox")!;
        var engineBox = this.FindControl<TextBox>("EngineBox")!;
        var codeText = this.FindControl<SelectableTextBlock>("CodeText")!;
        var meterText = this.FindControl<SelectableTextBlock>("MeterText")!;
        var titleText = this.FindControl<SelectableTextBlock>("TitleText")!;
        var statusText = this.FindControl<SelectableTextBlock>("StatusText")!;
        var hintText = this.FindControl<SelectableTextBlock>("HintText")!;
        var cancelBtn = this.FindControl<Button>("CancelBtn")!;
        var deleteBtn = this.FindControl<Button>("DeleteBtn")!;
        var editBtn = this.FindControl<Button>("EditBtn")!;
        var saveBtn = this.FindControl<Button>("SaveBtn")!;

        yearBox.Minimum = FieldChecks.MinVehicleYear;
        yearBox.Maximum = FieldChecks.MaxVehicleYear;

        var statusOpts = VehicleStatus.All.Select(s => new Opt(s.Code, s.Label)).ToList();
        statusBox.ItemsSource = statusOpts;

        var types = Load(() => DesktopServices.Lookups.List(session, "vehicle_types"));
        var cats = Load(() => DesktopServices.Lookups.List(session, "vehicle_categories"));
        var brands = Load(() => DesktopServices.Lookups.ListBrands(session, "vehicle"));
        var branches = Load(() => DesktopServices.Lookups.List(session, "branches"));
        var drivers = Load(() => DesktopServices.Lookups.ListPersonnel(session));
        typeBox.ItemsSource = types; catBox.ItemsSource = cats; brandBox.ItemsSource = brands;
        branchBox.ItemsSource = branches; driverBox.ItemsSource = drivers;
        // LookupBox aramayı/sayfalamayı kendi yapar (LookupPaging) → ayrı AsyncPopulator gerekmez.

        VehicleDetail? d = null;
        try { d = DesktopServices.Vehicles.Get(session, vehicleId); } catch { }
        if (d is null)
        {
            statusText.Text = "Kayıt bulunamadı."; statusText.IsVisible = true;
            editBtn.IsVisible = false; deleteBtn.IsVisible = false;
            cancelBtn.Click += (_, _) => Close(null);
            return;
        }

        titleText.Text = $"{d.InternalCode}";
        codeText.Text = d.InternalCode;
        plate.Text = d.Plate ?? "";
        statusBox.SelectedItem = statusOpts.FirstOrDefault(o => o.Id == (string.IsNullOrEmpty(d.Status) ? "active" : d.Status));
        yearBox.Value = d.ProductionYear;
        noteBox.Text = d.StatusNote ?? "";
        typeBox.SelectedItem = types.FirstOrDefault(o => o.Id == d.VehicleTypeId);
        catBox.SelectedItem = cats.FirstOrDefault(o => o.Id == d.CategoryId);
        chassisBox.Text = d.ChassisNo ?? ""; engineBox.Text = d.EngineNo ?? "";
        meterText.Text = $"Sayaç: {d.CurrentMeter:0.##} {DepoWise.Application.Ui.MeterUnitOptions.Label(d.MeterUnit)}";   // 2026-09-03

        // Marka + Model (kademeli): önce markanın modellerini yükle, modeli seç, SONRA marka değişimini dinle.
        void LoadModels(string? brandId)
        {
            var models = string.IsNullOrEmpty(brandId)
                ? new List<Opt>()
                : Load(() => DesktopServices.Lookups.ListVehicleModels(session, brandId!));
            modelBox.ItemsSource = models;
            return;
        }
        brandBox.SelectedItem = brands.FirstOrDefault(o => o.Id == d.BrandId);
        LoadModels(d.BrandId);
        modelBox.SelectedItem = (modelBox.ItemsSource as List<Opt>)?.FirstOrDefault(o => o.Id == d.VehicleModelId);
        branchBox.SelectedItem = branches.FirstOrDefault(o => o.Id == d.BranchId);
        driverBox.SelectedItem = drivers.FirstOrDefault(o => o.Id == d.DriverPersonnelId);
        // Marka değişince modeller yenilenir (yalnız düzenleme sırasında kullanıcı değiştirirse tetiklenir).
        brandBox.SelectionChanged += (_, _) =>
        {
            LoadModels((brandBox.SelectedItem as Opt)?.Id);
            modelBox.SelectedItem = null;
        };

        var editable = new Control[] { plate, statusBox, yearBox, noteBox, typeBox, catBox, brandBox, modelBox, branchBox, driverBox, chassisBox, engineBox };
        void SetLocked(bool locked) { foreach (var c in editable) c.IsEnabled = !locked; }
        SetLocked(true);
        editBtn.IsVisible = canEdit;
        deleteBtn.IsVisible = canDelete;

        editBtn.Click += (_, _) =>
        {
            SetLocked(false);
            editBtn.IsVisible = false;
            saveBtn.IsVisible = true;
            hintText.IsVisible = true;
        };

        saveBtn.Click += async (_, _) =>
        {
            statusText.IsVisible = false;
            if (branchBox.SelectedItem is not Opt branchOpt)
            { statusText.Text = "Şantiye / şube seçimi zorunludur."; statusText.IsVisible = true; return; }
            var statusCode = (statusBox.SelectedItem as Opt)?.Id ?? "active";
            // Yumuşak uyarı — ANA FORMLA PARİTE (VehiclesViewModel): plaka standart Türk biçimine uymuyorsa
            // sor (iş makinesi/plakasız araç için kullanıcı geçebilir). Sayaç uyarısı burada YOK çünkü sayaç
            // bu pencerede salt-okunur (değiştirilemez).
            if (!DepoWise.Application.Ui.FieldChecks.PlateLooksTurkish(plate.Text)
                && !await ConfirmService.AskAsync(this, "Plaka standart Türk plaka biçimine (34 ABC 123) uymuyor. İş makinesi/plakasız araç ise geçebilirsiniz.\n\nYine de kaydedilsin mi?", "Plaka Uyarısı", "Evet, Kaydet")) return;
            // Onay penceresi (kullanıcı isteği 2026-07-19) — bu pencerenin ÜZERİNDE (owner=this).
            if (!await ConfirmService.AskAsync(this, "Araç bilgileri güncellensin mi?", "Kaydet")) return;
            try
            {
                DesktopServices.Vehicles.Update(session, vehicleId, new UpdateVehicle(
                    Plate: string.IsNullOrWhiteSpace(plate.Text) ? null : plate.Text!.Trim(),
                    ProductionYear: yearBox.Value is { } yv ? (int)yv : (int?)null,
                    Status: statusCode,
                    StatusNote: VehicleStatus.NeedsNote(statusCode) && !string.IsNullOrWhiteSpace(noteBox.Text) ? noteBox.Text!.Trim() : null,
                    ChassisNo: string.IsNullOrWhiteSpace(chassisBox.Text) ? null : chassisBox.Text!.Trim(),
                    EngineNo: string.IsNullOrWhiteSpace(engineBox.Text) ? null : engineBox.Text!.Trim(),
                    VehicleTypeId: (typeBox.SelectedItem as Opt)?.Id,
                    CategoryId: (catBox.SelectedItem as Opt)?.Id,
                    BrandId: (brandBox.SelectedItem as Opt)?.Id,
                    VehicleModelId: (modelBox.SelectedItem as Opt)?.Id,
                    BranchId: branchOpt.Id,
                    DriverPersonnelId: (driverBox.SelectedItem as Opt)?.Id),
                    // DÜZENLEME KİLİDİ: pencere açıldığındaki sürüm — kayıt arada değiştiyse üzerine yazma.
                    expectedVersion: d.Version);
                Close("saved");
            }
            catch (DepoWise.Application.Security.ConcurrencyException ex)
            {
                statusText.Text = ex.Message; statusText.IsVisible = true;
                if (await ConfirmService.AskAsync(this,
                        ex.Message + "\n\nPencereyi kapatıp kaydı güncel hâliyle yeniden açmak ister misiniz? " +
                        "(\"Formda kal\" derseniz yazdıklarınız durur.)",
                        "Kayıt değişti", okText: "Kapat ve yenile", cancelText: "Formda kal"))
                    Close("stale");
            }
            catch (Exception ex) { statusText.Text = "Güncellenemedi: " + ex.Message; statusText.IsVisible = true; }
        };

        deleteBtn.Click += async (_, _) =>
        {
            statusText.IsVisible = false;
            if (!await ConfirmService.AskAsync(this, $"'{d.InternalCode}' aracı silinsin mi? Kayıt çöp kutusuna alınır.",
                    "Araç Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
            try { DesktopServices.Vehicles.Delete(session, vehicleId); Close("deleted"); }
            catch (Exception ex) { statusText.Text = "Silinemedi: " + ex.Message; statusText.IsVisible = true; }
        };

        cancelBtn.Click += (_, _) => Close(null);

        // Fotoğraflar sunucudan ASENKRON gelir (pencere açılışı bloklanmaz). Hata olursa bölüm gizli kalır.
        _ = FotograflariYukleAsync(session, vehicleId);

        // ⭐ FAZ 4.7: sağ bilgi panelindeki veriler EK SEKMELER olarak yüklenir (yerel; anında).
        SekmeVerileriniYukle(session, vehicleId, codeText.Text ?? "");
    }

    /// <summary>
    /// ⭐ FAZ 4.7 (kullanıcı isteği 2026-09-06) — SEKME VERİLERİ.
    ///
    /// Kullanıcı: <i>"…tabloda 1 kez sol tık yaptığımda sağda bulunan bilgi panelindeki verilerin
    /// EK OLARAK sekmeler hâlinde bu pencerede görüntülenmesini istiyorum."</i>
    ///
    /// Sağ paneldeki dört liste (uyumlu malzemeler · muayene/sigorta · bakımlar · işlem geçmişi)
    /// burada da gösterilir. Veriler AYNI servislerden okunur — ikinci bir veri yolu kurulmadı.
    /// Salt-okunurdur; ekleme/silme kendi ekranlarındadır.
    ///
    /// ⚠️ Her liste ayrı try/catch: biri (ör. çevrimdışı bir uç) hata verse bile pencere açılır ve
    /// diğer sekmeler çalışır — kullanıcı boş bir pencereyle kalmaz.
    /// </summary>
    private void SekmeVerileriniYukle(SessionContext session, string vehicleId, string vehicleCode)
    {
        void Doldur<T>(string listeAdi, string bosAdi, System.Func<System.Collections.Generic.IEnumerable<T>> getir,
            System.Func<T, string> metin)
        {
            var liste = this.FindControl<ListBox>(listeAdi);
            var bos = this.FindControl<SelectableTextBlock>(bosAdi);
            if (liste is null) return;
            var satirlar = new System.Collections.Generic.List<string>();
            try { foreach (var x in getir()) satirlar.Add(metin(x)); }
            catch { satirlar.Clear(); }
            liste.ItemsSource = satirlar;
            if (bos is not null) bos.IsVisible = satirlar.Count == 0;
        }

        Doldur("MaterialsList", "MaterialsEmpty",
            () => DesktopServices.Materials.MaterialsForVehicle(session, vehicleId),
            m => $"{m.Code} — {m.Name}  ·  stok: {m.Quantity:0.##}");

        Doldur("InspectionsList", "InspectionsEmpty",
            () => DesktopServices.Inspection.List(session).Where(x => x.VehicleCode == vehicleCode),
            i => $"{i.DocTypeText}: son {i.LastText} · sonraki {i.NextText}");

        Doldur("MaintenancesList", "MaintenancesEmpty",
            () => DesktopServices.Maintenance.ListMaintenances(session, vehicleId),
            b => $"{b.PerformedDisplay} — {b.DefinitionName} ({b.StatusText})");

        // İşlem geçmişi = Günlük Faaliyet hareketleri + sistem olayları (araç ekranındaki panelle AYNI kaynak).
        var gecmis = new System.Collections.Generic.List<(long Tarih, string Metin)>();
        try
        {
            foreach (var mv in DesktopServices.DailyActivity.GetForVehicle(session, vehicleId, "movement"))
                gecmis.Add((mv.ActivityDate,
                    $"{System.DateTimeOffset.FromUnixTimeMilliseconds(mv.ActivityDate).LocalDateTime:dd.MM.yyyy} · " +
                    (mv.MovementKind == "transfer" ? "Transfer" : "Hareket") + " — " + (mv.Description ?? "")));
        }
        catch { }
        try
        {
            foreach (var h in DesktopServices.Vehicles.RecentHistory(session, vehicleId, 100))
                gecmis.Add((h.Date, $"{h.DateText} · Sistem — {(h.Detail is null ? h.Label : h.Label + " (" + h.Detail + ")")}"));
        }
        catch { }
        var gecmisListe = this.FindControl<ListBox>("HistoryList");
        var gecmisBos = this.FindControl<SelectableTextBlock>("HistoryEmpty");
        if (gecmisListe is not null)
        {
            var satirlar = gecmis.OrderByDescending(x => x.Tarih).Select(x => x.Metin).ToList();
            gecmisListe.ItemsSource = satirlar;
            if (gecmisBos is not null) gecmisBos.IsVisible = satirlar.Count == 0;
        }
    }

    /// <summary>Kullanıcı isteği (2026-09-02): çift-tık penceresinde de fotoğraflar görünür.
    /// Kaynak SUNUCUDUR (ADR-182) → başka bilgisayarda eklenen fotoğraf da gelir. Salt görüntüleme.</summary>
    private async Task FotograflariYukleAsync(SessionContext session, string vehicleId)
    {
        var section = this.FindControl<StackPanel>("PhotoSection");
        var panel = this.FindControl<StackPanel>("PhotoPanel");
        var note = this.FindControl<SelectableTextBlock>("PhotoNote");
        if (section is null || panel is null || note is null) return;
        try
        {
            var (fotograflar, cevrimdisi) = await DesktopPhotos.YukleAsync(session, "vehicle", vehicleId);
            panel.Children.Clear();
            foreach (var f in fotograflar)
            {
                try
                {
                    panel.Children.Add(new Avalonia.Controls.Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(f.Bytes)),
                        Height = 110,
                        Stretch = Avalonia.Media.Stretch.Uniform,
                    });
                }
                catch { /* bozuk görsel atlanır */ }
            }
            // ⭐ FAZ 4.7: fotoğraflar artık kendi SEKMESİNDE. Sekme daima durur; boşsa bilgi yazılır
            // (eskiden bölüm tamamen gizleniyordu ve kullanıcı "fotoğraf yok mu, yüklenmedi mi" bilemiyordu).
            note.Text = cevrimdisi
                ? "Çevrimdışı: yalnız bu bilgisayardaki fotoğraflar gösteriliyor."
                : (panel.Children.Count == 0 ? "Bu araca ait fotoğraf yok." : "");
            note.IsVisible = note.Text.Length > 0;
            section.IsVisible = true;
        }
        catch { /* fotoğraf yüklenemedi → bölüm gizli kalır, düzenleme akışı etkilenmez */ }
    }

    private static List<Opt> Load(Func<IReadOnlyList<LookupItem>> get)
    {
        try { return get().Select(x => new Opt(x.Id, x.Name)).ToList(); }
        catch { return new List<Opt>(); }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
