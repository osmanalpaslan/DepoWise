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
using DepoWise.Infrastructure.Materials;   // MUH-01c: LookupItem (tedarikçi eşlemesi)

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// G4-1 — CARİ HESAPLAR (masaüstü, ANA KULLANIM KANALI).
///
/// Sol: cari listesi (arama + tip/durum filtresi + sayfalama). Sağ: seçili carinin kartı —
/// genel bilgiler, finansal özet (borç/alacak/bakiye) ve hesap hareketleri.
///
/// <b>ÇEVRİMDIŞI:</b> yerel <c>PartyService</c>/<c>PartyLedgerService</c> doğrudan çağrılır (yerel SQLite).
/// İş kuralları SERVİSTEDİR → web ile birebir aynı doğrulama, aynı yetki, aynı bakiye.
///
/// <b>⚠️ STOKLA SINIR:</b> bu ekran stok tablolarına dokunmaz. Cari borç/alacağı ile stok defteri
/// AYRI kalır; stok yazımının tek yolu <c>StockService</c>'tir.
///
/// <b>PERFORMANS:</b> tüm cariler RAM'e ÇEKİLMEZ — sunucu tarafı sayfalama (varsayılan 50) ve
/// bakiyeler tek sorguda gelir (satır başına ayrı okuma YOK).
/// </summary>
public sealed partial class PartiesViewModel : ViewModelBase
{
    private readonly SessionContext _session;
    private const int PageSize = 50;

    /// <summary>G4-3d — ORTAK ŞUBE KAPSAMI. Seçim OKUMA filtresidir; yazmada tekil
    /// <see cref="BranchScopeSelector.ActiveWriteBranchId"/> kullanılır.</summary>
    public BranchScopeSelector BranchScope { get; }

    public bool CanCreate => AccessControl.Can(_session, PartyService.Module, PermissionAction.Create);
    public bool CanEdit => AccessControl.Can(_session, PartyService.Module, PermissionAction.Edit);
    public bool CanDelete => AccessControl.Can(_session, PartyService.Module, PermissionAction.Delete);

    public ObservableCollection<PartyListRow> Rows { get; } = new();
    public ObservableCollection<PartyStatementRow> Ledger { get; } = new();

    /// <summary>Açılır liste öğesi. ValueTuple Avalonia bağlamalarında çözülemediği için gerçek tip.</summary>
    public sealed record Option(string Key, string Label);

    /// <summary>Cari tipi seçenekleri — katalogdan (web ile AYNI etiketler).</summary>
    public IReadOnlyList<Option> TypeOptions { get; } =
        new[] { new Option("", "Tümü") }
            .Concat(PartyTypes.All.Select(x => new Option(x.Key, x.Label))).ToList();

    /// <summary>Elle girilebilen belge türleri (fatura/tahsilat G4-2/G4-3'ten gelecek).</summary>
    public IReadOnlyList<Option> ManualDocTypes { get; } =
        PartyDocTypes.All.Where(x => PartyDocTypes.ManualEntry.Contains(x.Key))
            .Select(x => new Option(x.Key, x.Label)).ToList();

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _typeFilter = "";
    [ObservableProperty] private bool _onlyActive = true;
    [ObservableProperty] private int _page = 1;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private bool _busy;

