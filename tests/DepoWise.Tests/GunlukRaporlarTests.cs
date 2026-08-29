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
/// ═══ ADR-182 · S3 — GÜN BAZLI YENİ RAPORLAR ═══ (ARA İŞ 2, 2026-08-29)
///
/// İki yeni katalog raporu:
/// <list type="bullet">
///   <item><b>fuel-daily</b> "Yakıt Tüketim — Günlük" (PK-G1=A): her satır bir (ARAÇ, GÜN);
///   yalnız FİŞİ OLAN gün/araçlar. Günlerin toplamı DÖNEM raporuna eşittir.</item>
///   <item><b>stock-movements-daily</b> "Stok Hareketleri — Günlük" (PK-G2=A): her satır bir
///   (GÜN, HAREKET TÜRÜ) → işlem sayısı + giriş/çıkış miktar toplamı.</item>
/// </list>
///
/// <b>Mevcut raporlar korunur</b> (regresyon burada): <c>fuel</c> · <c>stock-movements</c> ·
/// <c>vehicle-daily</c> davranışları DEĞİŞMEDİ. Gün kovası tam sayı bölmesidir → iki lehçede birebir
/// (PostgreSQL karşılığı <c>PostgresGunlukRaporlarTests</c>'te ayrıca kanıtlanır).
/// </summary>
public class GunlukRaporlarTests : IDisposable
{
    private const long Gun = 86_400_000L;
    private static readonly long G1 = new DateTimeOffset(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    private static readonly long G2 = G1 + Gun;
    private static readonly long G3 = G1 + 2 * Gun;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    public GunlukRaporlarTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_gunluk_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var users = new UserService(_factory, new SabitSaat());
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Seed();
    }

