using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// G4-2 — FATURALAR (masaüstü, ANA KULLANIM KANALI).
///
/// Sol: fatura listesi (arama + yön/durum filtresi + sayfalama). Sağ: seçili faturanın detayı
/// veya yeni fatura formu.
///
/// <b>ÇEVRİMDIŞI:</b> yerel <c>InvoiceService</c> doğrudan çağrılır (yerel SQLite). İş kuralları
/// SERVİSTEDİR → web ile birebir aynı doğrulama, aynı toplam, aynı yetki.
///
/// <b>⚠️ PARALEL DEFTER YOK:</b> bu ekran ne stok ne cari tablosuna yazar. Kaydet'e basıldığında
/// tek çağrı yapılır (<c>InvoiceService.Create</c>); stok ve cari etkisini o servis, kendi
/// sahiplerine (StockService / PartyLedgerService) TEK transaction'da yaptırır.
///
/// <b>⚠️ SİLME YOK:</b> ekranda "Sil" butonu YOKTUR; yalnız gerekçeli "İptal" vardır ve iptal
/// ters kayıt üretir. Çift iptal serviste engellenir.
///
/// <b>ÇİFT KAYIT KORUMASI:</b> form açıldığında bir <c>operation_id</c> üretilir ve kayıt başarılı
/// olana kadar SABİT kalır. Kullanıcı iki kez kaydete basarsa ikinci istek aynı anahtarla gider →
/// ikinci fatura, ikinci cari borcu ve ikinci stok hareketi OLUŞMAZ.
/// </summary>
public sealed partial class InvoicesViewModel : ViewModelBase, IDeepLinkTarget
{
    private readonly SessionContext _session;
    // ═══ FAZ 3b-5 (ADR-223) — ALAN GÖRÜNÜRLÜĞÜ ══════════════════════════════════════════════
    // ⭐ Karar servisin kendisinden (FieldAccess) gelir — web de AYNI fonksiyonun sonucunu
    // /api/field-access üzerinden okur. İki platformda ikinci bir yetki mantığı YOKTUR.
    // ⚠️ Bu bayrak GÜVENLİK DEĞİLDİR: gerçek kapı serviste. Burası "0,00 gösterme" içindir.
    /// <summary>Fatura tutarı bu kullanıcıya açık mı?</summary>
    public bool TutarGorunur => DepoWise.Application.Security.FieldAccess.Gorunur(_session, DepoWise.Application.Security.FieldProtectionCatalog.Invoices, DepoWise.Application.Security.FieldProtectionCatalog.GrandTotal);
    private const int PageSize = 50;

    /// <summary>G4-3d — ORTAK ŞUBE KAPSAMI. Seçim OKUMA filtresidir; yazmada tekil
    /// <see cref="BranchScopeSelector.ActiveWriteBranchId"/> kullanılır.</summary>
    public BranchScopeSelector BranchScope { get; }

    public bool CanCreate => AccessControl.Can(_session, InvoiceService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, InvoiceService.Module, PermissionAction.Edit);

    public ObservableCollection<InvoiceListRow> Rows { get; } = new();
    public ObservableCollection<InvoiceLineRecord> DetailLines { get; } = new();
    public ObservableCollection<InvoiceLineEditor> FormLines { get; } = new();

    /// <summary>Açılır liste öğesi. ValueTuple Avalonia bağlamalarında çözülemediği için gerçek tip.</summary>
    public sealed record Option(string Key, string Label);

    public IReadOnlyList<Option> DirectionFilters { get; } =
        new[] { new Option("", "Tümü") }
            .Concat(InvoiceDirections.All.Select(x => new Option(x.Key, x.Label))).ToList();

    public IReadOnlyList<Option> DirectionOptions { get; } =
        InvoiceDirections.All.Select(x => new Option(x.Key, x.Label)).ToList();