    public int PageCount => TotalCount == 0 ? 1 : (TotalCount + PageSize - 1) / PageSize;
    public string PageText => $"Sayfa {Page} / {PageCount} · {TotalCount} cari";
    public bool CanPrev => Page > 1;
    public bool CanNext => Page < PageCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowCard))]
    private PartyListRow? _selected;
    public bool HasSelection => Selected is not null;
    /// <summary>Kart yalnız hiçbir form açık DEĞİLKEN gösterilir (üç panel aynı sütunu paylaşır).</summary>
    public bool ShowCard => HasSelection && !FormOpen && !EntryOpen;

    // ── Seçili carinin finansal özeti (defterden TÜRETİLİR, saklanmaz) ──
    [ObservableProperty] private decimal _debit;
    [ObservableProperty] private decimal _credit;
    [ObservableProperty] private string _balanceText = "—";
    [ObservableProperty] private string _lastEntryText = "—";

    public PartiesViewModel(SessionContext session)
    {
        _session = session;
        // Ortak şube kapsamı — seçim değişince liste yenilenir (kullanıcı Ara demek zorunda kalmasın).
        BranchScope = new BranchScopeSelector(session, () => _ = Load());
        _ = Load();
    }

    partial void OnSelectedChanged(PartyListRow? value) => _ = LoadCard(value);

    [RelayCommand]
    private async Task Load()
    {
        Busy = true; FormError = null;
        try
        {
            await Task.Run(() =>
            {
                var res = DesktopServices.Parties.List(_session,
                    string.IsNullOrWhiteSpace(Search) ? null : Search,
                    string.IsNullOrWhiteSpace(TypeFilter) ? null : TypeFilter,
                    OnlyActive ? true : null, Page, PageSize, BranchScope.Filter);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Rows.Clear();
                    foreach (var r in res.Items) Rows.Add(r);
                    TotalCount = res.TotalCount;
                    Notify();
                    Status = res.TotalCount == 0 ? "Cari bulunamadı." : $"{res.TotalCount} cari";
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

    [RelayCommand]
    private async Task Find() { Page = 1; await Load(); }

    [RelayCommand]
    private async Task PrevPage() { if (CanPrev) { Page--; await Load(); } }

    [RelayCommand]
    private async Task NextPage() { if (CanNext) { Page++; await Load(); } }

    /// <summary>Seçili carinin kartı: finansal özet + hesap hareketleri (en yeni önce).</summary>
    private async Task LoadCard(PartyListRow? row)
    {
        Ledger.Clear();
        Debit = Credit = 0m; BalanceText = "—"; LastEntryText = "—";
        if (row is null) return;
        try
        {
            await Task.Run(() =>
            {
                // ⭐ G4-3d: kart bakiyesi ve ekstre de ŞUBE KAPSAMINDA (liste ile sessiz fark olmasın).
                var b = DesktopServices.PartyLedger.Balance(_session, row.Party.Id, BranchScope.Filter);
                var st = DesktopServices.PartyLedger.Statement(_session, row.Party.Id, branchIds: BranchScope.Filter);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    Debit = b.Debit; Credit = b.Credit;
                    BalanceText = b.BalanceText; LastEntryText = b.LastEntryText;
                    foreach (var e in st) Ledger.Add(e);
                });
            });
        }
        catch (Exception ex) { FormError = "Cari kartı yüklenemedi: " + ex.Message; }
    }

    /// <summary>Aktif/pasif — SİLME değil. Hareketi olan cari silinemez; pasif doğru yoldur.</summary>
    [RelayCommand]
    private async Task ToggleActive()
    {
        if (Selected is null || !CanEdit) return;
        var p = Selected.Party;
        var yeni = !p.IsActive;
        if (!await ConfirmService.AskAsync(
                $"'{p.Title}' carisi {(yeni ? "AKTİF" : "PASİF")} yapılsın mı?" +
                (yeni ? "" : "\n\nPasif cari yeni işlemlerde seçilemez; geçmiş hareketleri korunur."),
                "Cari Durumu")) return;
        try
        {
            DesktopServices.Parties.SetActive(_session, p.Id, yeni);
            Status = $"'{p.Title}' {(yeni ? "aktif" : "pasif")} yapıldı.";
            await Load();
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // G4-1b — CARİ FORMU (oluşturma / düzenleme)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Form açık mı? Açıkken liste solda kalır (bağlam kaybolmasın).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    [NotifyPropertyChangedFor(nameof(ShowCard))]
    private bool _formOpen;

    /// <summary>Düzenlenen carinin id'si; null = YENİ kayıt.</summary>
    private string? _editId;
    /// <summary>Düzenleme kilidi jetonu — kaydederken geri gönderilir (çakışma korumasi).</summary>
    private long _editVersion;

    public string FormTitle => _editId is null ? "Yeni Cari" : "Cari Düzenle";

    // ── MUH-01c (2026-09-04): TEDARİKÇİ ↔ CARİ KÖPRÜSÜ ───────────────────────────────────────────
    // Şemada Migration066'dan beri vardı ama arayüzden kurulamıyordu. Eşleme kurulmadan yakıt depo
    // girişi ve satın alma alımları (karşı tarafı `supplier_id` ile tutar) cari defterine bağlanamaz.
    // O tablolara ikinci bir `party_id` kolonu EKLENMEDİ — aynı satırda iki gerçeklik olurdu.
    public ObservableCollection<LookupItem> Suppliers { get; } = new();
    [ObservableProperty] private LookupItem? _fSupplier;
    private void LoadSuppliers()
    {
        try
        {
            Suppliers.Clear();
            foreach (var x in DesktopServices.Lookups.List(_session, "suppliers"))
                Suppliers.Add(new LookupItem(x.Id, x.Name));
        }
        catch { }
    }

    [ObservableProperty] private string _fCode = "";
    [ObservableProperty] private string _fTitle = "";
    [ObservableProperty] private string _fType = PartyTypes.Customer;
    [ObservableProperty] private bool _fIsPerson;
    [ObservableProperty] private string _fTaxOffice = "";
    [ObservableProperty] private string _fTaxNo = "";
    [ObservableProperty] private string _fNationalId = "";
    [ObservableProperty] private string _fPhone = "";
    [ObservableProperty] private string _fEmail = "";
    [ObservableProperty] private string _fAddress = "";
    [ObservableProperty] private string _fCity = "";
    [ObservableProperty] private string _fDistrict = "";
    [ObservableProperty] private string _fNote = "";
    [ObservableProperty] private bool _fIsActive = true;

    /// <summary>Kaydetme sürüyor mu — çift tıklamada İKİ kayıt oluşmasını engeller.</summary>
    [ObservableProperty] private bool _saving;

    /// <summary>Form tipi seçenekleri ("Tümü" olmadan — kayıtta tip zorunludur).</summary>
    public IReadOnlyList<Option> FormTypeOptions { get; } =
        PartyTypes.All.Select(x => new Option(x.Key, x.Label)).ToList();

    [RelayCommand]
    private void NewParty()
    {
        if (!CanCreate) { FormError = "Cari ekleme yetkiniz yok."; return; }
        _editId = null; _editVersion = 0;
        FCode = ""; FTitle = ""; FType = PartyTypes.Customer; FIsPerson = false;
        FTaxOffice = FTaxNo = FNationalId = FPhone = FEmail = FAddress = FCity = FDistrict = FNote = "";
        FSupplier = null;   // ⭐ MUH-01c
        FIsActive = true;
        FormError = null; Status = null;
        FormOpen = true;
    }

    [RelayCommand]
    private void EditParty()
    {
        if (Selected is null) return;
        if (!CanEdit) { FormError = "Cari düzenleme yetkiniz yok."; return; }
        var p = Selected.Party;
        _editId = p.Id; _editVersion = p.Version;
        FCode = p.Code; FTitle = p.Title; FType = p.PartyType; FIsPerson = p.IsPerson;
        FTaxOffice = p.TaxOffice ?? ""; FTaxNo = p.TaxNo ?? ""; FNationalId = p.NationalId ?? "";
        FPhone = p.Phone ?? ""; FEmail = p.Email ?? ""; FAddress = p.Address ?? "";
        FCity = p.City ?? ""; FDistrict = p.District ?? ""; FNote = p.Note ?? "";
        FSupplier = Suppliers.FirstOrDefault(x => x.Id == p.SupplierId);   // ⭐ MUH-01c: eşleme ön-doldurulur
        FIsActive = p.IsActive;
        FormError = null; Status = null;
        FormOpen = true;
    }

    [RelayCommand]
    private async Task CancelForm()
    {
        // Veri kaybı uyarısı: kullanıcı doldurduğu formu yanlışlıkla kapatmasın.
        if (!string.IsNullOrWhiteSpace(FCode) || !string.IsNullOrWhiteSpace(FTitle))
            if (!await ConfirmService.AskAsync("Girdiğiniz bilgiler kaydedilmeden kapatılacak. Devam edilsin mi?",
                    "Formu Kapat", "Evet, Kapat")) return;
        FormOpen = false; FormError = null;
    }

    /// <summary>Kaydet — doğrulama SERVİSTEDİR; burada yalnız kullanıcıya erken geri bildirim verilir.
    /// Aynı kurallar API ve servis katmanında da çalışır (UI atlanabilir).</summary>
    [RelayCommand]
    private async Task SaveParty()
    {
        if (Saving) return;                     // çift tıklama koruması
        FormError = null; Status = null;

        // Erken geri bildirim (servis aynı kuralları TEKRAR uygular)
        if (string.IsNullOrWhiteSpace(FCode)) { FormError = "Cari kodu zorunlu."; return; }
        if (string.IsNullOrWhiteSpace(FTitle)) { FormError = "Ünvan / ad soyad zorunlu."; return; }

        Saving = true;
        try
        {
            var kaydedilenId = _editId;
            await Task.Run(() =>
            {
                if (_editId is null)
                {
                    kaydedilenId = DesktopServices.Parties.Create(_session, new NewParty(
                        FCode, FTitle, FType, FIsPerson, N(FTaxOffice), N(FTaxNo), N(FNationalId),
                        N(FPhone), N(FEmail), N(FAddress), N(FCity), N(FDistrict), "TRY", N(FNote),
                        FSupplier?.Id));   // ⭐ MUH-01c köprü
                }
                else
                {
                    DesktopServices.Parties.Update(_session, _editId, new UpdateParty(
                        FCode, FTitle, FType, FIsPerson, N(FTaxOffice), N(FTaxNo), N(FNationalId),
                        N(FPhone), N(FEmail), N(FAddress), N(FCity), N(FDistrict), "TRY", N(FNote),
                        FIsActive, _editVersion, FSupplier?.Id));   // ⭐ MUH-01c köprü
                }
            });

            Status = _editId is null ? $"'{FTitle}' carisi eklendi." : $"'{FTitle}' güncellendi.";
            FormOpen = false;
            await Load();
            // Kaydedilen cari listede seçili kalsın (kullanıcı bağlamı kaybetmesin).
            Selected = Rows.FirstOrDefault(r => r.Party.Id == kaydedilenId);
        }
        catch (ConcurrencyException)
        {
            FormError = "Bu cari siz formu açtıktan sonra başka biri tarafından değiştirildi. " +
                        "Listeyi yenileyip yeniden düzenleyin.";
        }
        catch (Exception ex) { FormError = ex.Message; }   // doğrulama/benzersizlik mesajları serviste üretilir
        finally { Saving = false; }
    }

    private static string? N(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // G4-1b — ELLE CARİ HAREKETİ (açılış / düzeltme) + TERS KAYIT
    // ═════════════════════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCard))]
    private bool _entryOpen;
    [ObservableProperty] private string _eDocType = PartyDocTypes.Opening;
    [ObservableProperty] private bool _eIsDebit = true;
    [ObservableProperty] private decimal _eAmount;
    [ObservableProperty] private string _eDocNo = "";
    [ObservableProperty] private string _eDescription = "";
    [ObservableProperty] private DateTimeOffset? _eDate = DateTimeOffset.Now;
    [ObservableProperty] private DateTimeOffset? _eDueDate;
    [ObservableProperty] private PartyStatementRow? _selectedEntry;
    /// <summary>Ters kayıt gerekçesi — serviste de ZORUNLUDUR (boş gerekçe reddedilir).</summary>
    [ObservableProperty] private string _reverseReason = "";

    [RelayCommand]
    private void NewEntry()
    {
        if (Selected is null) { FormError = "Önce cari seçin."; return; }
        if (!CanCreate) { FormError = "Cari hareketi ekleme yetkiniz yok."; return; }
        EDocType = PartyDocTypes.Opening; EIsDebit = true; EAmount = 0m;
        EDocNo = ""; EDescription = ""; EDate = DateTimeOffset.Now; EDueDate = null;
        FormError = null; Status = null;
        EntryOpen = true;
    }

    [RelayCommand]
    private void CancelEntry() { EntryOpen = false; FormError = null; }

    /// <summary>Hareket kaydet. Yalnız ELLE girilebilir türler (açılış/düzeltme) — fatura/tahsilat
    /// kendi modüllerinden gelir ve servis bunu ayrıca zorlar.</summary>
    [RelayCommand]
    private async Task SaveEntry()
    {
        if (Saving || Selected is null) return;
        FormError = null;
        if (EAmount <= 0) { FormError = "Tutar sıfırdan büyük olmalıdır."; return; }

        Saving = true;
        try
        {
            var partyId = Selected.Party.Id;
            await Task.Run(() => DesktopServices.PartyLedger.Add(_session, new NewLedgerEntry(
                partyId, EDocType, EAmount, EIsDebit,
                EntryDate: IsGunuTarihi.Ms(EDate),   // ADR-184: takvim tarihi → UTC gün başı
                DocNo: N(EDocNo), Description: N(EDescription),
                DueDate: IsGunuTarihi.Ms(EDueDate),   // ADR-184
                // Tek jeton: kaydetme tekrarlanırsa (ağ/çift tık) ikinci hareket OLUŞMAZ.
                OperationId: Guid.NewGuid().ToString("N"))));

            Status = $"{(EIsDebit ? "Borç" : "Alacak")} hareketi eklendi: {EAmount:0.##}";
            EntryOpen = false;
            await Load();
            Selected = Rows.FirstOrDefault(r => r.Party.Id == partyId);
        }
        catch (Exception ex) { FormError = ex.Message; }
        finally { Saving = false; }
    }

    /// <summary>Ters kayıt — hareketi SİLMEZ; gerekçeli karşı kayıt yazar (muhasebe geçmişi korunur).</summary>
    [RelayCommand]
    private async Task ReverseEntry()
    {
        if (SelectedEntry is null || Selected is null) return;
        if (!CanEdit) { FormError = "Hareket düzeltme yetkiniz yok."; return; }
        var e = SelectedEntry.Entry;
        if (e.IsReversed) { FormError = "Bu hareket zaten iptal edilmiş."; return; }

        // Gerekçe forma bağlıdır (ayrı bir metin diyaloğu altyapısı kurulmadı — mevcut onay
        // standardı korunur ve kullanıcı gerekçeyi görerek yazar).
        if (string.IsNullOrWhiteSpace(ReverseReason))
        {
            FormError = "Düzeltme gerekçesi zorunlu — 'Gerekçe' alanını doldurun.";
            return;
        }
        if (!await ConfirmService.AskAsync(
                $"{e.DateText} · {e.TypeText} · {(e.Debit > 0 ? "Borç" : "Alacak")} {(e.Debit > 0 ? e.Debit : e.Credit):0.##}\n\n" +
                "Bu hareket SİLİNMEZ; aynı tutarda ters kayıt yazılır ve ikisi de bakiyeden düşer.\n" +
                $"Gerekçe: {ReverseReason.Trim()}\n\nDevam edilsin mi?",
                "Hareketi Ters Çevir", "Evet, Ters Çevir")) return;

        try
        {
            var partyId = Selected.Party.Id;
            DesktopServices.PartyLedger.Reverse(_session, e.Id, ReverseReason.Trim());
            ReverseReason = "";
            Status = "Hareket ters kayıtla düzeltildi.";
            await Load();
            Selected = Rows.FirstOrDefault(r => r.Party.Id == partyId);
        }
        catch (Exception ex) { FormError = ex.Message; }
    }

    [RelayCommand]
    private async Task Delete()
    {
        if (Selected is null || !CanDelete) return;
        var p = Selected.Party;
        if (!await ConfirmService.AskAsync(
                $"'{p.Title}' carisi silinsin mi?\n\nHesap hareketi olan cari SİLİNEMEZ; bu durumda pasif yapmalısınız.",
                "Cari Sil", "Evet, Sil")) return;
        try
        {
            DesktopServices.Parties.Delete(_session, p.Id);
            Selected = null;
            Status = "Cari silindi.";
            await Load();
        }
        catch (Exception ex) { FormError = ex.Message; }   // "hareketi var → silinemez" mesajı buradan gelir
    }
}
