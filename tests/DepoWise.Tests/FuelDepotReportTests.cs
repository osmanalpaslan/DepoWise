using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Depo Girişi Raporu (2026-08-08 — ortak standarda taşındı) hesaplama + davranış doğruluğu. Her satır bir yakıt
/// alım (depo giriş) kaydı. Senaryolar: normal kayıt, tutar hesabı, tedarikçi/şube filtreleri, yetkisiz şube
/// (fail-closed), tarih dışı hariç, ağırlıklı ortalama toplam birim fiyat, NumCell HAM/görüntü, TotalRow ayrımı,
/// para birimi kolonu, boş fatura no. Tek tablo + 1:1 LEFT JOIN (N+1 yok). Kolon sırası:
/// 0 Şube · 1 Tarih · 2 Tedarikçi · 3 Litre · 4 Birim Fiyat · 5 Tutar · 6 Fatura No · 7 Para Birimi.
/// </summary>
public class FuelDepotReportTests : IDisposable
{
    private const long Base = 1_700_000_000_000;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    public FuelDepotReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_depotrep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var users = new UserService(_factory, new TestClock());
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Seed();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Base);
    }

    private void Seed()
    {
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Base));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B2','A','Sahra',@n,@n);", ("@n", Base));
        Exec("INSERT INTO suppliers(id,company_id,name,created_at,updated_at) VALUES('S1','A','Petrol A',@n,@n);", ("@n", Base));
        Exec("INSERT INTO suppliers(id,company_id,name,created_at,updated_at) VALUES('S2','A','Petrol B',@n,@n);", ("@n", Base));

        // e1: B1, S1, 1000 L @ 40 → 40000 ; e2: B1, S2, 500 L @ 44 → 22000 ; e3: B2, S1, 200 L @ 42 → 8400 (fatura yok)
        Entry("e1", "B1", "S1", "1000", "40", "INV1", "TRY", Base);
        Entry("e2", "B1", "S2", "500", "44", "INV2", "TRY", Base);
        Entry("e3", "B2", "S1", "200", "42", null, "TRY", Base);
        // e4: tarih DIŞI (uzak gelecek) → geçerli aralıkta elenir
        Entry("e4", "B1", "S1", "999", "99", "INVX", "TRY", Base + 500_000_000_000L);
    }

    [Fact]
    public void Rapor_TemelYapi_TarihDisiHaric()
    {
        var t = Run();
        // RPR-V3 (2026-08-27): ad "Yakıt Depo Girişi" oldu — rapor yalnız YAKIT deposunu gösteriyor,
        // eski ad malzeme girişi arayan kullanıcıyı yanıltıyordu (uygulamanın geri kalanı zaten böyle diyordu).
        Assert.Equal("Yakıt Depo Girişi Raporu", t.Title);
        Assert.Equal(8, t.Headers.Count);
        Assert.Equal(3, t.Rows.Count);        // e1,e2,e3 (e4 tarih dışı)
        Assert.NotNull(t.Numeric);
        Assert.NotNull(t.TotalRow);
    }

    [Fact]
    public void NormalKayit_TumAlanlar_Dogru()
    {
        var e1 = Row(Run(), r => (string)r[6]! == "INV1");
        Assert.Equal("Merkez", (string)e1[0]!);
        Assert.Equal("Petrol A", (string)e1[2]!);
        Assert.Equal(1000.0, D(e1[3]), 3);
        Assert.Equal("1.000,00 L", Disp(e1[3]));
        Assert.Equal(40.0, D(e1[4]), 3);              // birim fiyat
        Assert.Equal(40000.0, D(e1[5]), 3);           // tutar = 1000*40
        Assert.Equal("TRY", (string)e1[7]!);          // para birimi kolonu
    }

    [Fact]
    public void Tutar_LitreCarpiBirimFiyat()
    {
        var e2 = Row(Run(), r => (string)r[6]! == "INV2");
        Assert.Equal(22000.0, D(e2[5]), 3);           // 500*44
    }

    [Fact]
    public void FaturaNoBos_EmptyString()
    {
        var e3 = Row(Run(), r => (string)r[0]! == "Sahra");
        Assert.Equal("", (string)e3[6]!);             // fatura no yok → boş
    }

    [Fact]
    public void Toplam_LitreTutar_VeAgirlikliOrtBirimFiyat()
    {
        var top = Run().TotalRow!;
        Assert.Equal("TOPLAM", (string)top[0]!);
        Assert.Equal(1700.0, D(top[3]), 3);           // litre 1000+500+200
        Assert.Equal(70400.0, D(top[5]), 3);          // tutar 40000+22000+8400
        Assert.Equal(70400.0 / 1700.0, D(top[4]), 4); // ağırlıklı ort. birim fiyat (basit ort. DEĞİL)
    }

    // ── Filtreler ──
    [Fact]
    public void TedarikciFiltresi_YalnizSeciliTedarikci()
    {
        var t = _reports.FuelDepot(_admin, Req(suppliers: new[] { "S1" }));
        Assert.Equal(2, t.Rows.Count);                // e1, e3 (Petrol A)
        Assert.All(t.Rows, r => Assert.Equal("Petrol A", (string)r[2]!));
    }

    [Fact]
    public void SubeFiltresi_YetkiliAdmin_AcikSecim()
    {
        var t = _reports.FuelDepot(_admin, Req(branches: new[] { "B1" }));
        Assert.Equal(2, t.Rows.Count);                // e1, e2 (Merkez)
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
    }

    [Fact]
    public void YetkisizKullanici_SubeDegistiremez_OturumSubesineDuser()
    {
        var set = new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>());
        var staff = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, set) { OperatingBranchId = "B1" };
        var t = _reports.FuelDepot(staff, Req(branches: new[] { "B2" }));   // B2 istese de B1'e kilitli
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
        Assert.DoesNotContain(t.Rows, r => (string)r[0]! == "Sahra");
    }

    // ── Toplam + NumCell ──
    [Fact]
    public void ToplamSatiri_SatirlardaDegil()
    {
        var t = Run();
        Assert.DoesNotContain(t.Rows, r => (string)r[0]! == "TOPLAM");
        Assert.StartsWith("TOPLAM", (string)t.TotalRow![0]!);
    }

    [Fact]
    public void NumCell_HamDeger_GoruntudenBagimsiz()
    {
        var e1 = Row(Run(), r => (string)r[6]! == "INV1");
        Assert.IsType<NumCell>(e1[5]);
        var n = (NumCell)e1[5]!;
        Assert.Equal(40000.0, n.Value, 3);
        Assert.Contains("₺", n.Display);
    }

    // ── Yardımcılar ──
    private TableModel Run() => _reports.FuelDepot(_admin, Req());

    private static ReportRequest Req(string[]? branches = null, string[]? suppliers = null)
        => new(true, 1, 2_000_000_000_000L, branches, null, null, null, null, null, suppliers);

    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => System.Convert.ToDouble(v),
    };

    private static string Disp(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };

    private static IReadOnlyList<object?> Row(TableModel t, Func<IReadOnlyList<object?>, bool> pred) => t.Rows.First(pred);

    private void Entry(string id, string branch, string supplier, string liters, string price, string? invoice, string currency, long date)
        => Exec(@"INSERT INTO fuel_depot_entries(id,company_id,supplier_id,liters,unit_price,currency_code,invoice_no,entry_date,op_branch_id,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@sup,@l,@pr,@cur,@inv,@d,@ob,@op,@n,@n,1,0);",
            ("@id", id), ("@sup", supplier), ("@l", liters), ("@pr", price), ("@cur", currency),
            ("@inv", (object?)invoice), ("@d", date), ("@ob", branch), ("@op", "op-" + id), ("@n", Base));

    private void Exec(string sql, params (string, object?)[] ps)
    {
        using var c = _factory.Create();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
