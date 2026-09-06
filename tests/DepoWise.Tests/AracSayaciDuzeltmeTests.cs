using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.1 — ARAÇ SAYACI DÜZELTİLEBİLİR OLMALI (2026-09-06) ═══
///
/// <b>GERÇEK OLAY (kullanıcı bildirdi).</b> `mustafa.alpaslan` bir araca yakıt dağıtımından yanlış ve
/// çok yüksek sayaç girdi, kaydı düzeltti; buna rağmen araç hâlâ hatalı sayacı gösterdi ve yeni
/// yakıt fişinde başlangıç sayacı kilitli geldiği için düzeltemedi. Hatalı araç: KAM-ME 059.
///
/// <b>KÖK NEDEN.</b> `vehicles.current_meter` yalnız ileri giden SAKLI bir değerdi; iptal ve düzeltme
/// ona hiç dokunmuyordu (kural Y2). Yanlış değer kalıcı oluyordu.
///
///  SY1 — 🔴 Yanlış sayaç DÜZELTİLİNCE araç sayacı da düzelir (asıl olay)
///  SY2 — 🔴 Yanlış sayaçlı kayıt İPTAL edilince araç sayacı geri iner
///  SY3 — Elle beyan edilen taban KORUNUR (kayıtsız araçta sayaç sıfıra düşmez)
///  SY4 — Bakım kaydı iptal edilince de sayaç düzelir
///  SY5 — Toplu onarım: geçmişte zehirlenmiş araçlar tek işlemde düzelir
///  SY6 — Geçerli kayıtların en yükseği kazanır (düzeltme diğer kayıtları düşürmez)
///  SY7 — Şüpheli sıçrama uyarısı: basamak hatası yakalanır, normal artış rahatsız etmez
///  SY8 — Metin sayaç kolonlarında SAYISAL karşılaştırma (9.000 &lt; 10.000)
/// </summary>
public class AracSayaciDuzeltmeTests : IDisposable
{
    private const string Co = "SYC";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly VehicleService _araclar;
    private readonly FuelService _yakit;
    private readonly MaintenanceService _bakim;
    private readonly SessionContext _admin;
    private readonly string _aracId, _sube;
    private static readonly long Gun = 1_700_000_000_000;

    public AracSayaciDuzeltmeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_sayac_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");