    public IReadOnlyList<Option> StatusFilters { get; } = new[]
    {
        new Option("", "Tümü"),
        new Option(InvoiceStatuses.Active, "Yürürlükte"),
        new Option(InvoiceStatuses.Cancelled, "İptal"),
    };

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _directionFilter = "";
    [ObservableProperty] private string _statusFilter = InvoiceStatuses.Active;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _busy;

    public int PageCount => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
    public string PageText => $"Sayfa {Page} / {PageCount} · {TotalCount} fatura";
    public bool CanPrev => Page > 1;
    public bool CanNext => Page < PageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    private InvoiceListRow? _selected;
    public bool HasSelection => Selected is not null;

    /// <summary>Detay yalnız form KAPALIYKEN gösterilir (iki panel aynı sütunu paylaşır).</summary>
    public bool ShowDetail => HasSelection && !FormOpen;

    // ── Seçili faturanın detayı ──
    [ObservableProperty] private InvoiceRecord? _detail;
    [ObservableProperty] private string _detailTotals = "—";
    /// <summary>İptal yalnız YÜRÜRLÜKTEKİ faturada ve Edit yetkisiyle mümkündür.</summary>
    public bool CanCancelSelected => Detail is not null && !Detail.IsCancelled && CanEdit;

    public InvoicesViewModel(SessionContext session)
    {
        _session = session;
        // Ortak şube kapsamı — seçim değişince liste yenilenir (kullanıcı Ara demek zorunda kalmasın).
        BranchScope = new BranchScopeSelector(session, () => _ = Load());
        _ = Load();
    }

