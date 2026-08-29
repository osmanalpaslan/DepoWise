using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// STK-08 — ATANMAMIŞ STOK DAĞITIMI (masaüstü, KARAR-8).
///
/// Geçmiş hareketlerde depo bilgisi girilmediği için stok "Atanmamış" kovasında duruyor. Sistem hangi
/// malzemenin hangi depoda olduğunu BİLMEZ ve TAHMİN ETMEZ — kullanıcı açıkça dağıtır.
///
/// 🔒 ÇEVRİMDIŞI: bu ekran API'ye HİÇ GİTMEZ. Liste ve dağıtım doğrudan yerel <c>StockService</c> ile
/// (yerel SQLite transaction) yapılır; depo listesi yerel <c>BranchService</c>'ten gelir. Bağlantı
/// geldiğinde hareketler mevcut <c>business-push</c> ile sunucuya taşınır — yeni protokol YOKTUR.
///
/// Dağıtım GERÇEK transfer hareketi üretir (kaynak = ATANMAMIŞ) → rapor, audit ve senkron mekanizmaları
/// kendiliğinden çalışır (bkz. <see cref="StockService.DistributeUnassigned"/>).
/// ⚠️ Transferler GERİ ALINMAZ (2026-08-06 kararı: iki deponun stoğunu etkiler) — yanlış dağıtımın
/// düzeltmesi, o depodan doğru depoya YENİ bir transferdir. Ekran metinleri bunu açıkça söyler.
/// </summary>
public sealed partial class StockDistributeViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "stock", PermissionAction.Create);

    /// <summary>TRH-01 — İŞLEM TARİHİ (iş günü). Kayıt anı (created_at) bundan bağımsızdır.</summary>
    [ObservableProperty] private DateTimeOffset? _docDate = new DateTimeOffset(DateTime.Today);

    /// <summary>TRH-01 — kullanıcı işlem tarihini değiştirebilir mi (btn-backdate). Yetki yoksa alan
    /// kilitlenir. Asıl kapı SUNUCUDADIR (DateEntryPolicy); arayüz kilidi güvenlik sayılmaz.</summary>
    public bool CanBackDate => DepoWise.Application.Security.DateEntryPolicy.Serbest(_session);
    public string DocDateHint => CanBackDate
        ? "İşlemin gerçekten yapıldığı gün. Geçmiş veya ileri tarih seçebilirsiniz; kaydın sisteme girildiği an ayrıca loglanır."
        : "Geri/ileri tarihli işlem yetkiniz yok — tarih bugüne sabitlidir.";

    /// <summary>Dağıtılabilecek malzemeler (ATANMAMIŞ kovasında miktarı olanlar).</summary>
    public ObservableCollection<UnassignedLineVm> Lines { get; } = new();

    /// <summary>Hedef seçenekleri — YALNIZ gerçek depolar. "Atanmamış" hedef olarak SUNULMAZ.</summary>
    public ObservableCollection<BranchRow> Targets { get; } = new();

    [ObservableProperty] private BranchRow? _target;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _busy;

    public bool HasRows => Lines.Count > 0;
    public bool IsEmpty => Lines.Count == 0;

    /// <summary>Ekran altındaki özet — kullanıcı kaydetmeden önce ne yaptığını görsün.</summary>
    public string SummaryText
    {
        get
        {
            var sel = Selected();
            return sel.Count == 0
                ? "Miktar girilmedi."
                : $"{sel.Count} malzeme · toplam {sel.Sum(l => l.Amount):0.##}";
        }
    }

    public StockDistributeViewModel(SessionContext session)
    {
        _session = session;
        LoadTargets();
        Load();
    }

    private List<UnassignedLineVm> Selected() => Lines.Where(l => l.Amount > 0).ToList();

    /// <summary>Depo listesi YEREL veritabanından — internet gerekmez (çevrimdışı kırmızı çizgi).</summary>
    private void LoadTargets()
    {
        Targets.Clear();
        try { foreach (var b in DesktopServices.Branches.List(_session)) Targets.Add(b); }
        catch { }
    }

    /// <summary>H-1 — "kaç kayıt var / kaç kayıt gösteriliyor". Metin SERVİSTEN gelir
    /// (<see cref="UnassignedPage.CountText"/>) → web ile masaüstü AYNI cümleyi gösterir.</summary>
    [ObservableProperty] private string _countText = "";

    /// <summary>Sayfaya sığmayan kayıt var mı? Ekran bunu vurgulu gösterir (gözden kaçmasın).</summary>
    [ObservableProperty] private bool _truncated;

    [RelayCommand]
    private void Load()
    {
        Lines.Clear();
        try
        {
            // TEK sorgu (StockService.ListUnassignedPage) — malzeme başına ayrı okuma YOK.
            // H-1: varsayılan ÜST SINIR (2000) istenir; canlıdaki 676 satır tek sayfaya sığar. Yine de
            // aşılırsa CountText kullanıcıya kaç kaydın ekranda OLMADIĞINI açıkça söyler.
            var page = DesktopServices.Stock.ListUnassignedPage(_session,
                string.IsNullOrWhiteSpace(Search) ? null : Search);
            foreach (var m in page.Items)
                Lines.Add(new UnassignedLineVm(m.MaterialId, m.Code, m.Name, m.Quantity, this));
            CountText = page.Truncated
                ? page.CountText + " Listeyi kod/ad ile arayarak daraltabilirsiniz; dağıtılan kalemler listeden düşer."
                : page.CountText;
            Truncated = page.Truncated;
        }
        catch (Exception ex) { FormError = "Liste yüklenemedi: " + ex.Message; CountText = ""; Truncated = false; }
        Notify();
    }

    internal void Notify()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(SummaryText));
    }

    [RelayCommand]
    private void FillAll(UnassignedLineVm? line)
    {
        if (line is null || line.Available <= 0) return;
        line.Amount = line.Available;   // yalnız alanı DOLDURUR; kaydetmez (sessiz işlem yok)
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null; Status = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        if (Targets.Count == 0)
        {
            // STK-08 bulgusu: firmada hiç depo tanımlı olmayabilir → kullanıcıyı yönlendir.
            FormError = "Firmanızda tanımlı depo/şantiye yok. Önce Şubeler ekranından en az bir depo oluşturun.";
            return;
        }
        if (Target is null) { FormError = "Hedef depo/şantiye seçin."; return; }

        var sel = Selected();
        if (sel.Count == 0) { FormError = "En az bir malzeme için dağıtılacak miktar girin."; return; }
        // Ekranda da engelle (servis zaten reddeder) — kullanıcı kaydete basmadan uyarılsın.
        var asan = sel.FirstOrDefault(l => l.Amount > l.Available);
        if (asan is not null)
        {
            FormError = $"{asan.Code}: dağıtılacak miktar ({asan.Amount:0.##}) atanmamış stoktan ({asan.Available:0.##}) fazla olamaz.";
            return;
        }

        var toplam = sel.Sum(l => l.Amount);
        if (!await ConfirmService.AskAsync(
            $"{sel.Count} malzeme, toplam {toplam:0.##} birim \"{Target.Name}\" deposuna aktarılacak.\n\n" +
            "Bu işlem gerçek bir transfer hareketi oluşturur. Yanlış depoya dağıtırsanız düzeltme yolu, " +
            "o depodan doğru depoya YENİ bir transfer yapmaktır (transferler geri alınmaz — " +
            "iki deponun stoğunu etkiler).\n\nDevam edilsin mi?",
            "Atanmamış Stok Dağıtımı", "Evet, Dağıt")) return;

        Busy = true;
        try
        {
            // ÇEVRİMDIŞI: doğrudan yerel servis → yerel SQLite transaction. API çağrısı YOK.
            // Tek belge + tek transaction: bir satır yetersizse TAMAMI geri alınır.
            DesktopServices.Stock.DistributeUnassigned(_session,
                sel.Select(l => new StockLine(l.MaterialId, l.Amount)).ToList(),
                Target.Id, Guid.NewGuid().ToString("N"), note: null,
                docDate: IsGunuTarihi.Ms(DocDate));   // TRH-01: iş günü (created_at DEĞİL) — ADR-184
            Status = $"Dağıtım kaydedildi: {sel.Count} malzeme → {Target.Name}.";
            Load();   // kalan atanmamış miktarlar tazelensin
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
        finally { Busy = false; }
    }

    /// <summary>Ekrandaki bir satır: malzeme + atanmamış miktar + kullanıcının gireceği dağıtım miktarı.</summary>
    public sealed partial class UnassignedLineVm : ObservableObject
    {
        private readonly StockDistributeViewModel _parent;

        public UnassignedLineVm(string materialId, string code, string name, decimal available, StockDistributeViewModel parent)
        {
            MaterialId = materialId; Code = code; Name = name; Available = available; _parent = parent;
        }

        public string MaterialId { get; }
        public string Code { get; }
        public string Name { get; }

        /// <summary>ATANMAMIŞ kovasındaki mevcut miktar. Negatif olabilir (ADR-086 devralınan eksik stok)
        /// — o durumda dağıtılamaz, alan kapalıdır.</summary>
        public decimal Available { get; }

        public bool CanDistribute => Available > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(RemainingText))]
        private decimal _amount;

        partial void OnAmountChanged(decimal value) => _parent.Notify();

        public string AvailableText => Available.ToString("0.##");
        public string RemainingText => (Available - Amount).ToString("0.##");
    }
}