        var uid = new UserService(_f).EnsureInitialAdmin(Co, "sayac_admin", "Sayac!2026", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _araclar = new VehicleService(_f);
        _yakit = new FuelService(_f);
        _bakim = new MaintenanceService(_f);
        _sube = new DepoWise.Infrastructure.Organization.BranchService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));

        // Araç: elle beyan edilen taban 150.000 km (araç kartı açılışı).
        _aracId = _araclar.Create(_admin, new NewVehicle("KAM-ME 059", "06 FZ 4146", CurrentMeter: 150_000m));

        // Yakıt deposunda litre olsun (dağıtım bakiye ister).
        _yakit.AddDepotEntry(_admin, new NewDepotEntry(5_000m, 40m, "TRY", EntryDate: Gun), Op());
    }

    private static string Op() => Guid.NewGuid().ToString("N");

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Veritabanındaki HAM araç sayacı.</summary>
    private decimal Sayac()
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT current_meter FROM vehicles WHERE id=@id;";
        cmd.AddWithValue("@id", _aracId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private string YakitVer(decimal sayac, decimal litre = 50m)
        => _yakit.Distribute(_admin, new NewDistribution(_aracId, litre, sayac, DistributionDate: Gun), Op());

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ SY1 — ASIL OLAY ══════════════════

    /// <summary>
    /// 🔴 Kullanıcının yaşadığı senaryonun birebir kendisi: yanlış (çok yüksek) sayaç girildi,
    /// kayıt DÜZELTİLDİ; araç sayacı da düzelmelidir. Düzeltme öncesi hatalı değerin GERÇEKTEN
    /// yazıldığı da doğrulanır — aksi hâlde test sahte yeşil olurdu.
    /// </summary>
    [Fact]
    public void SY1_Yanlis_Sayac_Duzeltilince_Arac_Sayaci_Da_Duzelir()
    {
        var kayit = YakitVer(1_555_000m);            // basamak hatası: 155.000 yerine 1.555.000
        Assert.Equal(1_555_000m, Sayac());           // hatalı değer gerçekten işlendi

        _yakit.UpdateDistribution(_admin, kayit,
            new NewDistribution(_aracId, 50m, 155_000m, DistributionDate: Gun), Op(), "sayaç yanlış girildi");

        Assert.Equal(155_000m, Sayac());             // ⭐ düzeltme sayaca da yansıdı
    }

    // ══════════════════ SY2 — İPTAL ══════════════════

    [Fact]
    public void SY2_Yanlis_Kayit_Iptal_Edilince_Sayac_Geri_Iner()
    {
        YakitVer(160_000m);                          // doğru kayıt
        var hatali = YakitVer(1_600_000m);           // hatalı kayıt
        Assert.Equal(1_600_000m, Sayac());

        _yakit.CancelDistribution(_admin, hatali, "yanlış sayaç");

        Assert.Equal(160_000m, Sayac());             // geçerli kayıtların en yükseğine döndü
    }

    // ══════════════════ SY3 — TABAN KORUNUR ══════════════════

    /// <summary>
    /// 🔴 En tehlikeli yan etki bu olurdu: yeniden hesap, elle beyan edilen tabanı da silip sayacı
    /// sıfırlasaydı VERİ KAYBI olurdu. Araç kartında beyan edilen 150.000 km korunmalıdır.
    /// </summary>
    [Fact]
    public void SY3_Elle_Beyan_Edilen_Taban_Korunur()
    {
        var kayit = YakitVer(160_000m);
        _yakit.CancelDistribution(_admin, kayit, "yanlış kayıt");

        Assert.Equal(150_000m, Sayac());             // araç kartındaki beyan; SIFIR DEĞİL
    }

    // ══════════════════ SY4 — BAKIM ══════════════════

    [Fact]
    public void SY4_Bakim_Iptalinde_De_Sayac_Duzelir()
    {
        var tanim = new MaintenanceDefinitionService(_f)
            .Create(_admin, new NewMaintenanceDefinition("Yağ Bakımı", 10_000m, "km"));
        var bakim = _bakim.Save(_admin, new NewMaintenance(_aracId, tanim, PerformedKm: 900_000m,
            PerformedDate: Gun), Op());
        Assert.Equal(900_000m, Sayac());

        _bakim.Cancel(_admin, bakim, "yanlış km");

        Assert.Equal(150_000m, Sayac());
    }

    // ══════════════════ SY5 — TOPLU ONARIM ══════════════════

    /// <summary>
    /// Kullanıcı: <i>"bu sorun başka araçlarda da bulunmakta."</i> Geçmişte zehirlenmiş kayıtlar
    /// (düzeltme kodu yokken oluşmuş) tek işlemde onarılabilmelidir. Zehirlenmeyi birebir taklit
    /// etmek için araç satırı DOĞRUDAN veritabanında yükseltilir — eski hatanın bıraktığı durum budur.
    /// </summary>
    [Fact]
    public void SY5_Toplu_Onarim_Gecmisteki_Zehirlenmeyi_Duzeltir()
    {
        YakitVer(170_000m);
        Calistir($"UPDATE vehicles SET current_meter='9999999' WHERE id='{_aracId}';");
        Assert.Equal(9_999_999m, Sayac());

        var duzelen = _araclar.RecalculateAllMeters(_admin);

        Assert.Equal(1, duzelen);
        Assert.Equal(170_000m, Sayac());
    }

    // ══════════════════ SY6 — EN YÜKSEK GEÇERLİ KAYIT ══════════════════

    [Fact]
    public void SY6_Duzeltme_Diger_Gecerli_Kayitlari_Dusurmez()
    {
        YakitVer(200_000m);                          // gerçek, geçerli kayıt
        var hatali = YakitVer(2_000_000m);

        _yakit.UpdateDistribution(_admin, hatali,
            new NewDistribution(_aracId, 50m, 180_000m, DistributionDate: Gun), Op(), "düzeltme");

        Assert.Equal(200_000m, Sayac());             // 200.000 hâlâ geçerli → sayaç oraya iner, dibe değil
    }

    // ══════════════════ SY7 — ÖNLEME ══════════════════

    [Theory]
    [InlineData(150_000, 1_555_000, true)]   // basamak hatası → uyar
    [InlineData(150_000, 155_000, false)]    // normal artış → uyarma
    [InlineData(150_000, 149_000, false)]    // düşük değer → bu kuralın konusu değil
    [InlineData(0, 150_000, false)]          // ilk kayıt → uyarma
    [InlineData(100, 5_000, false)]          // oran büyük ama mutlak fark küçük → uyarma
    public void SY7_Supheli_Sicrama_Uyarisi(decimal mevcut, decimal girilen, bool uyarmali)
        => Assert.Equal(uyarmali, MeterRule.SuspiciousJump(mevcut, girilen));

    // ══════════════════ SY8 — METİN KOLONUNDA SAYISAL KARŞILAŞTIRMA ══════════════════

    /// <summary>
    /// 🔴 Sayaç kolonları TEXT'tir. SQL <c>MAX()</c> kullanılsaydı metin sıralaması yapılır ve
    /// "9000" &gt; "10000" çıkardı — sayaç sessizce yanlış hesaplanırdı.
    /// </summary>
    [Fact]
    public void SY8_Metin_Sayac_Sayisal_Karsilastirilir()
    {
        YakitVer(9_000m);
        YakitVer(10_000m);
        var kayit = YakitVer(11_000m);
        _yakit.CancelDistribution(_admin, kayit, "iptal");

        Assert.Equal(150_000m, Sayac());             // taban zaten daha yüksek

        // Tabanı düşürüp yalnız kayıtların karşılaştırmasını sına.
        Calistir($"DELETE FROM vehicle_meter_logs WHERE vehicle_id='{_aracId}' AND source='vehicle_create';");
        _araclar.RecalculateMeter(_admin, _aracId);

        Assert.Equal(10_000m, Sayac());              // 9.000 değil 10.000
    }

    // ══════════════════ SY9 — FAZ 4 FINAL QA BULGUSU ══════════════════

    /// <summary>
    /// ⭐ SY9 — <b>NEGATİF SAYAÇ REDDEDİLİR</b> (FAZ 4 final QA sırasında ÖLÇÜLDÜ, 2026-09-06).
    ///
    /// Final QA'nın API bataryasında araç KAYIT AÇILIŞINDA sayaç −5000 verilebiliyordu ve sessizce
    /// yazılıyordu (log ekranında "Sayaç: — → -5000" olarak görüldü). Sayaç, yakıt tüketimi ve
    /// bakım periyodu hesaplarının GİRDİSİDİR; eksi bir başlangıç bu hesapları ve raporları bozar.
    /// Doğrudan sayaç değiştirme yolunda (SetMeter) koruma "geriye gitmez" kuralıyla zaten vardı;
    /// eksik olan KAYIT AÇILIŞIYDI. İki yol da kapatıldı.
    /// </summary>
    [Fact]
    public void SY9_Negatif_Sayac_Reddedilir()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            _araclar.Create(_admin, new NewVehicle("NEG-01", "06 NEG 01", CurrentMeter: -5000m)));
        Assert.Contains("eksi", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Doğrudan sayaç yazma yolu da aynı kuralı uygular.
        Assert.Throws<ArgumentException>(() => _araclar.SetMeter(_admin, _aracId, -1m));

        // Sıfır GEÇERLİDİR (yeni araç sıfır kilometreyle açılabilir).
        var id = _araclar.Create(_admin, new NewVehicle("SIFIR-01", "06 SFR 01", CurrentMeter: 0m));
        Assert.False(string.IsNullOrEmpty(id));
    }
}
