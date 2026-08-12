using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// G4-3b — ŞUBE BAZLI ÖN MUHASEBE (kullanıcı isteği 2026-08-12).
///
/// <b>🔴 KAPATILAN ÜÇ GERÇEK AÇIK:</b>
/// <list type="number">
///   <item><b>Web/API'de şube kapsamı HİÇ uygulanmıyordu.</b> <c>PermissionSnapshot.ToSession()</c>
///     şube bilgisi taşımadığı için <c>BranchScope.Active(s)</c> web tarafında DAİMA null'dı →
///     her kullanıcı her şubenin cari/fatura/kasa verisini görebiliyordu.</item>
///   <item><b>İzinli şube kümesi (user_scopes) ön muhasebede hiç sorulmuyordu.</b> Eski
///     <c>EnforceOwnBranch</c> yalnız oturumun ÇALIŞMA şubesine bakıyordu; bu bir görünüm
///     tercihidir, güvenlik kapısı değil.</item>
///   <item><b>Rapor <c>BranchIds</c> doğrulanmıyordu.</b> Şube seçme yetkisi olan kullanıcı
///     isteğe elle yetkisiz bir <c>branch_id</c> yazarak o şubenin verisini okuyabiliyordu.</item>
/// </list>
///
/// Tek otorite <see cref="BranchAccess"/>'tir:
/// <c>ETKİN = İZİNLİ ∩ (İSTENEN ?? OTURUM ?? İZİNLİ)</c>, fail-closed.
/// </summary>
public class BranchScopeAccountingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly InvoiceService _invoices;
    private readonly InvoiceQueryService _invoiceQ;
    private readonly FinanceService _finance;
    private readonly FinanceQueryService _financeQ;
    private readonly SessionContext _admin;
    private readonly string _ankara, _duzce;
    private const string CoA = "A";

    public BranchScopeAccountingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g43b_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        _invoices = new InvoiceService(_factory, _stock, _ledger, _clock);
        _invoiceQ = new InvoiceQueryService(_factory, _clock);
        _finance = new FinanceService(_factory, _ledger, _clock);
        _financeQ = new FinanceQueryService(_factory);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    /// <summary>Tam yetkili ama YALNIZ verilen şubelerle kapsamlanmış kullanıcı.</summary>
    private static SessionContext Kullanici(string id, params string[] scope) =>
        new(id, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission(PartyService.Module, true, true, true, true),
            new ModulePermission(InvoiceService.Module, true, true, true, true),
            new ModulePermission(FinanceService.Module, true, true, true, true),
            // Fatura stok defterine, tahsilat cari defterine yazar → o modüllerin yetkisi de gerekir
            // (tasarım gereği: her defterin kendi kapısı vardır).
            new ModulePermission("stock", true, true, true, true),
        }))
        { ScopeBranchIds = scope.Length == 0 ? null : scope };

    /// <summary>Açık kapsamı olmayan ama ANA ŞUBESİ atanmış kullanıcı (users.branch_id yolu).</summary>
    private static SessionContext AnaSubeli(string id, string branchId) =>
        new(id, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission(FinanceService.Module, true, true, true, true),
            new ModulePermission(PartyService.Module, true, true, true, true),
        }))
        { HomeBranchId = branchId };

    private string Mat(string code) => _materials.Create(_admin, new NewMaterial(code, code));
    private string Cari(string code = "C-001") => _parties.Create(_admin, new NewParty(code, "Test Cari", PartyTypes.Both));
    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private string Hesap(string code, string? branchId) =>
        _finance.CreateAccount(_admin, new NewFinanceAccount(code, code, FinanceAccountKinds.Cash, BranchId: branchId));

    // ═════════════════════════════════════════════════════════════════════════
    // A — BRANCHACCESS FORMÜLÜ (saf; veritabanı yok)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>A1 — Açık kapsam varsa YALNIZ o şubeler izinlidir.</summary>
    [Fact]
    public void A1_Acik_Kapsam_Sinirlar()
    {
        var u = Kullanici("u1", _duzce);
        Assert.Equal(new[] { _duzce }, BranchAccess.Allowed(u));
        Assert.True(BranchAccess.CanAccess(u, _duzce));
        Assert.False(BranchAccess.CanAccess(u, _ankara));
        Assert.True(BranchAccess.CanAccess(u, null));   // şubesiz kayıt herkese açık
    }

    /// <summary>A2 — Açık kapsam ADMİN BYPASS'INI DA BAĞLAR (süper admin bir yöneticiyi kısıtlayabilsin).</summary>
    [Fact]
    public void A2_Acik_Kapsam_Admini_De_Baglar()
    {
        var kisitliAdmin = new SessionContext("ad2", CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { ScopeBranchIds = new[] { _duzce } };
        Assert.False(BranchAccess.CanAccess(kisitliAdmin, _ankara));
    }

    /// <summary>A3 — Açık kapsam yoksa admin SINIRSIZDIR (mevcut davranış bozulmaz).</summary>
    [Fact]
    public void A3_Admin_Sinirsiz()
    {
        Assert.Null(BranchAccess.Allowed(_admin));
        Assert.True(BranchAccess.CanAccess(_admin, _ankara));
        Assert.True(BranchAccess.CanAccess(_admin, _duzce));
    }

    /// <summary>A4 — Açık kapsam yoksa kullanıcının ANA ŞUBESİ tek izinli şubedir.</summary>
    [Fact]
    public void A4_Ana_Sube_Kapsam_Olur()
    {
        var u = AnaSubeli("u2", _duzce);
        Assert.Equal(new[] { _duzce }, BranchAccess.Allowed(u));
        Assert.False(BranchAccess.CanAccess(u, _ankara));
    }

    /// <summary>
    /// A5 — ⭐ KESİŞİM: kullanıcı elle YETKİSİZ şube isteyemez. İstenen küme izinliyle kesiştirilir,
    /// sessizce genişletilmez.
    /// </summary>
    [Fact]
    public void A5_Istenen_Kapsamla_Kesisir()
    {
        var u = Kullanici("u3", _duzce);
        var eff = BranchAccess.Effective(u, new[] { _ankara, _duzce });
        Assert.Equal(new[] { _duzce }, eff);

        // Yalnız yetkisiz şube istenirse sonuç BOŞ olur (her şey değil!).
        Assert.Empty(BranchAccess.Effective(u, new[] { _ankara })!);
    }

    /// <summary>A6 — "Tümü" = kullanıcının YETKİLİ olduğu şubeler; tek şubeliyse yine tek şube.</summary>
    [Fact]
    public void A6_Tumu_Yetkili_Subeler_Demektir()
    {
        var tekSubeli = Kullanici("u4", _duzce);
        Assert.Equal(new[] { _duzce }, BranchAccess.Effective(tekSubeli));       // "Tümü" → yalnız DÜZCE

        var ikiSubeli = Kullanici("u5", _ankara, _duzce);
        Assert.Equal(2, BranchAccess.Effective(ikiSubeli)!.Count);

        Assert.Null(BranchAccess.Effective(_admin));                              // admin → sınırsız
    }

    /// <summary>
    /// A7 — ⭐ YETKİ DEVRİ TAVANI: kullanıcı kendisinde OLMAYAN şubeyi devredemez
    /// (G1'in "sahip olmadığın yetkiyi veremezsin" kuralının şube karşılığı).
    /// </summary>
    [Fact]
    public void A7_Sahip_Olmadigi_Subeyi_Devredemez()
    {
        var duzceli = Kullanici("u6", _duzce);

        Assert.Equal(new[] { _duzce }, BranchAccess.GrantCeiling(duzceli, new[] { _ankara, _duzce }));
        Assert.Empty(BranchAccess.GrantCeiling(duzceli, new[] { _ankara })!);
        Assert.Throws<ForbiddenException>(() => BranchAccess.RequireGrantable(duzceli, new[] { _ankara }));
        BranchAccess.RequireGrantable(duzceli, new[] { _duzce });                  // kendi kapsamı: serbest

        // Sınırsız devreden istediğini verebilir (mevcut admin davranışı bozulmaz).
        Assert.Equal(new[] { _ankara }, BranchAccess.GrantCeiling(_admin, new[] { _ankara }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // B — KASA / BANKA
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>B1 — Kullanıcı yalnız kendi şubesinin (ve şubesiz) hesaplarını GÖRÜR.</summary>
    [Fact]
    public void B1_Yalniz_Kendi_Subesinin_Hesaplari_Gorunur()
    {
        Hesap("K-ANK", _ankara);
        Hesap("K-DUZ", _duzce);
        Hesap("K-GENEL", null);

        var duzceli = Kullanici("u7", _duzce);
        var kodlar = _financeQ.Accounts(duzceli).Select(x => x.Account.Code).OrderBy(x => x).ToList();

        Assert.Equal(new[] { "K-DUZ", "K-GENEL" }, kodlar);   // ANKARA YOK, firma geneli VAR
        Assert.Equal(3, _financeQ.Accounts(_admin).Count);    // admin hepsini görür
    }

    /// <summary>B2 — ⭐ Kapsam dışı hesap id'si BİLİNSE BİLE okunamaz (UI filtresi değil, gerçek kapı).</summary>
    [Fact]
    public void B2_Kapsam_Disi_Hesap_Id_Ile_Okunamaz()
    {
        var ankaraHesap = Hesap("K-ANK", _ankara);
        var duzceli = Kullanici("u8", _duzce);

        Assert.Throws<ForbiddenException>(() => _financeQ.Account(duzceli, ankaraHesap));
        Assert.Throws<ForbiddenException>(() => _financeQ.Balance(duzceli, ankaraHesap));
        Assert.Throws<ForbiddenException>(() => _financeQ.Statement(duzceli, ankaraHesap));
    }

    /// <summary>B3 — ⭐ Kapsam dışı hesaba TAHSİLAT YAZILAMAZ (servis katmanı; API atlanarak da geçilemez).</summary>
    [Fact]
    public void B3_Kapsam_Disi_Hesaba_Tahsilat_Yazilamaz()
    {
        var ankaraHesap = Hesap("K-ANK", _ankara);
        var c = Cari();
        var duzceli = Kullanici("u9", _duzce);

        Assert.Throws<ForbiddenException>(() => _finance.Add(duzceli,
            new NewFinanceEntry(ankaraHesap, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c)));
    }

    /// <summary>B4 — Kendi şubesinin hesabına yazabilir; ana şube (users.branch_id) yolu da çalışır.</summary>
    [Fact]
    public void B4_Kendi_Subesine_Yazabilir()
    {
        var duzceHesap = Hesap("K-DUZ", _duzce);
        var c = Cari();

        _finance.Add(Kullanici("u10", _duzce), new NewFinanceEntry(duzceHesap, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c));
        _finance.Add(AnaSubeli("u11", _duzce), new NewFinanceEntry(duzceHesap, FinanceTxnTypes.Receipt, 50m, Op(), PartyId: c));

        Assert.Equal(150m, _financeQ.Balance(_admin, duzceHesap));
    }

    /// <summary>B5 — ⭐ Elle branchIds göndererek kapsam GENİŞLETİLEMEZ (liste yolu).</summary>
    [Fact]
    public void B5_Elle_BranchId_Ile_Kapsam_Genisletilemez()
    {
        Hesap("K-ANK", _ankara);
        Hesap("K-DUZ", _duzce);
        var duzceli = Kullanici("u12", _duzce);

        // Kullanıcı ANKARA'yı da istiyor — kesişim yalnız DÜZCE bırakır.
        var kodlar = _financeQ.Accounts(duzceli, branchIds: new[] { _ankara, _duzce })
            .Select(x => x.Account.Code).ToList();
        Assert.Equal(new[] { "K-DUZ" }, kodlar);

        // Yalnız ANKARA isterse HİÇBİR şubeli hesap dönmez.
        Assert.DoesNotContain("K-ANK",
            _financeQ.Accounts(duzceli, branchIds: new[] { _ankara }).Select(x => x.Account.Code));
    }

    /// <summary>B6 — Yönetici birden fazla yetkili şubeyi birlikte görebilir.</summary>
    [Fact]
    public void B6_Yonetici_Coklu_Sube_Gorur()
    {
        Hesap("K-ANK", _ankara);
        Hesap("K-DUZ", _duzce);
        var yonetici = Kullanici("u13", _ankara, _duzce);

        Assert.Equal(2, _financeQ.Accounts(yonetici).Count);
        Assert.Single(_financeQ.Accounts(yonetici, branchIds: new[] { _duzce }));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // C — FATURA
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>C1 — ⭐ Kapsam dışı şubeye FATURA KESİLEMEZ.</summary>
    [Fact]
    public void C1_Kapsam_Disi_Subeye_Fatura_Kesilemez()
    {
        var m = Mat("M-1"); var c = Cari();
        var duzceli = Kullanici("u14", _duzce);

        Assert.Throws<ForbiddenException>(() => _invoices.Create(duzceli, new NewInvoice(
            InvoiceDirections.Purchase, c, new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(),
            BranchId: _ankara)));
    }

    /// <summary>C2 — Fatura kendi şubesine kesilir; şube belirtilmezse kullanıcının şubesine DÜŞER.</summary>
    [Fact]
    public void C2_Sube_Belirtilmezse_Kendi_Subesine_Duser()
    {
        var m = Mat("M-1"); var c = Cari();
        var duzceli = Kullanici("u15", _duzce);

        var r = _invoices.Create(duzceli, new NewInvoice(
            InvoiceDirections.Purchase, c, new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op()));

        Assert.Equal(_duzce, _invoiceQ.Get(_admin, r.Id).BranchId);
    }

    /// <summary>C3 — ⭐ Kapsam dışı şubenin faturası LİSTEDE GÖRÜNMEZ ve id ile OKUNAMAZ.</summary>
    [Fact]
    public void C3_Kapsam_Disi_Fatura_Gorunmez()
    {
        var m = Mat("M-1"); var c = Cari();
        var ankaraFatura = _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, c, new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(),
            BranchId: _ankara));
        _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, c, new[] { new NewInvoiceLine(m, null, null, 1m, 200m) }, Op(),
            BranchId: _duzce));

        var duzceli = Kullanici("u16", _duzce);
        Assert.Equal(1, _invoiceQ.List(duzceli).TotalCount);           // yalnız DÜZCE faturası
        Assert.Equal(2, _invoiceQ.List(_admin).TotalCount);            // admin ikisini de görür
        Assert.Throws<ForbiddenException>(() => _invoiceQ.Get(duzceli, ankaraFatura.Id));
    }

    /// <summary>C4 — ⭐ Kapsam dışı fatura İPTAL EDİLEMEZ ve DÜZENLENEMEZ.</summary>
    [Fact]
    public void C4_Kapsam_Disi_Fatura_Degistirilemez()
    {
        var m = Mat("M-1"); var c = Cari();
        var f = _invoices.Create(_admin, new NewInvoice(
            InvoiceDirections.Purchase, c, new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(),
            BranchId: _ankara));
        var duzceli = Kullanici("u17", _duzce);

        Assert.Throws<ForbiddenException>(() => _invoices.Cancel(duzceli, f.Id, "gerekçe"));
        Assert.Throws<ForbiddenException>(() => _invoices.UpdateInfo(duzceli, f.Id, "X", null, null));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D — CARİ
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// D1 — ⭐ Cari KARTI firma genelinde TEKİLDİR (şubeye kopyalanmaz) ama BAKİYE şube kapsamına göre
    /// hesaplanır. Aynı cari birden çok şubede hareket görebilir; veri tekrarı YOKTUR.
    /// </summary>
    [Fact]
    public void D1_Tek_Cari_Sube_Bazli_Bakiye()
    {
        var c = Cari();
        var kAnk = Hesap("K-ANK", _ankara);
        var kDuz = Hesap("K-DUZ", _duzce);

        _finance.Add(_admin, new NewFinanceEntry(kAnk, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c, BranchId: _ankara));
        _finance.Add(_admin, new NewFinanceEntry(kDuz, FinanceTxnTypes.Receipt, 50m, Op(), PartyId: c, BranchId: _duzce));

        // Cari kaydı TEK.
        Assert.Single(_parties.List(_admin).Items);

        // ⭐ FİRMA TOPLAMI = YETKİLİ ŞUBE TOPLAMLARI (sessiz fark yok).
        Assert.Equal(-150m, _ledger.Balance(_admin, c).Balance);
        Assert.Equal(-100m, _ledger.Balance(Kullanici("u18", _ankara), c).Balance);
        Assert.Equal(-50m, _ledger.Balance(Kullanici("u19", _duzce), c).Balance);
    }

    /// <summary>D2 — Cari ekstresi de şube kapsamına göre süzülür.</summary>
    [Fact]
    public void D2_Ekstre_Sube_Kapsaminda()
    {
        var c = Cari();
        var kAnk = Hesap("K-ANK", _ankara);
        var kDuz = Hesap("K-DUZ", _duzce);
        _finance.Add(_admin, new NewFinanceEntry(kAnk, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c, BranchId: _ankara));
        _finance.Add(_admin, new NewFinanceEntry(kDuz, FinanceTxnTypes.Receipt, 50m, Op(), PartyId: c, BranchId: _duzce));

        Assert.Equal(2, _ledger.Statement(_admin, c).Count);
        Assert.Single(_ledger.Statement(Kullanici("u20", _duzce), c));
    }

    /// <summary>D3 — ⭐ Kapsam dışı şubeye ELLE cari hareketi girilemez.</summary>
    [Fact]
    public void D3_Kapsam_Disi_Elle_Hareket_Girilemez()
    {
        var c = Cari();
        var duzceli = Kullanici("u21", _duzce);

        Assert.Throws<ForbiddenException>(() => _ledger.Add(duzceli, new NewLedgerEntry(
            c, PartyDocTypes.Opening, 100m, true, null, null, null, null, "TRY", _ankara, null, null, Op())));

        // Kendi şubesine girebilir.
        _ledger.Add(duzceli, new NewLedgerEntry(
            c, PartyDocTypes.Opening, 100m, true, null, null, null, null, "TRY", _duzce, null, null, Op()));
        Assert.Equal(100m, _ledger.Balance(duzceli, c).Balance);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // E — GERİLEME KORUMASI (mevcut davranış bozulmadı mı?)
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// E1 — Şubesi/kapsamı olmayan kullanıcı SINIRSIZ kalır. Bu BİLİNÇLİ bir karardır: aksi halde
    /// bugün çalışan (şubesi atanmamış) kullanıcılar sessizce kilitlenirdi. Sıkılaştırmanın yolu
    /// kullanıcıya şube ATAMAKTIR.
    /// </summary>
    [Fact]
    public void E1_Subesiz_Kullanici_Sinirsiz_Kalir()
    {
        var subesiz = Kullanici("u22");   // ne kapsam ne ana şube
        Assert.Null(BranchAccess.Allowed(subesiz));
        Assert.True(BranchAccess.CanAccess(subesiz, _ankara));

        Hesap("K-ANK", _ankara);
        Hesap("K-DUZ", _duzce);
        Assert.Equal(2, _financeQ.Accounts(subesiz).Count);
    }

    /// <summary>E2 — Şubesiz (firma geneli) kayıtlar HİÇBİR kullanıcıdan gizlenmez.</summary>
    [Fact]
    public void E2_Subesiz_Kayitlar_Gizlenmez()
    {
        Hesap("K-GENEL", null);
        var duzceli = Kullanici("u23", _duzce);
        Assert.Single(_financeQ.Accounts(duzceli));
    }

    /// <summary>E3 — Admin davranışı DEĞİŞMEDİ: kapsam atanmadıkça her şeyi görür ve yazar.</summary>
    [Fact]
    public void E3_Admin_Davranisi_Degismedi()
    {
        var m = Mat("M-1"); var c = Cari();
        var kAnk = Hesap("K-ANK", _ankara);
        _invoices.Create(_admin, new NewInvoice(InvoiceDirections.Purchase, c,
            new[] { new NewInvoiceLine(m, null, null, 1m, 100m) }, Op(), BranchId: _ankara));
        _finance.Add(_admin, new NewFinanceEntry(kAnk, FinanceTxnTypes.Receipt, 10m, Op(), PartyId: c, BranchId: _ankara));

        Assert.Equal(1, _invoiceQ.List(_admin).TotalCount);
        Assert.Equal(10m, _financeQ.Balance(_admin, kAnk));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
