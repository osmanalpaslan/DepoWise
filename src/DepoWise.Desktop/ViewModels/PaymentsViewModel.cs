using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// G4-3 — TAHSİLAT / ÖDEME (masaüstü, ANA KULLANIM KANALI).
///
/// Türkiye ön muhasebe akışı: cari seç → kasa/banka seç → tutar gir → (isteğe bağlı) açık
/// faturalara dağıt → kaydet.
///
/// <b>YÖN KULLANICIYA AÇIK YAZILIR:</b> tahsilat = para GİRER, müşterinin borcu azalır;
/// ödeme = para ÇIKAR, bizim borcumuz azalır. Kullanıcı tahmin etmek zorunda kalmaz.
///
/// <b>⚠️ PARALEL DEFTER YOK:</b> ekran tek çağrı yapar (<c>FinanceService.Add</c>); kasa hareketi,
/// cari hareketi ve fatura kapaması AYNI transaction'da o servis tarafından yazılır.
///
/// <b>FATURA KALANI SAKLANMAZ:</b> listede görünen "kalan" her seferinde hesaplanır.
///
/// <b>ÇİFT KAYIT KORUMASI:</b> form açıldığında bir <c>operation_id</c> üretilir ve kayıt başarılı
/// olana kadar SABİT kalır → iki kez kaydete basmak ikinci tahsilat üretmez.
/// </summary>
public sealed partial class PaymentsViewModel : ViewModelBase
{
    private readonly SessionContext _session;
    private const int PageSize = 50;

    /// <summary>G4-3d — ORTAK ŞUBE KAPSAMI. Seçim OKUMA filtresidir; yazmada tekil
    /// <see cref="BranchScopeSelector.ActiveWriteBranchId"/> kullanılır.</summary>
    public BranchScopeSelector BranchScope { get; }

    public bool CanCreate => AccessControl.Can(_session, FinanceService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, FinanceService.Module, PermissionAction.Edit);

    public ObservableCollection<FinanceTxnRow> Rows { get; } = new();
    public ObservableCollection<OpenInvoiceLine> OpenInvoices { get; } = new();

    /// <summary>Açılır liste öğesi. ValueTuple Avalonia bağlamalarında çözülemediği için gerçek tip.</summary>
    public sealed record Option(string Key, string Label);

    /// <summary>Yalnız TAHSİLAT ve ÖDEME — transfer kendi ekranında, açılış/düzeltme hesap ekranında.</summary>
    public IReadOnlyList<Option> TypeOptions { get; } =
        FinanceTxnTypes.All.Where(x => FinanceTxnTypes.PartyAffecting.Contains(x.Key))
            .Select(x => new Option(x.Key, x.Label)).ToList();

    public IReadOnlyList<Option> TypeFilters { get; } =
        new[] { new Option("", "Tümü") }
            .Concat(FinanceTxnTypes.All.Where(x => FinanceTxnTypes.PartyAffecting.Contains(x.Key))
                .Select(x => new Option(x.Key, x.Label))).ToList();

    /// <summary>Ödeme yöntemi — Türkiye'de alışılmış seçenekler. SABİT KURAL DEĞİL, serbest metindir:
    /// listede olmayan bir yöntem yazılabilir, ileride POS/çek/senet aynı alana girer.</summary>
    public IReadOnlyList<Option> PaymentMethods { get; } = new[]
    {
        new Option("nakit", "Nakit"),
        new Option("havale", "Havale / EFT"),
        new Option("kredi_karti", "Kredi Kartı"),
        new Option("cek", "Çek"),
        new Option("senet", "Senet"),
    };

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _typeFilter = "";
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _busy;

    public int PageCount => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
    public string PageText => $"Sayfa {Page} / {PageCount} · {TotalCount} işlem";
    public bool CanPrev => Page > 1;
    public bool CanNext => Page < PageCount;

    public PaymentsViewModel(SessionContext session)
    {
        _session = session;
        // Ortak şube kapsamı — seçim değişince liste yenilenir (kullanıcı Ara demek zorunda kalmasın).
        BranchScope = new BranchScopeSelector(session, () => _ = Load());
        _ = Load();
    }

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
                var res = DesktopServices.FinanceQueries.Transactions(_session,
                    null,
                    string.IsNullOrWhiteSpace(TypeFilter) ? null : TypeFilter,
                    null,
                    string.IsNullOrWhiteSpace(Search) ? null : Search,
                    null, null, Page, PageSize, BranchScope.Filter);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Rows.Clear();
                    foreach (var r in res.Items) Rows.Add(r);
                    TotalCount = res.TotalCount;
                    Notify();
                    Status = res.TotalCount == 0 ? "İşlem bulunamadı." : $"{res.TotalCount} işlem";
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

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // TAHSİLAT / ÖDEME FORMU
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Formda tek açık fatura satırı: kapatılacak mı, ne kadar?</summary>
    public sealed partial class OpenInvoiceLine : ObservableObject
    {
        private readonly Action _changed;
        public OpenInvoiceLine(OpenInvoiceRow row, Action changed)
        {
            Row = row;
            _changed = changed;
            _amount = 0m;
        }

