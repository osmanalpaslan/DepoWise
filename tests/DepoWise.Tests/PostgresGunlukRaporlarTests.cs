using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ⭐ ADR-182 · S3 (2026-08-29) — YENİ GÜNLÜK RAPORLARIN POSTGRESQL PARİTESİ.
///
/// Kanıtlanan noktalar (SQLite karşılığı: <see cref="GunlukRaporlarTests"/>):
/// <list type="bullet">
///   <item><b>fuel-daily</b> — <c>distribution_date / 86400000</c> bigint bölmesi PostgreSQL'de de
///   AYNI gün kovasını üretir; gün sınırının iki ucu (00:00:00.000 · 23:59:59.999) dahildir;
///   fişi olmayan araç/gün satırı ÜRETİLMEZ; günlerin toplamı DÖNEM (<c>fuel</c>) raporuna eşittir.</item>
///   <item><b>stock-movements-daily</b> — gün × tür özeti; miktar toplamları
///   <c>SqlDialect.ExactSumText</c> ile <c>numeric</c> üzerinden TAM KESİN toplanır (kayan nokta
///   artığı yok); transfer iki bacak olarak sayılır.</item>
///   <item>Yakıt Tüketim raporunun ADR-182 kapsam sözleşmesi (yalnız fişi olan araç) PG'de de geçerli.</item>
/// </list>
///
/// ⚠️ Yalnız DEPOWISE_PG_URL (doğrulanmış BOŞ test veritabanı — PostgresTestGuard çift kilidi) ile
/// koşar; yoksa ATLANIR. Production'a hiçbir koşulda bağlanılmaz.
/// </summary>
[Collection("PostgresSchema")]
public class PostgresGunlukRaporlarTests
{
    private static string? PgUrl => Environment.GetEnvironmentVariable("DEPOWISE_PG_URL");

    private const long G = 86_400_000;
    private static long Day(long i) => (20_000 + i) * G;

