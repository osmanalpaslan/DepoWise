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
/// G4-3d — ŞUBE SEÇİCİ UI'ININ DAYANDIĞI SÖZLEŞME.
///
/// UI (web <c>BranchPicker.razor</c> ve masaüstü <c>BranchScopeSelector</c>) şu üç şeyi yapar:
/// <list type="number">
///   <item>kullanıcıya YALNIZ yetkili şubelerini gösterir,</item>
///   <item>varsayılan olarak KENDİ ÇALIŞMA ŞUBESİNİ seçer,</item>
///   <item>yazma için TEKİL bir "aktif çalışma şubesi" belirler.</item>
/// </list>
///
/// <b>UI test edilmez — DAYANDIĞI KURALLAR test edilir.</b> Bu testler kırılırsa iki ekran da yanlış
/// davranır. Gerçek tıklama testi ayrıdır ve YAPILMAMIŞTIR (bkz. GUI checklist).
///
/// ⚠️ UI kısıtı GÜVENLİK DEĞİLDİR; asıl kapı <see cref="BranchAccess"/>'tedir ve
/// <c>BranchScopeAccountingTests</c> / <c>BranchScopeSyncGrantTests</c> ile kilitlidir.
/// </summary>
public class BranchScopeUiContractTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly FinanceService _finance;
    private readonly FinanceQueryService _financeQ;
    private readonly SessionContext _admin;
    private readonly string _ankara, _duzce, _karaman;
    private const string CoA = "A";

    public BranchScopeUiContractTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g43d_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        _finance = new FinanceService(_factory, _ledger, _clock);
        _financeQ = new FinanceQueryService(_factory);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_factory, _clock);
        _ankara = branches.Create(_admin, new NewBranch("ANKARA"));
        _duzce = branches.Create(_admin, new NewBranch("DÜZCE"));
        _karaman = branches.Create(_admin, new NewBranch("KARAMAN"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static SessionContext Kullanici(string id, string[]? scope = null, string? home = null, string? operating = null)
        => new(id, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission(PartyService.Module, true, true, true, true),
            new ModulePermission(FinanceService.Module, true, true, true, true),
        }))
        { ScopeBranchIds = scope, HomeBranchId = home, OperatingBranchId = operating };

    private string Hesap(string code, string? branchId) =>
        _finance.CreateAccount(_admin, new NewFinanceAccount(code, code, FinanceAccountKinds.Cash, BranchId: branchId));

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Seçicinin göstereceği şube listesi — <c>BranchAccess.Allowed</c>'dan türer.</summary>
    private static IReadOnlyList<string> SeciciListesi(SessionContext s, IReadOnlyList<string> tumSubeler)
    {
        var allowed = BranchAccess.Allowed(s);
        return allowed is null ? tumSubeler : tumSubeler.Where(b => allowed.Contains(b, StringComparer.Ordinal)).ToList();
    }

    /// <summary>Seçicinin varsayılan seçimi (web ve masaüstünde AYNI kural).</summary>
    private static string? VarsayilanSecim(SessionContext s, IReadOnlyList<string> secilebilir)
    {
        var varsayilan = s.OperatingBranchId ?? s.HomeBranchId;
        if (varsayilan is not null && secilebilir.Contains(varsayilan)) return varsayilan;
        if (BranchAccess.Allowed(s) is not null && secilebilir.Count == 1) return secilebilir[0];
        return null;   // "tüm yetkili şubeler"
    }

    /// <summary>Yazma için tekil aktif şube (web/masaüstü AYNI sıra).</summary>
    private static string? AktifYazmaSubesi(SessionContext s, IReadOnlyList<string> secili, IReadOnlyList<string> secilebilir)
    {
        if (secili.Count == 1) return secili[0];
        if (!string.IsNullOrEmpty(s.OperatingBranchId)) return s.OperatingBranchId;
        if (!string.IsNullOrEmpty(s.HomeBranchId)) return s.HomeBranchId;
        if (secilebilir.Count == 1) return secilebilir[0];
        return null;
    }

    private IReadOnlyList<string> Tum => new[] { _ankara, _duzce, _karaman };

    // ═════════════════════════════════════════════════════════════════════════
    // A — SEÇİCİ LİSTESİ
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>U1 — ⭐ Seçici YETKİSİZ şubeyi HİÇ göstermez.</summary>
    [Fact]
    public void U1_Yetkisiz_Sube_Listede_Yok()
    {
        var liste = SeciciListesi(Kullanici("u1", scope: new[] { _duzce }), Tum);
        Assert.Equal(new[] { _duzce }, liste);
        Assert.DoesNotContain(_ankara, liste);
        Assert.DoesNotContain(_karaman, liste);
    }

    /// <summary>U2 — Yönetici yalnız YETKİLİ şubelerini görür (tüm firma şubelerini değil).</summary>
    [Fact]
    public void U2_Yonetici_Yalniz_Yetkili_Subeleri_Gorur()
    {
        var liste = SeciciListesi(Kullanici("u2", scope: new[] { _ankara, _duzce }), Tum);
        Assert.Equal(2, liste.Count);
        Assert.DoesNotContain(_karaman, liste);
    }

    /// <summary>U3 — Admin (kapsamsız) tüm şubeleri görür — mevcut davranış korunur.</summary>
    [Fact]
    public void U3_Admin_Tumunu_Gorur() => Assert.Equal(3, SeciciListesi(_admin, Tum).Count);

    /// <summary>U4 — Ana şubesi olan kullanıcı yalnız onu görür (users.branch_id yolu).</summary>
    [Fact]
    public void U4_Ana_Sube_Yolu()
        => Assert.Equal(new[] { _duzce }, SeciciListesi(Kullanici("u4", home: _duzce), Tum));

    // ═════════════════════════════════════════════════════════════════════════
    // B — VARSAYILAN SEÇİM
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// U5 — ⭐ Normal kullanıcı ekranı açtığında KENDİ ŞUBESİ seçili gelir.
    /// "Firma geneli" varsayılan DEĞİLDİR.
    /// </summary>
    [Fact]
    public void U5_Varsayilan_Kendi_Subesi()
    {
        var u = Kullanici("u5", home: _duzce);
        var secilebilir = SeciciListesi(u, Tum);
        Assert.Equal(_duzce, VarsayilanSecim(u, secilebilir));
    }

    /// <summary>U6 — Oturumun ÇALIŞMA şubesi ana şubeden ÖNCE gelir (kullanıcı onu seçerek girdi).</summary>
    [Fact]
    public void U6_Calisma_Subesi_Onceliklidir()
    {
        var u = Kullanici("u6", scope: new[] { _ankara, _duzce }, home: _ankara, operating: _duzce);
        Assert.Equal(_duzce, VarsayilanSecim(u, SeciciListesi(u, Tum)));
    }

    /// <summary>U7 — Çok şubeli yönetici, çalışma şubesi yoksa "tüm yetkili şubeler" ile açılır.</summary>
    [Fact]
    public void U7_Coklu_Yetkili_Varsayilan_Tumu()
    {
        var u = Kullanici("u7", scope: new[] { _ankara, _duzce });
        Assert.Null(VarsayilanSecim(u, SeciciListesi(u, Tum)));   // null = tüm YETKİLİ şubeler
    }

    /// <summary>U8 — Tek yetkili şubesi olan kullanıcıda o şube otomatik seçilir.</summary>
    [Fact]
    public void U8_Tek_Yetkili_Otomatik_Secilir()
    {
        var u = Kullanici("u8", scope: new[] { _karaman });
        Assert.Equal(_karaman, VarsayilanSecim(u, SeciciListesi(u, Tum)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // C — YAZMA: TEKİL AKTİF ŞUBE
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// U9 — ⭐ ÇOKLU SEÇİM YAZMAYA GEÇMEZ: iki şube seçiliyken aktif yazma şubesi seçimden değil,
    /// oturumun çalışma şubesinden gelir. Kayıt "hangi şubeye?" belirsizliğiyle yazılmaz.
    /// </summary>
    [Fact]
    public void U9_Coklu_Secim_Yazmaya_Gecmez()
    {
        var u = Kullanici("u9", scope: new[] { _ankara, _duzce }, operating: _ankara);
        var secilebilir = SeciciListesi(u, Tum);
        var aktif = AktifYazmaSubesi(u, new[] { _ankara, _duzce }, secilebilir);
        Assert.Equal(_ankara, aktif);   // çalışma şubesi — "ikisi birden" DEĞİL
    }

    /// <summary>U10 — Tek şube seçiliyse yazma o şubeye gider.</summary>
    [Fact]
    public void U10_Tek_Secim_Yazmaya_Gecer()
    {
        var u = Kullanici("u10", scope: new[] { _ankara, _duzce });
        Assert.Equal(_duzce, AktifYazmaSubesi(u, new[] { _duzce }, SeciciListesi(u, Tum)));
    }

    /// <summary>
    /// U11 — ⭐ UI'nın belirlediği aktif şube SERVİSTE de kabul edilebilir olmalı
    /// (UI ile servis aynı kaynaktan türediği için çelişmez).
    /// </summary>
    [Fact]
    public void U11_Aktif_Sube_Serviste_Kabul_Edilir()
    {
        var u = Kullanici("u11", scope: new[] { _duzce }, operating: _duzce);
        var aktif = AktifYazmaSubesi(u, Array.Empty<string>(), SeciciListesi(u, Tum));
        Assert.Equal(_duzce, aktif);
        Assert.Equal(_duzce, BranchAccess.Resolve(u, aktif));   // servis de aynı sonuca varır
    }

    /// <summary>
    /// U12 — ⭐ UI atlanırsa bile servis korur: kullanıcı elle YETKİSİZ şube gönderirse REDDEDİLİR.
    /// (UI kısıtı güvenlik değildir; bu test onu belgeliyor.)
    /// </summary>
    [Fact]
    public void U12_UI_Atlanirsa_Servis_Korur()
    {
        var ankaraHesap = Hesap("K-ANK", _ankara);
        var c = _parties.Create(_admin, new NewParty("C-1", "Cari", PartyTypes.Both));
        var u = Kullanici("u12", scope: new[] { _duzce });

        Assert.Throws<ForbiddenException>(() => _finance.Add(u,
            new NewFinanceEntry(ankaraHesap, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c, BranchId: _ankara)));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D — SEÇİM DEĞİŞİNCE VERİ DEĞİŞİR
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>U13 — ⭐ Şube seçimi değişince liste GERÇEKTEN değişir (filtre servise gidiyor).</summary>
    [Fact]
    public void U13_Secim_Degisince_Veri_Degisir()
    {
        Hesap("K-ANK", _ankara);
        Hesap("K-DUZ", _duzce);
        var yonetici = Kullanici("u13", scope: new[] { _ankara, _duzce });

        Assert.Equal(2, _financeQ.Accounts(yonetici).Count);                                   // tüm yetkili
        Assert.Single(_financeQ.Accounts(yonetici, branchIds: new[] { _duzce }));               // yalnız DÜZCE
        Assert.Single(_financeQ.Accounts(yonetici, branchIds: new[] { _ankara }));              // yalnız ANKARA
    }

    /// <summary>
    /// U14 — ⭐ YETKİ DEĞİŞİRSE ESKİ SEÇİM GEÇERSİZDİR: seçici geçersiz seçimi düşürür,
    /// servis de kesişimle korur → yetkisi kalmayan şube kullanılmaya devam etmez.
    /// </summary>
    [Fact]
    public void U14_Yetki_Degisince_Eski_Secim_Gecersiz()
    {
        Hesap("K-ANK", _ankara);
        Hesap("K-DUZ", _duzce);

        // Kullanıcının ANKARA yetkisi KALDIRILDI; ekranda hâlâ ANKARA seçili kalmış olsun.
        var artikSadeceDuzce = Kullanici("u14", scope: new[] { _duzce });

        // Seçici: geçersiz seçim listeden düşer.
        Assert.DoesNotContain(_ankara, SeciciListesi(artikSadeceDuzce, Tum));

        // Servis: eski seçim gönderilse bile ANKARA verisi GELMEZ (kesişim boş → şubeli kayıt yok).
        var sonuc = _financeQ.Accounts(artikSadeceDuzce, branchIds: new[] { _ankara });
        Assert.DoesNotContain("K-ANK", sonuc.Select(x => x.Account.Code));
    }

    /// <summary>
    /// U15 — ⭐ Cari bakiyesi LİSTE ile KART arasında sessiz fark üretmez: ikisi de aynı şube
    /// kapsamını kullanır ("firma toplamı = yetkili şube toplamları").
    /// </summary>
    [Fact]
    public void U15_Liste_Ve_Kart_Bakiyesi_Ayni_Kapsamda()
    {
        var c = _parties.Create(_admin, new NewParty("C-1", "Cari", PartyTypes.Both));
        var kAnk = Hesap("K-ANK", _ankara);
        var kDuz = Hesap("K-DUZ", _duzce);
        _finance.Add(_admin, new NewFinanceEntry(kAnk, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c, BranchId: _ankara));
        _finance.Add(_admin, new NewFinanceEntry(kDuz, FinanceTxnTypes.Receipt, 40m, Op(), PartyId: c, BranchId: _duzce));

        var duzceli = Kullanici("u15", scope: new[] { _duzce });

        var listeBakiye = _parties.List(duzceli).Items.Single().Balance;   // liste satırı
        var kartBakiye = _ledger.Balance(duzceli, c).Balance;             // kart
        Assert.Equal(kartBakiye, listeBakiye);
        Assert.Equal(-40m, listeBakiye);                                   // yalnız DÜZCE hareketi

        // Admin firma toplamını görür.
        Assert.Equal(-140m, _parties.List(_admin).Items.Single().Balance);
    }

    /// <summary>U16 — Çoklu şube seçiminde bakiye YALNIZ seçilen şubelerden toplanır.</summary>
    [Fact]
    public void U16_Coklu_Sube_Bakiyesi()
    {
        var c = _parties.Create(_admin, new NewParty("C-1", "Cari", PartyTypes.Both));
        var kAnk = Hesap("K-ANK", _ankara);
        var kDuz = Hesap("K-DUZ", _duzce);
        var kKar = Hesap("K-KAR", _karaman);
        _finance.Add(_admin, new NewFinanceEntry(kAnk, FinanceTxnTypes.Receipt, 100m, Op(), PartyId: c, BranchId: _ankara));
        _finance.Add(_admin, new NewFinanceEntry(kDuz, FinanceTxnTypes.Receipt, 40m, Op(), PartyId: c, BranchId: _duzce));
        _finance.Add(_admin, new NewFinanceEntry(kKar, FinanceTxnTypes.Receipt, 7m, Op(), PartyId: c, BranchId: _karaman));

        var yonetici = Kullanici("u16", scope: new[] { _ankara, _duzce });

        Assert.Equal(-140m, _ledger.Balance(yonetici, c).Balance);                        // yetkili ikisi
        Assert.Equal(-40m, _ledger.Balance(yonetici, c, new[] { _duzce }).Balance);       // yalnız DÜZCE
        // KARAMAN istense bile kapsam dışı → kesişim boş → yalnız şubesiz hareketler (yok) = 0
        Assert.Equal(0m, _ledger.Balance(yonetici, c, new[] { _karaman }).Balance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
