using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ RPT-GUNLUK (2026-08-29, PK-R1=A) — "Araç Raporu — Günlük" (`vehicle-daily`) sözleşme kilitleri.
///
/// Kilitlenen kurallar:
///  • Mevcut `vehicle` DÖNEM raporuna DOKUNULMADI — günlük değerlerin toplamı dönem raporuyla BİREBİR
///    tutarlıdır (aynı istekle iki rapor karşılaştırılır).
///  • Aralıktaki HER GÜN gösterilir; boş gün 0 ("-") satırıyla görünür (satır sayısı = gün × araç).
///  • Tarih sınırları RPR-06 ile aynı: gün başı (00:00:00.000) ve gün sonu (23:59:59.999) DAHİL.
///  • Oranlar (ort. fiyat/tüketim/birim maliyet) TOPLANMAZ — o günün değerlerinden yeniden hesaplanır.
///  • "Gün İçi Son Sayaç" = o günkü EN GEÇ yakıt fişinin sayacı; fiş yoksa "-".
///  • Tenant + BranchAccess + soft-delete + yetki (reports ÜST kapısı + report_vehicle kategori kapısı)
///    dönem raporuyla aynı sözleşmede.
/// </summary>
public class VehicleDailyReportTests : IDisposable
{
    private const long G = 86_400_000;
    private static long Day(long i) => (20_000 + i) * G;   // gün başlangıcı (UTC, deterministik)

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin, _adminB;
    private readonly string _mat;

    public VehicleDailyReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_vehdaily_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var users = new UserService(_factory);
        var uidA = users.EnsureInitialAdmin("A", "admina", "admin123", RoleKeys.CompanyAdmin);
        var uidB = users.EnsureInitialAdmin("B", "adminb", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uidA, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _adminB = new SessionContext(uidB, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = new MaterialService(_factory).Create(_admin, new NewMaterial("MAT1", "Parça"));
        Seed();
    }

    private void Seed()
    {
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Day(0)));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B2','A','Sahra',@n,@n);", ("@n", Day(0)));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('BB','B','Uzak',@n,@n);", ("@n", Day(0)));
        Exec("INSERT INTO maintenance_definitions(id,company_id,name,created_at,updated_at) VALUES('DEF1','A','Periyodik',@n,@n);", ("@n", Day(0)));

        Veh("v1", "V1", "A", "km", "B1");
        Veh("v2", "V2", "A", "hour", "B2");
        Veh("vdel", "VDEL", "A", "km", "B1", deleted: true);   // soft-delete: hiç görünmemeli
        Veh("vb", "VB", "B", "km", "BB");                      // tenant: A raporunda görünmemeli

        // ── V1 (KM) — GÜN 1: 3 fiş (gün BAŞI tam sınır + gün içi + prev NULL sayaçlı) ──
        Fuel("f1", "v1", "A", "1000", "1200", "100", "40", Day(1));                 // sınır: 00:00:00.000 DAHİL
        Fuel("f2", "v1", "A", "1200", "1300", "50", "44", Day(1) + 3_600_000);
        FuelNullPrev("f2n", "v1", "A", "1350", "10", "40", Day(1) + 7_200_000);     // km katkısı 0; SON sayaç 1350
        // GÜN 3: gün SONU tam sınır fişi (23:59:59.999 DAHİL)
        Fuel("f3", "v1", "A", "1350", "1400", "20", "50", Day(3) + G - 1);
        // Aralık DIŞI fişler (gün 0 ve gün 4) — hiçbir güne yansımamalı
        Fuel("fx0", "v1", "A", "1", "2", "999", "99", Day(0) + 5_000);
        Fuel("fx4", "v1", "A", "1", "2", "999", "99", Day(4));
        // Soft-delete edilmiş fiş — sayılmamalı
        Fuel("fdel", "v1", "A", "1", "2", "999", "99", Day(1) + 10_000, deleted: true);

        // ── V1 — GÜN 2: yalnız bakım (400) + doğrudan parça (150); yakıt YOK ──
        Maint("m1", "v1", Day(2), new[] { ("2", "150"), ("1", "100") });
        Issue("d1", "v1", Day(2), "3", "50");

        // ── V2 (SAAT) — GÜN 1: tek fiş (60 saat, 80 L, 45 ₺/L) ──
        Fuel("f5", "v2", "A", "500", "560", "80", "45", Day(1) + 1000);

