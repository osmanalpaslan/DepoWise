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
/// G4-1 — ÖN MUHASEBE / CARİ ALTYAPISI (kullanıcı isteği 2026-08-12).
///
/// <b>İKİ TEMEL KURAL:</b>
/// <list type="number">
///   <item><b>Bakiye TÜRETİLİR:</b> <c>parties</c>'te bakiye kolonu YOKTUR; her zaman defterden
///     (<c>Σ direction × amount</c>) hesaplanır — stok defterindeki kuralın cari karşılığı.</item>
///   <item><b>Stokla sınır:</b> cari işlemleri <c>stock_movements</c>/<c>stock_balances</c>'a
///     ASLA dokunmaz. Stok defterinin tek yazıcısı <c>StockService</c> olmaya devam eder.</item>
/// </list>
/// </summary>
public class PartyAccountingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly SessionContext _admin;
    private const string CoA = "A";
    private const string CoB = "B";

    public PartyAccountingTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g41_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(CoA, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, CoA, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static SessionContext Staff(string co = CoA, params (string Mod, bool V, bool C, bool E, bool D)[] perms)
        => new("st", co, new[] { RoleKeys.Staff },
            new PermissionSet(perms.Select(p => new ModulePermission(p.Mod, p.V, p.C, p.E, p.D))));

    private static SessionContext AdminOf(string co) => new("ad" + co, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

    private string Party(string code = "C-001", string title = "Örnek Ltd. Şti.", string type = PartyTypes.Customer)
        => _parties.Create(_admin, new NewParty(code, title, type));

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // CARİ TEMEL
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>1 — Cari oluşturma: alanlar korunur, kart okunabilir.</summary>
    [Fact]
    public void P01_Cari_Olusturulur()
    {
        var id = _parties.Create(_admin, new NewParty("C-100", "Anadolu İnşaat A.Ş.", PartyTypes.Both,
            TaxOffice: "Çankaya", TaxNo: "1234567890", Phone: "05001112233", City: "Ankara"));

        var p = _parties.Get(_admin, id);
        Assert.Equal("C-100", p.Code);
        Assert.Equal("Anadolu İnşaat A.Ş.", p.Title);
        Assert.Equal(PartyTypes.Both, p.PartyType);
        Assert.Equal("Müşteri + Tedarikçi", p.TypeText);
        Assert.Equal("1234567890", p.TaxNo);
        Assert.True(p.IsActive);
        Assert.Equal("TRY", p.Currency);
    }

    /// <summary>2 — Düzenleme alanları günceller ve sürümü artırır (düzenleme kilidi jetonu).</summary>
    [Fact]
    public void P02_Cari_Duzenlenir()
    {
        var id = Party();
        var v = _parties.Get(_admin, id).Version;
        _parties.Update(_admin, id, new UpdateParty("C-001", "Yeni Ünvan", PartyTypes.Supplier,
            Phone: "05009998877", Version: v));

        var p = _parties.Get(_admin, id);
        Assert.Equal("Yeni Ünvan", p.Title);
        Assert.Equal(PartyTypes.Supplier, p.PartyType);
        Assert.Equal("05009998877", p.Phone);
        Assert.True(p.Version > v);
    }

    /// <summary>3 — Düzenleme kilidi: arada başkası kaydettiyse ikinci kayıt REDDEDİLİR.</summary>
    [Fact]
    public void P03_Duzenleme_Kilidi_Calisir()
    {
        var id = Party();
        var eski = _parties.Get(_admin, id).Version;
        _parties.Update(_admin, id, new UpdateParty("C-001", "İlk", PartyTypes.Customer, Version: eski));

        Assert.Throws<ConcurrencyException>(() =>
            _parties.Update(_admin, id, new UpdateParty("C-001", "İkinci", PartyTypes.Customer, Version: eski)));
        Assert.Equal("İlk", _parties.Get(_admin, id).Title);
    }

    /// <summary>4 — Aktif/pasif: pasif cari SİLİNMEZ, listede durum filtresiyle ayrışır.</summary>
    [Fact]
    public void P04_Aktif_Pasif()
    {
        var id = Party();
        _parties.SetActive(_admin, id, false);
        Assert.False(_parties.Get(_admin, id).IsActive);

        Assert.Empty(_parties.List(_admin, onlyActive: true).Items);
        Assert.Single(_parties.List(_admin, onlyActive: false).Items);
        Assert.Single(_parties.List(_admin).Items);           // filtre yok → hepsi
    }

    /// <summary>5 — Cari KODU firma içinde benzersizdir (oluşturmada ve düzenlemede).</summary>
    [Fact]
    public void P05_Cari_Kodu_Benzersiz()
    {
        Party("C-777");
        var ex = Assert.Throws<InvalidOperationException>(() => Party("C-777", "Başka"));
        Assert.Contains("zaten kullanılıyor", ex.Message);

        var ikinci = Party("C-888", "İkinci");
        Assert.Throws<InvalidOperationException>(() =>
            _parties.Update(_admin, ikinci, new UpdateParty("C-777", "İkinci", PartyTypes.Customer)));
    }

    /// <summary>6 — Silinen kodun YENİDEN kullanılabilmesi (kısmi benzersizlik indeksi).</summary>
    [Fact]
    public void P06_Silinen_Kod_Yeniden_Kullanilabilir()
    {
        var id = Party("C-DEL");
        _parties.Delete(_admin, id);
        var yeni = Party("C-DEL", "Yeni Cari");   // aynı kod → hata OLMAMALI
        Assert.Equal("C-DEL", _parties.Get(_admin, yeni).Code);
    }

    /// <summary>7 — Doğrulama: zorunlu alanlar, tip, VKN/TCKN hane sayısı, para birimi.</summary>
    [Theory]
    [InlineData("", "Ad", PartyTypes.Customer, null, null, "Cari kodu zorunlu")]
    [InlineData("C-1", "", PartyTypes.Customer, null, null, "Ünvan")]
    [InlineData("C-1", "Ad", "gecersiz", null, null, "Cari tipi geçersiz")]
    [InlineData("C-1", "Ad", PartyTypes.Customer, "123", null, "10 haneli")]
    [InlineData("C-1", "Ad", PartyTypes.Customer, "12345678AB", null, "10 haneli")]
    [InlineData("C-1", "Ad", PartyTypes.Customer, null, "123", "11 haneli")]
    public void P07_Dogrulama_Hatali_Veriyi_Reddeder(string code, string title, string type,
        string? taxNo, string? nationalId, string beklenenMesaj)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _parties.Create(_admin, new NewParty(code, title, type, TaxNo: taxNo, NationalId: nationalId)));
        Assert.Contains(beklenenMesaj, ex.Message);
    }

    /// <summary>8 — Vergi no ve TCKN BOŞ bırakılabilir (perakende/serbest cari); zorunlu tutulmaz.</summary>
    [Fact]
    public void P08_Vergi_Bilgisi_Zorunlu_Degil()
    {
        var id = _parties.Create(_admin, new NewParty("C-BOS", "Perakende Müşteri", PartyTypes.Customer));
        var p = _parties.Get(_admin, id);
        Assert.Null(p.TaxNo);
        Assert.Null(p.NationalId);
        Assert.Equal("—", p.TaxIdText);
    }

    /// <summary>9 — FİRMA İZOLASYONU: başka firmanın carisi görülemez/düzenlenemez.</summary>
    [Fact]
    public void P09_Firma_Izolasyonu()
    {
        var id = Party("C-IZO");
        var digerFirma = AdminOf(CoB);

        Assert.Throws<ForbiddenException>(() => _parties.Get(digerFirma, id));
        Assert.Throws<ForbiddenException>(() => _parties.Update(digerFirma, id, new UpdateParty("X", "Y", PartyTypes.Customer)));
        Assert.Throws<ForbiddenException>(() => _parties.Delete(digerFirma, id));
        Assert.Throws<ForbiddenException>(() => _parties.SetActive(digerFirma, id, false));
        Assert.Empty(_parties.List(digerFirma).Items);
    }

    /// <summary>10 — Aynı cari KODU İKİ FARKLI firmada kullanılabilir (izolasyon).</summary>
    [Fact]
    public void P10_Ayni_Kod_Iki_Firmada_Kullanilabilir()
    {
        Party("C-ORTAK");
        var b = AdminOf(CoB);
        var id = _parties.Create(b, new NewParty("C-ORTAK", "B Firmasının Carisi", PartyTypes.Customer));
        Assert.Equal("C-ORTAK", _parties.Get(b, id).Code);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // CARİ HAREKET / BAKİYE
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>11 — Borç/alacak ve bakiye: Bakiye = Borç − Alacak.</summary>
    [Fact]
    public void P11_Borc_Alacak_Bakiye()
    {
        var id = Party();
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 1000m, IsDebit: true));
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Adjustment, 250.50m, IsDebit: true));
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Adjustment, 400m, IsDebit: false));

        var b = _ledger.Balance(_admin, id);
        Assert.Equal(1250.50m, b.Debit);
        Assert.Equal(400m, b.Credit);
        Assert.Equal(850.50m, b.Balance);
        Assert.Equal(3, b.EntryCount);
        Assert.Contains("Borç", b.BalanceText);
    }

    /// <summary>12 — Alacak fazlaysa bakiye NEGATİF ve metin "Alacak" der.</summary>
    [Fact]
    public void P12_Alacakli_Cari()
    {
        var id = Party();
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 100m, IsDebit: false));
        var b = _ledger.Balance(_admin, id);
        Assert.Equal(-100m, b.Balance);
        Assert.Contains("Alacak", b.BalanceText);
    }

    /// <summary>13 — Ondalık tutarlar kayan noktaya DÜŞMEZ (Money/decimal kuralı).</summary>
    [Fact]
    public void P13_Ondalik_Kayan_Noktaya_Dusmez()
    {
        var id = Party();
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 0.1m, IsDebit: true));
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 0.2m, IsDebit: true));
        Assert.Equal(0.3m, _ledger.Balance(_admin, id).Balance);
    }

    /// <summary>14 — EKSTRE: kronolojik yürüyen bakiye doğru hesaplanır.</summary>
    [Fact]
    public void P14_Ekstre_Yuruyen_Bakiye()
    {
        var id = Party();
        long g = 1_700_000_000_000;
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 500m, true, EntryDate: g));
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Adjustment, 200m, false, EntryDate: g + 86_400_000));
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Adjustment, 100m, true, EntryDate: g + 172_800_000));

        var rows = _ledger.Statement(_admin, id, newestFirst: false);
        Assert.Equal(3, rows.Count);
        Assert.Equal(500m, rows[0].RunningBalance);
        Assert.Equal(300m, rows[1].RunningBalance);
        Assert.Equal(400m, rows[2].RunningBalance);
        Assert.Equal(400m, _ledger.Balance(_admin, id).Balance);   // ekstre sonu = bakiye
    }

    /// <summary>15 — IDEMPOTENCY: aynı <c>operation_id</c> ikinci kez hareket ÜRETMEZ.
    /// ⚠️ G4-1b'den beri ELLE giriş yalnız açılış/düzeltme kabul eder (fatura kullanıcı yolundan
    /// yazılamaz); belge yolunun idempotency'si <see cref="PartyManualEntryTests"/> M04'te doğrulanır.</summary>
    [Fact]
    public void P15_Ayni_OperationId_Ikinci_Hareket_Uretmez()
    {
        var id = Party();
        var op = Op();
        var e1 = _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 1000m, true, OperationId: op));
        var e2 = _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 1000m, true, OperationId: op));

        Assert.Equal(e1, e2);
        var b = _ledger.Balance(_admin, id);
        Assert.Equal(1, b.EntryCount);
        Assert.Equal(1000m, b.Balance);
    }

    /// <summary>16 — TERS KAYIT: hareket silinmez; karşı kayıt yazılır ve İKİSİ DE bakiyeye girmez.</summary>
    [Fact]
    public void P16_Ters_Kayit_Silmez_Bakiyeden_Duser()
    {
        var id = Party();
        var e = _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 750m, true));
        Assert.Equal(750m, _ledger.Balance(_admin, id).Balance);

        _ledger.Reverse(_admin, e, "Yanlış tutar");

        Assert.Equal(0m, _ledger.Balance(_admin, id).Balance);
        Assert.Equal(0, _ledger.Balance(_admin, id).EntryCount);
        // Defterde İZ kalır: iki satır da duruyor (silme yok).
        var rows = _ledger.Statement(_admin, id);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Entry.IsReversed));
    }

    /// <summary>17 — Çift iptal engellenir; gerekçe zorunludur.</summary>
    [Fact]
    public void P17_Cift_Iptal_Ve_Gerekcesiz_Iptal_Engellenir()
    {
        var id = Party();
        var e = _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 100m, true));
        Assert.Throws<ArgumentException>(() => _ledger.Reverse(_admin, e, "  "));
        _ledger.Reverse(_admin, e, "Hata");
        Assert.Throws<InvalidOperationException>(() => _ledger.Reverse(_admin, e, "Tekrar"));
    }

    /// <summary>18 — Hareket doğrulaması: pozitif tutar, geçerli belge türü, geçerli para birimi,
    /// var olan cari.</summary>
    [Fact]
    public void P18_Hareket_Dogrulamasi()
    {
        var id = Party();
        Assert.Throws<ArgumentException>(() => _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 0m, true)));
        Assert.Throws<ArgumentException>(() => _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, -5m, true)));
        Assert.Throws<ArgumentException>(() => _ledger.Add(_admin, new NewLedgerEntry(id, "yok-boyle", 5m, true)));
        Assert.Throws<ArgumentException>(() => _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 5m, true, Currency: "XXX")));
        Assert.Throws<ForbiddenException>(() => _ledger.Add(_admin, new NewLedgerEntry("yok", PartyDocTypes.Opening, 5m, true)));
    }

    /// <summary>19 — HAREKETİ OLAN cari SİLİNEMEZ (muhasebe geçmişi korunur); pasife alınır.</summary>
    [Fact]
    public void P19_Hareketi_Olan_Cari_Silinemez()
    {
        var id = Party();
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 10m, true));

        var ex = Assert.Throws<InvalidOperationException>(() => _parties.Delete(_admin, id));
        Assert.Contains("silinemez", ex.Message);
        Assert.Contains("PASİF", ex.Message);

        _parties.SetActive(_admin, id, false);   // önerilen yol çalışıyor
        Assert.False(_parties.Get(_admin, id).IsActive);
    }

    /// <summary>20 — Hareketlerde FİRMA İZOLASYONU: başka firma cari hareketini göremez/yazamaz.</summary>
    [Fact]
    public void P20_Hareket_Firma_Izolasyonu()
    {
        var id = Party();
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 100m, true));
        var b = AdminOf(CoB);

        Assert.Throws<ForbiddenException>(() => _ledger.Balance(b, id));
        Assert.Throws<ForbiddenException>(() => _ledger.Statement(b, id));
        Assert.Throws<ForbiddenException>(() => _ledger.Add(b, new NewLedgerEntry(id, PartyDocTypes.Opening, 5m, true)));
    }

    /// <summary>21 — Liste bakiyeleri TEK sorguda gelir ve doğrudur (N+1 yok).</summary>
    [Fact]
    public void P21_Liste_Bakiyeleri_Dogru()
    {
        var a = Party("C-A", "A Cari");
        var c = Party("C-B", "B Cari");
        _ledger.Add(_admin, new NewLedgerEntry(a, PartyDocTypes.Opening, 300m, true));
        _ledger.Add(_admin, new NewLedgerEntry(c, PartyDocTypes.Opening, 120m, false));

        var rows = _parties.List(_admin).Items.ToDictionary(x => x.Party.Code);
        Assert.Equal(300m, rows["C-A"].Balance);
        Assert.Equal(-120m, rows["C-B"].Balance);
        Assert.Equal(0m, rows["C-A"].Credit);
    }

    /// <summary>22 — Arama ve sayfalama: kod/ünvan/vergi no/telefon üzerinde; tüm kayıtlar RAM'e çekilmez.</summary>
    [Fact]
    public void P22_Arama_Ve_Sayfalama()
    {
        for (int i = 0; i < 30; i++)
            _parties.Create(_admin, new NewParty($"C-{i:D3}", $"Cari {i}", PartyTypes.Customer, Phone: $"0500000{i:D4}"));

        var s1 = _parties.List(_admin, search: "Cari 7");
        Assert.Equal("C-007", Assert.Single(s1.Items).Party.Code);

        var s2 = _parties.List(_admin, search: "05000000005");
        Assert.Single(s2.Items);

        var p1 = _parties.List(_admin, page: 1, pageSize: 10);
        Assert.Equal(10, p1.Items.Count);
        Assert.Equal(30, p1.TotalCount);
        var p3 = _parties.List(_admin, page: 3, pageSize: 10);
        Assert.Equal(10, p3.Items.Count);
        Assert.NotEqual(p1.Items[0].Party.Id, p3.Items[0].Party.Id);
    }

    /// <summary>23 — Tip filtresi: "Müşteri" araması "Müşteri + Tedarikçi" carileri de getirir.</summary>
    [Fact]
    public void P23_Tip_Filtresi()
    {
        Party("C-M", "Müşteri", PartyTypes.Customer);
        Party("C-T", "Tedarikçi", PartyTypes.Supplier);
        Party("C-H", "Her ikisi", PartyTypes.Both);

        Assert.Equal(2, _parties.List(_admin, partyType: PartyTypes.Customer).Items.Count);   // M + H
        Assert.Equal(2, _parties.List(_admin, partyType: PartyTypes.Supplier).Items.Count);   // T + H
        Assert.Single(_parties.List(_admin, partyType: PartyTypes.Both).Items);               // yalnız H
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // YETKİ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>24 — Dört aksiyon ayrı ayrı zorlanır; kapı SERVİSTEDİR (API/UI atlansa da geçerli).</summary>
    [Fact]
    public void P24_Yetki_Aksiyon_Bazinda_Zorlanir()
    {
        var id = Party();

        var yalnizGoren = Staff(CoA, ("parties", true, false, false, false));
        Assert.NotNull(_parties.Get(yalnizGoren, id));                       // View VAR
        Assert.Throws<ForbiddenException>(() => _parties.Create(yalnizGoren, new NewParty("X", "Y", PartyTypes.Customer)));
        Assert.Throws<ForbiddenException>(() => _parties.Update(yalnizGoren, id, new UpdateParty("C-001", "Z", PartyTypes.Customer)));
        Assert.Throws<ForbiddenException>(() => _parties.Delete(yalnizGoren, id));
        Assert.Throws<ForbiddenException>(() => _ledger.Add(yalnizGoren, new NewLedgerEntry(id, PartyDocTypes.Opening, 1m, true)));

        var yetkisiz = Staff(CoA);
        Assert.Throws<ForbiddenException>(() => _parties.Get(yetkisiz, id));
        Assert.Throws<ForbiddenException>(() => _parties.List(yetkisiz));
        Assert.Throws<ForbiddenException>(() => _ledger.Balance(yetkisiz, id));
        Assert.Throws<ForbiddenException>(() => _ledger.Statement(yetkisiz, id));
    }

    /// <summary>25 — Create yetkisi olan ama Edit olmayan kullanıcı oluşturur, düzenleyemez.</summary>
    [Fact]
    public void P25_Create_Var_Edit_Yok()
    {
        var k = Staff(CoA, ("parties", true, true, false, false));
        var id = _parties.Create(k, new NewParty("C-CR", "Oluşturdum", PartyTypes.Customer));
        Assert.Throws<ForbiddenException>(() => _parties.Update(k, id, new UpdateParty("C-CR", "Değiştir", PartyTypes.Customer)));
        Assert.Throws<ForbiddenException>(() => _parties.SetActive(k, id, false));
    }

    /// <summary>26 — ⭐ YETKİ DEVRETME SINIRI: cari SİLME yetkisi olmayan aktör, onu başkasına VEREMEZ
    /// (G1b escalation korumasının cari modülündeki regresyonu).</summary>
    [Fact]
    public void P26_Kendinde_Olmayan_Cari_Yetkisi_Devredilemez()
    {
        var users = new UserService(_factory, _clock);
        var perms = new PermissionService(_factory, _clock);
        var aktorId = users.EnsureInitialAdmin(CoA, "cari_aktor", "Test!2026", RoleKeys.Staff);
        var hedefId = users.EnsureInitialAdmin(CoA, "cari_hedef", "Test!2026", RoleKeys.Staff);

        // Aktörde: cari görüntüleme + ekleme VAR; düzenleme/silme YOK.
        var aktor = new SessionContext(aktorId, CoA, new[] { RoleKeys.Staff }, new PermissionSet(new[]
        {
            new ModulePermission("parties", true, true, false, false),
            new ModulePermission("permissions", true, false, true, false),
        }));

        perms.SaveForUser(aktor, hedefId, new[] { new ModulePermission("parties", true, true, true, true) },
            Array.Empty<string>());

        var yazilan = perms.GetForUser(_admin, hedefId).Modules.Single(m => m.ModuleKey == "parties");
        Assert.True(yazilan.CanView);      // aktörde VAR
        Assert.True(yazilan.CanCreate);    // aktörde VAR
        Assert.False(yazilan.CanEdit);     // aktörde YOK → kırpıldı
        Assert.False(yazilan.CanDelete);   // aktörde YOK → kırpıldı
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // ⭐ STOKLA SINIR
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>27 — ⭐ EN KRİTİK: cari işlemleri stok defterine HİÇ dokunmaz.
    /// Cari oluşturma, hareket ekleme ve ters kayıt sonrası <c>stock_movements</c> ve
    /// <c>stock_balances</c> satır sayıları DEĞİŞMEZ.</summary>
    [Fact]
    public void P27_Cari_Islemleri_Stok_Defterine_Dokunmaz()
    {
        // Önce gerçek bir stok hareketi oluştur (defter boş olmasın ki "değişmedi" anlamlı olsun).
        var materials = new MaterialService(_factory, _clock);
        var stock = new StockService(_factory, _clock);
        var branches = new BranchService(_factory, _clock);
        var depo = branches.Create(_admin, new NewBranch("Depo A"));
        var mat = materials.Create(_admin, new NewMaterial("STK-1", "Test Malzeme"));
        stock.ReceiveIn(_admin, new[] { new StockLine(mat, 10m) }, Op(), branchId: depo);

        var (hareketOnce, bakiyeOnce) = StockCounts();

        // Cari tarafında yoğun işlem
        var id = Party("C-STK", "Stok Sınır Testi");
        var e = _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 5000m, true));
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Adjustment, 1200m, false));
        _ledger.Reverse(_admin, e, "Test");
        _parties.Update(_admin, id, new UpdateParty("C-STK", "Güncel", PartyTypes.Both));
        _parties.SetActive(_admin, id, false);

        var (hareketSonra, bakiyeSonra) = StockCounts();
        Assert.Equal(hareketOnce, hareketSonra);
        Assert.Equal(bakiyeOnce, bakiyeSonra);

        // Stok bakiyesi de birebir aynı
        Assert.Equal(10m, stock.GetBalanceAt(_admin, mat, depo));
    }

    private (long Movements, long Balances) StockCounts()
    {
        using var conn = _factory.Create();
        using var c1 = conn.CreateCommand();
        c1.CommandText = "SELECT COUNT(*) FROM stock_movements;";
        var m = Convert.ToInt64(c1.ExecuteScalar());
        using var c2 = conn.CreateCommand();
        c2.CommandText = "SELECT COUNT(*) FROM stock_balances;";
        return (m, Convert.ToInt64(c2.ExecuteScalar()));
    }

    /// <summary>28 — Cari tablosunda BAKİYE KOLONU YOKTUR (bakiye türetilir, saklanmaz).</summary>
    [Fact]
    public void P28_Parties_Tablosunda_Bakiye_Kolonu_Yok()
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM parties LIMIT 0;";
        using var r = cmd.ExecuteReader();
        var kolonlar = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToList();
        Assert.DoesNotContain("balance", kolonlar, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("debit", kolonlar, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("credit", kolonlar, StringComparer.OrdinalIgnoreCase);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // AUDIT · MIGRATION
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>29 — Cari ve hareket değişiklikleri audit'e yazılır.</summary>
    [Fact]
    public void P29_Audit_Kaydi_Olusur()
    {
        var id = Party("C-AUD");
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 10m, true));

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type IN ('party','party_ledger') AND company_id='A';";
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 2);
    }

    /// <summary>30 — Migration idempotenttir ve mevcut veriyi bozmaz.</summary>
    [Fact]
    public void P30_Migration_Idempotent()
    {
        var id = Party("C-MIG");
        _ledger.Add(_admin, new NewLedgerEntry(id, PartyDocTypes.Opening, 42m, true));

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            new Migration066_Parties().Up(conn, tx);   // ikinci kez
            tx.Commit();
        }

        Assert.Equal(42m, _ledger.Balance(_admin, id).Balance);
        Assert.Equal("C-MIG", _parties.Get(_admin, id).Code);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
