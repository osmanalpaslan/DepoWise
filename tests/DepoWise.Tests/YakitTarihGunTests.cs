using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ADR-182 (2026-08-29 · ARA İŞ 2 / S1) — YAKIT TARİHİ ve GÜN DAVRANIŞI ═══
///
/// <b>Kullanıcının bildirdiği belirti:</b> "1 Ağustos → 1 Ağustos ve 2 Ağustos → 2 Ağustos raporlarında
/// AYNI araçlar geliyor." İnceleme İKİ ayrı kök neden buldu:
///
/// <list type="number">
///   <item><b>Yazım hatası (masaüstü, PK-T2):</b> <c>FuelViewModel</c> seçilen günü HAM
///   <c>DateTimeOffset.ToUnixTimeMilliseconds()</c> ile gönderiyordu. Avalonia DatePicker günü YEREL
///   ofsetle verir (TR = +03:00) → "2 Ağustos" veritabanına <b>1 Ağustos 21:00 UTC</b> yazılıyor, fiş
///   raporlarda BİR GÜN ERKEN görünüyordu. Web (<c>Fuel.razor</c>) bu hatayı taşımıyordu.</item>
///   <item><b>Kapsam sözleşmesi (PK-T1=A):</b> rapor "tam filo" idi — aralıkta fişi olmayan araç da
///   0/"-" ile listeleniyordu, bu yüzden araç LİSTESİ her aralıkta aynıydı. Kullanıcı kararıyla artık
///   yalnız aralıkta fişi OLAN araçlar listelenir.</item>
/// </list>
///
/// <b>Bu sınıf ikisini de deterministik olarak kilitler</b> ve (PK-T1'in yalnız bu rapora ait olduğunu
/// kanıtlamak için) <c>vehicle</c> + <c>vehicle-daily</c> raporlarının TAM FİLO davranışını korur.
/// Kayıtlar sabit UTC günlerine seed edilir; makinenin saat dilimi sonucu ETKİLEMEZ.
/// </summary>
public class YakitTarihGunTests : IDisposable
{
    private const long Gun = 86_400_000L;
    /// <summary>2026-08-01 00:00:00.000 UTC.</summary>
    private static readonly long Ag1 = new DateTimeOffset(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    /// <summary>2026-08-02 00:00:00.000 UTC.</summary>
    private static readonly long Ag2 = Ag1 + Gun;
    /// <summary>2026-08-10 (aralık DIŞI).</summary>
    private static readonly long Ag10 = Ag1 + 9 * Gun;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    public YakitTarihGunTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ykttrh_" + Guid.NewGuid().ToString("N") + ".db");
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
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(Ag1 + 12 * 3_600_000);
    }

    private void Seed()
    {
        Exec("INSERT INTO branches(id,company_id,name,created_at,updated_at) VALUES('B1','A','Merkez',@n,@n);", ("@n", Ag1));
        Veh("va", "VA"); Veh("vb", "VB"); Veh("vc", "VC"); Veh("vd", "VD");

        Fis("f-a1", "va", "100", "200", "50", "40", Ag1);                 // yalnız 1 Ağustos
        Fis("f-b1", "vb", "100", "150", "30", "40", Ag2);                 // 2 Ağustos gün BAŞI (00:00:00.000)
        Fis("f-b2", "vb", "150", "180", "20", "40", Ag2 + Gun - 1);       // 2 Ağustos gün SONU (23:59:59.999)
        Fis("f-c1", "vc", "100", "300", "70", "40", Ag10);                // aralık DIŞI
        // VD: hiç fişi yok.
    }

    // ══════════════ A) YAZIM SEMANTİĞİ (S1a — PK-T2) ══════════════