        public OpenInvoiceRow Row { get; }
        public string InvoiceNo => Row.InvoiceNo;
        public string DateText => Row.DateText;
        public string DueText => Row.DueText;
        public decimal Remaining => Row.Remaining;
        public string RemainingText => $"{Row.Remaining:0.00} {Row.Currency}";

        [ObservableProperty] private bool _selected;
        [ObservableProperty] private decimal _amount;

        partial void OnSelectedChanged(bool value)
        {
            // İşaretlenince kalan tutar otomatik dolar — kullanıcı çoğu zaman tamamını kapatır.
            if (value && Amount == 0m) Amount = Row.Remaining;
            if (!value) Amount = 0m;
            _changed();
        }

        partial void OnAmountChanged(decimal value) => _changed();
    }

    [ObservableProperty] private bool _formOpen;
    [ObservableProperty] private string _fType = FinanceTxnTypes.Receipt;
    [ObservableProperty] private string? _fAccountId;
    [ObservableProperty] private string? _fPartyId;
    [ObservableProperty] private decimal _fAmount;
    [ObservableProperty] private DateTimeOffset? _fDate = DateTimeOffset.Now;
    [ObservableProperty] private string _fPaymentMethod = "nakit";
    [ObservableProperty] private string _fDocNo = "";
    [ObservableProperty] private string _fReferenceNo = "";
    [ObservableProperty] private string _fDescription = "";
    [ObservableProperty] private bool _saving;
    [ObservableProperty] private string _allocationSummary = "—";

    private string _operationId = "";

    public ObservableCollection<Option> AccountOptions { get; } = new();
    public ObservableCollection<Option> PartyOptions { get; } = new();

    /// <summary>Kullanıcıya yönü AÇIKÇA anlatır — "hangi yöne gidiyor" tahmini kalmasın.</summary>
    public string DirectionHint => FType == FinanceTxnTypes.Receipt
        ? "TAHSİLAT: Kasa/banka ARTAR, müşterinin size olan borcu AZALIR."
        : "ÖDEME: Kasa/banka AZALIR, sizin tedarikçiye olan borcunuz AZALIR.";

    partial void OnFTypeChanged(string value)
    {
        OnPropertyChanged(nameof(DirectionHint));
        _ = LoadOpenInvoices();     // yön değişince kapatılabilecek fatura kümesi de değişir
    }

    partial void OnFPartyIdChanged(string? value) => _ = LoadOpenInvoices();

    [RelayCommand]
    private async Task NewEntry()
    {
        if (!CanCreate) { FormError = "İşlem yetkiniz yok."; return; }
        FormError = null;
        _operationId = "pay-" + Guid.NewGuid().ToString("N");
        FType = FinanceTxnTypes.Receipt;
        FAccountId = null; FPartyId = null; FAmount = 0m;
        FDate = DateTimeOffset.Now; FPaymentMethod = "nakit";
        FDocNo = ""; FReferenceNo = ""; FDescription = "";
        OpenInvoices.Clear();
        AllocationSummary = "—";
        FormOpen = true;
        await LoadLookups();
    }

