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
/// G4-1b — ELLE CARİ HAREKETİ + FORM KURALLARI (kullanıcı isteği 2026-08-12).
///
/// <b>🔴 BU TURDA KAPATILAN GERÇEK AÇIK:</b> <c>PartyDocTypes.ManualEntry</c> yalnız bir KATALOG
/// listesiydi; servis her belge türünü kabul ediyordu. Arayüz/API atlanıp doğrudan
/// <c>docType: "invoice"</c> ile hareket yazılabiliyordu → G4-2 aynı faturayı işlediğinde cari
/// <b>İKİ KEZ</b> borçlanırdı (sahte belge + mükerrer borç). Artık kullanıcı yolu
/// (<see cref="PartyLedgerService.Add"/>) yalnız AÇILIŞ ve DÜZELTME kabul eder; belge kaynaklı
/// türler <see cref="PartyLedgerService.AddFromDocument"/> ile ve KAYNAK BELGE zorunluluğuyla yazılır.
/// </summary>
public class PartyManualEntryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PartyService _parties;
    private readonly PartyLedgerService _ledger;
    private readonly SessionContext _admin;
    private const string Co = "A";

    public PartyManualEntryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_g41b_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _parties = new PartyService(_factory, _clock);
        _ledger = new PartyLedgerService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(Co, "admin", "Test!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private string Party(string code = "C-1")
        => _parties.Create(_admin, new NewParty(code, "Test Cari", PartyTypes.Customer));

    private static SessionContext Staff(params (string M, bool V, bool C, bool E, bool D)[] p)
        => new("st", Co, new[] { RoleKeys.Staff },
            new PermissionSet(p.Select(x => new ModulePermission(x.M, x.V, x.C, x.E, x.D))));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // ⭐ ELLE GİRİŞ KISITI — bu turun ana düzeltmesi
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>1 — Kullanıcı yolu YALNIZ açılış ve düzeltme kabul eder.</summary>
    [Theory]
    [InlineData(PartyDocTypes.Opening)]
    [InlineData(PartyDocTypes.Adjustment)]
    public void M01_Elle_Girilebilir_Turler_Kabul_Edilir(string tur)
    {
        var p = Party();
        var id = _ledger.Add(_admin, new NewLedgerEntry(p, tur, 100m, true));
        Assert.False(string.IsNullOrWhiteSpace(id));
    }

    /// <summary>2 — ⭐ Belge kaynaklı türler kullanıcı yolundan YAZILAMAZ. Kapı SERVİSTEDİR:
    /// arayüz ve API atlanıp servis doğrudan çağrılsa da reddedilir.</summary>
    [Theory]
    [InlineData(PartyDocTypes.Invoice)]
    [InlineData(PartyDocTypes.Payment)]
    [InlineData(PartyDocTypes.Receipt)]
    public void M02_Belge_Kaynakli_Turler_Elle_Girilemez(string tur)
    {
        var p = Party();
        var ex = Assert.Throws<ArgumentException>(() => _ledger.Add(_admin, new NewLedgerEntry(p, tur, 100m, true)));
        Assert.Contains("elle girilemez", ex.Message);
        Assert.Equal(0, _ledger.Balance(_admin, p).EntryCount);   // hiçbir şey yazılmadı
    }

    /// <summary>3 — Belge yolu (G4-2/G4-3 için) çalışır AMA kaynak belge ZORUNLUDUR.</summary>
    [Fact]
    public void M03_Belge_Yolu_Kaynak_Belge_Ister()
    {
        var p = Party();
        var eksik = Assert.Throws<ArgumentException>(() => _ledger.AddFromDocument(_admin,
            new NewLedgerEntry(p, PartyDocTypes.Invoice, 500m, true)));
        Assert.Contains("kaynak belge zorunludur", eksik.Message);

        var id = _ledger.AddFromDocument(_admin, new NewLedgerEntry(p, PartyDocTypes.Invoice, 500m, true,
            SourceType: "invoice", SourceId: "INV-1", OperationId: "op-inv-1"));
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(500m, _ledger.Balance(_admin, p).Balance);
    }

    /// <summary>4 — Belge yolunda da IDEMPOTENCY geçerli: aynı fatura iki kez işlenirse cari
    /// İKİ KEZ borçlanmaz (G4-2 için asıl güvence).</summary>
    [Fact]
    public void M04_Belge_Yolu_Idempotent()
    {
        var p = Party();
        var dto = new NewLedgerEntry(p, PartyDocTypes.Invoice, 750m, true,
            SourceType: "invoice", SourceId: "INV-9", OperationId: "op-inv-9");
        var a = _ledger.AddFromDocument(_admin, dto);
        var b = _ledger.AddFromDocument(_admin, dto);

        Assert.Equal(a, b);
        var bal = _ledger.Balance(_admin, p);
        Assert.Equal(750m, bal.Balance);
        Assert.Equal(1, bal.EntryCount);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // ELLE HAREKET ALANLARI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>5 — Açılış borcu / açılış alacağı doğru yönde yazılır.</summary>
    [Fact]
    public void M05_Acilis_Borc_Ve_Alacak()
    {
        var p = Party();
        _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Opening, 1000m, IsDebit: true));
        _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Opening, 400m, IsDebit: false));
        var b = _ledger.Balance(_admin, p);
        Assert.Equal(1000m, b.Debit);
        Assert.Equal(400m, b.Credit);
        Assert.Equal(600m, b.Balance);
    }

    /// <summary>6 — Tarih, vade, belge no ve açıklama saklanır ve ekstrede görünür.</summary>
    [Fact]
    public void M06_Tarih_Vade_BelgeNo_Aciklama_Saklanir()
    {
        var p = Party();
        long tarih = 1_700_000_000_000, vade = tarih + 30L * 86_400_000;
        _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Opening, 250m, true,
            EntryDate: tarih, DocNo: "ACL-2026-1", Description: "Devir bakiyesi", DueDate: vade));

        var e = Assert.Single(_ledger.Statement(_admin, p)).Entry;
        Assert.Equal(tarih, e.EntryDate);
        Assert.Equal(vade, e.DueDate);
        Assert.Equal("ACL-2026-1", e.DocNo);
        Assert.Equal("Devir bakiyesi", e.Description);
        Assert.NotEqual("—", e.DueText);
    }

    /// <summary>7 — Şube boyutu korunur (ileride şube bazlı cari raporu için).</summary>
    [Fact]
    public void M07_Sube_Bilgisi_Saklanir()
    {
        var branches = new BranchService(_factory, _clock);
        var depo = branches.Create(_admin, new NewBranch("Merkez"));
        var p = Party();
        _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Opening, 10m, true, BranchId: depo));
        Assert.Equal(depo, Assert.Single(_ledger.Statement(_admin, p)).Entry.BranchId);
    }

    /// <summary>8 — Ondalık tutar korunur (Money/decimal disiplini).</summary>
    [Fact]
    public void M08_Ondalik_Tutar_Korunur()
    {
        var p = Party();
        _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Opening, 1234.56m, true));
        Assert.Equal(1234.56m, _ledger.Balance(_admin, p).Balance);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // YETKİ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>9 — Hareket ekleme CREATE, ters kayıt EDIT yetkisi ister (aksiyonlar ayrışır).</summary>
    [Fact]
    public void M09_Hareket_Yetkileri_Ayrisir()
    {
        var p = Party();
        var e = _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Opening, 10m, true));

        var yalnizGoren = Staff(("parties", true, false, false, false));
        Assert.Throws<ForbiddenException>(() => _ledger.Add(yalnizGoren, new NewLedgerEntry(p, PartyDocTypes.Opening, 1m, true)));
        Assert.Throws<ForbiddenException>(() => _ledger.Reverse(yalnizGoren, e, "Deneme"));

        var ekleyebilen = Staff(("parties", true, true, false, false));
        _ledger.Add(ekleyebilen, new NewLedgerEntry(p, PartyDocTypes.Opening, 1m, true));   // CREATE var
        Assert.Throws<ForbiddenException>(() => _ledger.Reverse(ekleyebilen, e, "Deneme")); // EDIT yok
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // ⭐ STOK İZOLASYONU (elle hareket + belge yolu)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>10 — ⭐ Elle hareket, ters kayıt ve belge yolu stok tablolarını DEĞİŞTİRMEZ.</summary>
    [Fact]
    public void M10_Cari_Hareketleri_Stok_Defterine_Dokunmaz()
    {
        var materials = new MaterialService(_factory, _clock);
        var stock = new StockService(_factory, _clock);
        var branches = new BranchService(_factory, _clock);
        var depo = branches.Create(_admin, new NewBranch("Depo"));
        var mat = materials.Create(_admin, new NewMaterial("M-1", "Malzeme"));
        stock.ReceiveIn(_admin, new[] { new StockLine(mat, 25m) }, "op-" + Guid.NewGuid().ToString("N"), branchId: depo);

        var once = StockCounts();

        var p = Party();
        var e = _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Opening, 5000m, true));
        _ledger.Add(_admin, new NewLedgerEntry(p, PartyDocTypes.Adjustment, 250m, false));
        _ledger.Reverse(_admin, e, "Düzeltme");
        _ledger.AddFromDocument(_admin, new NewLedgerEntry(p, PartyDocTypes.Invoice, 100m, true,
            SourceType: "invoice", SourceId: "X", OperationId: "op-x"));

        Assert.Equal(once, StockCounts());
        Assert.Equal(25m, stock.GetBalanceAt(_admin, mat, depo));
    }

    private (long M, long B) StockCounts()
    {
        using var conn = _factory.Create();
        using var c1 = conn.CreateCommand();
        c1.CommandText = "SELECT COUNT(*) FROM stock_movements;";
        var m = Convert.ToInt64(c1.ExecuteScalar());
        using var c2 = conn.CreateCommand();
        c2.CommandText = "SELECT COUNT(*) FROM stock_balances;";
        return (m, Convert.ToInt64(c2.ExecuteScalar()));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // FORM AKIŞI (servis sözleşmesi — iki platformun UI'ı bu kuralları çağırır)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>11 — Form akışı: oluştur → düzenle → pasif. Her adım aynı servis kurallarından geçer.</summary>
    [Fact]
    public void M11_Form_Akisi_Uctan_Uca()
    {
        var id = _parties.Create(_admin, new NewParty("C-FORM", "İlk Ünvan", PartyTypes.Customer,
            TaxNo: "1234567890", Phone: "05001112233"));
        var v1 = _parties.Get(_admin, id).Version;

        _parties.Update(_admin, id, new UpdateParty("C-FORM", "Güncel Ünvan", PartyTypes.Both,
            IsPerson: false, TaxOffice: "Çankaya", TaxNo: "1234567890", Phone: "05009998877",
            City: "Ankara", District: "Çankaya", Version: v1));

        var p = _parties.Get(_admin, id);
        Assert.Equal("Güncel Ünvan", p.Title);
        Assert.Equal(PartyTypes.Both, p.PartyType);
        Assert.Equal("Ankara", p.City);
        Assert.True(p.IsActive);

        _parties.SetActive(_admin, id, false);
        Assert.False(_parties.Get(_admin, id).IsActive);
    }

    /// <summary>12 — Gerçek kişide hem VKN hem TCKN girilemez (form iki alanı da gösterir; kural serviste).</summary>
    [Fact]
    public void M12_Gercek_Kiside_Iki_Kimlik_Birden_Girilemez()
    {
        var ex = Assert.Throws<ArgumentException>(() => _parties.Create(_admin,
            new NewParty("C-K", "Ali Veli", PartyTypes.Customer, IsPerson: true,
                TaxNo: "1234567890", NationalId: "12345678901")));
        Assert.Contains("hem vergi no hem T.C.", ex.Message);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
