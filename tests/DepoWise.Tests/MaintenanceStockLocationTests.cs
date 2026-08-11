using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// BKM-04 / KARAR-9 (2026-08-11) — BAKIM MALZEMESİNİN ÇIKTIĞI DEPO.
///
/// <b>Kapatılan hata:</b> <see cref="MaintenanceService"/> stok yazarken lokasyonu SABİT boş yazıyordu
/// (<c>branch_id=NULL</c> + <c>Unassigned</c>) → her bakım tüketimi ATANMAMIŞ kovasına düşüyordu.
/// STK-08 geçmişi temizleme aracını vermişti ama bu yol YENİSİNİ üretmeye devam ediyordu.
///
/// <b>Kilitlenen kurallar (KARAR-9):</b>
///  • Kullanıcının seçtiği depo AYNEN uygulanır — sessizce oturum şubesine, aracın şubesine ya da
///    <c>op_branch_id</c>'ye ÇEVRİLMEZ.
///  • Defter (<c>stock_movements.branch_id</c>) ve bakiye (<c>stock_balances.location_id</c>) AYNI depoyu kullanır.
///  • <b>İPTAL: ters hareket ORİJİNAL hareketin deposuna yazar</b> — iptal anındaki oturumdan
///    yeniden hesaplanmaz. (Depo A'dan düşen, kullanıcı Depo B'ye geçse bile Depo A'ya döner.)
///  • Lokasyon verilmezse ATANMAMIŞ (geriye dönük davranış); bakım stok yüzünden ENGELLENMEZ.
///  • Yabancı/bilinmeyen/pasif lokasyon reddedilir (403) — istemciye körü körüne güvenilmez.
///
/// 🔒 ÇEVRİMDIŞI: bu sınıfın tamamı YEREL SQLite üzerindedir; hiçbir HTTP çağrısı yoktur
/// (<c>ApiTestHost</c> KULLANILMAZ) — masaüstünün gerçek yolu budur.
/// </summary>
public class MaintenanceStockLocationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly MaintenanceService _maintenance;
    private readonly MaintenanceDefinitionService _defs;
    private readonly DailyActivityService _daily;
    private readonly ReportService _reports;
    private readonly BranchService _branches;
    private readonly SessionContext _depoAOturum;
    private readonly string _depoA, _depoB, _mat, _mat2, _vehicle, _def;

    public MaintenanceStockLocationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_bkm04_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        SeedCompany(_factory, "A");

        _materials = new MaterialService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _maintenance = new MaintenanceService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _daily = new DailyActivityService(_factory, _maintenance, _clock, _defs);
        _reports = new ReportService(_factory);
        _branches = new BranchService(_factory, _clock);

        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        var admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        _depoA = _branches.Create(admin, new NewBranch("Depo A"));
        _depoB = _branches.Create(admin, new NewBranch("Depo B"));
        // Masaüstündeki gerçek oturum: kullanıcı GİRİŞTE Depo A'yı seçmiş.
        _depoAOturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };

        _mat = _materials.Create(_depoAOturum, new NewMaterial("BKM-1", "Yağ filtresi"));
        _mat2 = _materials.Create(_depoAOturum, new NewMaterial("BKM-2", "Hava filtresi"));

        var vehicles = new VehicleService(_factory, _clock);
        _vehicle = vehicles.Create(_depoAOturum, new NewVehicle("IS-01", "34ABC01", 2020, 1000m, "km", _depoA));
        _def = _defs.Create(_depoAOturum, new NewMaintenanceDefinition("Periyodik Bakım", 10000m, "km"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static void SeedCompany(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Depoya açılış stoğu koyar (bakımın düşeceği stok).</summary>
    private void Stok(string materialId, string? location, decimal qty)
        => _opening.RecordOpening(_depoAOturum, materialId, qty, Op(), branchId: location);

    private string Bakim(string? stockLocation, params (string MaterialId, decimal Qty, bool Team)[] lines)
        => _maintenance.Save(_depoAOturum, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 5000m,
            PerformedDate: _clock.UtcNow.ToUnixTimeMilliseconds(),   // bakım raporu performed_date'e göre filtreler
            Materials: lines.Select(l => new MaintenanceMaterialLine(l.MaterialId, l.Qty, l.Team)).ToList(),
            StockLocationId: stockLocation), Op());

    private decimal Bakiye(string materialId, string location)
        => _stock.GetBalanceAt(_depoAOturum, materialId, location);

    private decimal FirmaToplami(string materialId) => _stock.GetBalance(_depoAOturum, materialId);

    /// <summary>Bir malzemenin TERS KAYIT (usage_reverse) hareketleri.
    ///
    /// ⚠️ Sıra indeksiyle (<c>[1]</c>) seçmek FLAKY'dir: test saati dondurulmuş olduğu için orijinal
    /// hareket ile ters kaydın <c>created_at</c> değeri AYNI olur ve <c>ORDER BY created_at, id</c>
    /// rastgele GUID'e düşer. Tür üzerinden seçmek deterministiktir. (Üretim etkilenmez: iptal her
    /// hareketi KENDİ deposuna geri yazar, sıradan bağımsızdır — aşağıdaki testlerden biri bunu kanıtlar.)</summary>
    private List<(string Type, string? Branch, decimal Qty)> TersKayitlar(string materialId)
        => Hareketler(materialId).Where(x => x.Type == "usage_reverse").ToList();

    /// <summary>Defterdeki tüketim hareketlerinin (tip, lokasyon, miktar) listesi — kronolojik.</summary>
    private List<(string Type, string? Branch, decimal Qty)> Hareketler(string materialId)
    {
        var list = new List<(string, string?, decimal)>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT movement_type, branch_id, quantity FROM stock_movements " +
            "WHERE company_id='A' AND material_id=@m AND movement_type IN ('usage','usage_reverse') " +
            "ORDER BY created_at, id;";
        cmd.AddWithValue("@m", materialId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), Money.Parse(r.GetString(2))));
        return list;
    }

    // ══════════════ 1. SEÇİLEN DEPO GERÇEKTEN UYGULANIYOR MU ══════════════

    /// <summary>3+4 — Seçilen depo hem DEFTERE hem BAKİYEYE yazılır. İkisi ayrışırsa stok sessizce tutarsızlaşır.</summary>
    [Fact]
    public void Secilen_Depodan_Duser_Hem_Defter_Hem_Bakiye()
    {
        Stok(_mat, _depoA, 10m);

        Bakim(_depoA, (_mat, 4m, false));

        Assert.Equal(6m, Bakiye(_mat, _depoA));                       // bakiye: seçilen depo
        var mv = Assert.Single(Hareketler(_mat));
        Assert.Equal("usage", mv.Type);
        Assert.Equal(_depoA, mv.Branch);                              // defter: AYNI depo
        Assert.Equal(4m, mv.Qty);
        Assert.Equal(0m, Bakiye(_mat, StockBalanceWriter.Unassigned)); // ATANMAMIŞ'a HİÇ dokunulmadı
    }

    /// <summary>2 — KIRMIZI ÇİZGİ: kullanıcı oturum şubesinden FARKLI bir depo seçerse stok O DEPODAN düşer.
    /// Sistem sessizce kullanıcının şubesine (Depo A) geri dönmez.</summary>
    [Fact]
    public void Farkli_Depo_Secilirse_O_Depodan_Duser_Oturum_Subesi_EZMEZ()
    {
        Stok(_mat, _depoA, 10m);
        Stok(_mat, _depoB, 10m);

        // Oturum Depo A'da; kullanıcı bilerek Depo B'yi seçti (parça merkez depodan geldi).
        Bakim(_depoB, (_mat, 3m, false));

        Assert.Equal(10m, Bakiye(_mat, _depoA));   // oturum şubesine DOKUNULMADI
        Assert.Equal(7m, Bakiye(_mat, _depoB));    // seçilen depo düştü
        Assert.Equal(_depoB, Assert.Single(Hareketler(_mat)).Branch);
    }

    /// <summary>Aracın şubesi (Depo A) stok lokasyonunu BELİRLEMEZ — KARAR-9 md. 10.</summary>
    [Fact]
    public void Aracin_Subesi_Stok_Lokasyonunu_Belirlemez()
    {
        Stok(_mat, _depoB, 10m);
        // Araç Depo A'ya kayıtlı; kullanıcı Depo B seçti → Depo B düşmeli.
        Bakim(_depoB, (_mat, 2m, false));

        Assert.Equal(8m, Bakiye(_mat, _depoB));
        Assert.Equal(0m, Bakiye(_mat, _depoA));
    }

    /// <summary>8 — Lokasyon GÖNDERİLMEZSE eski davranış: ATANMAMIŞ. Eski istemci kırılmaz.</summary>
    [Fact]
    public void Lokasyon_Verilmezse_ATANMAMIS_Eski_Davranis()
    {
        Stok(_mat, null, 10m);

        Bakim(null, (_mat, 4m, false));

        Assert.Equal(6m, Bakiye(_mat, StockBalanceWriter.Unassigned));
        Assert.Null(Assert.Single(Hareketler(_mat)).Branch);   // defterde branch_id NULL (eski biçim)
    }

    /// <summary>20 — Lokasyon seçimi FİRMA TOPLAMINI değiştirmez; yalnız kırılımı doğru depoya taşır.</summary>
    [Fact]
    public void Firma_Toplami_Degismez_Yalniz_Kirilim_Tasinir()
    {
        Stok(_mat, _depoA, 10m);
        Stok(_mat, _depoB, 5m);
        Assert.Equal(15m, FirmaToplami(_mat));

        Bakim(_depoB, (_mat, 5m, false));

        Assert.Equal(10m, FirmaToplami(_mat));      // 15 − 5
        Assert.Equal(10m, Bakiye(_mat, _depoA));    // A'ya dokunulmadı
        Assert.Equal(0m, Bakiye(_mat, _depoB));     // tamamı B'den çıktı
    }

    // ══════════════ 2. GÜVENLİK / İZOLASYON ══════════════

    /// <summary>9 — Başka firmanın deposu KABUL EDİLMEZ (403). İstemciye körü körüne güvenilmez.</summary>
    [Fact]
    public void Yabanci_Firmanin_Deposu_Reddedilir()
    {
        SeedCompany(_factory, "B");
        var users = new UserService(_factory, _clock);
        var otherUid = users.EnsureInitialAdmin("B", "admin_b", "admin123", RoleKeys.CompanyAdmin);
        var so = new SessionContext(otherUid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yabanci = _branches.Create(so, new NewBranch("Yabancı Depo"));

        Stok(_mat, _depoA, 10m);
        Assert.Throws<ForbiddenException>(() => Bakim(yabanci, (_mat, 1m, false)));

        // Hiçbir şey yazılmadı (rollback) — ne hareket ne bakiye.
        Assert.Empty(Hareketler(_mat));
        Assert.Equal(10m, Bakiye(_mat, _depoA));
    }

    /// <summary>10 — Pasif/silinmiş depo reddedilir (branches.is_deleted=1 = pasif).</summary>
    [Fact]
    public void Pasif_Silinmis_Depo_Reddedilir()
    {
        var gecici = _branches.Create(_depoAOturum, new NewBranch("Kapanan Şantiye"));
        _branches.Delete(_depoAOturum, gecici);

        Stok(_mat, _depoA, 10m);
        Assert.Throws<ForbiddenException>(() => Bakim(gecici, (_mat, 1m, false)));
        Assert.Empty(Hareketler(_mat));
    }

    /// <summary>Bilinmeyen (uydurma) lokasyon kimliği de reddedilir.</summary>
    [Fact]
    public void Bilinmeyen_Lokasyon_Reddedilir()
    {
        Stok(_mat, _depoA, 10m);
        Assert.Throws<ForbiddenException>(() => Bakim("boyle-bir-depo-yok", (_mat, 1m, false)));
        Assert.Empty(Hareketler(_mat));
    }

    /// <summary>12 — Firmada HİÇ uygun depo yoksa bakım stok yüzünden ENGELLENMEZ (2026-08-06 kararı korunur);
    /// hareket ATANMAMIŞ olarak devam eder ve kayıt oluşur.</summary>
    [Fact]
    public void Hic_Depo_Yoksa_Bakim_Engellenmez_ATANMAMIS_Devam_Eder()
    {
        Stok(_mat, null, 3m);

        var id = Bakim(null, (_mat, 5m, false));   // stok da yetersiz — yine de engellenmez

        Assert.False(string.IsNullOrEmpty(id));
        Assert.Equal(-2m, Bakiye(_mat, StockBalanceWriter.Unassigned));   // negatife düşebilir (ADR-086)
    }

    /// <summary>Negatif stok kuralı DEĞİŞMEDİ: seçilen depoda yetersizse de engellenmez, o depo eksiye düşer.</summary>
    [Fact]
    public void Negatif_Stok_Engellenmez_Secilen_Depo_Eksiye_Duser()
    {
        Stok(_mat, _depoA, 1m);

        Bakim(_depoA, (_mat, 5m, false));

        Assert.Equal(-4m, Bakiye(_mat, _depoA));
        Assert.Equal(0m, Bakiye(_mat, StockBalanceWriter.Unassigned));   // eksik ATANMAMIŞ'a KAYMAZ
    }

    // ══════════════ 3. İPTAL SİMETRİSİ (BKM-04'ün en kritik kabul kriteri) ══════════════

    /// <summary>13 — İptal, ters hareketi ORİJİNAL hareketin deposuna yazar.</summary>
    [Fact]
    public void Iptal_Orijinal_Hareketin_Deposuna_Geri_Yazar()
    {
        Stok(_mat, _depoB, 10m);
        var id = Bakim(_depoB, (_mat, 4m, false));
        Assert.Equal(6m, Bakiye(_mat, _depoB));

        _maintenance.Cancel(_depoAOturum, id, "yanlış kayıt");

        Assert.Equal(10m, Bakiye(_mat, _depoB));   // geri döndü
        var mvs = Hareketler(_mat);
        Assert.Equal(2, mvs.Count);
        var ters = Assert.Single(TersKayitlar(_mat));
        Assert.Equal(_depoB, ters.Branch);         // ters kayıt AYNI depoda
    }

    /// <summary>14 — 🔴 EN KRİTİK: iptal anında kullanıcı BAŞKA şubeyle giriş yapmış olsa bile
    /// ters hareket ORİJİNAL depoya yazılır. Oturumdan yeniden hesaplanmaz.
    ///
    /// Senaryo: Depo A'dan 5 düştü → kullanıcı Depo B ile oturum açtı → iptal → +5 DEPO A'ya döner,
    /// Depo B'ye KESİNLİKLE dönmez.</summary>
    [Fact]
    public void Iptal_Sirasinda_Oturum_Subesi_Degisse_de_Orijinal_Depo_Korunur()
    {
        Stok(_mat, _depoA, 5m);
        Stok(_mat, _depoB, 5m);
        var id = Bakim(_depoA, (_mat, 5m, false));
        Assert.Equal(0m, Bakiye(_mat, _depoA));

        // Kullanıcı şimdi DEPO B ile giriş yaptı.
        var depoBOturum = new SessionContext(_depoAOturum.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoB };

        _maintenance.Cancel(depoBOturum, id, "yanlış araç");

        Assert.Equal(5m, Bakiye(_mat, _depoA));   // ORİJİNAL depoya döndü
        Assert.Equal(5m, Bakiye(_mat, _depoB));   // Depo B ŞİŞMEDİ
        Assert.Equal(_depoA, Assert.Single(TersKayitlar(_mat)).Branch);
    }

    /// <summary>Aynı bakımda FARKLI depolardan düşülmüş satırlar (art arda kayıtlar) iptalde
    /// her biri KENDİ deposuna döner — tek bir depoya toplanmaz.</summary>
    [Fact]
    public void Iptal_Her_Hareketi_Kendi_Deposuna_Geri_Yazar()
    {
        Stok(_mat, _depoA, 10m);
        Stok(_mat2, _depoA, 10m);
        var id = Bakim(_depoA, (_mat, 2m, false), (_mat2, 3m, false));

        _maintenance.Cancel(_depoAOturum, id, "iptal");

        Assert.Equal(10m, Bakiye(_mat, _depoA));
        Assert.Equal(10m, Bakiye(_mat2, _depoA));
        Assert.Equal(_depoA, Assert.Single(TersKayitlar(_mat)).Branch);
        Assert.Equal(_depoA, Assert.Single(TersKayitlar(_mat2)).Branch);
    }

    /// <summary>Lokasyonsuz (eski) bakım iptalinde ters kayıt da ATANMAMIŞ'a döner — simetri korunur.</summary>
    [Fact]
    public void Eski_Lokasyonsuz_Bakimin_Iptali_ATANMAMISA_Doner()
    {
        Stok(_mat, null, 10m);
        var id = Bakim(null, (_mat, 4m, false));

        _maintenance.Cancel(_depoAOturum, id, "iptal");

        Assert.Equal(10m, Bakiye(_mat, StockBalanceWriter.Unassigned));
        Assert.Null(Assert.Single(TersKayitlar(_mat)).Branch);
    }

    /// <summary>İptal İKİ KEZ çağrılırsa stok İKİNCİ KEZ geri eklenmez (mevcut idempotency korundu).</summary>
    [Fact]
    public void Cift_Iptal_Stogu_Iki_Kez_Geri_Eklemez()
    {
        Stok(_mat, _depoA, 10m);
        var id = Bakim(_depoA, (_mat, 4m, false));

        _maintenance.Cancel(_depoAOturum, id, "iptal");
        _maintenance.Cancel(_depoAOturum, id, "tekrar iptal");

        Assert.Equal(10m, Bakiye(_mat, _depoA));
        Assert.Equal(2, Hareketler(_mat).Count);   // 1 usage + 1 reverse (ikinci ters kayıt YOK)
    }

    // ══════════════ 4. EKİP STOĞU / KARIŞIK SATIRLAR ══════════════

    /// <summary>15 — Ekip stoğu işaretli satır HİÇBİR depodan düşmez (davranış değişmedi).</summary>
    [Fact]
    public void Ekip_Stogu_Isaretli_Satir_Hicbir_Depodan_Dusmez()
    {
        Stok(_mat, _depoA, 10m);

        Bakim(_depoA, (_mat, 4m, true));

        Assert.Equal(10m, Bakiye(_mat, _depoA));
        Assert.Empty(Hareketler(_mat));   // hiç hareket üretmedi
    }

    /// <summary>16 — Karışık satırlar: yalnız İŞARETSİZ malzeme seçilen depodan düşer.</summary>
    [Fact]
    public void Karisik_Satirlar_Yalniz_Isaretsiz_Olan_Secilen_Depodan_Duser()
    {
        Stok(_mat, _depoB, 10m);
        Stok(_mat2, _depoB, 10m);

        Bakim(_depoB, (_mat, 3m, false), (_mat2, 7m, true));

        Assert.Equal(7m, Bakiye(_mat, _depoB));    // işaretsiz → düştü
        Assert.Equal(10m, Bakiye(_mat2, _depoB));  // işaretli → düşmedi
        Assert.Single(Hareketler(_mat));
        Assert.Empty(Hareketler(_mat2));
    }

    /// <summary>Karışık satırlı bakımın iptali: yalnız gerçekten düşen satır geri döner, diğeri ŞİŞMEZ.</summary>
    [Fact]
    public void Karisik_Satirli_Bakimin_Iptali_Ekip_Stogunu_Sismez()
    {
        Stok(_mat, _depoB, 10m);
        Stok(_mat2, _depoB, 10m);
        var id = Bakim(_depoB, (_mat, 3m, false), (_mat2, 7m, true));

        _maintenance.Cancel(_depoAOturum, id, "iptal");

        Assert.Equal(10m, Bakiye(_mat, _depoB));
        Assert.Equal(10m, Bakiye(_mat2, _depoB));   // hiç düşmemişti → geri de eklenmedi
        Assert.Empty(Hareketler(_mat2));
    }

    // ══════════════ 5. IDEMPOTENCY ══════════════

    /// <summary>17 — Aynı operationId ikinci kez gönderilirse ÇİFT stok hareketi oluşmaz.</summary>
    [Fact]
    public void Ayni_OperationId_Ikinci_Kez_Cift_Hareket_Uretmez()
    {
        Stok(_mat, _depoA, 10m);
        var op = Op();
        var dto = new NewMaintenance(VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 5000m,
            Materials: new[] { new MaintenanceMaterialLine(_mat, 4m) }, StockLocationId: _depoA);

        var id1 = _maintenance.Save(_depoAOturum, dto, op);
        var id2 = _maintenance.Save(_depoAOturum, dto, op);

        Assert.Equal(id1, id2);
        Assert.Equal(6m, Bakiye(_mat, _depoA));
        Assert.Single(Hareketler(_mat));
    }

    // ══════════════ 6. GÜNLÜK FAALİYET YOLLARI ══════════════

    /// <summary>7 — Günlük Faaliyet → Bakım yolu AYNI lokasyon semantiğini kullanır.</summary>
    [Fact]
    public void Gunluk_Faaliyet_Bakim_Yolu_Ayni_Lokasyonu_Kullanir()
    {
        Stok(_mat, _depoB, 10m);

        _daily.SaveMaintenanceActivity(_depoAOturum, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 6000m,
            Materials: new[] { new MaintenanceMaterialLine(_mat, 4m) },
            StockLocationId: _depoB), Op());

        Assert.Equal(6m, Bakiye(_mat, _depoB));
        Assert.Equal(0m, Bakiye(_mat, StockBalanceWriter.Unassigned));
        Assert.Equal(_depoB, Assert.Single(Hareketler(_mat)).Branch);
    }

    /// <summary>7b — "İlave Yağ / İlave Filtre / Tamir" yolu da aynı lokasyonu kullanır.</summary>
    [Theory]
    [InlineData(ExtraActivityTypes.ExtraOil)]
    [InlineData(ExtraActivityTypes.ExtraFilter)]
    [InlineData(ExtraActivityTypes.Repair)]
    public void Gunluk_Faaliyet_Ilave_Islem_Yolu_Ayni_Lokasyonu_Kullanir(string tur)
    {
        Stok(_mat, _depoB, 10m);

        _daily.SaveExtraActivity(_depoAOturum, tur, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: "", PerformedKm: 7000m,
            Materials: new[] { new MaintenanceMaterialLine(_mat, 2m) },
            StockLocationId: _depoB), Op());

        Assert.Equal(8m, Bakiye(_mat, _depoB));
        Assert.Equal(_depoB, Assert.Single(Hareketler(_mat)).Branch);
    }

    // ══════════════ 7. RAPORLAR ══════════════

    /// <summary>21 — Stok Durumu raporu bakım tüketimini SEÇİLEN depoda gösterir.</summary>
    [Fact]
    public void Stok_Durumu_Raporu_Bakim_Tuketimini_Secilen_Depoda_Gosterir()
    {
        Stok(_mat, _depoA, 10m);
        Stok(_mat, _depoB, 10m);
        Bakim(_depoB, (_mat, 6m, false));

        var a = _reports.Run(_depoAOturum, "stock", new ReportRequest(true, LocationIds: new[] { _depoA }));
        var b = _reports.Run(_depoAOturum, "stock", new ReportRequest(true, LocationIds: new[] { _depoB }));

        Assert.Equal(10m, StokKolonu(a));
        Assert.Equal(4m, StokKolonu(b));   // bakım BURADAN düştü
    }

    private static decimal StokKolonu(TableModel t)
    {
        var i = 0;
        for (; i < t.Headers.Count; i++) if (t.Headers[i] == "Stok") break;
        return t.Rows.Where(r => (string?)r[0] == "BKM-1").Sum(r => Money.Parse((string?)r[i]));
    }

    /// <summary>22 — Bakım raporundaki "Şube" (op_branch_id) ile STOK LOKASYONU ayrı kavramlardır.
    /// Kullanıcı Depo A'da çalışıp Depo B'den malzeme çekerse: rapor Depo A'yı, stok Depo B'yi gösterir.</summary>
    [Fact]
    public void Bakim_Raporundaki_Sube_Stok_Lokasyonuyla_Karismaz()
    {
        Stok(_mat, _depoB, 10m);
        Bakim(_depoB, (_mat, 3m, false));

        var rapor = _reports.Run(_depoAOturum, "maintenance",
            new ReportRequest(true, FromDate: 0, ToDate: 9_999_999_999_999));
        var subeKolonu = rapor.Headers.ToList().IndexOf("Şube");
        Assert.True(subeKolonu >= 0);
        var satir = Assert.Single(rapor.Rows);
        Assert.Equal("Depo A", (string?)satir[subeKolonu]);   // KAYDI İŞLEYEN şube (oturum) — değişmedi

        Assert.Equal(_depoB, Assert.Single(Hareketler(_mat)).Branch);   // STOK ise Depo B'den çıktı
    }

    // ══════════════ 8. EXCEL İÇE AKTARIM ══════════════

    /// <summary>İçe aktarım oturumu hedef şubeyi taşır; bakım tüketimi o depodan düşer.
    /// (Import yolu yeniden tasarlanmadı — yalnız yeni sözleşmeye doğru bağlandığı doğrulanıyor.)</summary>
    [Fact]
    public void Ice_Aktarim_Oturumunun_Subesi_Bakim_Deposu_Olarak_Kullanilabilir()
    {
        Stok(_mat, _depoB, 10m);

        // ImportSession deseni: seçilen hedef şubeyle oturum kopyası (API ve masaüstünde aynı).
        var importSession = new SessionContext(_depoAOturum.UserId, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoB };

        _maintenance.Save(importSession, new NewMaintenance(
            VehicleId: _vehicle, DefinitionId: _def, PerformedKm: 8000m,
            Materials: new[] { new MaintenanceMaterialLine(_mat, 2m) },
            StockLocationId: importSession.OperatingBranchId), Op());

        Assert.Equal(8m, Bakiye(_mat, _depoB));
    }

    // ══════════════ 9. ÇEVRİMDIŞI → SENKRON ══════════════

    /// <summary>18+19 — Çevrimdışı girilen bakımın lokasyonu senkron sonrası SUNUCUDA korunur;
    /// aynı paket tekrarlanırsa KOPYA hareket oluşmaz.</summary>
    [Fact]
    public void Cevrimdisi_Bakim_Senkron_Sonrasi_Lokasyonu_Korur_ve_Kopya_Uretmez()
    {
        var serverPath = Path.Combine(Path.GetTempPath(), "dw_bkm04_srv_" + Guid.NewGuid().ToString("N") + ".db");
        var server = new SqliteConnectionFactory(serverPath);
        try
        {
            new MigrationRunner(server).Run();
            SeedCompany(server, "A");
            // Şubeler iş senkronunda taşınmaz (SNK-12) → sunucuda aynalanır.
            AynalaSubeler(server);

            Stok(_mat, _depoB, 10m);
            Bakim(_depoB, (_mat, 4m, false));   // ÇEVRİMDIŞI (yerel SQLite)

            var snapshot = new BusinessSyncService(_factory, _clock).BuildSnapshot("A");
            using (var doc = JsonDocument.Parse(snapshot))
                new BusinessSyncService(server, _clock).Apply("A", doc.RootElement);
            // Aynı paket İKİNCİ kez (idempotency)
            using (var doc = JsonDocument.Parse(snapshot))
                new BusinessSyncService(server, _clock).Apply("A", doc.RootElement);

            var sunucu = SunucuHareketleri(server, _mat);
            var mv = Assert.Single(sunucu);              // kopya YOK
            Assert.Equal(_depoB, mv.Branch);             // LOKASYON KORUNDU
            Assert.Equal(4m, mv.Qty);
        }
        finally { try { File.Delete(serverPath); } catch { } }
    }

    private void AynalaSubeler(SqliteConnectionFactory server)
    {
        foreach (var b in _branches.List(_depoAOturum))
        {
            using var conn = server.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id,'A',@n,'branch',1,1,1,0) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", b.Id);
            cmd.AddWithValue("@n", b.Name);
            cmd.ExecuteNonQuery();
        }
    }

    private static List<(string? Branch, decimal Qty)> SunucuHareketleri(SqliteConnectionFactory f, string materialId)
    {
        var list = new List<(string?, decimal)>();
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT branch_id, quantity FROM stock_movements WHERE company_id='A' AND material_id=@m AND movement_type='usage';";
        cmd.AddWithValue("@m", materialId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.IsDBNull(0) ? null : r.GetString(0), Money.Parse(r.GetString(1))));
        return list;
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