    private sealed class SabitSaat : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.FromUnixTimeMilliseconds(G1);
    }

    private void Seed()
    {
        // İkinci FİRMA (tenant sızıntısı testleri için) — 'A' oturum açılırken oluşur.
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
             "VALUES('B','Baska Firma',@n,@n,1,0,5,5,2);", ("@n", G1));

        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", G1));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B2','A','Sahra',@n,@n);", ("@n", G1));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('BB','B','Baska',@n,@n);", ("@n", G1));

        // ── Yakıt tarafı ──
        Veh("va", "VA", "B1"); Veh("vb", "VB", "B2"); Veh("vc", "VC", "B1");   // VC hiç fiş almaz
        VehFirma("vx", "VX", "BB", "B");                                        // başka FİRMA (tenant)

        // VA · 1 Ağustos: 2 fiş → km 300, litre 150, tutar 6200
        Fis("f1", "va", "1000", "1200", "100", "40", G1);
        Fis("f2", "va", "1200", "1300", "50", "44", G1 + 3_600_000);
        // VA · 2 Ağustos: 1 fiş → km 100, litre 40, tutar 1600
        Fis("f3", "va", "1300", "1400", "40", "40", G2);
        // VB · 2 Ağustos gün SONU (sınır) → km 50, litre 25, tutar 1000
        Fis("f4", "vb", "500", "550", "25", "40", G2 + Gun - 1);
        // Başka firmanın fişi — HİÇBİR raporda görünmemeli
        FisFirma("fx", "vx", "1", "2", "999", "99", G1, "B");

        // ── Stok tarafı ──
        Exec("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
             "VALUES('M1','A','MK1','Çimento',NULL,'0',@n,@n,1,0);", ("@n", G1));
        Exec("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
             "VALUES('MB','B','MKB','Baska',NULL,'0',@n,@n,1,0);", ("@n", G1));

        Hareket("h1", "A", "M1", "B1", "in", 1, "10.5", G1);      // 1 Ağustos giriş
        Hareket("h2", "A", "M1", "B1", "in", 1, "4.5", G1);       // 1 Ağustos giriş
        Hareket("h3", "A", "M1", "B1", "out", -1, "3", G1);       // 1 Ağustos çıkış
        Hareket("h4", "A", "M1", "B2", "transfer", -1, "2", G2);  // 2 Ağustos transfer (çıkış bacağı)
        Hareket("h5", "A", "M1", "B1", "transfer", 1, "2", G2);   // 2 Ağustos transfer (giriş bacağı)
        Hareket("hx", "B", "MB", "BB", "in", 1, "77", G1);        // başka FİRMA
    }

    // ══════════════ A) YAKIT TÜKETİM — GÜNLÜK ══════════════

    [Fact]
    public void GNL1_Katalog_Tanimi_Dogru()
    {
        var d = ReportCatalog.ByKey("fuel-daily");
        Assert.NotNull(d);
        Assert.Equal("Yakıt Tüketim — Günlük", d!.Name);
        Assert.Equal(ReportCategory.Fuel, d.Category);
        Assert.Equal("report_fuel", ReportCatalog.CategoryModule(d.Category));   // mevcut kategori yetkisi
        Assert.True(d.RequiresDate);
        Assert.True(d.UsesDate && d.UsesBranch && d.UsesVehicle && d.UsesVehicleType);
        Assert.Equal("fuel", d.DataModule);
    }

    [Fact]
    public void GNL2_Gun_Gun_Kirilim_Uretir()
    {
        var t = YakitGunluk(G1, G3 - 1);
        Assert.Equal("Yakıt Tüketim — Günlük", t.Title);
        Assert.Equal(14, t.Headers.Count);
        Assert.Equal("Tarih", t.Headers[0]);
        // VA(1 Ağu) · VA(2 Ağu) · VB(2 Ağu) = 3 satır; VC (fişsiz) HİÇ görünmez.
        Assert.Equal(3, t.Rows.Count);
        Assert.DoesNotContain(t.Rows, r => (string)r[2]! == "VC");
    }

    [Fact]
    public void GNL3_Gunun_Degerleri_Dogru_Oranlar_Gunluk_Hesaplanir()
    {
        var t = YakitGunluk(G1, G3 - 1);
        var va1 = Satir(t, "01.08.2026", "VA");
        Assert.Equal(2.0, D(va1[7]), 3);            // işlem sayısı
        Assert.Equal(300.0, D(va1[8]), 3);          // mesafe
        Assert.Equal(150.0, D(va1[9]), 3);          // litre
        Assert.Equal(150.0 / 300.0, D(va1[10]), 4); // ort. tüketim — GÜNÜN değerlerinden
        Assert.Equal(6200.0 / 150.0, D(va1[11]), 4);// ağırlıklı ort. fiyat
        Assert.Equal(6200.0, D(va1[12]), 3);        // maliyet
        Assert.Equal(6200.0 / 300.0, D(va1[13]), 4);// birim maliyet

        var va2 = Satir(t, "02.08.2026", "VA");
        Assert.Equal(100.0, D(va2[8]), 3);
        Assert.Equal(1600.0, D(va2[12]), 3);
    }

    /// <summary>⭐ EN KRİTİK: günlerin toplamı DÖNEM raporuyla BİREBİR aynıdır (TOPLAM satırı dahil).</summary>
    [Fact]
    public void GNL4_Gunluk_Toplamlar_Donem_Raporuyla_Birebir()
    {
        var gunluk = YakitGunluk(G1, G3 - 1);
        var donem = _reports.FuelConsumption(_admin, new ReportRequest(true, G1, G3 - 1));

        // Araç bazında: günlerin toplamı = dönem satırı
        foreach (var kod in new[] { "VA", "VB" })
        {
            var gunlukLitre = gunluk.Rows.Where(r => (string)r[2]! == kod).Sum(r => D(r[9]));
            var gunlukMaliyet = gunluk.Rows.Where(r => (string)r[2]! == kod).Sum(r => D(r[12]));
            var gunlukKm = gunluk.Rows.Where(r => (string)r[2]! == kod).Sum(r => D(r[8]));
            var d = donem.Rows.First(r => (string)r[1]! == kod);
            Assert.Equal(D(d[8]), gunlukLitre, 3);      // litre
            Assert.Equal(D(d[11]), gunlukMaliyet, 3);   // toplam maliyet
            Assert.Equal(D(d[7]), gunlukKm, 3);         // mesafe
        }

        // TOPLAM satırları da eşit
        Assert.Equal(D(donem.TotalRow![8]), D(gunluk.TotalRow![9]), 3);     // litre
        Assert.Equal(D(donem.TotalRow![11]), D(gunluk.TotalRow![12]), 3);   // maliyet
        Assert.Equal(D(donem.TotalRow![6]), D(gunluk.TotalRow![7]), 3);     // işlem sayısı
    }

    [Fact]
    public void GNL5_Gun_Sinirlari_Iki_Uc_Dahil()
    {
        // VB'nin tek fişi 2 Ağustos 23:59:59.999 → 2 Ağustos gününde görünür, 3 Ağustos'ta görünmez.
        Assert.Contains(YakitGunluk(G2, G2 + Gun - 1).Rows, r => (string)r[2]! == "VB");
        Assert.DoesNotContain(YakitGunluk(G3, G3 + Gun - 1).Rows, r => (string)r[2]! == "VB");
    }

    [Fact]
    public void GNL6_Bos_Gun_Satiri_Uretilmez()
    {
        // 3 Ağustos'ta hiç fiş yok → rapor BOŞ (vehicle-daily'nin aksine 0'lı satır üretilmez).
        Assert.Empty(YakitGunluk(G3, G3 + Gun - 1).Rows);
    }

    [Fact]
    public void GNL7_Arac_ve_Sube_Filtreleri_Calisir()
    {
        Assert.All(YakitGunluk(G1, G3 - 1, veh: new[] { "va" }).Rows, r => Assert.Equal("VA", (string)r[2]!));
        Assert.All(YakitGunluk(G1, G3 - 1, branch: new[] { "B2" }).Rows, r => Assert.Equal("Sahra", (string)r[1]!));
    }

    [Fact]
    public void GNL8_Tenant_Baska_Firma_Gorunmez()
        => Assert.DoesNotContain(YakitGunluk(G1, G3 - 1).Rows, r => (string)r[2]! == "VX");

    [Fact]
    public void GNL9_BranchAccess_Kapsam_Disi_Sube_Gelmez()
    {
        var izin = new PermissionSet(new[]
        {
            new ModulePermission("reports", true, false, false, false),
            new ModulePermission("report_fuel", true, false, false, false),
        }, Array.Empty<string>());
        var personel = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, izin) { ScopeBranchIds = new[] { "B1" } };
        var t = _reports.Run(personel, "fuel-daily", new ReportRequest(true, G1, G3 - 1));
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[1]!));
        Assert.DoesNotContain(t.Rows, r => (string)r[2]! == "VB");   // B2 aracı kapsam dışı
    }

    [Fact]
    public void GNL10_Yetki_Reports_Arti_Kategori_Gerekir()
    {
        var yalnizReports = Personel("reports");
        Assert.Throws<ForbiddenException>(() => _reports.Run(yalnizReports, "fuel-daily", Istek()));

        var yanlisKategori = Personel("reports", "report_stock");
        Assert.Throws<ForbiddenException>(() => _reports.Run(yanlisKategori, "fuel-daily", Istek()));

        var dogru = Personel("reports", "report_fuel");
        Assert.NotEmpty(_reports.Run(dogru, "fuel-daily", Istek()).Rows);
    }

    [Fact]
    public void GNL11_Siralama_Gun_Sonra_Sube_Arac()
    {
        var t = YakitGunluk(G1, G3 - 1);
        Assert.Equal("01.08.2026", (string)t.Rows[0][0]!);
        Assert.Equal("02.08.2026", (string)t.Rows[1][0]!);
        Assert.Equal("02.08.2026", (string)t.Rows[2][0]!);
        Assert.Equal("Merkez", (string)t.Rows[1][1]!);   // aynı günde Merkez (VA) önce, Sahra (VB) sonra
        Assert.Equal("Sahra", (string)t.Rows[2][1]!);
    }

    // ══════════════ B) STOK HAREKETLERİ — GÜNLÜK ══════════════

    [Fact]
    public void GNL12_Stok_Katalog_Tanimi_Dogru()
    {
        var d = ReportCatalog.ByKey("stock-movements-daily");
        Assert.NotNull(d);
        Assert.Equal("Stok Hareketleri — Günlük", d!.Name);
        Assert.Equal(ReportCategory.Stock, d.Category);
        Assert.Equal("report_stock", ReportCatalog.CategoryModule(d.Category));
        Assert.True(d.RequiresDate);
        Assert.True(d.UsesDate && d.UsesLocation && d.UsesMovementType && d.UsesSearch && d.UsesMaterial);
        Assert.Equal("stock", d.DataModule);
    }

    [Fact]
    public void GNL13_Gun_Carpi_Tur_Ozeti_Uretir()
    {
        var t = StokGunluk(G1, G3 - 1);
        Assert.Equal("Stok Hareketleri — Günlük", t.Title);
        Assert.Equal(new[] { "Tarih", "Tür", "İşlem Sayısı", "Giriş Miktarı", "Çıkış Miktarı" }, t.Headers);
        // 1 Ağu: Giriş + Çıkış · 2 Ağu: Transfer → 3 satır
        Assert.Equal(3, t.Rows.Count);
    }

    [Fact]
    public void GNL14_Giris_Cikis_Toplamlari_Kesin()
    {
        var t = StokGunluk(G1, G3 - 1);
        var giris = t.Rows.First(r => (string)r[0]! == "01.08.2026" && (string)r[1]! == "Giriş");
        Assert.Equal(15.0, D(giris[3]), 3);    // 10,5 + 4,5 — ondalık kesin
        Assert.Equal("15", Gor(giris[3]));     // görüntüde kayan nokta artığı YOK
        Assert.Equal(2.0, D(giris[2]), 3);     // işlem sayısı
        Assert.Equal(0.0, D(giris[4]), 3);     // çıkışı yok

        var cikis = t.Rows.First(r => (string)r[0]! == "01.08.2026" && (string)r[1]! == "Çıkış");
        Assert.Equal(3.0, D(cikis[4]), 3);
    }

    /// <summary>Transfer defterde İKİ satırdır: aynı gün hem giriş hem çıkış olarak sayılır.</summary>
    [Fact]
    public void GNL15_Transfer_Iki_Bacak_Olarak_Sayilir()
    {
        var t = StokGunluk(G2, G2 + Gun - 1);
        var tr = t.Rows.Single(r => (string)r[1]! == "Transfer");
        Assert.Equal(2.0, D(tr[2]), 3);   // iki hareket satırı
        Assert.Equal(2.0, D(tr[3]), 3);   // giriş bacağı
        Assert.Equal(2.0, D(tr[4]), 3);   // çıkış bacağı
    }

    [Fact]
    public void GNL16_Tur_ve_Depo_Filtreleri_Tek_Kaynaktan_Calisir()
    {
        var yalnizGiris = _reports.Run(_admin, "stock-movements-daily",
            new ReportRequest(true, G1, G3 - 1, MovementTypes: new[] { "in" }));
        Assert.All(yalnizGiris.Rows, r => Assert.Equal("Giriş", (string)r[1]!));

        var yalnizB2 = _reports.Run(_admin, "stock-movements-daily",
            new ReportRequest(true, G1, G3 - 1, LocationIds: new[] { "B2" }));
        Assert.All(yalnizB2.Rows, r => Assert.Equal("Transfer", (string)r[1]!));   // B2'de yalnız transfer var
    }

    [Fact]
    public void GNL17_Toplam_Satiri_Donemin_Tamami()
    {
        var t = StokGunluk(G1, G3 - 1);
        Assert.Equal(t.Rows.Sum(r => D(r[3])), D(t.TotalRow![3]), 3);
        Assert.Equal(t.Rows.Sum(r => D(r[4])), D(t.TotalRow![4]), 3);
        Assert.Equal(t.Rows.Sum(r => D(r[2])), D(t.TotalRow![2]), 3);
    }

    [Fact]
    public void GNL18_Stok_Tenant_ve_Yetki()
    {
        Assert.All(StokGunluk(G1, G3 - 1).Rows, r => Assert.NotEqual(77.0, D(r[3])));   // B firmasının 77'si yok

        Assert.Throws<ForbiddenException>(() => _reports.Run(Personel("reports"), "stock-movements-daily", Istek()));
        Assert.Throws<ForbiddenException>(() => _reports.Run(Personel("reports", "report_fuel"), "stock-movements-daily", Istek()));
        Assert.NotEmpty(_reports.Run(Personel("reports", "report_stock"), "stock-movements-daily", Istek()).Rows);
    }

    // ══════════════ C) MEVCUT RAPORLAR — REGRESYON ══════════════

    [Fact]
    public void GNL19_Detay_Stok_Hareketleri_Raporu_Degismedi()
    {
        var detay = _reports.Run(_admin, "stock-movements", new ReportRequest(true, G1, G3 - 1));
        Assert.Equal("Stok Hareketleri Raporu", detay.Title);
        Assert.Equal(12, detay.Headers.Count);
        Assert.Equal(5, detay.Rows.Count);   // A firmasının 5 hareketi satır-satır (özetlenmez)
    }

    [Fact]
    public void GNL20_Arac_Gunluk_Raporu_TamFilo_Kaldi()
    {
        // vehicle-daily hâlâ TÜM araçları × TÜM günleri üretir (fişsiz VC dahil) — fuel-daily'den farkı budur.
        var t = _reports.Run(_admin, "vehicle-daily", new ReportRequest(true, G1, G2 + Gun - 1));
        Assert.Equal(6, t.Rows.Count);   // 2 gün × 3 araç (VA, VB, VC)
        Assert.Contains(t.Rows, r => (string)r[1]! == "VC");
    }

    // ══════════════ Yardımcılar ══════════════

    private TableModel YakitGunluk(long from, long to, string[]? veh = null, string[]? branch = null)
        => _reports.Run(_admin, "fuel-daily", new ReportRequest(true, from, to, BranchIds: branch, VehicleIds: veh));

    private TableModel StokGunluk(long from, long to)
        => _reports.Run(_admin, "stock-movements-daily", new ReportRequest(true, from, to));

    private static ReportRequest Istek() => new(true, G1, G3 - 1);

    private static SessionContext Personel(params string[] moduller)
        => new("u-p", "A", new[] { RoleKeys.Staff },
            new PermissionSet(moduller.Select(m => new ModulePermission(m, true, false, false, false)).ToArray(), Array.Empty<string>()));

    private static IReadOnlyList<object?> Satir(TableModel t, string tarih, string kod)
        => t.Rows.First(r => (string)r[0]! == tarih && (string)r[2]! == kod);

    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => Convert.ToDouble(v),
    };

    private static string Gor(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };

    private void Veh(string id, string code, string branch) => VehFirma(id, code, branch, "A");

    private void VehFirma(string id, string code, string branch, string firma)
        => Exec(@"INSERT INTO vehicles(id,company_id,internal_code,meter_unit,branch_id,current_meter,created_at,updated_at,version,is_deleted)
                  VALUES(@id,@f,@code,'km',@b,'0',@n,@n,1,0);",
            ("@id", id), ("@f", firma), ("@code", code), ("@b", branch), ("@n", G1));

    private void Fis(string id, string veh, string prev, string cur, string liters, string price, long tarih)
        => FisFirma(id, veh, prev, cur, liters, price, tarih, "A");

    private void FisFirma(string id, string veh, string prev, string cur, string liters, string price, long tarih, string firma)
        => Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,@f,@v,@p,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,0);",
            ("@id", id), ("@f", firma), ("@v", veh), ("@p", prev), ("@c", cur), ("@l", liters), ("@pr", price),
            ("@d", tarih), ("@op", "op-" + id), ("@n", G1));

    private void Hareket(string id, string firma, string mat, string sube, string tur, int yon, string miktar, long tarih)
        => Exec(@"INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at)
                  VALUES(@id,@f,@m,@b,@t,@y,@q,@op,'',@n);",
            ("@id", id), ("@f", firma), ("@m", mat), ("@b", sube), ("@t", tur), ("@y", yon),
            ("@q", miktar), ("@op", "op-" + id), ("@n", tarih));

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