    /// <summary>
    /// Masaüstünün yeni kuralı (<c>FuelViewModel.IsGunuMs</c>) — seçilen GÜN, saat diliminden BAĞIMSIZ
    /// olarak UTC gün başına yazılır ve rapor tarih sınırıyla (<see cref="ReportDateRange.StartMs"/>)
    /// BİREBİR aynıdır. Web (<c>FieldChecks.ToUnixMs</c>) da aynı kuralı uygular.
    /// </summary>
    [Theory]
    [InlineData(3)]     // TR (+03:00) — kullanıcının makinesi
    [InlineData(0)]     // UTC
    [InlineData(-5)]    // batı yarım küre
    [InlineData(13)]    // uç doğu
    public void YKT1_SecilenGun_SaatDiliminden_Bagimsiz_UTC_GunBasi(int ofsetSaat)
    {
        var secim = new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.FromHours(ofsetSaat));
        Assert.Equal(Ag2, MasaustuKurali(secim));                  // her ofsette AYNI gün
        Assert.Equal(ReportDateRange.StartMs(secim), MasaustuKurali(secim));   // rapor filtresiyle parite
    }

    /// <summary>ESKİ (hatalı) dönüşümün belgesi: TR ofsetinde "2 Ağustos" bir gün ERKENe düşüyordu.
    /// Bu test hatanın geri gelmesini engeller — düzeltme kaldırılırsa kural yeniden ihlal edilir.</summary>
    [Fact]
    public void YKT2_Eski_HamDonusum_BirGun_Erkene_Dusuyordu()
    {
        var secim = new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.FromHours(3));
        var eski = secim.ToUnixTimeMilliseconds();                 // ESKİ kod
        Assert.True(eski < Ag2, "Ham dönüşüm 2 Ağustos'tan önceye düşmeliydi (hatanın tanımı).");
        Assert.InRange(eski, Ag1, Ag2 - 1);                        // 1 Ağustos penceresine düşüyor
        Assert.NotEqual(eski, MasaustuKurali(secim));              // yeni kural bunu düzeltir
    }

    /// <summary>Kaynak-düzeyi kilit: masaüstü yakıt ekranı HAM dönüşüme geri DÖNEMEZ.</summary>
    [Fact]
    public void YKT3_FuelViewModel_HamDonusum_Kullanmaz()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var vm = File.ReadAllText(Path.Combine(kok!.FullName, "src", "DepoWise.Desktop", "ViewModels", "FuelViewModel.cs"));

        Assert.DoesNotContain("DistDate?.ToUnixTimeMilliseconds()", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("DepotDate?.ToUnixTimeMilliseconds()", vm, StringComparison.Ordinal);
        Assert.Contains("IsGunuMs(DistDate)", vm, StringComparison.Ordinal);
        Assert.Contains("IsGunuMs(DepotDate)", vm, StringComparison.Ordinal);
        Assert.Contains("DateTimeKind.Utc", vm, StringComparison.Ordinal);
    }

    /// <summary>Uçtan uca: masaüstü kuralıyla yazılan fiş, seçilen GÜNÜN raporunda görünür; bir önceki
    /// günün raporunda GÖRÜNMEZ. (Servis kapısı: geri tarihli yazım <c>btn-backdate</c> ister — admin.)</summary>
    [Fact]
    public void YKT4_MasaustuKuraliyla_Yazilan_Fis_Dogru_Gunde_Gorunur()
    {
        var fuel = new FuelService(_factory, new SabitSaat());
        fuel.AddDepotEntry(_admin, new NewDepotEntry(500m, 40m), Guid.NewGuid().ToString("N"));   // dağıtım için depo stoğu
        var secim = new DateTimeOffset(new DateTime(2026, 8, 2), TimeSpan.FromHours(3));   // TR'de "2 Ağustos"
        fuel.Distribute(_admin, new NewDistribution("vd", 25m, 500m, 40m,
            DistributionDate: MasaustuKurali(secim)), Guid.NewGuid().ToString("N"));

        Assert.Contains("VD", Kodlar(Rapor(Ag1 + Gun, Ag1 + 2 * Gun - 1)));   // 2 Ağustos → VAR
        Assert.DoesNotContain("VD", Kodlar(Rapor(Ag1, Ag1 + Gun - 1)));       // 1 Ağustos → YOK
    }

    // ══════════════ B) RAPOR GÜN DAVRANIŞI (S1b — PK-T1=A) ══════════════

    /// <summary>⭐ Kullanıcının senaryosu: 1 Ağustos ve 2 Ağustos raporları ARTIK farklı araçlar döner.</summary>
    [Fact]
    public void YKT5_BirAgustos_ve_IkiAgustos_Farkli_Araclari_Getirir()
    {
        var birAgustos = Kodlar(Rapor(Ag1, Ag1 + Gun - 1));
        var ikiAgustos = Kodlar(Rapor(Ag2, Ag2 + Gun - 1));

        Assert.Equal(new[] { "VA" }, birAgustos);
        Assert.Equal(new[] { "VB" }, ikiAgustos);
        Assert.NotEqual(birAgustos, ikiAgustos);   // belirtinin ta kendisi: liste artık aynı DEĞİL
    }

    /// <summary>Gün sınırının İKİ UCU da dahildir: 00:00:00.000 ve 23:59:59.999 aynı güne düşer.</summary>
    [Fact]
    public void YKT6_GunSinirlari_IkiUc_Dahil()
    {
        var t = Rapor(Ag2, Ag2 + Gun - 1);
        var vb = t.Rows.Single(r => (string)r[1]! == "VB");
        Assert.Equal(2.0, Deger(vb[6]), 3);        // iki fiş de (gün başı + gün sonu) sayıldı
        Assert.Equal(50.0, Deger(vb[8]), 3);       // litre 30 + 20
    }

    /// <summary>Aralık dışında fişi olan araç listelenmez (fiş var ama bu aralıkta değil).</summary>
    [Fact]
    public void YKT7_AralikDisi_Fisi_Olan_Arac_Listelenmez()
        => Assert.DoesNotContain("VC", Kodlar(Rapor(Ag1, Ag2 + Gun - 1)));

    /// <summary>Hiç fişi olmayan araç listelenmez (ADR-182 sözleşmesi).</summary>
    [Fact]
    public void YKT8_Hic_Fisi_Olmayan_Arac_Listelenmez()
        => Assert.DoesNotContain("VD", Kodlar(Rapor(Ag1, Ag2 + Gun - 1)));

    /// <summary>İki günü kapsayan aralık ikisini de getirir (gün süzmesi aralığı daraltmaz).</summary>
    [Fact]
    public void YKT9_IkiGunluk_Aralik_Her_Iki_Araci_Getirir()
        => Assert.Equal(new[] { "VA", "VB" }, Kodlar(Rapor(Ag1, Ag2 + Gun - 1)));

    // ══════════════ C) REGRESYON — TAM FİLO YALNIZ BU RAPORDA KALKTI ══════════════

    /// <summary>⭐ "Araç Raporu" TAM FİLO davranışını KORUR — yakıtsız araçlar hâlâ listelenir.</summary>
    [Fact]
    public void YKT10_AracRaporu_TamFilo_KORUNDU()
    {
        var t = _reports.Run(_admin, "vehicle", new ReportRequest(true, Ag1, Ag2 + Gun - 1));
        Assert.Equal(4, t.Rows.Count);                       // VA VB VC VD — hepsi
        Assert.True(Icerir(t, "VD"), "Yakıtsız araç Araç Raporu'ndan düşmemeli (tam filo).");
        Assert.True(Icerir(t, "VC"), "Aralık dışı fişi olan araç da listelenmeli (tam filo).");
    }

    /// <summary>⭐ ADR-183 (kullanıcı düzeltmesi): "Araç Raporu — Günlük" verisi OLMAYAN satır üretmez.
    /// Tam filo görünümü yalnız dönem raporunda (YKT10) kalır — ikisinin ayrımı burada kilitlidir.</summary>
    [Fact]
    public void YKT11_AracGunluk_Verisiz_Satir_URETMEZ()
    {
        var t = _reports.Run(_admin, "vehicle-daily", new ReportRequest(true, Ag1, Ag2 + Gun - 1));
        Assert.Equal(2, t.Rows.Count);                       // VA(1 Ağu) + VB(2 Ağu) — yalnız fişi olanlar
        Assert.False(Icerir(t, "VD"), "Hiç verisi olmayan araç günlük raporda GÖRÜNMEMELİ.");
        Assert.False(Icerir(t, "VC"), "Aralık dışı fişi olan araç günlük raporda GÖRÜNMEMELİ.");
    }

    // ══════════════ Yardımcılar ══════════════

    /// <summary><c>FuelViewModel.IsGunuMs</c> ile AYNI kural (masaüstü projesi teste referans veremez;
    /// kuralın kendisi burada, VM'in bu kuralı kullandığı ise YKT3'te kilitlidir).</summary>
    private static long? MasaustuKurali(DateTimeOffset? d)
        => d is null ? null : new DateTimeOffset(DateTime.SpecifyKind(d.Value.Date, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    private TableModel Rapor(long from, long to) => _reports.FuelConsumption(_admin, new ReportRequest(true, from, to));

    private static string[] Kodlar(TableModel t) => t.Rows.Select(r => (string)r[1]!).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static bool Icerir(TableModel t, string kod)
        => t.Rows.Any(r => r.Any(c => c is string s && s == kod));

    private static double Deger(object? v) => v switch
    {
        NumCell n => n.Value,
        double d => d,
        null => 0,
        _ => Convert.ToDouble(v),
    };

    private void Veh(string id, string code)
        => Exec(@"INSERT INTO vehicles(id,company_id,internal_code,meter_unit,branch_id,current_meter,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@code,'km','B1','0',@n,@n,1,0);", ("@id", id), ("@code", code), ("@n", Ag1));

    private void Fis(string id, string veh, string prev, string cur, string liters, string price, long tarih)
        => Exec(@"INSERT INTO fuel_distributions(id,company_id,vehicle_id,prev_meter,current_meter,liters,unit_price,currency_code,distribution_date,operation_id,created_at,updated_at,version,is_deleted)
                  VALUES(@id,'A',@v,@p,@c,@l,@pr,'TRY',@d,@op,@n,@n,1,0);",
            ("@id", id), ("@v", veh), ("@p", prev), ("@c", cur), ("@l", liters), ("@pr", price),
            ("@d", tarih), ("@op", "op-" + id), ("@n", Ag1));

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
