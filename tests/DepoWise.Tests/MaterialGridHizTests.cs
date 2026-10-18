using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-22 / F-178 (AR-GE 2026-10-18) — MALZEME LİSTESİ HIZLI YOLU: SONUÇ BİREBİR AYNI ═══
///
/// Liste sayfası artık iki aşamalı (önce sayfa kimlikleri, türetilmiş kolonlar yalnız o satırlar için). Bu testler her
/// filtre/sıralama/sayfa birleşiminde sayfanın, DEĞİŞMEMİŞ tam liste yolunun (<c>SearchGridAll</c>, tek sorgu) aynı dilimiyle
/// ve toplam sayının tam listenin uzunluğuyla birebir aynı olduğunu kanıtlar.
///  MGH1 — SQLite: kart kolonu filtreleri (hızlı yol) + türetilmiş kolon filtreleri (genel yol) + "yalnız kritik" + sıralamalar
///  MGH2 — PostgreSQL: aynı karşılaştırma (üretim API'si PG'dedir)
///  ölçüm (300 bin, bu bilgisayar): ilk sayfa 1.598 → 86 ms, son sayfa 2.215 → 159 ms, ada göre sırala 2.236 → 190 ms,
///  ad ara 1.781 → 402 ms, stok filtresi 1.628 → 605 ms, yalnız kritik 1.409 → 653 ms (docs/arge/10)
/// </summary>
public class MaterialGridHizTests : IDisposable
{
    private const string Co = "MGH";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mgh_" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" }) try { File.Delete(p); } catch { }
    }

    [Fact]
    public void MGH1_Sqlite_Her_Sayfa_Tam_Listenin_Dilimi() => Karsilastir(new SqliteConnectionFactory(_dbPath), sifirla: null);

    /// <summary>Çeşitli veri: Türkçe harfler, boş tür/açıklama, iki depoda stok, eksi/sıfır/eksik bakiye, silinmiş kayıt,
    /// başka firmanın aynı kodlu kaydı, uyumlu araç ve muadil.</summary>
    internal static SessionContext Tohumla(IDbConnectionFactory f)
    {
        void Exec(string sql)
        {
            using var c = f.Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
        }
        foreach (var co in new[] { Co, "MGH-B" })
            Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{co}','{co}',1,1,1,0) ON CONFLICT(id) DO NOTHING;");
        var uid = new UserService(f).EnsureInitialAdmin(Co, "mgh_admin", "admin123", RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var sube = new DepoWise.Infrastructure.Organization.BranchService(f).Create(s, new DepoWise.Infrastructure.Organization.NewBranch("Depo 2"));
        for (int i = 0; i < 4; i++)
        {
            Exec($"INSERT INTO units(id,company_id,name,created_at,updated_at) VALUES('mgh-u{i}','{Co}','Birim{i}',1,1);");
            Exec($"INSERT INTO material_categories(id,company_id,name,created_at,updated_at) VALUES('mgh-c{i}','{Co}','Kategori {(i % 2 == 0 ? "Çelik" : "Iğdır")} {i}',1,1);");
        }
        string[] ad = { "Rulman", "rulman büyük", "Şaft", "ışık", "İstasyon", "Çınar", "Filtre", "filtre küçük", "Yağ", "Iğdır pimi" };
        var r = new Random(1018);
        using (var c = f.Create())
        using (var tx = c.BeginTransaction())
        {
            for (int i = 0; i < 160; i++)
            {
                foreach (var co in i % 9 == 0 ? new[] { Co, "MGH-B" } : new[] { Co })
                {
                    var id = $"{co}-m{i:D4}";
                    using var cmd = c.CreateCommand(); cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO materials(id,company_id,code,name,type,category_id,unit_id,min_stock,unit_price,currency_code,description,created_at,updated_at,version,is_deleted) " +
                                      "VALUES(@id,@co,@code,@name,@type,@cat,@unit,@min,@price,@cur,@desc,@ts,@ts,1,@del);";
                    cmd.AddWithValue("@id", id); cmd.AddWithValue("@co", co);
                    cmd.AddWithValue("@code", $"MLZ-{(i * 7) % 160:D4}");
                    cmd.AddWithValue("@name", ad[r.Next(ad.Length)] + (i % 5 == 0 ? "" : " " + r.Next(100)));
                    cmd.AddWithValue("@type", i % 4 == 0 ? DBNull.Value : (i % 3 == 0 ? "Sarf" : "Yedek Parça"));
                    cmd.AddWithValue("@cat", i % 6 == 0 ? DBNull.Value : $"mgh-c{i % 4}");
                    cmd.AddWithValue("@unit", $"mgh-u{i % 4}");
                    cmd.AddWithValue("@min", (i % 7).ToString());
                    cmd.AddWithValue("@price", (i * 13 % 500 / 10m).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    cmd.AddWithValue("@cur", i % 10 == 0 ? "USD" : "TRY");
                    cmd.AddWithValue("@desc", i % 3 == 0 ? DBNull.Value : (i % 11 == 0 ? "özel not" : "açıklama"));
                    cmd.AddWithValue("@ts", 1_700_000_000_000L + i);
                    cmd.AddWithValue("@del", i % 17 == 0 ? 1 : 0);
                    cmd.ExecuteNonQuery();
                    if (i % 8 != 0)   // bazılarının bakiyesi hiç yok
                    {
                        using var b = c.CreateCommand(); b.Transaction = tx;
                        b.CommandText = "INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES(@co,@id,'',@q,1);";
                        b.AddWithValue("@co", co); b.AddWithValue("@id", id); b.AddWithValue("@q", (r.Next(-3, 12)).ToString());
                        b.ExecuteNonQuery();
                    }
                    if (i % 5 == 0 && co == Co)   // ikinci depo
                    {
                        using var b = c.CreateCommand(); b.Transaction = tx;
                        b.CommandText = "INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES(@co,@id,@loc,'2.5',1);";
                        b.AddWithValue("@co", co); b.AddWithValue("@id", id); b.AddWithValue("@loc", sube);
                        b.ExecuteNonQuery();
                    }
                }
            }
            tx.Commit();
        }
        var veh = new VehicleService(f);
        var mats = new MaterialService(f);
        var a1 = veh.Create(s, new NewVehicle("ARC-1", Plate: "34 MGH 1"));
        var a2 = veh.Create(s, new NewVehicle("ARC-2"));
        mats.SetCompatibleVehicles(s, $"{Co}-m0003", new[] { a1, a2 });
        mats.SetCompatibleVehicles(s, $"{Co}-m0010", new[] { a2 });
        mats.AddEquivalent(s, $"{Co}-m0004", $"{Co}-m0006");
        return s;
    }

    internal static void Karsilastir(IDbConnectionFactory f, Action<IDbConnectionFactory>? sifirla)
    {
        sifirla?.Invoke(f);
        new MigrationRunner(f).Run();
        var s = Tohumla(f);
        var svc = new MaterialService(f);
        var bos = new MaterialGridFilter();
        var filtreler = new (string Ad, MaterialGridFilter F, bool Kritik)[]
        {
            ("filtresiz", bos, false),
            ("ad rulman", bos with { Name = "rulman" }, false),
            ("ad ı", bos with { Name = "ı" }, false),
            ("ad İ", bos with { Name = "İ" }, false),
            ("kod 1", bos with { Code = "1" }, false),
            ("tür sarf", bos with { Type = "sarf" }, false),
            ("açıklama not", bos with { Description = "not" }, false),
            ("para usd", bos with { Currency = "usd" }, false),
            ("ad+kod", bos with { Name = "filtre", Code = "0" }, false),
            ("stok 5", bos with { Stock = "5" }, false),
            ("stok >3", bos with { Stock = ">3" }, false),
            ("durum düşük", bos with { Status = "düşük" }, false),
            ("kategori çelik", bos with { Category = "çelik" }, false),
            ("uyumlu ARC", bos with { CompatibleVehicles = "ARC" }, false),
            ("muadil MLZ", bos with { Equivalents = "MLZ" }, false),
            ("fiyat 1-20", bos with { UnitPrice = "1-20" }, false),
            ("kritik", bos, true),
            ("kritik+ad", bos with { Name = "a" }, true),
        };
        var siralar = new (string? Kolon, bool Ters)[]
        {
            (null, false), (MaterialListColumns.Name, false), (MaterialListColumns.Name, true), (MaterialListColumns.Code, true),
            (MaterialListColumns.Stock, false), (MaterialListColumns.Status, true), (MaterialListColumns.Category, false),
        };
        int karsilastirma = 0;
        foreach (var (adi, flt, kritik) in filtreler)
            foreach (var (kolon, ters) in siralar)
            {
                var tum = svc.SearchGridAll(s, flt, kolon, ters, kritik);
                foreach (var boyut in new[] { 7, 25 })
                {
                    int sayfaSayisi = Math.Max(1, (int)Math.Ceiling(tum.Count / (double)boyut));
                    for (int p = 1; p <= sayfaSayisi + 1; p++)   // son sayfadan bir ileri de (boş sayfa, doğru toplam)
                    {
                        var g = svc.SearchGrid(s, flt, p, boyut, kolon, ters, kritik);
                        var beklenen = tum.Skip((p - 1) * boyut).Take(boyut).ToList();
                        var etiket = $"[{adi} | sıra {kolon ?? "-"}{(ters ? " ters" : "")} | boyut {boyut} | sayfa {p}]";
                        Assert.True(tum.Count == g.TotalCount, $"{etiket} toplam {g.TotalCount}, beklenen {tum.Count}");
                        Assert.True(beklenen.SequenceEqual(g.Items), $"{etiket} satırlar farklı: " +
                            string.Join(",", g.Items.Select(x => x.Code)) + " ≠ " + string.Join(",", beklenen.Select(x => x.Code)));
                        karsilastirma++;
                    }
                }
            }
        Assert.True(karsilastirma > 1000, "yeterli karşılaştırma yapılmadı: " + karsilastirma);
        // Başka firmanın kaydı hiçbir yolda görünmez
        Assert.DoesNotContain(svc.SearchGridAll(s, bos), x => x.Id.StartsWith("MGH-B-", StringComparison.Ordinal));
        Assert.DoesNotContain(svc.SearchGrid(s, bos, 1, 500).Items, x => x.Id.StartsWith("MGH-B-", StringComparison.Ordinal));
    }
}

[Collection("PostgresSchema")]
public class MaterialGridHizPgTests
{
    [SkippableFact]
    public void MGH2_Postgres_Her_Sayfa_Tam_Listenin_Dilimi()
    {
        PostgresTestGuard.SkipUnlessSafe();
        var f = new PostgresMigrationTests.NpgsqlTestFactory(PostgresTestGuard.Url!);
        MaterialGridHizTests.Karsilastir(f, PostgresTestGuard.ResetSchema);
    }
}
