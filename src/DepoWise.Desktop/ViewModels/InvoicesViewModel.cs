using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
public sealed partial class InvoicesViewModel : ViewModelBase
{
    private readonly SessionContext _session;
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

        Busy = true; FormError = null;
        try
        {
            var dto = new NewInvoice(
                FDirection, FPartyId!,
                FormLines.Select(x => x.ToDto()).ToList(),
                _operationId,
                SeriesId: null,
                ExternalNo: string.IsNullOrWhiteSpace(FExternalNo) ? null : FExternalNo,
                BranchId: string.IsNullOrWhiteSpace(FBranchId) ? null : FBranchId,
                InvoiceDate: FInvoiceDate?.ToUnixTimeMilliseconds(),
                DueDate: FDueDate?.ToUnixTimeMilliseconds(),
                Currency: "TRY",
                Note: string.IsNullOrWhiteSpace(FNote) ? null : FNote,
                AffectsStock: FAffectsStock);

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
