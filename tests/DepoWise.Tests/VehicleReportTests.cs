using System.Data.Common;
using DepoWise.Application.Common;
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
/// Araç Raporu (2026-08-07 — "Genel Rapor"un yerine) hesaplama doğruluğu. Veri doğrudan SQL ile seed edilir
/// (deterministik). Senaryolar: KM aracı, SAAT iş makinesi, yakıtsız/bakımsız/yalnız-stok-çıkışı araç, toplam
/// satırı, araç/tür/şube filtreleri, tarih dışı maliyet elenmesi. Tek-geçiş SQL (N+1 yok) çıktısı test edilir.
/// </summary>
public class VehicleReportTests : IDisposable
{
    private const long Base = 1_700_000_000_000;   // seed tarih tabanı
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly ReportService _reports;
    private readonly MaterialService _materials;
    private readonly SessionContext _admin;
    private readonly string _mat;

    public VehicleReportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_vehrep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        _materials = new MaterialService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _mat = _materials.Create(_admin, new NewMaterial("MAT1", "Parça"));
        Seed();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Base);
    }

    // Tüm seedi tek yerde: 5 araç + tür/şube/marka/model + maliyet kaynakları.
    private void Seed()
    {
        // Parents (FK için)
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Base));
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B2','A','Sahra',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_types(id,company_id,name,created_at,updated_at) VALUES('T1','A','Kamyon',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_types(id,company_id,name,created_at,updated_at) VALUES('T2','A','Is Makinesi',@n,@n);", ("@n", Base));
        Exec("INSERT INTO brands(id,company_id,name,created_at,updated_at) VALUES('BR1','A','Ford',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_models(id,company_id,brand_id,name,created_at,updated_at) VALUES('MD1','A','BR1','Cargo',@n,@n);", ("@n", Base));
        Exec("INSERT INTO maintenance_definitions(id,company_id,name,created_at,updated_at) VALUES('DEF1','A','Periyodik',@n,@n);", ("@n", Base));

        // Araçlar
        Veh("v1", "V1", "34ABC01", "km", "B1", "T1", "BR1", "MD1");
        Veh("v2", "V2", null, "hour", "B2", "T2", null, null);
        Veh("v3", "V3", null, "km", "B2", "T2", null, null);
        Veh("v4", "V4", null, "km", "B2", "T2", null, null);
        Veh("v5", "V5", null, "km", "B2", "T2", null, null);

        // V1 (KM): 2 yakıt fişi → km=300, litre=150, tutar=6200 ; bakım malzeme=400 ; doğrudan parça=150
        Fuel("f1", "v1", "1000", "1200", "100", "40");
        Fuel("f2", "v1", "1200", "1300", "50", "44");
        Maint("m1", "v1", new[] { ("2", "150"), ("1", "100") });   // 300 + 100 = 400
        Issue("d1", "v1", "3", "50");                              // 150

        // V2 (SAAT): 1 yakıt fişi → saat=60, litre=80, tutar=3600
        Fuel("f3", "v2", "500", "560", "80", "45");

        // V3: yalnız doğrudan stok çıkışı → parça=100
        Issue("d3", "v3", "5", "20");

        // V4: yalnız bakım malzemesi → 500
        Maint("m4", "v4", new[] { ("1", "500") });

        // V5: hiç maliyet yok (tam filo görünürlüğü testi)

        // V1'e TARİH DIŞI bir yakıt fişi (uzak gelecek) — geçerli aralıkta ELENMELİ
        FuelAt("fx", "v1", "9000", "9999", "999", "99", Base + 500_000_000_000L);
    }

    // ── Ana senaryo ──
    [Fact]
    public void AracBazinda_TumMaliyetler_DogruHesaplanir()
    {
        var t = Run();
        Assert.Equal("Araç Raporu", t.Title);
        Assert.Equal(14, t.Headers.Count);
        Assert.Equal(5, t.Rows.Count);                // 5 araç (TOPLAM artık ayrı TotalRow'da, satırlarda değil)
        Assert.NotNull(t.Numeric);
        Assert.NotNull(t.TotalRow);

        var v1 = Row(t, "V1");
        Assert.Equal("34ABC01", (string)v1[1]!);
        Assert.Equal("Ford Cargo", (string)v1[2]!);   // marka + model
        Assert.Equal("Merkez", (string)v1[3]!);
        Assert.Equal("KM", (string)v1[4]!);
        Assert.Equal(300.0, D(v1[5]), 3);             // dönem mesafe (tarih dışı fiş elendi) — HAM değer
        Assert.Equal(150.0, D(v1[6]), 3);             // litre
        Assert.Equal(6200.0 / 150.0, D(v1[7]), 4);    // ort. yakıt fiyatı HAM (yuvarlanmaz; görüntü 2 hane)
        Assert.Equal(6200.0, D(v1[8]), 3);            // yakıt maliyeti
        Assert.Equal(0.5, D(v1[9]), 3);               // ort. tüketim L/km (150/300)
        Assert.Equal(400.0, D(v1[10]), 3);            // bakım malzeme
        Assert.Equal(150.0, D(v1[11]), 3);            // doğrudan parça
        Assert.Equal(6750.0, D(v1[12]), 3);           // toplam
        Assert.Equal(6750.0 / 300.0, D(v1[13]), 6);   // ₺/km
    }

    [Fact]
    public void GorunumBicimi_HamDegerdenAyri()
    {
        var v1 = Row(Run(), "V1");
        // Görüntü biçimli (₺/L/km); HAM değer sayısal → ikisi AYRI (kullanıcı isteği: değer korunur).
        Assert.Equal("6.200,00", Disp(v1[8]).Replace("₺", "").Trim());   // yakıt maliyeti görüntü
        Assert.Equal("150,00 L", Disp(v1[6]));                            // litre görüntü
        Assert.Equal("300 km", Disp(v1[5]));                             // mesafe görüntü (km)
        Assert.EndsWith("/km", Disp(v1[13]));                            // birim maliyet ₺/km
    }

    [Fact]
    public void BosDeger_GoruntudeTire_DegerSifir()
    {
        var v5 = Row(Run(), "V5");   // hiç maliyeti yok
        Assert.Equal("-", Disp(v5[12]));   // görüntüde "-"
        Assert.Equal(0.0, D(v5[12]), 3);   // HAM değer 0 (filtre/sıralama bozulmaz)
    }

    [Fact]
    public void SaatBazliIsMakinesi_HesabiSaatUzerinden_KMDegil()
    {
        var v2 = Row(Run(), "V2");
        Assert.Equal("Saat", (string)v2[4]!);         // sayaç birimi SAAT
        Assert.Equal(60.0, D(v2[5]), 3);              // dönem "mesafe" = saat farkı
        Assert.Equal(80.0 / 60.0, D(v2[9]), 4);       // L/saat HAM (yuvarlanmaz)
        Assert.EndsWith("L/Saat", Disp(v2[9]));       // görüntü birimi SAAT
        Assert.Equal(3600.0, D(v2[12]), 3);           // toplam = yalnız yakıt
        Assert.Equal(60.0, D(v2[13]), 3);             // ₺/saat (3600/60)
        Assert.EndsWith("/Saat", Disp(v2[13]));       // birim maliyet ₺/Saat
    }

    [Fact]
    public void YalnizStokCikisiOlanArac_ParcaMaliyetiGirer_KMSifir()
    {
        var v3 = Row(Run(), "V3");
        Assert.Equal(0.0, D(v3[5]), 3);               // km yok
        Assert.Equal(0.0, D(v3[8]), 3);               // yakıt yok
        Assert.Equal(0.0, D(v3[10]), 3);              // bakım yok
        Assert.Equal(100.0, D(v3[11]), 3);            // doğrudan parça
        Assert.Equal(100.0, D(v3[12]), 3);            // toplam
        Assert.Equal(0.0, D(v3[13]), 3);              // km=0 → ₺/km 0 (sıfıra bölme koruması)
    }

    [Fact]
    public void YalnizBakimMalzemesiOlanArac_ToplamaGirer()
    {
        var v4 = Row(Run(), "V4");
        Assert.Equal(500.0, D(v4[10]), 3);
        Assert.Equal(0.0, D(v4[11]), 3);
        Assert.Equal(500.0, D(v4[12]), 3);
    }

    [Fact]
    public void HicMaliyetiOlmayanArac_YineDeGorunur_SifirlarLa()
    {
        var v5 = Row(Run(), "V5");
        Assert.Equal(0.0, D(v5[12]), 3);              // toplam 0 ama satır VAR (tam filo görünürlüğü)
    }

    [Fact]
    public void ToplamSatiri_ParaVeLitreToplamlari_Dogru()
    {
        var top = Run().TotalRow!;                    // pinned toplam satırı (satırlarda DEĞİL, ayrı)
        Assert.Equal("TOPLAM", (string)top[0]!);
        Assert.Equal(230.0, D(top[6]), 3);            // litre 150+80
        Assert.Equal(9800.0, D(top[8]), 3);           // yakıt 6200+3600
        Assert.Equal(900.0, D(top[10]), 3);           // bakım 400+500
        Assert.Equal(250.0, D(top[11]), 3);           // parça 150+100
        Assert.Equal(10950.0, D(top[12]), 3);         // genel toplam
    }

    // ── Filtreler ──
    [Fact]
    public void AracTuruFiltresi_YalnizSeciliTuru_Getirir()
    {
        var t = _reports.VehicleReport(_admin, new ReportRequest(true, 1, Base + 1, VehicleTypeIds: new[] { "T1" }));
        Assert.Single(t.Rows);   // T1 = yalnız V1 (toplam ayrı TotalRow'da)
        Assert.Equal("V1", (string)t.Rows[0][0]!);
    }

    [Fact]
    public void AracFiltresi_YalnizSeciliArac_Getirir()
    {
        var t = _reports.VehicleReport(_admin, new ReportRequest(true, 1, Base + 1, VehicleIds: new[] { "v2" }));
        Assert.Single(t.Rows);   // yalnız V2
        Assert.Equal("V2", (string)t.Rows[0][0]!);
    }

    [Fact]
    public void SubeFiltresi_YetkiliAdmin_AcikSecim_Uygulanir()
    {
        // Admin yetkili → açık B1 seçimi honor; V1 (B1) gelir, diğerleri (B2) gelmez.
        var t = _reports.VehicleReport(_admin, new ReportRequest(true, 1, Base + 1, BranchIds: new[] { "B1" }));
        Assert.Single(t.Rows);   // yalnız V1 (B1)
        Assert.Equal("V1", (string)t.Rows[0][0]!);
    }

    // ── Yardımcılar ──
    private TableModel Run() => _reports.VehicleReport(_admin, new ReportRequest(true, 1, 2_000_000_000_000L));

    // HAM sayısal değer (NumCell.Value) — sıralama/filtre bunu kullanır.
    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => System.Convert.ToDouble(v),
    };
    // GÖRÜNTÜ metni (NumCell.Display veya ham metin).
    private static string Disp(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };

    private static IReadOnlyList<object?> Row(TableModel t, string code)
        => t.Rows.First(r => (string)r[0]! == code);

    private void Veh(string id, string code, string? plate, string unit, string branch, string type, string? brand, string? model)
        => Exec(@"INSERT INTO vehicles(id,company_id,internal_code,plate,meter_unit,branch_id,vehicle_type_id,brand_id,vehicle_model_id,current_meter,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@code,@plate,@unit,@branch,@type,@brand,@model,'0',@n,@n,1,0);",
            ("@id", id), ("@code", code), ("@plate", (object?)plate), ("@unit", unit), ("@branch", branch),
            ("@type", type), ("@brand", (object?)brand), ("@model", (object?)model), ("@n", Base));

    private void Fuel(string id, string veh, string prev, string cur, string liters, string price)
        => FuelAt(id, veh, prev, cur, liters, price, Base);

    private void FuelAt(string id, string veh, string prev, string cur, string liters, string price, long date)
        => Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@v,@p,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,0);",
            ("@id", id), ("@v", veh), ("@p", prev), ("@c", cur), ("@l", liters), ("@pr", price),
            ("@d", date), ("@op", "op-" + id), ("@n", Base));

    private void Maint(string id, string veh, (string qty, string price)[] materials)
    {
        Exec(@"INSERT INTO vehicle_maintenances(id,company_id,vehicle_id,maintenance_def_id,performed_date,operation_id,is_cancelled,created_at,updated_at,version,is_deleted)
               VALUES(@id,'A',@v,'DEF1',@d,@op,0,@n,@n,1,0);",
            ("@id", id), ("@v", veh), ("@d", Base), ("@op", "op-" + id), ("@n", Base));
        int i = 0;
        foreach (var (qty, price) in materials)
            Exec("INSERT INTO maintenance_materials(id,company_id,maintenance_id,material_id,quantity,unit_price) VALUES(@id,'A',@m,@mat,@q,@pr);",
                ("@id", id + "-mm" + i++), ("@m", id), ("@mat", _mat), ("@q", qty), ("@pr", price));
    }

    private void Issue(string docId, string veh, string qty, string price)
    {
        Exec(@"INSERT INTO stock_documents(id,company_id,doc_type,doc_no,doc_date,vehicle_id,status,created_at,version,is_deleted)
               VALUES(@id,'A','out',@no,@d,@v,'active',@n,1,0);",
            ("@id", docId), ("@no", "DOC-" + docId), ("@d", Base), ("@v", veh), ("@n", Base));
        Exec(@"INSERT INTO stock_movements(id,company_id,material_id,movement_type,direction,quantity,unit_price,operation_id,created_at,document_id)
               VALUES(@id,'A',@mat,'out',-1,@q,@pr,@op,@n,@doc);",
            ("@id", docId + "-mv"), ("@mat", _mat), ("@q", qty), ("@pr", price), ("@op", "op-" + docId), ("@n", Base), ("@doc", docId));
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
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
