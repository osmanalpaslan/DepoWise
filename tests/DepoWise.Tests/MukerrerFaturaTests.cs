using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-82 (AR-GE 2026-10-27, F-183) — OLASI MÜKERRER FATURA ═══
///  MK1 — kesin: aynı cari + yön + belge no (boşluk/harf farkı önemsiz) → tek grup, 2 fatura
///  MK2 — olası: aynı cari + yön + gün + toplam ve belge no boş → grup; farklı belge numaraları → grup YOK
///  MK3 — farklı cari / farklı yön / farklı tutar → grup yok
///  MK4 — iptal edilen fatura gruba girmez (yanlış olan iptal edilince uyarı kalkar)
///  MK5 — "mükerrer değil" → grup düşer; tekrar işaretleme kopya üretmez; başka firmanın faturası reddedilir
///  MK6 — üç faturalı grupta yalnız bir çift işaretlenirse grup KALIR (her çift karar ister)
/// </summary>
public class MukerrerFaturaTests : IDisposable
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_mkr_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly InvoiceService _fatura;
    private readonly MukerrerFaturaService _mk;
    private readonly SessionContext _y;
    private readonly string _a, _b;
    private static readonly long Gun = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public MukerrerFaturaTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('MK','MK',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin("MK", "mk_admin", "Test!2026", RoleKeys.CompanyAdmin);
        _y = new SessionContext(uid, "MK", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var cari = new PartyService(_f);
        _a = cari.Create(_y, new NewParty("C-A", "Alfa", PartyTypes.Supplier));
        _b = cari.Create(_y, new NewParty("C-B", "Beta", PartyTypes.Supplier));
        _fatura = new InvoiceService(_f, new StockService(_f), new PartyLedgerService(_f));
        _mk = new MukerrerFaturaService(_f);
    }

    public void Dispose() { try { File.Delete(_yol); } catch { } }

    private string F(string cari, decimal tutar, string? dis = null, string yon = InvoiceDirections.Purchase, long? gun = null) =>
        _fatura.Create(_y, new NewInvoice(yon, cari, new[] { new NewInvoiceLine(null, "Kalem", null, 1, tutar, VatRate: 20) },
            Guid.NewGuid().ToString("N"), ExternalNo: dis, InvoiceDate: gun ?? Gun, AffectsStock: false)).Id;

    [Fact]
    public void MK1_Ayni_Belge_No_Kesin()
    {
        F(_a, 100, "ABC 123");
        F(_a, 250, "abc123", gun: Gun + 86_400_000 * 3);                  // tutar ve gün farklı ama belge no aynı
        var g = Assert.Single(_mk.Gruplar(_y));
        Assert.True(g.Kesin);
        Assert.Equal(2, g.Faturalar.Count);
        Assert.Contains("Aynı belge no", g.Baslik);
    }

    [Fact]
    public void MK2_Ayni_Gun_Tutar_Olasi_Farkli_Belge_No_Degil()
    {
        F(_a, 100);
        F(_a, 100, "X-1");
        var g = Assert.Single(_mk.Gruplar(_y));
        Assert.False(g.Kesin);
        F(_b, 300, "B-1");
        F(_b, 300, "B-2");                                                  // iki farklı belge no → farklı faturalar
        Assert.Single(_mk.Gruplar(_y));
    }

    [Fact]
    public void MK3_Farkli_Cari_Yon_Tutar_Grup_Yok()
    {
        F(_a, 100); F(_b, 100);
        F(_a, 200); F(_a, 200, yon: InvoiceDirections.Sales);
        F(_a, 300); F(_a, 301);
        Assert.Empty(_mk.Gruplar(_y));
    }

    [Fact]
    public void MK4_Iptal_Edilen_Gruba_Girmez()
    {
        F(_a, 100, "D-1");
        var ikinci = F(_a, 100, "D-1");
        Assert.Single(_mk.Gruplar(_y));
        _fatura.Cancel(_y, ikinci, "mükerrer giriş");
        Assert.Empty(_mk.Gruplar(_y));
    }

    [Fact]
    public void MK5_Mukerrer_Degil_Isareti()
    {
        var x = F(_a, 100, "E-1");
        var y = F(_a, 100, "E-1");
        _mk.MukerrerDegil(_y, new[] { x, y });
        _mk.MukerrerDegil(_y, new[] { y, x });                              // tekrar → kopya yok
        Assert.Empty(_mk.Gruplar(_y));
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM invoice_duplicate_dismissals WHERE company_id='MK';";
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        Assert.Throws<ForbiddenException>(() => _mk.MukerrerDegil(_y, new[] { x, "baska-firmanin-faturasi" }));
        Assert.Throws<ArgumentException>(() => _mk.MukerrerDegil(_y, new[] { x }));
    }

    [Fact]
    public void MK6_Uclu_Grupta_Tek_Cift_Yetmez()
    {
        var x = F(_a, 100, "G-1");
        var y = F(_a, 100, "G-1");
        F(_a, 100, "G-1");
        _mk.MukerrerDegil(_y, new[] { x, y });
        Assert.Equal(3, Assert.Single(_mk.Gruplar(_y)).Faturalar.Count);
    }
}
