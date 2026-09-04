using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Malzeme Giriş-Çıkış — 3 kayıt tipi (eski proje ile aynı): Yeni Kayıt / Transfer / Depo Çıkışı.
/// • Yeni Kayıt: tam malzeme formu (kod ile upsert) + stok ARTAR.
/// • Transfer: mevcut malzeme + şubeler arası taşıma.
/// • Depo Çıkışı: mevcut malzeme + stok AZALIR (negatif engeli).
/// StockService doc'lu hareket (operation_id idempotency). Altta son hareketler + iptal (ters kayıt).
/// </summary>
public sealed partial class StockEntryViewModel : ViewModelBase, IRefreshable
{
    /// <summary>Eşitleme yeni veri getirince açık ekranı yenile (kullanıcı isteği 2026-07-19).</summary>
    public void RefreshData() => Load();
    private readonly SessionContext _session;

    public bool CanWrite => AccessControl.Can(_session, "stock", PermissionAction.Create);

    /// <summary>
    /// ⭐ YET-05 (denetim 2026-08-26) — bu koşul SUNUCUYLA AYNI OLMAK ZORUNDA.
    /// Sunucu (<c>StockService.ReverseDocument</c>) <c>stock.Edit</c> + <c>btn-reverse</c> ister; burası ise
    /// yalnız <c>stock.Delete</c> soruyordu. İki yönlü hataya yol açıyordu: (1) Edit+buton verilen kullanıcı
    /// butonu HİÇ göremiyordu (verilen yetki kullanılamıyor), (2) yalnız Delete'i olan kullanıcı butonu
    /// görüp tıklayınca "yetki yok" hatası alıyordu.
    /// </summary>
    public bool CanReverse => AccessControl.Can(_session, "stock", PermissionAction.Edit)
                              && AccessControl.CanUseButton(_session, SpecialButtons.Reverse);

    // Kayıt tipi (kullanıcı isteği 2026-08-07): üst seviye "Transfer" KALDIRILDI → "Depo Çıkışı" altına Şube
    // İçi/Şube Dışı alt-seçimi olarak taşındı. Şube İçi = çıkış (IssueOut), Şube Dışı = transfer (Transfer).
    public ObservableCollection<string> KindOptions { get; } = new() { "Yeni Kayıt", "Depo Çıkışı" };
    public ObservableCollection<string> ExitScopeOptions { get; } = new() { "Şube İçi", "Şube Dışı" };
    public ObservableCollection<string> TypeOptions { get; } = new() { "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer" };
    public ObservableCollection<MaterialRefRow> MaterialResults { get; } = new();
    public ObservableCollection<BranchRow> Branches { get; } = new();

    /// <summary>
    /// TRF-01 (2026-09-04) — transferde HEDEF listesi. Kaynak (oturum) şubesi listeden ÇIKARILIR.
    ///
    /// Eskiden hedef kutusu tüm şubeleri listeliyordu; kullanıcı kendi şubesini seçebiliyor ve hatayı
    /// ancak <b>Kaydet'e bastıktan sonra</b> görüyordu ("Hedef şube, kendi şubenizden farklı olmalı").
    /// Web bunu zaten listeden dışlayarak çözüyordu — parite bu yönde kapatıldı: hata mesajı
    /// göstermek yerine <b>hatayı mümkün kılmamak</b> doğrusudur.
    ///
    /// Kaydet'teki kontrol KALDIRILMADI: liste bir kolaylıktır, kural sunucuda ve VM'de durur
    /// (<c>StockService</c> kaynak==hedef transferini zaten reddeder).
    /// </summary>
    public ObservableCollection<BranchRow> HedefSubeler { get; } = new();

    private void HedefSubeleriTazele()
    {
        // STK-12: kaynak artık her zaman oturum şubesi DEĞİL — "Tüm Şubeler" modunda kullanıcının
        // seçtiği çalışma deposudur. Dışlanacak olan da odur (aksi hâlde kullanıcı kendi seçtiği
        // depoyu hedef olarak seçebilir ve hatayı ancak Kaydet'te görürdü).
        HedefSubeler.Clear();
        var kaynak = EtkinLokasyon;
        foreach (var b in Branches.Where(b => b.Id != kaynak)) HedefSubeler.Add(b);
    }
    public ObservableCollection<LookupItem> Personnel { get; } = new();
    public ObservableCollection<VehicleListRow> Vehicles { get; } = new();
    public ObservableCollection<StockMovementRow> Movements { get; } = new();

    // Yeni Kayıt lookup'ları
    public ObservableCollection<LookupItem> Categories { get; } = new();
    public ObservableCollection<LookupItem> SubCategories { get; } = new();
    public ObservableCollection<LookupItem> Units { get; } = new();
    public ObservableCollection<LookupItem> Brands { get; } = new();
    public ObservableCollection<LookupItem> Suppliers { get; } = new();

