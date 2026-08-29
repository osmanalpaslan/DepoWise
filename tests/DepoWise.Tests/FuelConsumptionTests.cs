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
/// Yakıt Tüketim Raporu (2026-08-08 — Araç Raporu standardına taşındı) hesaplama + davranış doğruluğu.
/// ⭐ ADR-182 (2026-08-29, PK-T1=A): KAPSAM DEĞİŞTİ — yalnız aralıkta yakıt fişi OLAN araçlar listelenir
/// (eski "tam filo" sözleşmesi bu raporda kaldırıldı; `vehicle`/`vehicle-daily` tam filo KALDI). Veri doğrudan
/// SQL ile seed edilir (deterministik). Senaryolar: KM aracı, SAAT iş makinesi, yakıtsız araç (listelenmez), eksik
/// prev/current sayaç, sıfıra bölme, ağırlıklı ortalama fiyat, araç/tür/şube filtreleri, yetkisiz şube (fail-closed),
/// akıllı toplam (homojen vs karışık birim), TotalRow'un satırlardan ayrı olması (web özeti çift saymaz), NumCell
/// HAM değer + "-" görüntüsü. Tek-geçiş SQL (N+1 yok) çıktısı test edilir. Kolon sırası:
/// 0 Şube · 1 İç Kod · 2 Plaka · 3 Araç Adı · 4 Araç Türü · 5 Sayaç Birimi · 6 İşlem · 7 Mesafe · 8 Litre ·
/// 9 Ort. Tüketim · 10 Ort. Fiyat · 11 Toplam Maliyet · 12 Birim Maliyet.
/// </summary>
public class FuelConsumptionTests : IDisposable
{
    private const long Base = 1_700_000_000_000;
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    public FuelConsumptionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_fuelrep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory);
        var clock = new TestClock();
        var users = new UserService(_factory, clock);
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
        Exec("INSERT INTO vehicle_types(id,company_id,name,created_at,updated_at) VALUES('T1','A','Kamyon',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_types(id,company_id,name,created_at,updated_at) VALUES('T2','A','Is Makinesi',@n,@n);", ("@n", Base));
        Exec("INSERT INTO brands(id,company_id,name,created_at,updated_at) VALUES('BR1','A','Ford',@n,@n);", ("@n", Base));
        Exec("INSERT INTO vehicle_models(id,company_id,brand_id,name,created_at,updated_at) VALUES('MD1','A','BR1','Cargo',@n,@n);", ("@n", Base));

        Veh("v1", "V1", "34ABC01", "km", "B1", "T1", "BR1", "MD1");
        Veh("v2", "V2", null, "hour", "B2", "T2", null, null);
        Veh("v3", "V3", null, "km", "B2", "T1", null, null);   // yakıtsız → ADR-182 sonrası LİSTELENMEZ
        Veh("v4", "V4", null, "km", "B1", "T1", null, null);   // eksik prev sayaç

        // V1 (KM): 2 fiş → km=300, litre=150, tutar=100*40+50*44=6200
        Fuel("f1", "v1", "1000", "1200", "100", "40");
        Fuel("f2", "v1", "1200", "1300", "50", "44");
        // V2 (SAAT): 1 fiş → saat=60, litre=80, tutar=3600
        Fuel("f3", "v2", "500", "560", "80", "45");
        // V4 (KM): 1 fiş ama prev NULL → mesafe katkısı 0 (litre 30, tutar 1500)
        Fuel("f4", "v4", null, "200", "30", "50");
        // V1'e TARİH DIŞI fiş (uzak gelecek) → geçerli aralıkta ELENMELİ
        FuelAt("fx", "v1", "9000", "9999", "999", "99", Base + 500_000_000_000L);
    }

    // ── Temel yapı ──
    [Fact]
    public void Rapor_TemelYapi_Dogru()
    {
        var t = Run();
        Assert.Equal("Yakıt Tüketim", t.Title);
        Assert.Equal(13, t.Headers.Count);
        // ADR-182 (PK-T1=A): aralıkta fişi OLAN araçlar → V1, V2, V4 (V3'ün hiç fişi yok, artık listelenmez).
        Assert.Equal(3, t.Rows.Count);        // TOPLAM ayrı TotalRow'da
        Assert.NotNull(t.Numeric);
        Assert.NotNull(t.TotalRow);
    }

    [Fact]
    public void KmArac_TumDegerler_Dogru()
    {
        var v1 = Row(Run(), "V1");
        Assert.Equal("Merkez", (string)v1[0]!);
        Assert.Equal("34ABC01", (string)v1[2]!);
        Assert.Equal("Ford Cargo", (string)v1[3]!);   // marka + model
        Assert.Equal("Kamyon", (string)v1[4]!);
        Assert.Equal("KM", (string)v1[5]!);
        Assert.Equal(2.0, D(v1[6]), 3);               // işlem sayısı
        Assert.Equal(300.0, D(v1[7]), 3);             // mesafe (tarih dışı fiş elendi)
        Assert.Equal(150.0, D(v1[8]), 3);             // litre
        Assert.Equal(0.5, D(v1[9]), 3);               // ort. tüketim 150/300
        Assert.Equal(6200.0 / 150.0, D(v1[10]), 4);   // ağırlıklı ort. fiyat
        Assert.Equal(6200.0, D(v1[11]), 3);           // toplam maliyet
        Assert.Equal(6200.0 / 300.0, D(v1[12]), 4);   // ₺/km
    }

    [Fact]
    public void OrtalamaFiyat_AgirlikliOrtalama_BasitOrtalamaDegil()
    {
        // Basit ort. (40+44)/2 = 42 OLMAMALI; ağırlıklı = 6200/150 = 41,33.
        Assert.Equal(6200.0 / 150.0, D(Row(Run(), "V1")[10]), 4);
    }

    [Fact]
    public void SaatArac_HesabiSaatUzerinden_KMDegil()
    {
        var v2 = Row(Run(), "V2");
        Assert.Equal("Saat", (string)v2[5]!);
        Assert.Equal(60.0, D(v2[7]), 3);              // mesafe = saat farkı
        Assert.Equal(80.0 / 60.0, D(v2[9]), 4);       // L/saat HAM
        Assert.EndsWith("L/Saat", Disp(v2[9]));       // görüntü birimi SAAT
        Assert.EndsWith(" Saat", Disp(v2[7]));        // mesafe görüntü Saat
        Assert.Equal(60.0, D(v2[12]), 3);             // ₺/saat (3600/60)
        Assert.EndsWith("/Saat", Disp(v2[12]));
    }

    /// <summary>
    /// ⭐ ADR-182 (2026-08-29, PK-T1=A) — SÖZLEŞME DEĞİŞİKLİĞİ, testin gevşetilmesi DEĞİL.
    /// Eski kilit: <c>YakitAlmayanArac_TamFilo_GoruntudeTire_DegerSifir</c> — yakıt almayan araç 0/"-"
    /// ile LİSTELENİR derdi. Kullanıcı kararıyla bu rapor artık yalnız aralıkta fişi olan araçları
    /// gösterir; yeni kural burada kilitlenir. Tam filo görünürlüğü <c>vehicle</c> / <c>vehicle-daily</c>
    /// raporlarında KORUNUR (regresyon: <c>YakitTarihGunTests</c>).
    /// </summary>
    [Fact]
    public void YakitAlmayanArac_ARTIK_Listelenmez()
    {
        var t = Run();
        Assert.DoesNotContain(t.Rows, r => (string)r[1]! == "V3");   // V3'ün hiç yakıt fişi yok
        Assert.Contains(t.Rows, r => (string)r[1]! == "V1");         // fişi olanlar aynen listelenir
        Assert.Contains(t.Rows, r => (string)r[1]! == "V2");
        Assert.Contains(t.Rows, r => (string)r[1]! == "V4");
    }

    [Fact]
    public void EksikPrevSayac_MesafeSifir_SifiraBolmeKorumasi()
    {
        var v4 = Row(Run(), "V4");
        Assert.Equal(0.0, D(v4[7]), 3);               // prev NULL → mesafe 0
        Assert.Equal(30.0, D(v4[8]), 3);              // litre yine sayılır
        Assert.Equal(1500.0, D(v4[11]), 3);           // maliyet
        Assert.Equal(0.0, D(v4[9]), 3);               // km=0 → tüketim 0 (bölme koruması)
        Assert.Equal(0.0, D(v4[12]), 3);              // km=0 → ₺/km 0
    }

    [Fact]
    public void GorunumBicimi_HamDegerdenAyri()
    {
        var v1 = Row(Run(), "V1");
        Assert.Equal("6.200,00", Disp(v1[11]).Replace("₺", "").Trim());
        Assert.Equal("150,00 L", Disp(v1[8]));
        Assert.Equal("300 km", Disp(v1[7]));
        Assert.Equal("2", Disp(v1[6]));               // işlem sayısı tam sayı görüntü
    }

    [Fact]
    public void VarsayilanSiralama_SubeOnce()
    {
        // Şube -> Araç Adı; ilk satır Merkez (B1) olmalı (Sahra'dan önce).
        Assert.Equal("Merkez", (string)Run().Rows[0][0]!);
    }

    // ── Akıllı toplam (rule 9, "A") ──
    [Fact]
    public void Toplam_KarisikBirim_MesafeVeOrtalamalarBos_ParaVeLitreVar()
    {
        var top = Run().TotalRow!;                    // tüm filo → km + saat KARIŞIK
        Assert.Equal("TOPLAM", (string)top[0]!);
        Assert.Equal(4.0, D(top[6]), 3);              // işlem 2+1+0+1
        Assert.Equal(260.0, D(top[8]), 3);            // litre 150+80+0+30
        Assert.Equal(11300.0, D(top[11]), 3);         // toplam 6200+3600+0+1500
        Assert.Equal(11300.0 / 260.0, D(top[10]), 4); // ort. fiyat (birimden bağımsız → hep var)
        Assert.Equal("", Disp(top[7]));               // mesafe: karışık birim → BOŞ
        Assert.Equal("", Disp(top[9]));               // ort. tüketim → BOŞ
        Assert.Equal("", Disp(top[12]));              // birim maliyet → BOŞ
    }

    [Fact]
    public void Toplam_HomojenBirim_MesafeVeOrtalamalar_Hesaplanir()
    {
        // T1 = yalnız km araçları (V1,V3,V4) → homojen → mesafe/tüketim/birim hesaplanır.
        var t = _reports.FuelConsumption(_admin, new ReportRequest(true, 1, 2_000_000_000_000L, VehicleTypeIds: new[] { "T1" }));
        var top = t.TotalRow!;
        Assert.Equal(300.0, D(top[7]), 3);            // mesafe 300+0+0
        Assert.Equal(180.0 / 300.0, D(top[9]), 4);    // tüketim (150+30)/300
        Assert.Equal(7700.0 / 300.0, D(top[12]), 4);  // birim 7700/300
        Assert.EndsWith(" km", Disp(top[7]));
    }

    // ── Filtreler ──
    [Fact]
    public void AracFiltresi_YalnizSeciliArac()
    {
        var t = _reports.FuelConsumption(_admin, new ReportRequest(true, 1, 2_000_000_000_000L, VehicleIds: new[] { "v1" }));
        Assert.Single(t.Rows);
        Assert.Equal("V1", (string)t.Rows[0][1]!);
    }

    [Fact]
    public void AracTuruFiltresi_SQLdeUygulanir()
    {
        var t = _reports.FuelConsumption(_admin, new ReportRequest(true, 1, 2_000_000_000_000L, VehicleTypeIds: new[] { "T2" }));
        Assert.Single(t.Rows);                        // T2 = yalnız V2
        Assert.Equal("V2", (string)t.Rows[0][1]!);
    }

    [Fact]
    public void SubeFiltresi_YetkiliAdmin_AcikSecim()
    {
        var t = _reports.FuelConsumption(_admin, new ReportRequest(true, 1, 2_000_000_000_000L, BranchIds: new[] { "B1" }));
        Assert.Equal(2, t.Rows.Count);                // B1 = V1, V4
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
    }

    [Fact]
    public void YetkisizKullanici_SubeDegistiremez_OturumSubesineDuser()
    {
        // Staff: reports görüntüleme var ama şube-seçme yetkisi YOK; oturum şubesi B1.
        var set = new PermissionSet(new[] { new ModulePermission("reports", true, false, false, false) }, Array.Empty<string>());
        var staff = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, set) { OperatingBranchId = "B1" };
        // B2 göndermeye çalışsa bile → yalnız oturum şubesi B1 (fail-closed).
        var t = _reports.FuelConsumption(staff, new ReportRequest(true, 1, 2_000_000_000_000L, BranchIds: new[] { "B2" }));
        Assert.All(t.Rows, r => Assert.Equal("Merkez", (string)r[0]!));
        Assert.DoesNotContain(t.Rows, r => (string)r[1]! == "V2");   // B2 aracı gelmez
    }

    // ── TotalRow ayrımı + web özeti çift saymaz ──
    [Fact]
    public void ToplamSatiri_VeriSatirlariArasindaDegil()
    {
        var t = Run();
        Assert.DoesNotContain(t.Rows, r => (string)r[0]! == "TOPLAM");   // satırlarda TOPLAM yok
        Assert.NotNull(t.TotalRow);                                      // ayrı pinned
    }

    [Fact]
    public void WebOzeti_CiftSaymaz_ToplamSatirlarinToplamiylaEsit()
    {
        // Web BuildSummary yalnız veri satırlarını toplar; TotalRow ayrı olduğu için çift sayım oluşmaz.
        var t = Run();
        double litreSum = t.Rows.Sum(r => D(r[8]));
        double costSum = t.Rows.Sum(r => D(r[11]));
        Assert.Equal(litreSum, D(t.TotalRow![8]), 3);    // TotalRow litre = satırların toplamı (1x)
        Assert.Equal(costSum, D(t.TotalRow![11]), 3);
    }

    [Fact]
    public void NumCell_HamDeger_GoruntudenBagimsiz()
    {
        var v1 = Row(Run(), "V1");
        Assert.IsType<NumCell>(v1[11]);                  // sayısal hücre NumCell (HAM + görüntü)
        var n = (NumCell)v1[11]!;
        Assert.Equal(6200.0, n.Value, 3);               // HAM değer sıralama/filtre için
        Assert.Contains("₺", n.Display);                // görüntü biçimli
    }

    // ── Yardımcılar ──
    private TableModel Run() => _reports.FuelConsumption(_admin, new ReportRequest(true, 1, 2_000_000_000_000L));

    private static double D(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => System.Convert.ToDouble(v),
    };

    private static string Disp(object? v) => v switch { NumCell n => n.Display, null => "", _ => v.ToString() ?? "" };

    private static IReadOnlyList<object?> Row(TableModel t, string code)
        => t.Rows.First(r => (string)r[1]! == code);   // İç Kod = index 1

    private void Veh(string id, string code, string? plate, string unit, string branch, string type, string? brand, string? model)
        => Exec(@"INSERT INTO vehicles(id,company_id,internal_code,plate,meter_unit,branch_id,vehicle_type_id,brand_id,vehicle_model_id,current_meter,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@code,@plate,@unit,@branch,@type,@brand,@model,'0',@n,@n,1,0);",
            ("@id", id), ("@code", code), ("@plate", (object?)plate), ("@unit", unit), ("@branch", branch),
            ("@type", type), ("@brand", (object?)brand), ("@model", (object?)model), ("@n", Base));

    private void Fuel(string id, string veh, string? prev, string cur, string liters, string price)
        => FuelAt(id, veh, prev, cur, liters, price, Base);

    private void FuelAt(string id, string veh, string? prev, string cur, string liters, string price, long date)
        => Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@v,@p,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,0);",
            ("@id", id), ("@v", veh), ("@p", (object?)prev), ("@c", cur), ("@l", liters), ("@pr", price),
            ("@d", date), ("@op", "op-" + id), ("@n", Base));

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
