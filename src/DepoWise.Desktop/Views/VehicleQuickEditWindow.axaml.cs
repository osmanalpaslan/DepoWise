using System;
using System.Collections.Generic;
using System.Linq;
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
        var typeBox = this.FindControl<ComboBox>("TypeBox")!;
        var catBox = this.FindControl<ComboBox>("CatBox")!;
        var brandBox = this.FindControl<ComboBox>("BrandBox")!;
        var modelBox = this.FindControl<ComboBox>("ModelBox")!;
        var branchBox = this.FindControl<ComboBox>("BranchBox")!;
        var driverBox = this.FindControl<AutoCompleteBox>("DriverBox")!;
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
        meterText.Text = $"Sayaç: {d.CurrentMeter:0.##} {d.MeterUnit}";

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
    }

    private static List<Opt> Load(Func<IReadOnlyList<LookupItem>> get)
    {
        try { return get().Select(x => new Opt(x.Id, x.Name)).ToList(); }
        catch { return new List<Opt>(); }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