        // ── B firması — GÜN 1 fişi (tenant izolasyonu) ──
        Fuel("fb", "vb", "B", "10", "20", "30", "40", Day(1) + 1000);
    }

    private static ReportRequest Istek(long fromGun = 1, long toGun = 3,
        IReadOnlyList<string>? branchIds = null, IReadOnlyList<string>? vehicleIds = null)
        => new(true, Day(fromGun), Day(toGun) + G - 1, branchIds, VehicleIds: vehicleIds);

    private TableModel Gunluk(SessionContext? s = null, ReportRequest? req = null)
        => _reports.Run(s ?? _admin, "vehicle-daily", req ?? Istek());

    // ── Katalog ve şekil ──

    [Fact]
    public void Katalog_GunlukRapor_DogruTanimli()
    {
        var d = ReportCatalog.ByKey("vehicle-daily");
        Assert.NotNull(d);
        Assert.Equal("Araç Raporu — Günlük", d!.Name);
        Assert.Equal(ReportCategory.Vehicle, d.Category);          // → report_vehicle kategori kapısı
        Assert.Equal(ReportGroup.Standard, d.Group);
        Assert.True(d.RequiresDate);
        Assert.Equal("vehicles", d.DataModule);
        var donem = ReportCatalog.ByKey("vehicle")!;
        Assert.Equal(donem.Filters, d.Filters);                    // filtre seti dönem raporuyla AYNI
    }

    [Fact]
    public void Sekil_16Kolon_GunXArac_Satirlari()
    {
        var t = Gunluk();
        Assert.Equal(16, t.Headers.Count);
        Assert.Equal("Tarih", t.Headers[0]);
        Assert.Equal("Gün İçi Son Sayaç", t.Headers[15]);
        Assert.Equal(3 * 2, t.Rows.Count);   // 3 gün × 2 görünür araç (soft-delete + yabancı firma HARİÇ)
        Assert.NotNull(t.TotalRow);
    }

    // ── Gün değerleri ──

    [Fact]
    public void Gun1_V1_Degerler_Ve_Oranlar_GunluktenHesaplanir()
    {
        var r = Row(Gunluk(), 1, "V1");
        Assert.Equal(300.0, D(r[6]), 3);                  // km: 200+100 (NULL-prev fişi katmaz)
        Assert.Equal(160.0, D(r[7]), 3);                  // litre: 100+50+10 (silinen fiş HARİÇ)
        Assert.Equal(6600.0 / 160.0, D(r[8]), 6);         // ort. fiyat = GÜNÜN fuel/litre (toplanmadı)
        Assert.Equal(6600.0, D(r[9]), 3);                 // yakıt maliyeti: 4000+2200+400
        Assert.Equal(160.0 / 300.0, D(r[10]), 6);         // ort. tüketim = GÜNÜN litre/km
        Assert.Equal(6600.0, D(r[13]), 3);                // toplam (bakım/parça bugün yok)
        Assert.Equal(6600.0 / 300.0, D(r[14]), 6);        // birim maliyet = GÜNÜN total/km
        Assert.Equal(1350.0, D(r[15]), 3);                // gün içi SON sayaç (en geç fiş: f2n)
    }

    [Fact]
    public void Gun2_V1_YalnizBakimVeParca()
    {
        var r = Row(Gunluk(), 2, "V1");
        Assert.Equal(0.0, D(r[7]), 3);                    // yakıt yok
        Assert.Equal(400.0, D(r[11]), 3);                 // bakım malzeme
        Assert.Equal(150.0, D(r[12]), 3);                 // doğrudan parça
        Assert.Equal(550.0, D(r[13]), 3);                 // toplam
        Assert.Equal("-", Disp(r[15]));                   // fiş yok → son sayaç "-"
        Assert.Equal("-", Disp(r[8]));                    // oran da "-"
    }

    [Fact]
    public void Sinirlar_GunBasi_Ve_GunSonu_Dahil()
    {
        var t = Gunluk();
        Assert.Equal(100.0 + 50.0 + 10.0, D(Row(t, 1, "V1")[7]), 3);   // 00:00:00.000 fişi (f1) DAHİL
        Assert.Equal(20.0, D(Row(t, 3, "V1")[7]), 3);                  // 23:59:59.999 fişi (f3) DAHİL
        Assert.Equal(1400.0, D(Row(t, 3, "V1")[15]), 3);               // gün 3 son sayaç
    }

    [Fact]
    public void BosGun_SifirSatirla_Gorunur()
    {
        var t = Gunluk();
        var r = Row(t, 2, "V2");                          // V2'nin gün 2'sinde HİÇ veri yok
        Assert.Equal(0.0, D(r[7]), 3);
        Assert.Equal(0.0, D(r[13]), 3);
        Assert.Equal("-", Disp(r[7]));                    // görüntü "-", ham 0 — boş gün AÇIKÇA görünür
    }

    [Fact]
    public void TekGunluk_Aralik_YalnizOGun()
    {
        var t = Gunluk(req: Istek(1, 1));
        Assert.Equal(2, t.Rows.Count);                    // 1 gün × 2 araç
        Assert.All(t.Rows, r => Assert.Equal(TarihStr(1), (string)r[0]!));
    }

    [Fact]
    public void SaatBazliArac_BirimSaat()
    {
        var r = Row(Gunluk(), 1, "V2");
        Assert.Equal("Saat", (string)r[5]!);
        Assert.Equal(60.0, D(r[6]), 3);                   // 560-500 saat
        Assert.Equal(80.0, D(r[7]), 3);
        Assert.EndsWith("Saat", Disp(r[6]));              // görüntü saat cinsinden
    }

    // ── Dönem raporuyla TUTARLILIK (en kritik kilit) ──

    [Fact]
    public void GunlukToplamlar_DonemRaporuyla_Birebir()
    {
        var gunluk = Gunluk();
        var donem = _reports.Run(_admin, "vehicle", Istek());

        foreach (var kod in new[] { "V1", "V2" })
        {
            var d = donem.Rows.First(r => (string)r[0]! == kod);
            var g = gunluk.Rows.Where(r => (string)r[1]! == kod).ToList();
            Assert.Equal(3, g.Count);                                     // her gün bir satır
            Assert.Equal(D(d[5]), g.Sum(r => D(r[6])), 6);                // mesafe: günler toplamı = dönem
            Assert.Equal(D(d[6]), g.Sum(r => D(r[7])), 6);                // litre
            Assert.Equal(D(d[8]), g.Sum(r => D(r[9])), 6);                // yakıt maliyeti
            Assert.Equal(D(d[10]), g.Sum(r => D(r[11])), 6);              // bakım malzeme
            Assert.Equal(D(d[11]), g.Sum(r => D(r[12])), 6);              // doğrudan parça
            Assert.Equal(D(d[12]), g.Sum(r => D(r[13])), 6);              // toplam maliyet
        }

        // TOPLAM satırları da aynı (litre + para kolonları; oranlar iki raporda da boş).
        Assert.Equal(D(donem.TotalRow![6]), D(gunluk.TotalRow![7]), 6);   // litre
        Assert.Equal(D(donem.TotalRow[8]), D(gunluk.TotalRow[9]), 6);     // yakıt
        Assert.Equal(D(donem.TotalRow[10]), D(gunluk.TotalRow[11]), 6);   // bakım
        Assert.Equal(D(donem.TotalRow[11]), D(gunluk.TotalRow[12]), 6);   // parça
        Assert.Equal(D(donem.TotalRow[12]), D(gunluk.TotalRow[13]), 6);   // toplam
    }

    // ── Filtre / kapsam / güvenlik ──

    [Fact]
    public void AracFiltresi_Uygulanir()
    {
        var t = Gunluk(req: Istek(vehicleIds: new[] { "v2" }));
        Assert.Equal(3, t.Rows.Count);                    // 3 gün × yalnız V2
        Assert.All(t.Rows, r => Assert.Equal("V2", (string)r[1]!));
    }

    [Fact]
    public void Tenant_Izolasyonu_KarsiFirmaGorunmez()
    {
        var metin = string.Join("|", Gunluk().Rows.Select(r => (string)r[1]!));
        Assert.DoesNotContain("VB", metin, StringComparison.Ordinal);

        var tb = Gunluk(_adminB);                         // B firması yalnız kendi aracını görür
        Assert.All(tb.Rows, r => Assert.Equal("VB", (string)r[1]!));
        Assert.Equal(30.0, D(Row(tb, 1, "VB")[7]), 3);
    }

    [Fact]
    public void BranchAccess_KapsamliKullanici_YalnizKendiSubesi()
    {
        var kapsamli = new SessionContext("u-scope", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[]
            {
                new ModulePermission("reports", true, false, false, false),
                new ModulePermission("report_vehicle", true, false, false, false),
            }))
        { ScopeBranchIds = new[] { "B1" } };
        var t = Gunluk(kapsamli);
        Assert.Equal(3, t.Rows.Count);                    // yalnız V1 (B1) × 3 gün
        Assert.All(t.Rows, r => Assert.Equal("V1", (string)r[1]!));
    }

    [Fact]
    public void Yetki_Reports_Yok_403()
        => Assert.Throws<ForbiddenException>(() =>
            Gunluk(new SessionContext("u0", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty)));

    /// <summary>RPT-YETKI çift kapı: "reports" VAR ama kategori (report_vehicle) YOK → reddedilir.</summary>
    [Fact]
    public void Yetki_KategoriYok_403()
        => Assert.Throws<ForbiddenException>(() => Gunluk(Personel()));

    [Fact]
    public void Yetki_ReportsVeKategori_Yeter()
    {
        var t = Gunluk(Personel("report_vehicle"));
        Assert.Equal(6, t.Rows.Count);
    }

    [Fact]
    public void Siralama_GunSonraArac()
    {
        var rows = Gunluk().Rows;
        Assert.Equal(TarihStr(1), (string)rows[0][0]!);
        Assert.Equal("V1", (string)rows[0][1]!);          // gün içinde Şube→Araç (Merkez < Sahra)
        Assert.Equal("V2", (string)rows[1][1]!);
        Assert.Equal(TarihStr(2), (string)rows[2][0]!);   // sonra sonraki gün
    }

    // ── Yardımcılar ──

    private static SessionContext Personel(params string[] ekModuller)
        => new("u1", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }
                .Concat(ekModuller.Select(m => new ModulePermission(m, true, false, false, false))).ToArray()));

    private static string TarihStr(long gun)
        => DateTimeOffset.FromUnixTimeMilliseconds(Day(gun)).UtcDateTime.ToString("dd.MM.yyyy");

    private static IReadOnlyList<object?> Row(TableModel t, long gun, string kod)
        => t.Rows.First(r => (string)r[0]! == TarihStr(gun) && (string)r[1]! == kod);

    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => Convert.ToDouble(v),
    };

    private static string Disp(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };

    private void Veh(string id, string code, string co, string unit, string branch, bool deleted = false)
        => Exec(@"INSERT INTO vehicles(id,company_id,internal_code,meter_unit,branch_id,current_meter,created_at,updated_at,version,is_deleted)
                  VALUES(@id,@co,@code,@unit,@branch,'0',@n,@n,1,@del);",
            ("@id", id), ("@co", co), ("@code", code), ("@unit", unit), ("@branch", branch), ("@n", Day(0)), ("@del", deleted ? 1 : 0));

    private void Fuel(string id, string veh, string co, string prev, string cur, string liters, string price, long date, bool deleted = false)
        => Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,@co,@v,@p,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,@del);",
            ("@id", id), ("@co", co), ("@v", veh), ("@p", prev), ("@c", cur), ("@l", liters), ("@pr", price),
            ("@d", date), ("@op", "op-" + id), ("@n", Day(0)), ("@del", deleted ? 1 : 0));

    private void FuelNullPrev(string id, string veh, string co, string cur, string liters, string price, long date)
        => Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,@co,@v,NULL,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,0);",
            ("@id", id), ("@co", co), ("@v", veh), ("@c", cur), ("@l", liters), ("@pr", price),
            ("@d", date), ("@op", "op-" + id), ("@n", Day(0)));

    private void Maint(string id, string veh, long date, (string qty, string price)[] materials)
    {
        Exec(@"INSERT INTO vehicle_maintenances(id,company_id,vehicle_id,maintenance_def_id,performed_date,operation_id,is_cancelled,created_at,updated_at,version,is_deleted)
               VALUES(@id,'A',@v,'DEF1',@d,@op,0,@n,@n,1,0);",
            ("@id", id), ("@v", veh), ("@d", date), ("@op", "op-" + id), ("@n", Day(0)));
        int i = 0;
        foreach (var (qty, price) in materials)
            Exec("INSERT INTO maintenance_materials(id,company_id,maintenance_id,material_id,quantity,unit_price) VALUES(@id,'A',@m,@mat,@q,@pr);",
                ("@id", id + "-mm" + i++), ("@m", id), ("@mat", _mat), ("@q", qty), ("@pr", price));
    }

    private void Issue(string docId, string veh, long date, string qty, string price)
    {
        Exec(@"INSERT INTO stock_documents(id,company_id,doc_type,doc_no,doc_date,vehicle_id,status,created_at,version,is_deleted)
               VALUES(@id,'A','out',@no,@d,@v,'active',@n,1,0);",
            ("@id", docId), ("@no", "DOC-" + docId), ("@d", date), ("@v", veh), ("@n", Day(0)));
        Exec(@"INSERT INTO stock_movements(id,company_id,material_id,movement_type,direction,quantity,unit_price,operation_id,created_at,document_id)
               VALUES(@id,'A',@mat,'out',-1,@q,@pr,@op,@n,@doc);",
            ("@id", docId + "-mv"), ("@mat", _mat), ("@q", qty), ("@pr", price), ("@op", "op-" + docId), ("@n", Day(0)), ("@doc", docId));
    }

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
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