    // Ortak seçim alanı davranışı (madde 3, kullanıcı isteği 2026-08-06): tıklanınca en fazla 25 kayıt,
    // arama başlayınca sınırsız (SearchPopulator + SelectionSearch — bkz. Converters/Application.Ui).
    public Func<string, CancellationToken, Task<IEnumerable<object>>> BranchPopulator => SearchPopulator.For(() => Branches, b => b.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> PersonnelPopulator => SearchPopulator.For(() => Personnel, p => p.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> VehiclePopulator => SearchPopulator.For(() => Vehicles, v => v.Display);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> CategoryPopulator => SearchPopulator.For(() => Categories, c => c.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> SubCategoryPopulator => SearchPopulator.For(() => SubCategories, c => c.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> UnitPopulator => SearchPopulator.For(() => Units, u => u.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> BrandPopulator => SearchPopulator.For(() => Brands, b => b.Name);
    public Func<string, CancellationToken, Task<IEnumerable<object>>> SupplierPopulator => SearchPopulator.For(() => Suppliers, s => s.Name);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNew))]
    [NotifyPropertyChangedFor(nameof(IsExit))]
    [NotifyPropertyChangedFor(nameof(IsInBranchExit))]
    [NotifyPropertyChangedFor(nameof(IsOutBranchExit))]
    [NotifyPropertyChangedFor(nameof(ShowNewForm))]
    [NotifyPropertyChangedFor(nameof(ShowExitScope))]
    [NotifyPropertyChangedFor(nameof(ShowMaterialPicker))]
    [NotifyPropertyChangedFor(nameof(MaterialPickerLabel))]
    [NotifyPropertyChangedFor(nameof(MaterialPickerRequired))]
    [NotifyPropertyChangedFor(nameof(NewFieldsLocked))]
    [NotifyPropertyChangedFor(nameof(NewFieldsEnabled))]
    [NotifyPropertyChangedFor(nameof(ShowPrice))]
    [NotifyPropertyChangedFor(nameof(ShowSingleBranch))]
    [NotifyPropertyChangedFor(nameof(ShowSourceBranch))]
    [NotifyPropertyChangedFor(nameof(ShowTargetBranch))]
    [NotifyPropertyChangedFor(nameof(ShowPersonnel))]
    [NotifyPropertyChangedFor(nameof(QuantityLabel))]
    [NotifyPropertyChangedFor(nameof(ShowExitLines))]   // İş #8: çoklu malzeme sepeti yalnız Depo Çıkışı'nda
    private string _selectedKind = "Yeni Kayıt";

    /// <summary>Depo Çıkışı alt-kapsamı (kullanıcı isteği 2026-08-07): "Şube İçi" (=çıkış/IssueOut) ya da
    /// "Şube Dışı" (=transfer). Yalnız "Depo Çıkışı" tipinde anlamlıdır.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInBranchExit))]
    [NotifyPropertyChangedFor(nameof(IsOutBranchExit))]
    [NotifyPropertyChangedFor(nameof(ShowSingleBranch))]
    [NotifyPropertyChangedFor(nameof(ShowSourceBranch))]
    [NotifyPropertyChangedFor(nameof(ShowTargetBranch))]
    [NotifyPropertyChangedFor(nameof(ShowPersonnel))]
    [NotifyPropertyChangedFor(nameof(CanPickCostCenter))]   // TRF-01: transferde gizlenir
    private string _exitScope = "Şube İçi";

    public bool IsNew => SelectedKind == "Yeni Kayıt";
    public bool IsExit => SelectedKind == "Depo Çıkışı";
    /// <summary>Şube İçi Çıkış = malzeme aynı şubedeki personel/araca teslim, merkez depodan düşer (IssueOut).</summary>
    public bool IsInBranchExit => IsExit && ExitScope == "Şube İçi";
    /// <summary>Şube Dışı Çıkış = başka şubeye transfer (Transfer).</summary>
    public bool IsOutBranchExit => IsExit && ExitScope == "Şube Dışı";

    /// <summary>Yeni Kayıt → tam malzeme formu; Depo Çıkışı → mevcut malzeme seçici (zorunlu).
    /// Yeni Kayıt'ta da AYNI seçici gösterilir ama OPSİYONELDİR (madde 1.1, kullanıcı isteği 2026-08-06):
    /// mevcut malzeme seçilirse Kod/Ad/Tür/Birim/Kategori/Alt Kategori/Marka kilitlenip malzemeden doldurulur.</summary>
    public bool ShowNewForm => IsNew;
    public bool ShowExitScope => IsExit;   // Şube İçi/Şube Dışı alt-seçim yalnız Depo Çıkışı'nda
    public bool ShowMaterialPicker => IsExit || IsNew;
    public string MaterialPickerLabel => IsNew ? "Mevcut Malzemeye Giriş Yap (opsiyonel)" : "Malzeme";
    public bool MaterialPickerRequired => IsExit;
    /// <summary>Yeni Kayıt'ta mevcut malzeme seçildiyse malzeme kartı alanları (Kod/Ad/Tür/Birim/Kategori/Alt
    /// Kategori/Marka) kilitlenir — zaten malzemeden gelir, tekrar düzenlenmez. Tedarikçi/Birim Fiyat/Fatura-
    /// Fiş-İrsaliye/Açıklama HER ZAMAN aktif kalır (aynı malzeme farklı tedarikçiden farklı fiyatla alınabilir).</summary>
    public bool NewFieldsLocked => IsNew && HasMaterial;
    public bool NewFieldsEnabled => !NewFieldsLocked;
    public bool ShowPrice => IsNew;                 // birim fiyat yalnız girişte
    // Dinamik alan yönetimi (madde 6): işlem tipine göre yalnız gerekli alanlar GÖRÜNÜR.
    // Şube (Şubeniz) salt-okunur: Yeni Kayıt + Şube İçi. Kaynak/Hedef Şube: yalnız Şube Dışı (transfer).
    public bool ShowSingleBranch => IsNew || IsInBranchExit;
    public bool ShowSourceBranch => IsOutBranchExit;
    public bool ShowTargetBranch => IsOutBranchExit;
    /// <summary>Personel (teslim eden/alan): Yeni Kayıt + Şube İçi'nde görünür/gerekli; Şube Dışı'nda GİZLİ
    /// (transfer alıcısı şube; kişi teslim yok — kullanıcı isteği 2026-08-07 madde 5).</summary>
    public bool ShowPersonnel => IsNew || IsInBranchExit;
    public string QuantityLabel => IsExit ? "Çıkacak Miktar" : IsNew ? "Eklenecek Stok" : "Miktar";

    // İşlem şubesi = LOGIN (çalışma) şube (kullanıcı isteği 2026-08-06). Giriş/çıkışta İŞLEM şubesi,
    // transferde KAYNAK şube budur; şubeye bağlı kullanıcıda salt-okunur gösterilir, kullanıcı seçmez.
    public BranchRow? LoginBranch => Branches.FirstOrDefault(b => b.Id == _session.OperatingBranchId);
    public string LoginBranchName => LoginBranch?.Name ?? "—";

    // ── ⭐ STK-12 (2026-09-04) — "TÜM ŞUBELER" MODUNDA İŞLEM ──────────────────────────────────────
    //
    // ÇÖZÜLEN FARK: Web'de STK-04 ile bu mod AÇIK (depo açıkça seçilirse işlem yapılabilir), masaüstünde
    // ise `BranchGuard.RequireBranchAsync` Kaydet'in TAMAMINI kapatıyordu. Çok depolu firmada yönetici
    // masaüstünde hiç stok işlemi yapamıyor, çıkıp tek şube seçerek yeniden girmek zorunda kalıyordu.
    //
    // ⚠ KORUMA KALDIRILMADI, YERİ DEĞİŞTİ. BranchGuard bir yetki sınırı değil VERİ DOĞRULUĞU
    // korumasıdır: çalışma şubesi yokken kayıt açılırsa hareket ŞUBESİZ düşer ve hangi şantiyeye ait
    // olduğu kaybolur. Bu kaygı geçerlidir ve korunur — yalnız "hiç işlem yapamazsın" yerine
    // "işlemin yazılacağı depoyu AÇIKÇA seç" denir. Belirsiz stok hareketi yine oluşamaz.
    //
    // Masaüstünde bu desenin emsali zaten vardı: Atanmamış Stok Dağıtımı ekranı guard KULLANMAZ,
    // hedefi kullanıcıya açıkça seçtirir.

    /// <summary>Oturum "Tüm Şubeler" modunda mı? (çalışma şubesi seçilmemiş)</summary>
    public bool IsAllBranches => BranchGuard.IsAllBranches(_session);

    /// <summary>"Tüm Şubeler" modunda kullanıcının seçtiği ÇALIŞMA DEPOSU. Şubeye bağlı kullanıcıda
    /// kullanılmaz (orada lokasyon oturumdan gelir ve değiştirilemez).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EtkinLokasyonAdi))]
    private BranchRow? _calismaDeposu;

    partial void OnCalismaDeposuChanged(BranchRow? value)
    {
        HedefSubeleriTazele();               // kaynak değişti → hedef listesi yeniden süzülmeli
        OnPropertyChanged(nameof(EtkinLokasyon));
        PickMaterial(SelectedMaterial);      // gösterilen bakiye SEÇİLEN DEPONUN bakiyesi olmalı
    }

    /// <summary>
    /// İşlemin yazılacağı LOKASYON — web'deki <c>EffectiveLocation</c>'ın birebir karşılığı.
    /// Şubeye bağlı kullanıcıda oturum şubesi (değiştirilemez); "Tüm Şubeler" ile girende kullanıcının
    /// seçtiği depo. <b>Boşsa kayıt yapılmaz</b> (bkz. Save) — "Atanmamış" kovasına ASLA düşürülmez.
    /// </summary>
    public string? EtkinLokasyon => IsAllBranches ? CalismaDeposu?.Id : _session.OperatingBranchId;

    /// <summary>Ekranda gösterilecek etkin depo adı (onay metni ve salt-okunur alan için).</summary>
    public string EtkinLokasyonAdi
        => IsAllBranches
            ? (CalismaDeposu?.Name ?? "— (depo seçilmedi)")
            : LoginBranchName;

    /// <summary>"Tüm Şubeler" modunda kullanıcıya gösterilen yönlendirme (engelleme DEĞİL).</summary>
    public string TumSubelerUyarisi =>
        "\"Tüm Şubeler\" modundasınız. Stok bir depoya ait olmalıdır — işlem yapmadan önce " +
        "aşağıdan Depo / Şantiye seçin.";

    // ── Mevcut malzeme seçici (Transfer / Depo Çıkışı) ──
    [ObservableProperty] private string _materialSearch = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaterial))]
    [NotifyPropertyChangedFor(nameof(NewFieldsLocked))]
    [NotifyPropertyChangedFor(nameof(NewFieldsEnabled))]
    private MaterialRefRow? _selectedMaterial;
    public bool HasMaterial => SelectedMaterial != null;
    [ObservableProperty] private string _balanceText = "";
    /// <summary>Yeni Kayıt'ta mevcut malzeme seçildiğinde yüklenen tam kart (madde 1.1) — Tedarikçi değiştiyse
    /// kaydederken malzeme kartını güncellemek için diğer alanları KORUR (kullanıcı kararı 2026-08-07).</summary>
    private MaterialDetail? _pickedDetail;

    // ── Yeni Kayıt: malzeme kartı alanları ──
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _newType = "Yedek Parça";
    [ObservableProperty] private LookupItem? _selectedCategory;
    [ObservableProperty] private LookupItem? _selectedSubCategory;
    [ObservableProperty] private LookupItem? _selectedUnit;
    [ObservableProperty] private LookupItem? _selectedBrand;
    [ObservableProperty] private LookupItem? _selectedSupplier;

    // ── Ortak hareket alanları ──
    /// <summary>
    /// STK-11 (kullanıcı isteği 2026-08-26) — İŞLEM TARİHİ: hareketin AİT OLDUĞU iş günü.
    /// Varsayılan BUGÜN; kullanıcı geçmiş veya gelecek bir tarih seçebilir (üst sınır YOKTUR —
    /// ileri tarihli planlanmış hareket bilinçli olarak serbesttir).
    ///
    /// ⚠️ Bu alan yalnız <c>stock_documents.doc_date</c>'i belirler. Kaydın SİSTEME GİRİLDİĞİ
    /// gerçek zaman (<c>created_at</c> + audit) sunucu saatinden yazılır ve buradan
    /// DEĞİŞTİRİLEMEZ — kullanıcı geri tarih seçerek kaydı bugün attığını gizleyemez.
    /// </summary>
    [ObservableProperty] private DateTimeOffset? _docDate = new DateTimeOffset(DateTime.Today);

    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _invoiceNo = "";
    [ObservableProperty] private string _orderSlipNo = "";
    [ObservableProperty] private string _creditSlipNo = "";
    [ObservableProperty] private BranchRow? _branch;
    [ObservableProperty] private BranchRow? _fromBranch;
    [ObservableProperty] private BranchRow? _toBranch;
    [ObservableProperty] private LookupItem? _personnelSel;   // Şoför / teslim alan
    [ObservableProperty] private VehicleListRow? _vehicleSel; // Teslim eden / transfer edilen araç
    [ObservableProperty] private string? _formError;
    [ObservableProperty] private string? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _loadError;
    public bool HasError => LoadError != null;
    public bool HasRows => Movements.Count > 0;
    public bool IsEmpty => !HasError && Movements.Count == 0;

    public StockEntryViewModel(SessionContext session)
    {
        _session = session;
        Load();
        RefreshMaterials();
        LoadCostCenterOptions();   // MLY-01
    }

    // ── MLY-01 (ADR-168): opsiyonel maliyet merkezi seçimi ───────────────────────────────────────
    /// <summary>
    /// Alan yalnız cost_centers Edit yetkisi olana görünür (bağ yazmak veri değiştirir).
    ///
    /// ⭐ TRF-01 (2026-09-04) — <b>TRANSFERDE GİZLİ.</b> Alan eskiden işlem türünden bağımsız
    /// görünüyordu; kullanıcı transfer yaparken de doldurabiliyordu ama değer <b>hiçbir yere
    /// yazılmıyordu</b> (<c>BaglaMaliyetMerkezi</c> yalnız IssueOut dalında çağrılır) ve uyarı da
    /// verilmiyordu — sessizce yutuluyordu. Aynı kusur web'de de vardı.
    ///
    /// Neden "kaydet" değil de "gizle": depo→depo transfer bir <b>maliyet olayı değildir</b>;
    /// malzeme tüketilmez, yalnız yer değiştirir. Maliyet, malzeme kullanıldığında (Şube İçi çıkış)
    /// doğar ve orada zaten çalışıyor. Transferlerin maliyetlendirilmesi gerekirse doğru yer yol
    /// haritasındaki <c>MUH-04</c>'tür. Alan bugün hiçbir şey yapmadığı için gizlemek işlev kaldırmaz,
    /// yalnızca kullanıcının "kaydedildi" sanmasını önler.
    /// </summary>
    public bool CanPickCostCenter =>
        !IsOutBranchExit && AccessControl.Can(_session, "cost_centers", PermissionAction.Edit);
    public System.Collections.ObjectModel.ObservableCollection<ProjectPick> CostCenterOptions { get; } = new();
    [ObservableProperty] private ProjectPick? _formCostCenter;
    private void LoadCostCenterOptions()
    {
        try
        {
            CostCenterOptions.Clear();
            foreach (var (id, name) in DesktopServices.CostCenters.Options(_session))
                CostCenterOptions.Add(new ProjectPick(id, name));
        }
        catch { }
    }
    /// <summary>Kayıt SONRASI bağ — işlem zinciri değişmedi; bağ yazılamazsa kayıt "merkezsiz" kalır.</summary>
    private void BaglaMaliyetMerkezi(string entityType, string entityId, ProjectPick? merkez)
    {
        if (merkez is null) return;
        try { DesktopServices.CostCenters.Link(_session, entityType, entityId, merkez.Id); }
        catch (System.Exception ex) { Status = "Kayıt alındı; maliyet merkezi bağlanamadı: " + ex.Message; }
    }


    [RelayCommand]
    private void Load()
    {
        try
        {
            LoadError = null;
            Movements.Clear();
            foreach (var m in DesktopServices.Stock.RecentMovements(_session)) Movements.Add(m);
            if (Branches.Count == 0)
            {
                try { foreach (var b in DesktopServices.Branches.List(_session)) Branches.Add(b); } catch { }
                HedefSubeleriTazele();   // TRF-01: hedef listesinden kaynak şube çıkarılır
            }
            OnPropertyChanged(nameof(LoginBranchName));   // Branches yüklendi → login şube etiketini tazele
            if (Personnel.Count == 0)
                try { foreach (var p in DesktopServices.Lookups.ListPersonnel(_session)) Personnel.Add(p); } catch { }
            if (Vehicles.Count == 0)
                try { foreach (var v in DesktopServices.Vehicles.List(_session)) Vehicles.Add(v); } catch { }
            if (Categories.Count == 0)
                try { foreach (var c in DesktopServices.Lookups.ListCategories(_session)) Categories.Add(c); } catch { }
            if (Units.Count == 0)
                try { foreach (var u in DesktopServices.Lookups.List(_session, "units")) Units.Add(u); } catch { }
            if (Brands.Count == 0)
                try { foreach (var b in DesktopServices.Lookups.ListBrands(_session, "material")) Brands.Add(b); } catch { }
            if (Suppliers.Count == 0)
                try { foreach (var sp in DesktopServices.Lookups.List(_session, "suppliers")) Suppliers.Add(sp); } catch { }
            Status = $"{Movements.Count} hareket";
        }
        catch (Exception ex) { LoadError = ex.Message; Status = "Hata: " + ex.Message; }
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnSelectedCategoryChanged(LookupItem? value)
    {
        SelectedSubCategory = null;
        SubCategories.Clear();
        if (value is not null)
            try { foreach (var sc in DesktopServices.Lookups.ListCategories(_session, value.Id)) SubCategories.Add(sc); } catch { }
    }

    partial void OnMaterialSearchChanged(string value) => RefreshMaterials();

    private void RefreshMaterials()
    {
        MaterialResults.Clear();
        var term = MaterialSearch?.Trim();
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 30 },
                string.IsNullOrEmpty(term) ? null : term);
            foreach (var m in page.Items) MaterialResults.Add(new MaterialRefRow(m.Id, m.Code, m.Name));
        }
        catch { }
    }

    [RelayCommand]
    private void PickMaterial(MaterialRefRow? m)
    {
        if (m is null) return;
        SelectedMaterial = m;
        MaterialSearch = $"{m.Code} - {m.Name}";
        MaterialResults.Clear();
        // 🔴 STK-05 (D-4): giriş/çıkış YALNIZ oturumun deposunda yapılır → gösterilen bakiye de O DEPONUN
        // bakiyesidir. Firma toplamı gösterilseydi kullanıcı "15 var" görüp çıkışın reddedilmesine şaşırırdı
        // (o depoda 10 varken). Firma geneli toplam artık malzeme kartındaki kırılımda görünüyor.
        try
        {
            // STK-12: bakiye ETKİN lokasyonun bakiyesidir. "Tüm Şubeler" modunda depo henüz
            // seçilmediyse bakiye gösterilmez — web de aynını yapar ("Bakiye için önce depo seçin").
            var loc = EtkinLokasyon;
            BalanceText = loc is null
                ? "Bakiye için önce depo seçin."
                : $"{EtkinLokasyonAdi} stoğu: {DesktopServices.Stock.GetBalanceAt(_session, m.Id, loc):0.##}";
        }
        catch { BalanceText = ""; }

        _pickedDetail = null;
        if (IsNew)
        {
            // madde 1.1: mevcut malzeme seçildi — kart alanları malzemeden doldurulup kilitlenir; Tedarikçi
            // yalnız ÖNERİ olarak dolduruluyor, kilitlenmiyor (bu girişte farklı tedarikçi seçilebilir).
            try
            {
                var d = DesktopServices.Materials.GetDetail(_session, m.Id);
                _pickedDetail = d;
                Code = d.Code; Name = d.Name; NewType = d.Type ?? "Diğer";
                SelectedUnit = Units.FirstOrDefault(u => u.Id == d.UnitId);
                ResolvePickedCategory(d.CategoryId);
                SelectedBrand = Brands.FirstOrDefault(b => b.Id == d.BrandId);
                SelectedSupplier = Suppliers.FirstOrDefault(s => s.Id == d.SupplierId);
            }
            catch { }
        }
    }

    /// <summary>1.7 ile aynı "ebeveyn tara" mantığı (bkz. MaterialsViewModel.ResolveEditCategory): category_id
    /// yaprak (alt kategori varsa onu) tutar; üst kutu bulunamazsa tüm üst kategorileri tarayıp alt kategoriyi
    /// içerenini bulur.</summary>
    private void ResolvePickedCategory(string? categoryId)
    {
        SelectedCategory = null; SelectedSubCategory = null;
        if (string.IsNullOrEmpty(categoryId)) return;
        var top = Categories.FirstOrDefault(c => c.Id == categoryId);
        if (top is not null) { SelectedCategory = top; return; }
        foreach (var t in Categories)
        {
            List<LookupItem> subs;
            try { subs = DesktopServices.Lookups.ListCategories(_session, t.Id).ToList(); }
            catch { continue; }
            if (subs.Any(x => x.Id == categoryId))
            {
                SelectedCategory = t;   // OnSelectedCategoryChanged tetiklenir → SubCategories yüklenir
                SelectedSubCategory = SubCategories.FirstOrDefault(s => s.Id == categoryId);
                return;
            }
        }
    }

    /// <summary>Yeni Kayıt'ta yanlışlıkla/vazgeçilerek seçilen mevcut malzemeyi bırakıp gerçekten YENİ bir
    /// malzeme kartı doldurmaya dönmek için (madde 1.1) — Miktar/Fiyat/Fatura/Personel/Araç KORUNUR.</summary>
    [RelayCommand]
    private void ClearPickedMaterial()
    {
        SelectedMaterial = null; MaterialSearch = ""; BalanceText = ""; _pickedDetail = null;
        Code = ""; Name = ""; NewType = "Yedek Parça";
        SelectedCategory = null; SelectedSubCategory = null; SelectedUnit = null;
        SelectedBrand = null; SelectedSupplier = null;
        RefreshMaterials();
    }

    /// <summary>Koda göre mevcut malzemeyi bul (tam eşleşme). Yeni Kayıt'ta upsert için.</summary>
    private string? FindMaterialIdByCode(string code)
    {
        try
        {
            var page = DesktopServices.Materials.List(_session, new PageRequest { Limit = 50 }, code);
            return page.Items.FirstOrDefault(x =>
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))?.Id;
        }
        catch { return null; }
    }

    [RelayCommand]
    private async Task Save()
    {
        FormError = null;
        if (!CanWrite) { FormError = "Yetki yok."; return; }
        // ⭐ STK-12: "Tüm Şubeler" modunda artık İŞLEM YAPILABİLİR — ama depo AÇIKÇA seçilirse.
        // Eskiden burada `BranchGuard.RequireBranchAsync` vardı ve kaydın tamamını kapatıyordu.
        // Koruma kaldırılmadı, YERİ DEĞİŞTİ: belirsiz (şubesiz) stok hareketi yine oluşamaz —
        // yalnız kullanıcı çıkıp yeniden giriş yapmak zorunda kalmıyor. Web'in STK-04 kuralının aynısı.
        if (EtkinLokasyon is null)
        {
            FormError = "Önce işlemin yapılacağı depoyu/şantiyeyi seçin. Stok bir depoya ait olmalıdır.";
            return;
        }
        // madde 8: personel "işlemi yapan/teslim alan" — Yeni Kayıt + Şube İçi'nde zorunlu. Şube Dışı (transfer)
        // alanı gizli olduğundan zorunlu değildir (kullanıcı isteği 2026-08-07).
        if (ShowPersonnel && PersonnelSel is null) { FormError = "Personel (işlemi yapan) zorunludur."; return; }
        // madde 7 — İş #8: sepetteki satırlar da denetlenir (aksi halde sepete konan çok büyük miktar
        // uyarısız geçerdi; formdaki miktar 0 olduğu için eski kontrol hiç tetiklenmezdi).
        var biggest = ExitLines.Count == 0 ? Quantity : Math.Max(Quantity, ExitLines.Max(l => l.Quantity));
        if (DepoWise.Application.Ui.FieldChecks.IsSuspiciouslyLarge(biggest)
            && !await ConfirmService.AskAsync($"Miktar çok büyük görünüyor ({biggest:0.##}). Emin misiniz?", "Miktar Uyarısı", "Evet, Doğru")) return;

        string? Doc(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var note = Doc(Note);
        var inv = Doc(InvoiceNo); var ord = Doc(OrderSlipNo); var crd = Doc(CreditSlipNo);
        var op = Guid.NewGuid().ToString("N");

        try
        {
            if (IsNew)
            {
                if (Quantity < 0) { FormError = "Eklenecek stok negatif olamaz."; return; }
                if (!HasMaterial)
                {
                    if (string.IsNullOrWhiteSpace(Code)) { FormError = "Kod zorunlu."; return; }
                    if (string.IsNullOrWhiteSpace(Name)) { FormError = "Ad zorunlu."; return; }
                    if (SelectedUnit is null) { FormError = "Birim seçin."; return; }
                }
                var confirmMsg = HasMaterial
                    ? "Mevcut malzemeye stok girişi yapılsın mı? (stok ARTAR)"
                    : "Malzeme kaydedilip stok girişi yapılsın mı? (stok ARTAR)";
                if (!await ConfirmService.AskAsync(confirmMsg, "Yeni Kayıt")) return;

                string materialId;
                if (HasMaterial)
                {
                    materialId = SelectedMaterial!.Id;   // madde 1.1: mevcut malzeme — kart alanları değiştirilmez
                }
                else
                {
                    var code = Code.Trim();
                    var categoryId = SelectedSubCategory?.Id ?? SelectedCategory?.Id;
                    materialId = FindMaterialIdByCode(code) ?? DesktopServices.Materials.Create(_session, new NewMaterial(
                        Code: code, Name: Name.Trim(),
                        Type: string.IsNullOrWhiteSpace(NewType) ? null : NewType,
                        CategoryId: categoryId, UnitId: SelectedUnit!.Id,
                        BrandId: SelectedBrand?.Id, SupplierId: SelectedSupplier?.Id,
                        UnitPrice: UnitPrice, Currency: "TRY",
                        Description: note));
                }
                if (Quantity > 0)
                    DesktopServices.Stock.ReceiveIn(_session,
                        new[] { new StockLine(materialId, Quantity, UnitPrice > 0 ? UnitPrice : null) }, op,
                        branchId: EtkinLokasyon, personnelId: PersonnelSel?.Id, vehicleId: VehicleSel?.Id, note: note,
                        docDate: IsGunuTarihi.Ms(DocDate),   // STK-11: işlem tarihi (created_at DEĞİL)
                        invoiceNo: inv, orderSlipNo: ord, creditSlipNo: crd);   // giriş şubesi = login şube

                // madde 1.1 (kullanıcı kararı 2026-08-07): mevcut malzemeye girişte Tedarikçi değiştirildiyse
                // malzeme kartı güncellenir (diğer alanlar KORUNUR). materials:edit yetkisi yoksa veya kayıt
                // arada değiştiyse stok girişi zaten TAMAMLANDI — bu ikincil güncelleme sessizce atlanır.
                if (HasMaterial && _pickedDetail is not null && SelectedSupplier?.Id != _pickedDetail.SupplierId)
                {
                    try
                    {
                        DesktopServices.Materials.Update(_session, materialId, new UpdateMaterial(
                            Code: _pickedDetail.Code, Name: _pickedDetail.Name, Type: _pickedDetail.Type,
                            CategoryId: _pickedDetail.CategoryId, UnitId: _pickedDetail.UnitId, BrandId: _pickedDetail.BrandId,
                            SupplierId: SelectedSupplier?.Id, MinStock: _pickedDetail.MinStock, UnitPrice: _pickedDetail.UnitPrice,
                            Description: _pickedDetail.Description, TemplateId: _pickedDetail.TemplateId), _pickedDetail.Version);
                    }
                    catch { }
                }

                Status = HasMaterial
                    ? "Mevcut malzemeye stok girişi yapıldı."
                    : "Yeni kayıt: malzeme oluşturuldu/güncellendi ve stok eklendi.";
            }
            else
            {
                // İş #8: sepetteki satırlar + (varsa) formda duran seçim birlikte TEK belgede işlenir.
                var exitLines = BuildExitLines();
                if (exitLines.Count == 0) { FormError = "Malzeme seçin (ya da listeye ekleyin)."; return; }
                var lineText = exitLines.Count == 1 ? "" : $" ({exitLines.Count} malzeme, tek belge)";

                if (IsInBranchExit)   // Şube İçi Çıkış = merkez depodan düşer (IssueOut)
                {
                    if (!await ConfirmService.AskAsync($"Şube içi çıkış kaydedilsin mi?{lineText} (stok AZALIR)", "Depo Çıkışı — Şube İçi")) return;
                    var issueRes = DesktopServices.Stock.IssueOut(_session, exitLines, op,
                        branchId: EtkinLokasyon, personnelId: PersonnelSel?.Id, vehicleId: VehicleSel?.Id, note: note,
                        docDate: IsGunuTarihi.Ms(DocDate),   // STK-11
                        invoiceNo: inv, orderSlipNo: ord, creditSlipNo: crd);   // çıkış şubesi = login şube
                    BaglaMaliyetMerkezi("stock_document", issueRes.DocumentId, FormCostCenter);   // MLY-01
                    Status = exitLines.Count == 1 ? "Şube içi çıkış kaydedildi." : $"Şube içi çıkış kaydedildi ({exitLines.Count} malzeme).";
                }
                else // Şube Dışı Çıkış = Transfer — kaynak = ETKİN LOKASYON (şubeli kullanıcıda login şube,
                     // "Tüm Şubeler" modunda kullanıcının seçtiği depo); kullanıcı yalnız HEDEFİ seçer
                {
                    var from = EtkinLokasyon;
                    if (string.IsNullOrEmpty(from)) { FormError = "Şubeniz belirlenemedi."; return; }
                    if (ToBranch is null) { FormError = "Hedef şube seçin."; return; }
                    if (ToBranch.Id == from) { FormError = "Hedef şube, kaynak depodan farklı olmalı."; return; }
                    if (!await ConfirmService.AskAsync($"{EtkinLokasyonAdi} → {ToBranch.Name} şube dışı çıkışı (transfer) kaydedilsin mi?{lineText}", "Depo Çıkışı — Şube Dışı")) return;
                    DesktopServices.Stock.Transfer(_session, exitLines, from, ToBranch.Id, op, note,
                        docDate: IsGunuTarihi.Ms(DocDate),   // STK-11
                        personnelId: PersonnelSel?.Id, vehicleId: VehicleSel?.Id,
                        invoiceNo: inv, orderSlipNo: ord, creditSlipNo: crd);
                    Status = exitLines.Count == 1 ? "Şube dışı çıkış (transfer) kaydedildi." : $"Şube dışı çıkış (transfer) kaydedildi ({exitLines.Count} malzeme).";
                }
            }

            ClearForm();
            Load();
        }
        catch (Exception ex) { FormError = "Kaydedilemedi: " + ex.Message; }
    }

    // ══════════════ ÇOK MALZEMELİ ÇIKIŞ (İş #8, 2026-08-09) ══════════════
    // Eskiden bir çıkış/transfer belgesinde YALNIZ BİR malzeme olabiliyordu; 10 malzeme veren depocu
    // 10 ayrı belge açmak zorundaydı. Servis katmanı (ReceiveIn/IssueOut) zaten çok satırlıydı; transfer
    // de bu işte çok satırlı hâle getirildi. Ekranda "listeye ekle" sepeti eklendi.
    //
    // Geriye uyumluluk: sepet BOŞ bırakılıp tek malzeme + miktar yazılırsa eski davranış aynen sürer.

    public sealed record ExitLine(string MaterialId, string Code, string Name, decimal Quantity)
    {
        public string Display => $"{Code} — {Name}";
        public string QtyDisplay => Quantity.ToString("0.##");
    }

    public ObservableCollection<ExitLine> ExitLines { get; } = new();

    /// <summary>Sepet yalnız Depo Çıkışı'nda görünür (Yeni Kayıt malzeme KARTI oluşturur; oraya sepet uymaz).</summary>
    public bool ShowExitLines => IsExit;
    public bool HasExitLines => ExitLines.Count > 0;
    public string ExitLinesSummary => ExitLines.Count == 0
        ? "Listeye malzeme eklemeden tek malzeme de kaydedebilirsiniz."
        : $"{ExitLines.Count} malzeme listede — Kaydet'e basınca hepsi TEK belgede işlenir.";

    [RelayCommand]
    private void AddExitLine()
    {
        FormError = null;
        if (SelectedMaterial is null) { FormError = "Önce malzeme seçin."; return; }
        if (Quantity <= 0) { FormError = "Miktar sıfırdan büyük olmalı."; return; }
        // Aynı malzeme tekrar eklenirse miktarlar TOPLANIR (iki ayrı satır kullanıcıyı yanıltırdı).
        var existing = ExitLines.FirstOrDefault(l => l.MaterialId == SelectedMaterial.Id);
        if (existing is not null)
        {
            ExitLines[ExitLines.IndexOf(existing)] = existing with { Quantity = existing.Quantity + Quantity };
        }
        else
        {
            ExitLines.Add(new ExitLine(SelectedMaterial.Id, SelectedMaterial.Code, SelectedMaterial.Name, Quantity));
        }
        ClearPickedMaterial();
        Quantity = 0;
        NotifyExitLines();
    }

    [RelayCommand]
    private void RemoveExitLine(ExitLine? line)
    {
        if (line is null) return;
        ExitLines.Remove(line);
        NotifyExitLines();
    }

    private void NotifyExitLines()
    {
        OnPropertyChanged(nameof(HasExitLines));
        OnPropertyChanged(nameof(ExitLinesSummary));
    }

    /// <summary>Kaydetmede kullanılacak satırlar: sepet + (varsa) formda duran seçim.
    /// Kullanıcı "listeye ekle"ye basmayı unutursa seçimi kaybetmeyelim diye formdaki de dahil edilir.</summary>
    private List<StockLine> BuildExitLines()
    {
        var lines = ExitLines.Select(l => new StockLine(l.MaterialId, l.Quantity)).ToList();
        if (SelectedMaterial is not null && Quantity > 0)
        {
            var idx = lines.FindIndex(l => l.MaterialId == SelectedMaterial.Id);
            if (idx >= 0) lines[idx] = lines[idx] with { Quantity = lines[idx].Quantity + Quantity };
            else lines.Add(new StockLine(SelectedMaterial.Id, Quantity));
        }
        return lines;
    }

    [RelayCommand]
    private async Task ReverseMovement(StockMovementRow? row)
    {
        if (row?.DocumentId is null) return;
        if (!CanReverse) { Status = "Yetki yok."; return; }
        if (!await ConfirmService.AskAsync(
                "Bu stok hareketi İPTAL edilsin mi? (stok etkisi ters kayıtla geri alınır)",
                "Hareketi İptal Et", "Evet, İptal", "Vazgeç", danger: true)) return;
        try
        {
            DesktopServices.Stock.ReverseDocument(_session, row.DocumentId, "Kullanıcı iptali");
            Status = "Hareket iptal edildi (ters kayıt).";
            Load();
        }
        catch (Exception ex) { Status = "İptal edilemedi: " + ex.Message; }
    }

    [RelayCommand]
    private void ClearForm()
    {
        SelectedMaterial = null; MaterialSearch = ""; BalanceText = ""; _pickedDetail = null;
        Code = ""; Name = ""; NewType = "Yedek Parça";
        SelectedCategory = null; SelectedSubCategory = null; SelectedUnit = null;
        SelectedBrand = null; SelectedSupplier = null;
        ExitLines.Clear(); NotifyExitLines();   // İş #8: sepet de temizlenir
        Quantity = 0; UnitPrice = 0; Note = ""; Branch = null; FromBranch = null; ToBranch = null;
        PersonnelSel = null; VehicleSel = null;
        InvoiceNo = ""; OrderSlipNo = ""; CreditSlipNo = "";
        DocDate = new DateTimeOffset(DateTime.Today);   // STK-11: her yeni kayıtta varsayılan BUGÜN
        ExitScope = "Şube İçi";
        FormError = null;
        RefreshMaterials();
    }
}