    [SkippableFact]
    public void PostgreSQLde_gunluk_yakit_ve_stok_raporlari_SQLite_ile_ayni_sozlesmede()
    {
        PostgresTestGuard.SkipUnlessSafe();

        var factory = new PostgresMigrationTests.NpgsqlTestFactory(PgUrl!);
        PostgresTestGuard.ResetSchema(factory);
        new MigrationRunner(factory).Run();

        void Exec(string sql, params (string, object?)[] ps)
        {
            using var c = factory.Create();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted,machine_quota,max_users,max_admins) " +
             "VALUES('A','A',1,1,1,0,5,20,5);");
        var uid = new UserService(factory).EnsureInitialAdmin("A", "admin_gr", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Day(0)));
        foreach (var (id, kod) in new[] { ("v1", "V1"), ("v2", "V2") })
            Exec(@"INSERT INTO vehicles(id,company_id,internal_code,meter_unit,branch_id,current_meter,created_at,updated_at,version,is_deleted)
                   VALUES(@id,'A',@kod,'km','B1','0',@n,@n,1,0);", ("@id", id), ("@kod", kod), ("@n", Day(0)));

        void Fis(string id, string veh, string prev, string cur, string liters, string price, long date) =>
            Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                   VALUES(@id,'A',@v,@p,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,0);",
                ("@id", id), ("@v", veh), ("@p", prev), ("@c", cur), ("@l", liters), ("@pr", price),
                ("@d", date), ("@op", "op-" + id), ("@n", Day(0)));

        // V1: 1. günde iki fiş (gün BAŞI sınırı dahil) + 3. günde bir fiş (gün SONU sınırı dahil)
        Fis("f1", "v1", "1000", "1200", "100", "40", Day(1));
        Fis("f2", "v1", "1200", "1300", "50", "44", Day(1) + 3_600_000);
        Fis("f3", "v1", "1300", "1350", "20", "50", Day(3) + G - 1);
        Fis("fx", "v1", "1", "2", "999", "99", Day(9));            // aralık DIŞI
        // V2: hiç fiş almaz → fuel-daily'de ve (ADR-182 sonrası) fuel'de GÖRÜNMEZ

        var istek = new ReportRequest(true, Day(1), Day(3) + G - 1);
        var reports = new ReportService(factory);

        // ── fuel-daily ──
        var gunluk = reports.Run(s, "fuel-daily", istek);
        Assert.Equal(14, gunluk.Headers.Count);
        Assert.Equal(2, gunluk.Rows.Count);                        // yalnız FİŞ OLAN günler: 1. ve 3. gün (2. gün YOK)
        Assert.All(gunluk.Rows, r => Assert.Equal("V1", (string)r[2]!));   // fişsiz V2 hiç listelenmez

        var g1 = gunluk.Rows.First();
        Assert.Equal(2.0, Deger(g1[7]), 3);                        // 1. gün iki fiş — gün BAŞI sınırı dahil
        Assert.Equal(300.0, Deger(g1[8]), 3);
        Assert.Equal(150.0, Deger(g1[9]), 3);
        Assert.Equal(6200.0, Deger(g1[12]), 3);

        var g3 = gunluk.Rows.Last();
        Assert.Equal(1.0, Deger(g3[7]), 3);                        // gün SONU 23:59:59.999 sınırı dahil
        Assert.Equal(1000.0, Deger(g3[12]), 3);

        // ── günlük ≡ dönem (fuel) ──
        var donem = reports.Run(s, "fuel", istek);
        Assert.Single(donem.Rows);                                 // ADR-182: yalnız fişi olan araç (V2 yok)
        Assert.Equal(Deger(donem.Rows[0][8]), gunluk.Rows.Sum(r => Deger(r[9])), 3);    // litre
        Assert.Equal(Deger(donem.Rows[0][11]), gunluk.Rows.Sum(r => Deger(r[12])), 3);  // maliyet
        Assert.Equal(Deger(donem.TotalRow![8]), Deger(gunluk.TotalRow![9]), 3);

        // ── stock-movements-daily ──
        Exec("INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
             "VALUES('M1','A','MK1','Çimento',NULL,'0',@n,@n,1,0);", ("@n", Day(0)));

        void Hareket(string id, string tur, int yon, string miktar, long tarih) =>
            Exec(@"INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at)
                   VALUES(@id,'A','M1','B1',@t,@y,@q,@op,'',@n);",
                ("@id", id), ("@t", tur), ("@y", yon), ("@q", miktar), ("@op", "op-" + id), ("@n", tarih));

        Hareket("h1", "in", 1, "10.5", Day(1));
        Hareket("h2", "in", 1, "4.5", Day(1) + G - 1);             // gün SONU sınırı → AYNI güne düşer
        Hareket("h3", "out", -1, "3", Day(1));
        Hareket("h4", "transfer", 1, "2", Day(3));
        Hareket("h5", "transfer", -1, "2", Day(3));

        var stok = reports.Run(s, "stock-movements-daily", istek);
        Assert.Equal(5, stok.Headers.Count);
        Assert.Equal(3, stok.Rows.Count);                          // (1.gün Giriş) (1.gün Çıkış) (3.gün Transfer)

        var giris = stok.Rows.First(r => (string)r[1]! == "Giriş");
        Assert.Equal(2.0, Deger(giris[2]), 3);                     // iki hareket — gün sınırı dahil
        Assert.Equal(15.0, Deger(giris[3]), 3);                    // 10,5 + 4,5 → numeric ile TAM
        Assert.Equal("15", Goster(giris[3]));                      // kayan nokta artığı YOK

        var transfer = stok.Rows.First(r => (string)r[1]! == "Transfer");
        Assert.Equal(2.0, Deger(transfer[3]), 3);                  // giriş bacağı
        Assert.Equal(2.0, Deger(transfer[4]), 3);                  // çıkış bacağı

        // Detay rapor DEĞİŞMEDİ (regresyon): 5 hareket satır-satır.
        Assert.Equal(5, reports.Run(s, "stock-movements", istek).Rows.Count);

        // ── daily-activity (ADR-182 · S4): kayıt tipi eşlemesi ve çoklu seçim PG'de de aynı ──
        Exec("INSERT INTO personnel(id,company_id,full_name,created_at,updated_at,version,is_deleted) " +
             "VALUES('p1','A','Ali Usta',@n,@n,1,0);", ("@n", Day(0)));

        void Faaliyet(string id, string tip, string? kind, long tarih, string aciklama, int? gun) =>
            Exec(@"INSERT INTO daily_activities(id,company_id,activity_type,movement_kind,vehicle_id,from_location_id,to_location_id,
                       operator_id,duration_days,description,source_module,stock_processed,activity_date,operation_id,op_branch_id,
                       created_at,updated_at,version,is_deleted)
                   VALUES(@id,'A',@t,@k,'v1','B1',NULL,'p1',@g,@a,'daily_activity',0,@d,@op,'B1',@n,@n,1,0);",
                ("@id", id), ("@t", tip), ("@k", (object?)kind), ("@g", (object?)gun), ("@a", aciklama),
                ("@d", tarih), ("@op", "op-" + id), ("@n", Day(0)));

        Faaliyet("d1", "maintenance", null, Day(1), "Bakım", 2);
        Faaliyet("d2", "movement", "movement", Day(2), "Sahaya sevk", null);
        Faaliyet("d3", "movement", "transfer", Day(3), "Transfer", 1);

        var hepsi = reports.Run(s, "daily-activity", istek);                       // tip seçilmedi → TÜM tipler
        Assert.Equal(8, hepsi.Headers.Count);
        Assert.Equal(3, hepsi.Rows.Count);
        Assert.Equal("Transfer", (string)hepsi.Rows[0][1]!);                       // en yeni gün üstte

        var yalnizHareket = reports.Run(s, "daily-activity",
            new ReportRequest(true, Day(1), Day(3) + G - 1, ActivityTypes: new[] { "movement" }));
        Assert.Single(yalnizHareket.Rows);                                          // transfer AYRIŞTI
        Assert.Equal("Hareket", (string)yalnizHareket.Rows[0][1]!);

        var ikili = reports.Run(s, "daily-activity",
            new ReportRequest(true, Day(1), Day(3) + G - 1, ActivityTypes: new[] { "maintenance", "transfer" }));
        Assert.Equal(2, ikili.Rows.Count);
        Assert.Equal(3.0, Deger(ikili.TotalRow![6]), 3);                            // süre toplamı 2 + 1
    }

    private static double Deger(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => Convert.ToDouble(v),
    };

    private static string Goster(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };
}