    partial void OnDetailChanged(InvoiceRecord? value) => OnPropertyChanged(nameof(CanCancelSelected));
    partial void OnSelectedChanged(InvoiceListRow? value) => _ = LoadDetail(value);

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // LİSTE
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task Load()
    {
        Busy = true; FormError = null;
        try
        {
            await Task.Run(() =>
            {
                var res = DesktopServices.InvoiceQueries.List(_session,
                    string.IsNullOrWhiteSpace(Search) ? null : Search,
                    string.IsNullOrWhiteSpace(DirectionFilter) ? null : DirectionFilter,
                    string.IsNullOrWhiteSpace(StatusFilter) ? null : StatusFilter,
                    null, null, null, Page, PageSize, BranchScope.Filter);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Rows.Clear();
                    foreach (var r in res.Items) Rows.Add(r);
                    TotalCount = res.TotalCount;
                    Notify();
                    Status = res.TotalCount == 0 ? "Fatura bulunamadı." : $"{res.TotalCount} fatura";
                });
            });
        }
        catch (Exception ex) { FormError = "Liste yüklenemedi: " + ex.Message; }
        finally { Busy = false; }
    }

    // ⭐ M-54 (AR-GE 2026-10-24, F-057): Excel / Yazdır — ekrandaki süzgeçlerle filtrelenmiş TÜM sonuç (sayfa değil); web ile
    // aynı servis ve aynı tablo (InvoiceListeTablosu); tutar gizliyse kolon yok. Yetki: "export".
    public bool CanExport => DepoWise.Application.Security.AccessControl.Can(_session, "export", DepoWise.Application.Security.PermissionAction.View);

    private DepoWise.Application.Reports.TableModel DisaTablo()
        => DepoWise.Infrastructure.Accounting.InvoiceListeTablosu.ToTableModel(
            DepoWise.Infrastructure.Reporting.ListeDisaAktarim.TumSayfalar((p, n) => DesktopServices.InvoiceQueries.List(_session,
                string.IsNullOrWhiteSpace(Search) ? null : Search,
                string.IsNullOrWhiteSpace(DirectionFilter) ? null : DirectionFilter,
                string.IsNullOrWhiteSpace(StatusFilter) ? null : StatusFilter,
                null, null, null, p, n, BranchScope.Filter)), TutarGorunur);

    [RelayCommand]
    private async Task ExportExcel()
    {
        if (!CanExport) { FormError = "Dışa aktarım yetkiniz yok."; return; }
        try
        {
            var hedef = await FilePickerService.SaveExcelAsync("Faturalar.xlsx");
            if (hedef is null) return;
            var tablo = await Task.Run(DisaTablo);
            await System.IO.File.WriteAllBytesAsync(hedef, DesktopServices.Excel.Export(tablo));
            Status = $"Excel kaydedildi ({tablo.Rows.Count} fatura): {hedef}";
        }
        catch (Exception ex) { FormError = "Excel aktarılamadı: " + ex.Message; }
    }

    [RelayCommand]
    private async Task Yazdir()
    {
        if (!CanExport) { FormError = "Yazdırma yetkiniz yok (dışa aktarım yetkisi gerekir)."; return; }
        try
        {
            var tablo = await Task.Run(DisaTablo);
            var hedef = await YazdirmaYardimcisi.YazdirAsync(tablo, _session);
            if (hedef is not null) Status = $"Yazdırma dosyası hazır ({tablo.Rows.Count} fatura): {hedef}";
        }
        catch (Exception ex) { FormError = "Yazdırılamadı: " + ex.Message; }
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
    }

    [RelayCommand] private async Task Find() { Page = 1; await Load(); }
    [RelayCommand] private async Task PrevPage() { if (CanPrev) { Page--; await Load(); } }
    [RelayCommand] private async Task NextPage() { if (CanNext) { Page++; await Load(); } }

    private async Task LoadDetail(InvoiceListRow? row)
    {
        DetailLines.Clear();
        Detail = null; DetailTotals = "—";
        if (row is null) return;
        try
        {
            await Task.Run(() =>
            {
                var d = DesktopServices.InvoiceQueries.Get(_session, row.Id);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Detail = d;
                    foreach (var l in d.Lines) DetailLines.Add(l);
                    DetailTotals =
                        $"Ara toplam {d.Subtotal:0.00} · İskonto {d.DiscountTotal:0.00} · " +
                        $"KDV {d.VatTotal:0.00} · Tevkifat {d.WithholdingTotal:0.00} · " +
                        $"GENEL TOPLAM {d.GrandTotal:0.00} {d.Currency}";
                });
            });
        }
        catch (Exception ex) { FormError = "Fatura detayı yüklenemedi: " + ex.Message; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // İPTAL (SİLME DEĞİL)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>İptal paneli açık mı? Gerekçe ZORUNLU olduğu için ayrı bir alan gerekir.</summary>
    [ObservableProperty] private bool _cancelPanelOpen;
    [ObservableProperty] private string _cancelReason = "";

    /// <summary>Kullanıcıya iptalin NE YAPACAĞI açıkça yazılır (tahmin etmek zorunda kalmasın).</summary>
    public string CancelWarning => Detail is null ? "" :
        $"'{Detail.InvoiceNo}' faturası SİLİNMEZ; kaydı durur ve etkisi ters kayıtlarla sıfırlanır:\n" +
        $"• Cari hareketi ters yönde yazılır ({Detail.GrandTotal:0.00} {Detail.Currency}).\n" +
        (Detail.StockDocumentId is null ? "• Stok etkisi yok."
         : Detail.Direction == InvoiceDirections.Purchase
            ? "• Stoktan ÇIKIŞ yapılır — mal tüketilmişse iptal REDDEDİLİR (yarım iptal olmaz)."
            : "• Stoğa GİRİŞ yapılır.");

    [RelayCommand]
    private void OpenCancelPanel()
    {
        if (!CanCancelSelected) return;
        CancelReason = "";
        FormError = null;
        OnPropertyChanged(nameof(CancelWarning));
        CancelPanelOpen = true;
    }

    [RelayCommand]
    private void CloseCancelPanel() => CancelPanelOpen = false;

    /// <summary>
    /// Gerekçeli iptal. Kayıt silinmez; ters stok ve ters cari hareketi oluşur. Alış faturası
    /// iptali stok ÇIKIŞI demektir — mal tüketilmişse servis reddeder ve fatura yürürlükte kalır.
    /// </summary>
    [RelayCommand]
    private async Task CancelInvoice()
    {
        if (Detail is null || !CanCancelSelected) return;
        if (string.IsNullOrWhiteSpace(CancelReason)) { FormError = "İptal gerekçesi zorunlu."; return; }
        var d = Detail;
        // ⭐ FAZ 4.2: önce STANDART iptal onayı, sonra ekrana özel bilgi (kullanıcının istediği sıra).
        if (!await ConfirmService.ConfirmCancelAsync(
                $"'{d.InvoiceNo}' faturası iptal edilecek. Cari ve stok etkileri ters kayıtla geri alınır; kayıt silinmez.")) return;
        try
        {
            DesktopServices.Invoices.Cancel(_session, d.Id, CancelReason);
            CancelPanelOpen = false;
            Status = $"'{d.InvoiceNo}' iptal edildi.";
            await Load();
            Selected = Rows.FirstOrDefault(x => x.Id == d.Id);
        }
        catch (Exception ex) { FormError = "İptal edilemedi: " + ex.Message; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // YENİ FATURA FORMU
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetail))]
    private bool _formOpen;

    [ObservableProperty] private string _fDirection = InvoiceDirections.Purchase;
    [ObservableProperty] private string? _fPartyId;
    [ObservableProperty] private string? _fBranchId;
    [ObservableProperty] private string _fExternalNo = "";
    [ObservableProperty] private DateTimeOffset? _fInvoiceDate = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? _fDueDate;
    [ObservableProperty] private string _fNote = "";
    [ObservableProperty] private bool _fAffectsStock = true;

    // ═══ M-25 (AR-GE 2026-10-18, F-072) — MAL KABULDEN FATURA ═══════════════════════════════════════════
    // Satın Alma ekranındaki "Faturalaştır" buraya "kabul:<belge>" ile gelir; form kabulden dolar, stok kutusu kapalı
    // ve kilitlidir (mal kabulle stoğa girdi). Eşleştirme kuralları (ikinci fatura yok, miktar ≤ kabul) serviste.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KabulBagli))]
    [NotifyPropertyChangedFor(nameof(StokKutusuAcik))]
    private string? _kabulBelgesi;
    [ObservableProperty] private string? _kabulNotu;
    public bool KabulBagli => KabulBelgesi is not null;
    public bool StokKutusuAcik => KabulBelgesi is null;

    public void OpenEntity(string entityId)
    {
        if (entityId.StartsWith("kabul:", StringComparison.Ordinal)) _ = KabuldenFormAc(entityId["kabul:".Length..]);
    }

    internal async Task KabuldenFormAc(string belgeId)
    {
        if (!CanCreate) { Status = "Fatura oluşturma yetkiniz yok."; return; }
        KabulFaturaTaslagi t;
        try { t = DesktopServices.Invoices.KabulTaslagi(_session, belgeId); }
        catch (Exception ex) { Status = ex.Message; return; }
        await NewInvoiceForm();
        // LoadLookups listeleri arayüz kuyruğuna bırakır → önce onlar dolsun, yoksa cari seçimi sonradan temizlenirdi.
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
        FDirection = InvoiceDirections.Purchase;
        FAffectsStock = false;
        KabulBelgesi = t.ReceiptDocumentId;
        KabulNotu = "Mal kabulden fatura: " + t.Not + ". Stok mal kabulle girdi; bu fatura stoğa İKİNCİ kez girmez. " +
                    "KDV oranını ve satıcının fatura numarasını kontrol edin." + (t.Uyari is null ? "" : " ⚠ " + t.Uyari);
        if (t.PartyId is not null)
        {
            if (PartyOptions.All(o => o.Key != t.PartyId)) PartyOptions.Add(new Option(t.PartyId, t.PartyText ?? t.PartyId));
            FPartyId = t.PartyId;
        }
        if (!string.IsNullOrWhiteSpace(t.BranchId)) FBranchId = t.BranchId;
        FNote = t.Not;
        FormLines.Clear();
        foreach (var l in t.Lines)
            FormLines.Add(new InvoiceLineEditor(_session, RecalcTotals)
            {
                MaterialId = l.MaterialId, MaterialText = $"{l.MaterialCode} — {l.MaterialName}", Unit = l.Unit ?? "",
                Quantity = l.Quantity, UnitPrice = l.UnitPrice,
            });
        RecalcTotals();
    }
    [ObservableProperty] private string _formTotals = "—";

    /// <summary>Kayıt tamamlanana kadar SABİT idempotency anahtarı (çift tıklama koruması).</summary>
    private string _operationId = "";

    public ObservableCollection<Option> PartyOptions { get; } = new();
    public ObservableCollection<Option> BranchOptions { get; } = new();
    public ObservableCollection<Option> VatOptions { get; } = new();


    [RelayCommand]
    private async Task NewInvoiceForm()
    {
        if (!CanCreate) { FormError = "Fatura oluşturma yetkiniz yok."; return; }
        FormError = null;
        _operationId = "inv-" + Guid.NewGuid().ToString("N");
        FDirection = InvoiceDirections.Purchase;
        FPartyId = null; FExternalNo = ""; FNote = "";
        // Varsayılan: AKTİF ÇALIŞMA ŞUBESİ (tekil). Çoklu seçim yazmada kullanılmaz.
        FBranchId = BranchScope.ActiveWriteBranchId;
        FInvoiceDate = DateTimeOffset.Now; FDueDate = null; FAffectsStock = true;
        KabulBelgesi = null; KabulNotu = null;   // M-25: elle açılan form bağsızdır
        FormLines.Clear();
        AddLine();
        FormOpen = true;
        await LoadLookups();
        RecalcTotals();
    }

    /// <summary>Açılır liste kaynakları — form açılırken BİR KEZ yüklenir (satır başına sorgu yok).</summary>
    private async Task LoadLookups()
    {
        try
        {
            await Task.Run(() =>
            {
                var parties = DesktopServices.Parties.List(_session, null, null, true, 1, 500).Items
                    .Select(x => new Option(x.Party.Id, $"{x.Party.Code} — {x.Party.Title}")).ToList();
                // ⭐ G4-3d: yalnız YETKİLİ şubeler (tüm firma şubeleri DEĞİL).
                var branches = BranchScope.Branches.Select(b => new Option(b.Key, b.Label)).ToList();
                // KDV oranları KATALOGDAN gelir; katalog boşsa kullanıcı elle yazabilir (oran serbest alan).
                var vats = DesktopServices.InvoiceQueries.VatRates(_session)
                    .Select(v => new Option(v.Rate.ToString("0.##"), v.Label ?? $"%{v.Rate:0.##}")).ToList();

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    PartyOptions.Clear(); foreach (var x in parties) PartyOptions.Add(x);
                    BranchOptions.Clear(); foreach (var x in branches) BranchOptions.Add(x);
                    VatOptions.Clear(); foreach (var x in vats) VatOptions.Add(x);
                });
            });
        }
        catch (Exception ex) { FormError = "Seçenekler yüklenemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void AddLine()
    {
        FormLines.Add(new InvoiceLineEditor(_session, RecalcTotals));
        RecalcTotals();
    }

    [RelayCommand]
    private void RemoveLine(InvoiceLineEditor? line)
    {
        if (line is null) return;
        FormLines.Remove(line);
        RecalcTotals();
    }

    /// <summary>Toplamlar SERVİSTEKİ fonksiyonla hesaplanır — ekran ile kayıt aynı sayıyı verir.</summary>
    private void RecalcTotals()
    {
        if (FormLines.Count == 0) { FormTotals = "—"; return; }
        var t = InvoiceService.Totals(FormLines.Select(x => x.ToDto()).ToList());
        FormTotals = $"Ara toplam {t.Subtotal:0.00} · İskonto {t.DiscountTotal:0.00} · " +
                     $"KDV {t.VatTotal:0.00} · Tevkifat {t.WithholdingTotal:0.00} · " +
                     $"GENEL TOPLAM {t.GrandTotal:0.00}";
    }

    [RelayCommand]
    private void CloseForm()
    {
        FormOpen = false;
        FormError = null;
    }

    [RelayCommand]
    private async Task SaveInvoice()
    {
        if (!CanCreate) { FormError = "Fatura oluşturma yetkiniz yok."; return; }
        if (string.IsNullOrWhiteSpace(FPartyId)) { FormError = "Cari seçin."; return; }
        if (FormLines.Count == 0) { FormError = "En az bir satır ekleyin."; return; }

        FormError = null;
        // ⭐ M-24 (K-09 a): alış faturası stoğu etkileyecekse çift stok riski kontrol edilir; ⭐ M-51 (K-13 a): para işlemi onayı.
        // Metinler servisten gelir — web aynı metni API'den alır (iki platform aynı cümle).
        var satirlar = FormLines.Select(x => x.ToDto()).ToList();
        var stokEtkiler = FAffectsStock && KabulBelgesi is null;   // M-25: kabulden fatura stok yazmaz
        try
        {
            var uyarilar = stokEtkiler
                ? DesktopServices.Invoices.CiftStokUyarilari(_session, FDirection, FPartyId!, FExternalNo,
                    satirlar.Where(l => !string.IsNullOrWhiteSpace(l.MaterialId)).Select(l => l.MaterialId!).ToList(),
                    IsGunuTarihi.Ms(FInvoiceDate))
                : System.Array.Empty<string>();
            if (uyarilar.Count > 0)
            {
                if (await ConfirmService.AskAsync(InvoiceService.CiftStokMetni(uyarilar), "Çift Stok Riski",
                        "Stoğu Etkilemeden Kaydet", "Diğer Seçenek", danger: true))
                { stokEtkiler = false; FAffectsStock = false; }
                else if (!await ConfirmService.AskAsync(InvoiceService.CiftStokZorlaMetni, "Çift Stok Riski",
                             "Evet, Stoğa Girsin", "Vazgeç", danger: true))
                    return;
            }
            else if (!await ConfirmService.AskAsync(InvoiceService.KayitOnayMetni(FDirection, satirlar, "TRY"), "Fatura Kaydı", "Kaydet", "Vazgeç"))
                return;
        }
        catch (Exception ex) { FormError = ex.Message; return; }

        Busy = true;
        try
        {
            var dto = new NewInvoice(
                FDirection, FPartyId!,
                satirlar,
                _operationId,
                SeriesId: null,
                ExternalNo: string.IsNullOrWhiteSpace(FExternalNo) ? null : FExternalNo,
                BranchId: string.IsNullOrWhiteSpace(FBranchId) ? null : FBranchId,
                InvoiceDate: IsGunuTarihi.Ms(FInvoiceDate),   // ADR-184: takvim tarihi → UTC gün başı
                DueDate: IsGunuTarihi.Ms(FDueDate),           // ADR-184
                Currency: "TRY",
                Note: string.IsNullOrWhiteSpace(FNote) ? null : FNote,
                AffectsStock: stokEtkiler,
                ReceiptDocumentId: KabulBelgesi);   // ⭐ M-25

            var r = DesktopServices.Invoices.Create(_session, dto);
            Status = r.AlreadyExisted
                ? $"Bu fatura zaten kaydedilmişti ({r.InvoiceNo}) — ikinci kayıt oluşturulmadı."
                : $"Fatura kaydedildi: {r.InvoiceNo}";
            FormOpen = false;
            await Load();
            Selected = Rows.FirstOrDefault(x => x.Id == r.Id);
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Busy = false; }
    }
}
