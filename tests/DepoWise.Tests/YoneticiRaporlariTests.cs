using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-57 (AR-GE 2026-10-25, F-091) — YÖNETİCİ KARAR RAPORLARI ═══
///  YR1 — katalog: üç rapor, doğru kategori / filtre / veri evi
///  YR2 — 🔴 Eksi Stok: yalnız eksi (malzeme × depo) bakiyeler, en eksi önce, son çıkış türü; başka firma GELMEZ
///  YR3 — 🔴 Eksi Stok: şube kapsamı dışındaki depo görünmez (Stok Durumu ile aynı kural)
///  YR4 — 🔴 Ay Sonu Stok Değeri: geçmiş tarihteki miktar defterden; değer = miktar × kart fiyatı; toplam
///  YR5 — 🔴 Ay Sonu Stok Değeri: bitiş = bugün iken miktar Stok Durumu ile AYNI (defter = bakiye)
///  YR6 — 🔴 Ay Sonu Stok Değeri: birim fiyat gizliyse fiyat ve değer kolonları tabloda YOK
///  YR7 — Makine Kullanım Oranı: çalışılan gün (yakıt ∪ sayaç artışı, aynı gün tek), oran, sayaç artışı, arıza duruşu;
///        hiç çalışmayan makine %0 ile listede; dönem dışı ve sayaç düşüşü sayılmaz
///  YR8 — Makine Kullanım Oranı: iş emri görme yetkisi yoksa arıza kolonları tabloda YOK
/// </summary>
public class YoneticiRaporlariTests : IDisposable
{
    private const long GunMs = 86_400_000L;
    private const long Simdi = 1_700_000_000_000;   // 2023-11-14 22:13 UTC
    private static readonly long Bugun = Simdi / GunMs;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly StockService _stock;
    private readonly ReportService _reports;
    private readonly SessionContext _s;
    private readonly TestClock _clock = new();
    private readonly string _depoA, _depoB, _m1, _m2, _m3, _aracA, _aracB;

    public YoneticiRaporlariTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_yonrpr_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('A','A',1,1,1,0);");
        Exec("INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('B','B',1,1,1,0);");
        _stock = new StockService(_factory, _clock);
        _reports = new ReportService(_factory, _clock);
        var uid = new UserService(_factory, _clock).EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _s = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var br = new BranchService(_factory, _clock);
        _depoA = br.Create(_s, new NewBranch("Merkez Depo"));
        _depoB = br.Create(_s, new NewBranch("Şantiye B"));
        var mats = new MaterialService(_factory, _clock);
        _m1 = mats.Create(_s, new NewMaterial("YF-1", "Yağ filtresi"));
        _m2 = mats.Create(_s, new NewMaterial("HF-2", "Hava filtresi"));
        _m3 = mats.Create(_s, new NewMaterial("ELD-3", "Eldiven"));
        Exec($"UPDATE materials SET unit_price='12.5' WHERE id='{_m1}';");
        Exec($"UPDATE materials SET unit_price='4' WHERE id='{_m2}';");