    private async Task LoadLookups()
    {
        try
        {
            await Task.Run(() =>
            {
                var accounts = DesktopServices.FinanceQueries.Accounts(_session)
                    .Select(a => new Option(a.Account.Id, $"{a.Account.Code} — {a.Account.Name} ({a.BalanceText})")).ToList();
                var parties = DesktopServices.Parties.List(_session, null, null, true, 1, 500).Items
                    .Select(p => new Option(p.Party.Id, $"{p.Party.Code} — {p.Party.Title}")).ToList();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    AccountOptions.Clear(); foreach (var a in accounts) AccountOptions.Add(a);
                    PartyOptions.Clear(); foreach (var p in parties) PartyOptions.Add(p);
                });
            });
        }
        catch (Exception ex) { FormError = "Seçenekler yüklenemedi: " + ex.Message; }
    }

    /// <summary>
    /// Seçili carinin AÇIK faturaları. Tahsilatta yalnız SATIŞ, ödemede yalnız ALIŞ faturaları
    /// gelir — ters eşleşme kullanıcıya hiç gösterilmez (servis de ayrıca reddeder).
    /// </summary>
    private async Task LoadOpenInvoices()
    {
        OpenInvoices.Clear();
        AllocationSummary = "—";
        if (string.IsNullOrWhiteSpace(FPartyId)) return;
        var dir = FType == FinanceTxnTypes.Receipt ? InvoiceDirections.Sales : InvoiceDirections.Purchase;
        try
        {
            var pid = FPartyId!;
            await Task.Run(() =>
            {
                var list = DesktopServices.FinanceQueries.OpenInvoices(_session, pid, dir);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var r in list) OpenInvoices.Add(new OpenInvoiceLine(r, RecalcAllocation));
                    RecalcAllocation();
                });
            });
        }
        catch (Exception ex) { FormError = "Açık faturalar yüklenemedi: " + ex.Message; }
    }

    private void RecalcAllocation()
    {
        var chosen = OpenInvoices.Where(x => x.Selected).ToList();
        if (chosen.Count == 0) { AllocationSummary = "Fatura seçilmedi — bağımsız cari hareketi olarak kaydedilir."; return; }
        var total = chosen.Sum(x => x.Amount);
        AllocationSummary = $"{chosen.Count} faturaya toplam {total:0.00} dağıtıldı" +
                            (total > FAmount ? " ⚠ işlem tutarını aşıyor" : "");
    }

    partial void OnFAmountChanged(decimal value) => RecalcAllocation();

    [RelayCommand]
    private void CloseForm() { FormOpen = false; FormError = null; }

    /// <summary>Seçili faturaların kalanını işlem tutarına doldurur (hızlı kullanım).</summary>
    [RelayCommand]
    private void FillFromInvoices()
    {
        var total = OpenInvoices.Where(x => x.Selected).Sum(x => x.Amount);
        if (total > 0) FAmount = total;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (Saving) return;
        FormError = null;
        if (string.IsNullOrWhiteSpace(FAccountId)) { FormError = "Kasa/banka hesabı seçin."; return; }
        if (string.IsNullOrWhiteSpace(FPartyId)) { FormError = "Cari seçin."; return; }
        if (FAmount <= 0) { FormError = "Tutar sıfırdan büyük olmalıdır."; return; }

        Saving = true;
        try
        {
            var allocations = OpenInvoices
                .Where(x => x.Selected && x.Amount > 0)
                .Select(x => new InvoiceAllocationInput(x.Row.Id, x.Amount))
                .ToList();

            var r = DesktopServices.Finance.Add(_session, new NewFinanceEntry(
                FAccountId!, FType, FAmount, _operationId,
                PartyId: FPartyId,
                TxnDate: FDate?.ToUnixTimeMilliseconds(),
                Description: Nz(FDescription),
                DocNo: Nz(FDocNo),
                PaymentMethod: Nz(FPaymentMethod),
                ReferenceNo: Nz(FReferenceNo),
                Allocations: allocations));

            Status = r.AlreadyExisted
                ? "Bu işlem zaten kaydedilmişti — ikinci kayıt oluşturulmadı."
                : $"{FinanceTxnTypes.Label(FType)} kaydedildi." +
                  (allocations.Count > 0 ? $" {allocations.Count} fatura kapatıldı." : "");
            FormOpen = false;
            await Load();
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Saving = false; }
    }

    private static string? Nz(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // TERS KAYIT (SİLME DEĞİL)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [ObservableProperty] private FinanceTxnRow? _selected;
    [ObservableProperty] private bool _reversePanelOpen;
    [ObservableProperty] private string _reverseReason = "";

    public bool CanReverseSelected =>
        Selected is { } t && !t.IsReversed && !t.IsReversalEntry && CanEdit;

    partial void OnSelectedChanged(FinanceTxnRow? value)
    {
        OnPropertyChanged(nameof(CanReverseSelected));
        OnPropertyChanged(nameof(ReverseWarning));
    }

    public string ReverseWarning => Selected is not { } t ? "" :
        $"'{t.TypeText}' hareketi ({t.Amount:0.00} {t.Currency}) SİLİNMEZ; " +
        "kaydı durur ve etkisi karşı kayıtla sıfırlanır:\n" +
        "• Kasa/banka bakiyesi eski hâline döner.\n" +
        "• Cari hareketi ters yönde yazılır.\n" +
        "• Bu işlemle kapatılan faturaların KALANI geri artar.";

    [RelayCommand]
    private void OpenReversePanel()
    {
        if (!CanReverseSelected) return;
        ReverseReason = "";
        FormError = null;
        OnPropertyChanged(nameof(ReverseWarning));
        ReversePanelOpen = true;
    }

    [RelayCommand] private void CloseReversePanel() => ReversePanelOpen = false;

    [RelayCommand]
    private async Task ReverseTxn()
    {
        if (Selected is null || !CanReverseSelected) return;
        if (string.IsNullOrWhiteSpace(ReverseReason)) { FormError = "İptal gerekçesi zorunlu."; return; }
        try
        {
            DesktopServices.Finance.Reverse(_session, Selected.Id, ReverseReason);
            ReversePanelOpen = false;
            Status = "İşlem ters kayıtla iptal edildi.";
            await Load();
        }
        catch (Exception ex) { FormError = "İptal edilemedi: " + ex.Message; }
    }
}
