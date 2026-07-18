using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.Views;

/// <summary>
/// Çift-tık ile açılan malzeme "hızlı düzenle" penceresi (kullanıcı isteği 2026-07-19): Düzelt / Kaydet / Sil.
/// "Düzelt"e basılana kadar alanlar KİLİTLİ. Kod-arkası (DataContext bağlaması YOK) — ColumnPickerWindow ile
/// aynı düşük-riskli desen. NOT: fotoğraf/muadil/uyumlu araçlar KORUNUR (bu pencerede değişmez).
/// Close değeri: "saved" / "deleted" / null.
/// </summary>
public partial class MaterialQuickEditWindow : Window
{
    private sealed class Opt
    {
        public string Id { get; }
        public string Name { get; }
        public Opt(string id, string name) { Id = id; Name = name; }
        public override string ToString() => Name;
    }

    public MaterialQuickEditWindow() => InitializeComponent();

    public MaterialQuickEditWindow(SessionContext session, string materialId)
    {
        InitializeComponent();

        var canEdit = AccessControl.Can(session, "materials", PermissionAction.Edit);
        var canDelete = AccessControl.Can(session, "materials", PermissionAction.Delete);

        var code = this.FindControl<TextBox>("CodeBox")!;
        var name = this.FindControl<TextBox>("NameBox")!;
        var typeBox = this.FindControl<ComboBox>("TypeBox")!;
        var catBox = this.FindControl<ComboBox>("CatBox")!;
        var unitBox = this.FindControl<ComboBox>("UnitBox")!;
        var brandBox = this.FindControl<ComboBox>("BrandBox")!;
        var supBox = this.FindControl<ComboBox>("SupBox")!;
        var minBox = this.FindControl<NumericUpDown>("MinBox")!;
        var priceBox = this.FindControl<NumericUpDown>("PriceBox")!;
        var descBox = this.FindControl<TextBox>("DescBox")!;
        var stockText = this.FindControl<SelectableTextBlock>("StockText")!;
        var titleText = this.FindControl<SelectableTextBlock>("TitleText")!;
        var statusText = this.FindControl<SelectableTextBlock>("StatusText")!;
        var hintText = this.FindControl<SelectableTextBlock>("HintText")!;
        var cancelBtn = this.FindControl<Button>("CancelBtn")!;
        var deleteBtn = this.FindControl<Button>("DeleteBtn")!;
        var editBtn = this.FindControl<Button>("EditBtn")!;
        var saveBtn = this.FindControl<Button>("SaveBtn")!;

        // Tür seçenekleri (Malzeme formuyla aynı liste)
        typeBox.ItemsSource = new[] { "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer" };

        // Tanımlar (hepsi düz liste — alt kategoriler de dâhil, mevcut değeri seçebilmek için)
        var cats = Load(() => DesktopServices.Lookups.List(session, "material_categories"));
        var units = Load(() => DesktopServices.Lookups.List(session, "units"));
        var brands = Load(() => DesktopServices.Lookups.ListBrands(session, "material"));
        var sups = Load(() => DesktopServices.Lookups.List(session, "suppliers"));
        catBox.ItemsSource = cats; unitBox.ItemsSource = units; brandBox.ItemsSource = brands; supBox.ItemsSource = sups;

        // Kaydı yükle
        MaterialDetail? d = null;
        try { d = DesktopServices.Materials.GetDetail(session, materialId); } catch { }
        if (d is null)
        {
            statusText.Text = "Kayıt bulunamadı."; statusText.IsVisible = true;
            editBtn.IsVisible = false; deleteBtn.IsVisible = false;
            cancelBtn.Click += (_, _) => Close(null);
            return;
        }

        titleText.Text = $"{d.Code} — {d.Name}";
        code.Text = d.Code; name.Text = d.Name;
        typeBox.SelectedItem = string.IsNullOrWhiteSpace(d.Type) ? "Yedek Parça" : d.Type;
        catBox.SelectedItem = cats.FirstOrDefault(o => o.Id == d.CategoryId);
        unitBox.SelectedItem = units.FirstOrDefault(o => o.Id == d.UnitId);
        brandBox.SelectedItem = brands.FirstOrDefault(o => o.Id == d.BrandId);
        supBox.SelectedItem = sups.FirstOrDefault(o => o.Id == d.SupplierId);
        minBox.Value = d.MinStock; priceBox.Value = d.UnitPrice;
        descBox.Text = d.Description ?? "";
        stockText.Text = d.Stock.ToString("0.##");

        // Başlangıçta KİLİTLİ (salt-okunur)
        var editable = new Control[] { code, name, typeBox, catBox, unitBox, brandBox, supBox, minBox, priceBox, descBox };
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

        saveBtn.Click += (_, _) =>
        {
            statusText.IsVisible = false;
            var codeVal = (code.Text ?? "").Trim();
            var nameVal = (name.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(codeVal) || string.IsNullOrWhiteSpace(nameVal))
            { statusText.Text = "Kod ve ad zorunlu."; statusText.IsVisible = true; return; }
            if (unitBox.SelectedItem is not Opt unitOpt)
            { statusText.Text = "Birim seçin."; statusText.IsVisible = true; return; }
            try
            {
                DesktopServices.Materials.Update(session, materialId, new UpdateMaterial(
                    Code: codeVal, Name: nameVal,
                    Type: typeBox.SelectedItem as string,
                    CategoryId: (catBox.SelectedItem as Opt)?.Id,
                    UnitId: unitOpt.Id,
                    BrandId: (brandBox.SelectedItem as Opt)?.Id,
                    SupplierId: (supBox.SelectedItem as Opt)?.Id,
                    MinStock: (decimal)(minBox.Value ?? 0),
                    UnitPrice: (decimal)(priceBox.Value ?? 0),
                    Description: string.IsNullOrWhiteSpace(descBox.Text) ? null : descBox.Text!.Trim()));
                // Uyumlu araçlar / muadiller / fotoğraflar DEĞİŞTİRİLMEZ (korunur).
                Close("saved");
            }
            catch (Exception ex) { statusText.Text = "Güncellenemedi: " + ex.Message; statusText.IsVisible = true; }
        };

        // İki aşamalı silme (iç içe modal açmadan onay): ilk tık uyarır, ikinci tık siler.
        var deleteArmed = false;
        deleteBtn.Click += (_, _) =>
        {
            if (!deleteArmed)
            {
                deleteArmed = true;
                deleteBtn.Content = "Emin misiniz? Tekrar Sil";
                return;
            }
            try { DesktopServices.Materials.Delete(session, materialId); Close("deleted"); }
            catch (Exception ex) { statusText.Text = "Silinemedi: " + ex.Message; statusText.IsVisible = true; deleteArmed = false; deleteBtn.Content = "Sil"; }
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