        var vs = new VehicleService(_factory, _clock);
        _aracA = vs.Create(_s, new NewVehicle("EKS-01", "34EKS01", 2020, 1000m, "hour", _depoA));
        _aracB = vs.Create(_s, new NewVehicle("EKS-02", "34EKS02", 2021, 500m, "hour", _depoA));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Simdi);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private void Exec(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>K-05 istisnası gibi defter satırı: bakım tüketimi (çıkış yönlü) — bakiye yeniden hesaplanır.</summary>
    private void HamTuketim(string company, string mat, string? depo, decimal miktar, long t, string not = "bakım")
    {
        Exec("INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction,quantity,operation_id,note,created_at) " +
             $"VALUES('{Guid.NewGuid():N}','{company}','{mat}',{(depo is null ? "NULL" : "'" + depo + "'")},'usage',-1,'{miktar.ToString(System.Globalization.CultureInfo.InvariantCulture)}','{Op()}','{not}',{t});");
        _stock.RecomputeBalances(company);
    }

    private static int K(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        throw new Xunit.Sdk.XunitException("Sütun yok: " + baslik);
    }

    private static string Metin(object? o) => o switch { NumCell n => n.Display, null => "", _ => o.ToString() ?? "" };

    private TableModel Calistir(SessionContext s, string key, long? bas = null, long? son = null, IReadOnlyList<string>? depolar = null)
        => _reports.Run(s, key, new ReportRequest(true, bas, son, LocationIds: depolar), ReportLimits.DefaultMaxRows);

    [Fact]
    public void YR1_Katalog()
    {
        var eksi = ReportCatalog.ByKey("stock-negative")!;
        Assert.Equal(ReportCategory.Stock, eksi.Category);
        Assert.True(eksi.UsesLocation);
        Assert.False(eksi.RequiresDate);
        Assert.Equal("stock", eksi.DataModule);
        var deger = ReportCatalog.ByKey("stock-value-month")!;
        Assert.Equal(ReportCategory.Stock, deger.Category);
        Assert.True(deger.UsesDate && deger.RequiresDate && deger.UsesLocation);
        Assert.Equal("stock", deger.DataModule);
        var makine = ReportCatalog.ByKey("machine-utilization")!;
        Assert.Equal(ReportCategory.Vehicle, makine.Category);
        Assert.True(makine.RequiresDate && makine.UsesBranch && makine.UsesVehicle && makine.UsesVehicleType);
        Assert.Equal("vehicles", makine.DataModule);
    }

    private void EksiStokHazirla()
    {
        _stock.ReceiveIn(_s, new[] { new StockLine(_m1, 5m, 12.5m) }, Op(), _depoA, docDate: Simdi - 5 * GunMs);
        HamTuketim("A", _m1, _depoA, 8m, Simdi - 2 * GunMs, "bakım-1");            // A: -3
        _stock.ReceiveIn(_s, new[] { new StockLine(_m2, 10m, 4m) }, Op(), _depoB, docDate: Simdi - 5 * GunMs);
        HamTuketim("A", _m2, _depoB, 12m, Simdi - GunMs, "bakım-2");               // B: -2
        _stock.ReceiveIn(_s, new[] { new StockLine(_m3, 10m, 1m) }, Op(), _depoA, docDate: Simdi - 5 * GunMs);   // pozitif
        // başka firmanın eksi bakiyesi
        Exec("INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) VALUES('mb','B','YAB-1','Yabancı',1,1,1,0);");
        HamTuketim("B", "mb", null, 7m, Simdi - GunMs);
    }

    [Fact]
    public void YR2_EksiStok_Yalniz_Eksi_Bakiyeler()
    {
        EksiStokHazirla();
        var t = Calistir(_s, "stock-negative");
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("YF-1", t.Rows[0][K(t, "Kod")]);                         // en eksi önce
        Assert.Equal("-3", Metin(t.Rows[0][K(t, "Stok")]));
        Assert.Equal("Merkez Depo", t.Rows[0][K(t, "Depo / Şantiye")]);
        Assert.Equal("bakım-1", t.Rows[0][K(t, "Son Çıkış Notu")]);
        Assert.Equal(DepoWise.Application.Ui.MovementTypeOptions.Label("usage"), t.Rows[0][K(t, "Son Çıkış Türü")]);
        Assert.Equal("HF-2", t.Rows[1][K(t, "Kod")]);
        Assert.Equal("Şantiye B", t.Rows[1][K(t, "Depo / Şantiye")]);
        Assert.DoesNotContain(t.Rows, r => (string?)r[K(t, "Kod")] is "ELD-3" or "YAB-1");
    }

    [Fact]
    public void YR3_EksiStok_Sube_Kapsami()
    {
        EksiStokHazirla();
        var kisitli = new SessionContext(_s.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { ScopeBranchIds = new[] { _depoA } };
        var t = Calistir(kisitli, "stock-negative");
        Assert.Single(t.Rows);
        Assert.Equal("YF-1", t.Rows[0][K(t, "Kod")]);
        // kapsam dışı depoyu açıkça istemek de boş döner (filtre sessizce kalkmaz)
        Assert.Empty(Calistir(kisitli, "stock-negative", depolar: new[] { _depoB }).Rows);
        // depo filtresi: yalnız seçilen depo
        var b = Calistir(_s, "stock-negative", depolar: new[] { _depoB });
        Assert.Equal("HF-2", Assert.Single(b.Rows)[K(b, "Kod")]);
    }

    private void DegerHazirla()
    {
        _stock.ReceiveIn(_s, new[] { new StockLine(_m1, 10m, 12.5m) }, Op(), _depoA, docDate: Simdi - 40 * GunMs);
        _stock.IssueOut(_s, new[] { new StockLine(_m1, 4m, 12.5m) }, Op(), _depoA, docDate: Simdi - 35 * GunMs);
        _stock.ReceiveIn(_s, new[] { new StockLine(_m1, 5m, 12.5m) }, Op(), _depoA, docDate: Simdi - 10 * GunMs);
        _stock.ReceiveIn(_s, new[] { new StockLine(_m2, 3m, 4m) }, Op(), _depoB, docDate: Simdi - 10 * GunMs);
    }

    [Fact]
    public void YR4_AySonu_Gecmis_Tarih_Defterden()
    {
        DegerHazirla();
        var t = Calistir(_s, "stock-value-month", Simdi - 60 * GunMs, Simdi - 20 * GunMs);
        var satir = Assert.Single(t.Rows);                                     // HF-2 o tarihte yoktu
        Assert.Equal("YF-1", satir[K(t, "Kod")]);
        Assert.Equal("6", Metin(satir[K(t, "Stok")]));                          // 10 − 4 (sonraki +5 girmez)
        Assert.Equal(75d, ((NumCell)satir[K(t, "Stok Değeri")]!).Value);         // 6 × 12,5
        Assert.Equal(75d, ((NumCell)t.TotalRow![K(t, "Stok Değeri")]!).Value);
        Assert.Equal("TRY", t.TotalRow![K(t, "Para Birimi")]);

        var bugun = Calistir(_s, "stock-value-month", Simdi - 60 * GunMs, Simdi);
        Assert.Equal(11d * 12.5 + 3 * 4, ((NumCell)bugun.TotalRow![K(bugun, "Stok Değeri")]!).Value);
        // depo filtresi
        var b = Calistir(_s, "stock-value-month", Simdi - 60 * GunMs, Simdi, new[] { _depoB });
        Assert.Equal("HF-2", Assert.Single(b.Rows)[K(b, "Kod")]);
    }

    [Fact]
    public void YR5_AySonu_Bugun_StokDurumu_Ile_Ayni()
    {
        DegerHazirla();
        EksiStokHazirla();   // eksi bakiye ve ham tüketim de defterde
        var deger = Calistir(_s, "stock-value-month", Simdi - 60 * GunMs, Simdi);
        var durum = Calistir(_s, "stock");
        foreach (var r in durum.Rows)
        {
            var kod = (string)r[0]!;
            var beklenen = Money.Parse(Metin(r[2]));
            var bulunan = deger.Rows.FirstOrDefault(x => (string?)x[0] == kod);
            if (beklenen == 0m) { Assert.Null(bulunan); continue; }
            Assert.NotNull(bulunan);
            Assert.Equal((double)beklenen, ((NumCell)bulunan![K(deger, "Stok")]!).Value);
        }
    }

    [Fact]
    public void YR6_AySonu_Fiyat_Gizliyse_Kolonlar_Yok()
    {
        DegerHazirla();
        static ModulePermission Gor(string m) => new(m, true, false, false, false);
        var personel = new SessionContext("u-per", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { Gor("reports"), Gor("report_stock"), Gor("stock"), Gor("materials") }))
        {
            ProtectedFields = new HashSet<string> { FieldAccess.ProtectionKey(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice) },
        };
        var t = Calistir(personel, "stock-value-month", Simdi - 60 * GunMs, Simdi);
        Assert.DoesNotContain("Birim Fiyat", t.Headers);
        Assert.DoesNotContain("Stok Değeri", t.Headers);
        Assert.Equal(t.Headers.Count, t.Rows[0].Count);
        Assert.Equal(t.Headers.Count, t.TotalRow!.Count);
        // koruma yokken aynı kullanıcı görür
        personel.ProtectedFields = new HashSet<string>();
        Assert.Contains("Stok Değeri", Calistir(personel, "stock-value-month", Simdi - 60 * GunMs, Simdi).Headers);
    }

    private void MakineHazirla()
    {
        // A aracı: bugün yakıt fişi (+ fişin yazdığı sayaç kaydı, aynı gün → tek gün)
        var yakit = new FuelService(_factory, _clock);
        yakit.AddDepotEntry(_s, new NewDepotEntry(500m, 40m, EntryDate: Simdi - 30 * GunMs), Op());
        yakit.Distribute(_s, new NewDistribution(_aracA, 50m, 1100m, 40m, DistributionDate: Simdi - 3_600_000), Op());
        void Sayac(string v, decimal eski, decimal yeni, long t)
            => Exec("INSERT INTO vehicle_meter_logs(id,company_id,vehicle_id,old_value,new_value,source,created_at) " +
                    $"VALUES('{Guid.NewGuid():N}','A','{v}','{eski}','{yeni}','vehicle_form',{t});");
        Sayac(_aracA, 1100, 1120, (Bugun - 5) * GunMs + 1000);      // çalışılan 2. gün, +20
        Sayac(_aracA, 1120, 1110, (Bugun - 3) * GunMs + 1000);      // sayaç düşüşü → sayılmaz
        Sayac(_aracA, 900, 990, (Bugun - 20) * GunMs);              // dönem dışı → sayılmaz
        Exec("INSERT INTO vehicle_meter_logs(id,company_id,vehicle_id,old_value,new_value,source,created_at) " +
             $"VALUES('{Guid.NewGuid():N}','A','{_aracB}','500','800','recalc:fuel',{(Bugun - 2) * GunMs});");   // yeniden hesaplama → çalışma değil
        // (araç kaydının yazdığı "0 → başlangıç sayacı" satırı da [vehicle_create] çalışma sayılmaz)
        // arıza: 6 saat, tamamlandı
        Exec("INSERT INTO work_orders(id,company_id,wo_no,title,status,priority,created_by,created_at,updated_at,version,is_deleted,fault_vehicle_id,actual_end) " +
             $"VALUES('wo1','A','A-1','Arıza','completed','normal','u',{Simdi - 10 * 3_600_000L},{Simdi},1,0,'{_aracA}',{Simdi - 4 * 3_600_000L});");
    }

    [Fact]
    public void YR7_Makine_Kullanim_Orani()
    {
        MakineHazirla();
        var t = Calistir(_s, "machine-utilization", (Bugun - 9) * GunMs, Simdi);
        Assert.Equal(2, t.Rows.Count);                                          // hiç çalışmayan B de listede
        var a = t.Rows.Single(r => (string?)r[K(t, "Araç İç Kod")] == "EKS-01");
        Assert.Equal(10d, ((NumCell)a[K(t, "Dönem Günü")]!).Value);
        Assert.Equal(2d, ((NumCell)a[K(t, "Çalışılan Gün")]!).Value);
        Assert.Equal(20d, ((NumCell)a[K(t, "Kullanım Oranı")]!).Value);
        Assert.Equal(120d, ((NumCell)a[K(t, "Sayaç Artışı")]!).Value);         // fiş +100, form +20
        Assert.Equal(1d, ((NumCell)a[K(t, "Arıza Sayısı")]!).Value);
        Assert.Equal(6d, ((NumCell)a[K(t, "Arıza Duruşu (Saat)")]!).Value);
        var b = t.Rows.Single(r => (string?)r[K(t, "Araç İç Kod")] == "EKS-02");
        Assert.Equal(0d, ((NumCell)b[K(t, "Kullanım Oranı")]!).Value);
        Assert.Equal(10d, ((NumCell)t.TotalRow![K(t, "Kullanım Oranı")]!).Value);   // 2 ÷ (10 × 2)
    }

    [Fact]
    public void YR8_Makine_IsEmri_Yetkisi_Yoksa_Ariza_Kolonlari_Yok()
    {
        MakineHazirla();
        static ModulePermission Gor(string m) => new(m, true, false, false, false);
        var personel = new SessionContext("u-per", "A", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { Gor("reports"), Gor("report_vehicle"), Gor("vehicles") }));
        var t = Calistir(personel, "machine-utilization", (Bugun - 9) * GunMs, Simdi);
        Assert.DoesNotContain("Arıza Sayısı", t.Headers);
        Assert.DoesNotContain("Arıza Duruşu (Saat)", t.Headers);
        Assert.Equal(t.Headers.Count, t.Rows[0].Count);
        Assert.Contains("Kullanım Oranı", t.Headers);
    }
}
