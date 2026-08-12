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
/// G4-3 — KASA / BANKA (masaüstü, ANA KULLANIM KANALI).
///
/// Sol: hesap listesi (kasa + banka, tür filtresi, bakiyeleriyle). Sağ: seçili hesabın ekstresi,
/// hesap formu veya iç transfer paneli.
///
/// <b>ÇEVRİMDIŞI:</b> yerel <c>FinanceService</c> doğrudan çağrılır (yerel SQLite). İş kuralları
/// SERVİSTEDİR → web ile birebir aynı doğrulama, aynı bakiye, aynı yetki.
///
/// <b>⚠️ PARALEL DEFTER YOK:</b> bu ekran ne cari ne stok tablosuna yazar. Tahsilat/ödeme burada
/// DEĞİL, Tahsilat/Ödeme ekranındadır (<see cref="PaymentsViewModel"/>) — hesap tanımı ile para
/// hareketi ayrı işlerdir.
///
/// <b>⚠️ SİLME YOK (hareket için):</b> hareketlerde "Sil" butonu YOKTUR; yalnız gerekçeli
/// "Ters Kayıt" vardır. Hesap tanımı silinebilir ama YALNIZ hareketi yoksa.
///
/// <b>BAKİYE SAKLANMAZ:</b> ekranda görünen her bakiye defterden hesaplanır.
/// </summary>
public sealed partial class FinanceViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    /// <summary>G4-3d — ORTAK ŞUBE KAPSAMI. Seçim OKUMA filtresidir; yazmada tekil
    /// <see cref="BranchScopeSelector.ActiveWriteBranchId"/> kullanılır.</summary>
    public BranchScopeSelector BranchScope { get; }

    public bool CanCreate => AccessControl.Can(_session, FinanceService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, FinanceService.Module, PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, FinanceService.Module, PermissionAction.Delete);

    public ObservableCollection<FinanceAccountRow> Rows { get; } = new();
    public ObservableCollection<FinanceStatementRow> Statement { get; } = new();

    /// <summary>Açılır liste öğesi. ValueTuple Avalonia bağlamalarında çözülemediği için gerçek tip.</summary>
    public sealed record Option(string Key, string Label);

    public IReadOnlyList<Option> KindFilters { get; } =
        new[] { new Option("", "Tümü") }
            .Concat(FinanceAccountKinds.All.Select(x => new Option(x.Key, x.Label))).ToList();

    public IReadOnlyList<Option> KindOptions { get; } =
        FinanceAccountKinds.All.Select(x => new Option(x.Key, x.Label)).ToList();

    /// <summary>Hesap ekranından ELLE girilebilen hareket türleri — tahsilat/ödeme buraya girmez.</summary>
    public IReadOnlyList<Option> ManualTxnTypes { get; } =
        FinanceTxnTypes.All.Where(x => FinanceTxnTypes.ManualEntry.Contains(x.Key))
            .Select(x => new Option(x.Key, x.Label)).ToList();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _kindFilter = "";
    [ObservableProperty] private bool _onlyActive = true;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _busy;

    /// <summary>Kasa + banka toplamı — firmanın elindeki toplam para (hesaplanır, saklanmaz).</summary>
    [ObservableProperty] private string _totalText = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowStatement))]
    private FinanceAccountRow? _selected;
    public bool HasSelection => Selected is not null;

    /// <summary>Ekstre yalnız hiçbir panel açık DEĞİLKEN gösterilir (paneller aynı sütunu paylaşır).</summary>
    public bool ShowStatement => HasSelection && !FormOpen && !TransferOpen && !EntryOpen;

    public FinanceViewModel(SessionContext session)
    {
        _session = session;
        // Ortak şube kapsamı — seçim değişince liste yenilenir (kullanıcı Ara demek zorunda kalmasın).
        BranchScope = new BranchScopeSelector(session, () => _ = Load());
        _ = Load();
    }

    partial void OnSelectedChanged(FinanceAccountRow? value) => _ = LoadStatement(value);

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
                var rows = DesktopServices.FinanceQueries.Accounts(_session,
                    string.IsNullOrWhiteSpace(KindFilter) ? null : KindFilter,
                    OnlyActive,
                    string.IsNullOrWhiteSpace(Search) ? null : Search, BranchScope.Filter);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Rows.Clear();
                    foreach (var r in rows) Rows.Add(r);
                    var toplam = rows.Sum(x => x.Balance);
                    TotalText = rows.Count == 0 ? "—" : $"Toplam: {toplam:0.00} TL · {rows.Count} hesap";
                    Status = rows.Count == 0 ? "Hesap bulunamadı." : $"{rows.Count} hesap";
                });
            });
        }
        catch (Exception ex) { FormError = "Liste yüklenemedi: " + ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand] private async Task Find() => await Load();

    private async Task LoadStatement(FinanceAccountRow? row)
    {
        Statement.Clear();
        if (row is null) return;
        try
        {
            await Task.Run(() =>
            {
                var st = DesktopServices.FinanceQueries.Statement(_session, row.Account.Id);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var e in st) Statement.Add(e);
                });
            });
        }
        catch (Exception ex) { FormError = "Hesap ekstresi yüklenemedi: " + ex.Message; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // HESAP FORMU
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatement))]
    private bool _formOpen;

    [ObservableProperty] private string _formTitle = "Yeni Hesap";
    [ObservableProperty] private string _fCode = "";
    [ObservableProperty] private string _fName = "";
    [ObservableProperty] private string _fKind = FinanceAccountKinds.Cash;
    [ObservableProperty] private string? _fBranchId;
    [ObservableProperty] private string _fBankName = "";
    [ObservableProperty] private string _fBankBranch = "";
    [ObservableProperty] private string _fAccountNo = "";
    [ObservableProperty] private string _fIban = "";
    [ObservableProperty] private string _fNote = "";
    [ObservableProperty] private bool _fIsDefault;
    [ObservableProperty] private bool _fIsActive = true;
    [ObservableProperty] private bool _saving;

    private string? _editId;
    private long? _editVersion;

    /// <summary>Banka alanları yalnız BANKA hesabında anlamlıdır; kasada gizlenir.</summary>
    public bool ShowBankFields => FKind == FinanceAccountKinds.Bank;
    partial void OnFKindChanged(string value) => OnPropertyChanged(nameof(ShowBankFields));

    public ObservableCollection<Option> BranchOptions { get; } = new();

    [RelayCommand]
    private async Task NewAccount()
    {
        if (!CanCreate) { FormError = "Hesap oluşturma yetkiniz yok."; return; }
        _editId = null; _editVersion = null;
        FormTitle = "Yeni Hesap";
        FCode = ""; FName = ""; FKind = FinanceAccountKinds.Cash; FBranchId = null;
        FBankName = ""; FBankBranch = ""; FAccountNo = ""; FIban = ""; FNote = "";
        FIsDefault = false; FIsActive = true;
        FormError = null;
        FormOpen = true;
        await LoadBranches();
    }

    [RelayCommand]
    private async Task EditAccount()
    {
        if (Selected is null || !CanEdit) return;
        var a = DesktopServices.FinanceQueries.Account(_session, Selected.Account.Id);
        _editId = a.Id; _editVersion = a.Version;
        FormTitle = "Hesabı Düzenle";
        FCode = a.Code; FName = a.Name; FKind = a.AccountKind; FBranchId = a.BranchId;
        FBankName = a.BankName ?? ""; FBankBranch = a.BankBranch ?? "";
        FAccountNo = a.AccountNo ?? ""; FIban = a.Iban ?? ""; FNote = a.Note ?? "";
        FIsDefault = a.IsDefault; FIsActive = a.IsActive;
        FormError = null;
        FormOpen = true;
        await LoadBranches();
    }

    private async Task LoadBranches()
    {
        try
        {
            await Task.Run(() =>
            {
                // ⭐ G4-3d: yalnız YETKİLİ şubeler.
                var list = BranchScope.Branches.Select(b => new Option(b.Key, b.Label)).ToList();
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    BranchOptions.Clear();
                    BranchOptions.Add(new Option("", "Firma geneli"));
                    foreach (var b in list) BranchOptions.Add(b);
                });
            });
        }
        catch { /* şube listesi alınamazsa form yine çalışır (şube isteğe bağlı) */ }
    }

    [RelayCommand] private void CloseForm() { FormOpen = false; FormError = null; }

    [RelayCommand]
    private async Task SaveAccount()
    {
        if (Saving) return;
        Saving = true; FormError = null;
        try
        {
            var branch = string.IsNullOrWhiteSpace(FBranchId) ? null : FBranchId;
            if (_editId is null)
            {
                DesktopServices.Finance.CreateAccount(_session, new NewFinanceAccount(
                    FCode, FName, FKind, "TRY", branch,
                    Nz(FBankName), Nz(FBankBranch), Nz(FAccountNo), Nz(FIban), Nz(FNote), FIsDefault));
                Status = $"'{FName}' hesabı oluşturuldu.";
            }
            else
            {
                DesktopServices.Finance.UpdateAccount(_session, _editId, new UpdateFinanceAccount(
                    FCode, FName, FKind, "TRY", branch,
                    Nz(FBankName), Nz(FBankBranch), Nz(FAccountNo), Nz(FIban), Nz(FNote),
                    FIsDefault, FIsActive, _editVersion));
                Status = $"'{FName}' hesabı güncellendi.";
            }
            FormOpen = false;
            await Load();
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Saving = false; }
    }

    private static string? Nz(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Aktif/pasif — SİLME değil. Pasif hesap yeni işlemde seçilemez; geçmişi korunur.</summary>
    [RelayCommand]
    private async Task ToggleActive()
    {
        if (Selected is null || !CanEdit) return;
        var a = Selected.Account;
        var yeni = !a.IsActive;
        if (!await ConfirmService.AskAsync(
                $"'{a.Name}' hesabı {(yeni ? "AKTİF" : "PASİF")} yapılsın mı?" +
                (yeni ? "" : "\n\nPasif hesapta yeni işlem yapılamaz; geçmiş hareketleri ve bakiyesi korunur."),
                "Hesap Durumu")) return;
        try
        {
            DesktopServices.Finance.SetAccountActive(_session, a.Id, yeni);
            Status = $"'{a.Name}' {(yeni ? "aktif" : "pasif")} yapıldı.";
            await Load();
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteAccount()
    {
        if (Selected is null || !CanDelete) return;
        var a = Selected.Account;
        if (!await ConfirmService.AskAsync(
                $"'{a.Name}' hesabı silinsin mi?\n\n" +
                "Hareketi olan hesap SİLİNEMEZ — bu durumda hesabı pasif yapın.", "Hesabı Sil")) return;
        try
        {
            DesktopServices.Finance.DeleteAccount(_session, a.Id);
            Status = $"'{a.Name}' silindi.";
            Selected = null;
            await Load();
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // ELLE HAREKET (yalnız açılış / düzeltme)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatement))]
    private bool _entryOpen;

    [ObservableProperty] private string _eType = FinanceTxnTypes.Opening;
    [ObservableProperty] private decimal _eAmount;
    [ObservableProperty] private DateTimeOffset? _eDate = DateTimeOffset.Now;
    [ObservableProperty] private string _eDescription = "";
    private string _entryOp = "";

    [RelayCommand]
    private void NewEntry()
    {
        if (Selected is null || !CanCreate) { FormError = "Önce hesap seçin."; return; }
        _entryOp = "fin-" + Guid.NewGuid().ToString("N");   // kayıt bitene kadar SABİT (çift tıklama koruması)
        EType = FinanceTxnTypes.Opening;
        EAmount = 0m; EDate = DateTimeOffset.Now; EDescription = "";
        FormError = null;
        EntryOpen = true;
    }

    [RelayCommand] private void CloseEntry() { EntryOpen = false; FormError = null; }

    [RelayCommand]
    private async Task SaveEntry()
    {
        if (Selected is null || Saving) return;
        Saving = true; FormError = null;
        try
        {
            DesktopServices.Finance.Add(_session, new NewFinanceEntry(
                Selected.Account.Id, EType, EAmount, _entryOp,
                TxnDate: EDate?.ToUnixTimeMilliseconds(),
                Description: Nz(EDescription)));
            Status = "Hareket kaydedildi.";
            EntryOpen = false;
            var id = Selected.Account.Id;
            await Load();
            Selected = Rows.FirstOrDefault(x => x.Account.Id == id);
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Saving = false; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // İÇ TRANSFER
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStatement))]
    private bool _transferOpen;

    [ObservableProperty] private string? _tFrom;
    [ObservableProperty] private string? _tTo;
    [ObservableProperty] private decimal _tAmount;
    [ObservableProperty] private DateTimeOffset? _tDate = DateTimeOffset.Now;
    [ObservableProperty] private string _tDescription = "";
    private string _transferOp = "";

    public ObservableCollection<Option> AccountOptions { get; } = new();

    [RelayCommand]
    private void NewTransfer()
    {
        if (!CanCreate) { FormError = "İşlem yetkiniz yok."; return; }
        _transferOp = "trf-" + Guid.NewGuid().ToString("N");
        TFrom = Selected?.Account.Id; TTo = null; TAmount = 0m;
        TDate = DateTimeOffset.Now; TDescription = "";
        AccountOptions.Clear();
        foreach (var r in Rows) AccountOptions.Add(new Option(r.Account.Id, $"{r.Account.Code} — {r.Account.Name} ({r.BalanceText})"));
        FormError = null;
        TransferOpen = true;
    }

    [RelayCommand] private void CloseTransfer() { TransferOpen = false; FormError = null; }

    [RelayCommand]
    private async Task SaveTransfer()
    {
        if (Saving) return;
        if (string.IsNullOrWhiteSpace(TFrom) || string.IsNullOrWhiteSpace(TTo))
        { FormError = "Kaynak ve hedef hesap seçin."; return; }

        Saving = true; FormError = null;
        try
        {
            var r = DesktopServices.Finance.Transfer(_session, new NewFinanceTransfer(
                TFrom!, TTo!, TAmount, _transferOp, TDate?.ToUnixTimeMilliseconds(), Nz(TDescription)));
            Status = r.AlreadyExisted
                ? "Bu transfer zaten kaydedilmişti — ikinci kayıt oluşturulmadı."
                : "Transfer kaydedildi. (Toplam para değişmedi: bir hesaptan çıktı, diğerine girdi.)";
            TransferOpen = false;
            await Load();
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Saving = false; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // TERS KAYIT (SİLME DEĞİL)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [ObservableProperty] private bool _reversePanelOpen;
    [ObservableProperty] private string _reverseReason = "";
    [ObservableProperty] private FinanceStatementRow? _selectedTxn;

    /// <summary>Yalnız YÜRÜRLÜKTEKİ ve ters-kayıt-olmayan hareket iptal edilebilir.</summary>
    public bool CanReverseSelected =>
        SelectedTxn is { } t && !t.Txn.IsReversed && !t.Txn.IsReversalEntry && CanEdit;

    partial void OnSelectedTxnChanged(FinanceStatementRow? value)
    {
        OnPropertyChanged(nameof(CanReverseSelected));
        OnPropertyChanged(nameof(ReverseWarning));
    }

    /// <summary>Kullanıcıya ne olacağı AÇIKÇA yazılır (tahmin etmek zorunda kalmasın).</summary>
    public string ReverseWarning => SelectedTxn is not { } t ? "" :
        $"'{t.Txn.TypeText}' hareketi ({t.Txn.Amount:0.00} {t.Txn.Currency}) SİLİNMEZ; " +
        "kaydı durur ve etkisi karşı kayıtla sıfırlanır:\n" +
        "• Hesap bakiyesi eski hâline döner.\n" +
        (t.Txn.PartyId is null ? "• Cari etkisi yok.\n" : "• Cari hareketi ters yönde yazılır.\n") +
        (t.Txn.IsTransfer ? "• Transferin İKİ BACAĞI birlikte geri alınır (yarım transfer kalmaz).\n" : "") +
        "• Kapatılan fatura varsa kalanı geri artar.";

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
        if (SelectedTxn is null || !CanReverseSelected) return;
        if (string.IsNullOrWhiteSpace(ReverseReason)) { FormError = "İptal gerekçesi zorunlu."; return; }
        try
        {
            DesktopServices.Finance.Reverse(_session, SelectedTxn.Txn.Id, ReverseReason);
            ReversePanelOpen = false;
            Status = "Hareket ters kayıtla iptal edildi.";
            var id = Selected?.Account.Id;
            await Load();
            Selected = Rows.FirstOrDefault(x => x.Account.Id == id);
        }
        catch (Exception ex) { FormError = "İptal edilemedi: " + ex.Message; }
    }
}
