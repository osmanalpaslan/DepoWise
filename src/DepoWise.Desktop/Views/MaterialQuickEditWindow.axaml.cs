using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;

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

    // Son Hareketler paneli satırı (salt görüntü — biçimlendirilmiş).
    private sealed class MoveRow
    {
        public string DateStr { get; init; } = "";
        public string Label { get; init; } = "";
        public string QtyStr { get; init; } = "";
        public string? Reference { get; init; }
        public bool HasRef => !string.IsNullOrEmpty(Reference);
        public IBrush? QtyBrush { get; init; }
    }

    public MaterialQuickEditWindow() => InitializeComponent();

    public MaterialQuickEditWindow(SessionContext session, string materialId)
    {
        InitializeComponent();

        var canEdit = AccessControl.Can(session, "materials", PermissionAction.Edit);
        var canDelete = AccessControl.Can(session, "materials", PermissionAction.Delete);
        // madde 1.2/1.3: stok yalnız "stock" Create yetkisi olan kullanıcı DOĞRUDAN değiştirebilir (değişiklik
        // stok SAYIM/DÜZELTME hareketiyle uygulanır). Yetki yoksa alan salt-okunur gösterilir.
        var canEditStock = AccessControl.Can(session, "stock", PermissionAction.Create);

        var code = this.FindControl<TextBox>("CodeBox")!;
        var name = this.FindControl<TextBox>("NameBox")!;
        var typeBox = this.FindControl<ComboBox>("TypeBox")!;
        // İş #9: sabit tanım (lookup) alanları ana ekranlarla AYNI ortak bileşene geçirildi (aranabilir +
        // sayfalı). "Tür" 5 sabit değerdir → ComboBox olarak KALIR (lookup değil, enum).
        var catBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("CatBox")!;
        var subCatBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("SubCatBox")!;
        var unitBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("UnitBox")!;
        var brandBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("BrandBox")!;
        var supBox = this.FindControl<DepoWise.Desktop.Controls.LookupBox>("SupBox")!;
        var minBox = this.FindControl<NumericUpDown>("MinBox")!;
        var priceBox = this.FindControl<NumericUpDown>("PriceBox")!;
        var descBox = this.FindControl<TextBox>("DescBox")!;
        var stockBox = this.FindControl<NumericUpDown>("StockBox")!;
        var titleText = this.FindControl<SelectableTextBlock>("TitleText")!;
        var statusText = this.FindControl<SelectableTextBlock>("StatusText")!;
        var hintText = this.FindControl<SelectableTextBlock>("HintText")!;
        var cancelBtn = this.FindControl<Button>("CancelBtn")!;
        var deleteBtn = this.FindControl<Button>("DeleteBtn")!;
        var editBtn = this.FindControl<Button>("EditBtn")!;
        var saveBtn = this.FindControl<Button>("SaveBtn")!;

        // Tür seçenekleri (Malzeme formuyla aynı liste)
        typeBox.ItemsSource = new[] { "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer" };

        // Kategori YALNIZ üst-seviye (parent NULL); alt kategori ayrı kutuda, seçili kategoriye göre (madde 6).
        var topCats = Load(() => DesktopServices.Lookups.ListCategories(session, null));
        var units = Load(() => DesktopServices.Lookups.List(session, "units"));
        var brands = Load(() => DesktopServices.Lookups.ListBrands(session, "material"));
        var sups = Load(() => DesktopServices.Lookups.List(session, "suppliers"));
        catBox.ItemsSource = topCats; unitBox.ItemsSource = units; brandBox.ItemsSource = brands; supBox.ItemsSource = sups;

        // Kategori değişince alt kategori listesi o kategoriye göre yenilenir (kullanıcı seçiminde).
        bool resolvingCat = true;
        catBox.SelectionChanged += (_, _) =>
        {
            if (resolvingCat) return;
            var cid = (catBox.SelectedItem as Opt)?.Id;
            subCatBox.ItemsSource = cid is null ? new List<Opt>() : Load(() => DesktopServices.Lookups.ListCategories(session, cid));
            subCatBox.SelectedItem = null;
        };

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
        // G2-07 (2026-08-10): kaydın türü BOŞ ise "Diğer" gösterilir — "Yedek Parça" GERÇEK bir türdür ve
        // kullanıcı hiçbir şey değiştirmeden kaydettiğinde boş veri sessizce o sınıfa yazılırdı.
        // "Diğer" kanonik listede zaten var (MaterialType.Canonical) ve doğal "sınıflandırılmamış" kovasıdır.
        // Aynı kural dört malzeme kartı düzenleme ekranında da uygulanır. YENİ KAYIT varsayılanı DEĞİŞMEDİ.
        typeBox.SelectedItem = string.IsNullOrWhiteSpace(d.Type) ? "Diğer" : d.Type;
        // Mevcut category_id üst-seviye mi alt kategori mi? (parent'ı tara) — kutulara doğru dağıt.
        var topMatch = topCats.FirstOrDefault(o => o.Id == d.CategoryId);
        if (topMatch is not null)
        {
            catBox.SelectedItem = topMatch;
            subCatBox.ItemsSource = Load(() => DesktopServices.Lookups.ListCategories(session, topMatch.Id));
        }
        else if (!string.IsNullOrEmpty(d.CategoryId))
        {
            foreach (var top in topCats)
            {
                var subs = Load(() => DesktopServices.Lookups.ListCategories(session, top.Id));
                var subMatch = subs.FirstOrDefault(x => x.Id == d.CategoryId);
                if (subMatch is not null)
                {
                    catBox.SelectedItem = top;
                    subCatBox.ItemsSource = subs;
                    subCatBox.SelectedItem = subMatch;
                    break;
                }
            }
        }
        resolvingCat = false;
        unitBox.SelectedItem = units.FirstOrDefault(o => o.Id == d.UnitId);
        brandBox.SelectedItem = brands.FirstOrDefault(o => o.Id == d.BrandId);
        supBox.SelectedItem = sups.FirstOrDefault(o => o.Id == d.SupplierId);
        minBox.Value = d.MinStock; priceBox.Value = d.UnitPrice;
        descBox.Text = d.Description ?? "";
        stockBox.Value = d.Stock;
        var originalStock = d.Stock;

        // Son Hareketler (A3) — salt-okunur; malzemenin son 10 stok hareketi (giriş yeşil / çıkış kırmızı).
        try
        {
            var moves = DesktopServices.Stock.RecentForMaterial(session, materialId, 10);
            if (moves.Count > 0)
            {
                var inBrush = this.TryFindResource("SuccessBrush", out var ib) ? ib as IBrush : null;
                var outBrush = this.TryFindResource("DangerBrush", out var ob) ? ob as IBrush : null;
                this.FindControl<ItemsControl>("MovesList")!.ItemsSource = moves.Select(m => new MoveRow
                {
                    DateStr = DateTimeOffset.FromUnixTimeMilliseconds(m.Date).LocalDateTime.ToString("dd.MM.yyyy"),
                    Label = m.Label,
                    QtyStr = (m.Quantity >= 0 ? "+" : "") + m.Quantity.ToString("0.##"),
                    Reference = m.Reference,
                    QtyBrush = m.Quantity < 0 ? outBrush : inBrush,
                }).ToList();
                this.FindControl<Border>("MovesPanel")!.IsVisible = true;
            }
            else
            {
                this.FindControl<SelectableTextBlock>("NoMovesText")!.IsVisible = true;
            }
        }
        catch { /* hareket paneli en fazla gizli kalır; düzenlemeyi engellemez */ }

        // Kritik stok paneli (S3) — stok minimum altında/eşitse uyarı + onaylı TASLAK talep.
        if (d.MinStock > 0 && d.Stock <= d.MinStock)
        {
            var shortfall = Math.Max(1m, Math.Ceiling(d.MinStock - d.Stock));
            var critTitle = this.FindControl<SelectableTextBlock>("CriticalTitle")!;
            var critDetail = this.FindControl<SelectableTextBlock>("CriticalDetail")!;
            var reqBtn = this.FindControl<Button>("RequestBtn")!;
            critDetail.Text = $"Stok: {d.Stock:0.##} · Minimum: {d.MinStock:0.##} · Önerilen talep: {shortfall:0.##}";
            reqBtn.IsVisible = AccessControl.Can(session, "requests", PermissionAction.Create);
            this.FindControl<Border>("CriticalPanel")!.IsVisible = true;
            reqBtn.Click += async (_, _) =>
            {
                if (!await ConfirmService.AskAsync(this,
                        $"'{d.Name}' için {shortfall:0.##} birim talep TASLAĞI oluşturulsun mu?\n" +
                        "(Talepler ekranından şantiye/miktar tamamlayıp gönderebilirsiniz.)", "Talep Oluştur")) return;
                try
                {
                    DesktopServices.Requests.Create(session,
                        new NewRequest(new[] { new RequestItemInput(materialId, shortfall) }));
                    reqBtn.IsVisible = false;
                    var ok = this.TryFindResource("SuccessBrush", out var sb) ? sb as IBrush : null;
                    critTitle.Text = "Talep taslağı oluşturuldu";
                    if (ok is not null) critTitle.Foreground = ok;
                    critDetail.Text = "Talepler ekranından şantiye/miktarı tamamlayıp gönderebilirsiniz.";
                }
                catch (System.Exception ex)
                {
                    statusText.Text = "Talep oluşturulamadı: " + ex.Message; statusText.IsVisible = true;
                }
            };
        }

        // Başlangıçta KİLİTLİ (salt-okunur). Stok kutusu ayrıca "stock" yetkisine tabidir (madde 1.2).
        var editable = new Control[] { code, name, typeBox, catBox, subCatBox, unitBox, brandBox, supBox, minBox, priceBox, descBox };
        void SetLocked(bool locked) { foreach (var c in editable) c.IsEnabled = !locked; stockBox.IsEnabled = !locked && canEditStock; }
        SetLocked(true);
        editBtn.IsVisible = canEdit;
        deleteBtn.IsVisible = canDelete;

        // Değişiklik sayacı (S2) — kaydedilmemiş alan sayısı; iptalde uyarı için de kullanılır.
        var dirtyText = this.FindControl<SelectableTextBlock>("DirtyText")!;
        int Dirty()
        {
            int n = 0;
            if ((code.Text ?? "") != d.Code) n++;
            if ((name.Text ?? "") != d.Name) n++;
            // G2-07: karşılaştırma tabanı EKRANA YÜKLENEN değerle AYNI olmalı (yukarıdaki "Diğer" fallback'i).
            // Aksi halde boş türlü kayıt açılır açılmaz "1 kaydedilmemiş değişiklik" gösterirdi.
            if ((typeBox.SelectedItem as string ?? "") != (string.IsNullOrWhiteSpace(d.Type) ? "Diğer" : d.Type)) n++;
            if (((subCatBox.SelectedItem as Opt)?.Id ?? (catBox.SelectedItem as Opt)?.Id) != d.CategoryId) n++;
            if ((unitBox.SelectedItem as Opt)?.Id != d.UnitId) n++;
            if ((brandBox.SelectedItem as Opt)?.Id != d.BrandId) n++;
            if ((supBox.SelectedItem as Opt)?.Id != d.SupplierId) n++;
            if ((decimal)(minBox.Value ?? 0) != d.MinStock) n++;
            if ((decimal)(priceBox.Value ?? 0) != d.UnitPrice) n++;
            if ((descBox.Text ?? "") != (d.Description ?? "")) n++;
            if (canEditStock && (decimal)(stockBox.Value ?? 0) != originalStock) n++;   // madde 1.2
            return n;
        }
        void Recount()
        {
            var n = Dirty();
            dirtyText.Text = n > 0 ? $"{n} kaydedilmemiş değişiklik" : "";
            dirtyText.IsVisible = n > 0;
        }
        code.TextChanged += (_, _) => Recount();
        name.TextChanged += (_, _) => Recount();
        descBox.TextChanged += (_, _) => Recount();
        typeBox.SelectionChanged += (_, _) => Recount();
        catBox.SelectionChanged += (_, _) => Recount();
        subCatBox.SelectionChanged += (_, _) => Recount();
        unitBox.SelectionChanged += (_, _) => Recount();
        brandBox.SelectionChanged += (_, _) => Recount();
        supBox.SelectionChanged += (_, _) => Recount();
        minBox.ValueChanged += (_, _) => Recount();
        priceBox.ValueChanged += (_, _) => Recount();
        stockBox.ValueChanged += (_, _) => Recount();

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
            var codeVal = (code.Text ?? "").Trim();
            var nameVal = (name.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(codeVal) || string.IsNullOrWhiteSpace(nameVal))
            { statusText.Text = "Kod ve ad zorunlu."; statusText.IsVisible = true; return; }
            if (unitBox.SelectedItem is not Opt unitOpt)
            { statusText.Text = "Birim seçin."; statusText.IsVisible = true; return; }
            // Onay penceresi (kullanıcı isteği 2026-07-19) — bu pencerenin ÜZERİNDE (owner=this).
            if (!await ConfirmService.AskAsync(this, "Malzeme bilgileri güncellensin mi?", "Kaydet")) return;

            // madde 1.2/1.3/1.4: stok DOĞRUDAN değiştirildiyse güçlü uyarı + log. Devam → SAYIM/DÜZELTME hareketi;
            // Vazgeç → yalnız log (stok değişmez, kutu eski değere döner). Karar önce alınır, uygulama Update sonrası.
            var newStock = (decimal)(stockBox.Value ?? 0);
            bool stockChanged = canEditStock && newStock != originalStock;
            bool continueStock = false;
            if (stockChanged)
                continueStock = await ConfirmService.AskAsync(this, StockChangeLogService.WarningMessage,
                    "Doğrudan Stok Değişikliği", okText: "Devam Et", cancelText: "Vazgeç", danger: true);
            try
            {
                DesktopServices.Materials.Update(session, materialId, new UpdateMaterial(
                    Code: codeVal, Name: nameVal,
                    Type: typeBox.SelectedItem as string,
                    CategoryId: (subCatBox.SelectedItem as Opt)?.Id ?? (catBox.SelectedItem as Opt)?.Id,   // alt kategori seçiliyse o, yoksa kategori
                    UnitId: unitOpt.Id,
                    BrandId: (brandBox.SelectedItem as Opt)?.Id,
                    SupplierId: (supBox.SelectedItem as Opt)?.Id,
                    MinStock: (decimal)(minBox.Value ?? 0),
                    UnitPrice: (decimal)(priceBox.Value ?? 0),
                    Description: string.IsNullOrWhiteSpace(descBox.Text) ? null : descBox.Text!.Trim(),
                    // G2-04: mevcut ŞABLON BAĞI aynen geri gönderilir. Bu pencerede değiştirilmez, ama
                    // MaterialService.Update "template_id=@tpl" ile koşulsuz yazdığı için gönderilmezse
                    // bağ NULL'a düşerdi (şablonlu/şablon-dışı raporları bu kolondan beslenir).
                    // Aynı koruma StockEntryViewModel'deki malzeme güncellemesinde de uygulanır.
                    TemplateId: d.TemplateId),
                    // DÜZENLEME KİLİDİ: pencere açıldığındaki sürüm — kayıt arada değiştiyse üzerine yazma.
                    expectedVersion: d.Version);
                // Uyumlu araçlar / muadiller / fotoğraflar DEĞİŞTİRİLMEZ (korunur).

                // Doğrudan stok değişikliği kararını uygula/logla (madde 1.4): Devam → adjustment + log("continued");
                // Vazgeç → yalnız log("cancelled"), stok değişmez. Kart alanları zaten kaydedildi.
                if (stockChanged)
                {
                    DesktopServices.StockChangeLog.Record(session, materialId, newStock, continueStock,
                        StockChangeLogService.WarningMessage);
                    if (!continueStock) { stockBox.Value = originalStock; }
                }
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
            if (!await ConfirmService.AskAsync(this, $"'{d.Name}' malzemesi silinsin mi? Kayıt çöp kutusuna alınır.",
                    "Malzeme Sil", "Evet, Sil", "Vazgeç", danger: true)) return;
            try { DesktopServices.Materials.Delete(session, materialId); Close("deleted"); }
            catch (Exception ex) { statusText.Text = "Silinemedi: " + ex.Message; statusText.IsVisible = true; }
        };

        cancelBtn.Click += async (_, _) =>
        {
            if (saveBtn.IsVisible && Dirty() > 0 &&
                !await ConfirmService.AskAsync(this,
                    $"{Dirty()} kaydedilmemiş değişiklik var. Yine de çıkılsın mı? Değişiklikler kaybolur.",
                    "Değişiklikler kaybolacak", okText: "Evet, Çık", cancelText: "Formda kal", danger: true))
                return;
            Close(null);
        };

        // Fotoğraflar sunucudan ASENKRON gelir (pencere açılışı bloklanmaz).
        _ = FotograflariYukleAsync(session, materialId);
    }

    /// <summary>Kullanıcı isteği (2026-09-02): çift-tık penceresinde de fotoğraflar görünür.
    /// Kaynak SUNUCUDUR (ADR-182) → başka bilgisayarda eklenen fotoğraf da gelir. Salt görüntüleme.</summary>
    private async System.Threading.Tasks.Task FotograflariYukleAsync(SessionContext session, string materialId)
    {
        var section = this.FindControl<StackPanel>("PhotoSection");
        var panel = this.FindControl<StackPanel>("PhotoPanel");
        var note = this.FindControl<SelectableTextBlock>("PhotoNote");
        if (section is null || panel is null || note is null) return;
        try
        {
            var (fotograflar, cevrimdisi) = await DesktopPhotos.YukleAsync(session, "material", materialId);
            panel.Children.Clear();
            foreach (var f in fotograflar)
            {
                try
                {
                    panel.Children.Add(new Image
                    {
                        Source = new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(f.Bytes)),
                        Height = 110,
                        Stretch = Stretch.Uniform,
                    });
                }
                catch { /* bozuk görsel atlanır */ }
            }
            note.Text = "Çevrimdışı: yalnız bu bilgisayardaki fotoğraflar gösteriliyor.";
            note.IsVisible = cevrimdisi;
            section.IsVisible = panel.Children.Count > 0 || cevrimdisi;
        }
        catch { /* fotoğraf yüklenemedi → bölüm gizli kalır */ }
    }

    private static List<Opt> Load(Func<IReadOnlyList<LookupItem>> get)
    {
        try { return get().Select(x => new Opt(x.Id, x.Name)).ToList(); }
        catch { return new List<Opt>(); }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
