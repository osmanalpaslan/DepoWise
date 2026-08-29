using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RPR-T — KAPSAMLI RAPOR TARAMASI ═══ (kullanıcı isteği 2026-08-27)
///
/// <b>Neden bu sınıf var.</b> Mevcut rapor testleri her raporu tek tek, kendi kurallarıyla sınıyordu
/// (kolon, kapsam, yetki, tarih). Eksik olan ÇAPRAZ güvenceydi: <i>"normal yoldan girdiğim kayıt bu
/// raporda görünüyor mu?"</i> Kullanıcının bildirdiği hata tam olarak bu sınıftandı. Burada TEK bir
/// firmaya her modülden birer kayıt girilir ve <b>katalogdaki HER rapor</b> geniş tarih aralığıyla
/// çalıştırılıp <b>en az bir satır döndürdüğü</b> doğrulanır.
///
/// <b>Kataloğa yeni rapor eklenirse</b> bu test onu otomatik kapsar: ya veri üretilip listelenmeli, ya
/// da <see cref="VeriUretilmeyen"/> içine GEREKÇESİYLE yazılmalı. Böylece "sessizce boş rapor" eklenemez.
///
/// 🔒 Tamamen yerel SQLite; canlı veriye dokunmaz.
/// </summary>
public class RaporKapsamliTaramaTests : IDisposable
{
    private const string Co = "RPRT";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    /// <summary>Tüm test verisini kapsayan geniş aralık.</summary>
    private const long Gunes = 1_600_000_000_000, Batis = 1_800_000_000_000;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    /// <summary>Bu raporlar için veri üretilmez — HER BİRİNİN gerekçesi yazılıdır.
    /// Boş bırakmak serbest DEĞİLDİR: gerekçesiz eklenen anahtar testi düşürür.</summary>
    private static readonly IReadOnlyDictionary<string, string> VeriUretilmeyen =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["materials-nontemplate"] = "Şablon DIŞI malzemeleri listeler; bu senaryodaki malzeme şablonludur (kendi testi TemplateReportTests'te).",
            ["vehicles-nontemplate"] = "Şablon DIŞI araçları listeler; bu senaryodaki araç şablonludur (kendi testi TemplateReportTests'te).",
        };

    public RaporKapsamliTaramaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_rprt_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", Co);
            cmd.ExecuteNonQuery();
        }

        var users = new UserService(_f, _clock);
        var uid = users.EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _reports = new ReportService(_f, _clock);

        Tohumla();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private long Simdi => _clock.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Her modülden BİRER normal kayıt — ekranların kullandığı servislerin aynısıyla.</summary>
    private void Tohumla()
    {
        var depo = new DepoWise.Infrastructure.Organization.BranchService(_f, _clock).Create(_admin, new NewBranch("Merkez Depo"));

        var lookups = new LookupService(_f, _clock);
        var birim = lookups.AddUnit(_admin, "Adet");
        var tedarikci = lookups.AddSupplier(_admin, "Tedarikçi A");

        // Personel
        var personel = new PersonnelService(_f, new ScopeResolver(_f), _clock)
            .Create(_admin, new NewPersonnel("Ali Veli", "Operatör", "5550000000", depo));

        // Malzeme (şablonlu) + stok girişi
        var matTemplate = new MaterialTemplateService(_f, _clock)
            .Create(_admin, new NewMaterialTemplate("ŞBL-M", "Filtre şablonu", UnitId: birim));
        var materials = new MaterialService(_f, _clock);
        var mat = materials.Create(_admin, new NewMaterial("M-1", "Yağ filtresi", UnitId: birim,
            SupplierId: tedarikci, MinStock: 5m, TemplateId: matTemplate));

        var stock = new StockService(_f, _clock);
        stock.ReceiveIn(_admin, new[] { new StockLine(mat, 50m, 10m) }, Op(), branchId: depo);
        _clock.Advance(60_000);
        stock.IssueOut(_admin, new[] { new StockLine(mat, 5m) }, Op(), branchId: depo);
        _clock.Advance(60_000);
        stock.Count(_admin, new[] { new CountLine(mat, 44m) }, "sayım", Op(), branchId: depo);
        _clock.Advance(60_000);

        // Araç (şablonlu)
        var aracSablon = new VehicleTemplateService(_f, _clock)
            .Create(_admin, new NewVehicleTemplate("ŞBL-A", "Kamyon şablonu"));
        var vehicles = new VehicleService(_f, _clock);
        var arac = vehicles.Create(_admin, new NewVehicle("ARC-1", "06ABC01", 2020, 1000m, "km", depo,
            TemplateId: aracSablon));

        // Bakım
        var bakimTanim = new MaintenanceDefinitionService(_f, _clock)
            .Create(_admin, new NewMaintenanceDefinition("Periyodik", 10000m, "km"));
        new MaintenanceService(_f, _clock).Save(_admin, new NewMaintenance(arac, bakimTanim,
            TechnicianId: personel, PerformedKm: 1200m, PerformedDate: Simdi), Op());
        _clock.Advance(60_000);

        // Yakıt: depo girişi + dağıtım
        var fuel = new FuelService(_f, _clock);
        fuel.AddDepotEntry(_admin, new NewDepotEntry(1000m, 40m, SupplierId: tedarikci, EntryDate: Simdi), Op());
        _clock.Advance(60_000);
        fuel.Distribute(_admin, new NewDistribution(arac, 100m, 1300m, 40m, DistributionDate: Simdi), Op());
        _clock.Advance(60_000);

        // Muayene / sigorta — SONRAKİ TARİHİ OLAN normal kayıt
        new InspectionService(_f, _clock).Save(_admin,
            new NewInspection(arac, "inspection", Simdi, Simdi + 86_400_000L * 30));

        // Günlük faaliyet (ADR-182 · S4): "Günlük Faaliyet — Detay" raporunun verisi de NORMAL yoldan
        // girilir — muafiyet listesine yazmak yerine gerçek kayıt üretilir (bu sınıfın asıl kuralı).
        new DailyActivityService(_f, new MaintenanceService(_f, _clock), _clock).SaveMovement(_admin,
            new NewMovementActivity("movement", VehicleId: arac, FromLocationId: depo,
                OperatorId: personel, Description: "Sahaya sevk", ActivityDate: Simdi), Op());
        _clock.Advance(60_000);

        // Talep
        new RequestService(_f, stock, _clock).Create(_admin, new NewRequest(
            new[] { new RequestItemInput(mat, 2m) }, BranchId: depo, RequesterId: personel,
            RequestDate: Simdi));

        // Ön muhasebe: cari + kasa + fatura + tahsilat (imzalar AccountingReportTests ile aynı)
        var cari = new PartyService(_f, _clock).Create(_admin, new NewParty("C-001", "Örnek Ltd.", PartyTypes.Both));
        var ledger = new PartyLedgerService(_f, _clock);
        var invoices = new InvoiceService(_f, stock, ledger, _clock);
        var finance = new FinanceService(_f, ledger, _clock);

        var kasa = finance.CreateAccount(_admin, new NewFinanceAccount("K-1", "Merkez Kasa",
            FinanceAccountKinds.Cash, BranchId: depo));
        var fatura = invoices.Create(_admin, new NewInvoice(InvoiceDirections.Sales, cari,
            new[] { new NewInvoiceLine(mat, null, null, 1m, 1000m) }, Op(), BranchId: depo)).Id;
        finance.Add(_admin, new NewFinanceEntry(kasa, FinanceTxnTypes.Receipt, 300m, Op(),
            PartyId: cari, BranchId: depo,
            Allocations: new[] { new InvoiceAllocationInput(fatura, 300m) }));
    }

    /// <summary>Katalogdaki tüm rapor anahtarları (tek kaynak — yeni rapor otomatik kapsanır).</summary>
    public static TheoryData<string> TumRaporlar()
    {
        var d = new TheoryData<string>();
        foreach (var r in ReportCatalog.All) d.Add(r.Key);
        return d;
    }

    /// <summary>
    /// ⭐ ASIL GÜVENCE: her rapor, normal yoldan girilen veriyi GÖSTERMELİ. Boş dönen bir rapor,
    /// kullanıcı için "kaydım kayboldu" demektir — kullanıcının bildirdiği hata tam olarak buydu.
    /// </summary>
    [Theory]
    [MemberData(nameof(TumRaporlar))]
    public void RPRT1_Her_Rapor_Normal_Girilen_Veriyi_Gosterir(string anahtar)
    {
        var tablo = _reports.Run(_admin, anahtar, new ReportRequest(Executed: true, FromDate: Gunes, ToDate: Batis));

        if (VeriUretilmeyen.TryGetValue(anahtar, out var gerekce))
        {
            Assert.False(string.IsNullOrWhiteSpace(gerekce), $"{anahtar}: gerekçesiz muafiyet kabul edilmez.");
            return;
        }

        Assert.True(tablo.Rows.Count > 0,
            $"«{ReportCatalog.ByKey(anahtar)!.Name}» ({anahtar}) raporu BOŞ döndü — oysa bu modüle normal " +
            "yoldan kayıt girildi. Kullanıcı için bu 'girdiğim kayıt raporda yok' demektir.");
    }

    /// <summary>Her raporun başlık satırı olmalı; boş başlık, ekranda kolonsuz tablo demektir.</summary>
    [Theory]
    [MemberData(nameof(TumRaporlar))]
    public void RPRT2_Her_Raporun_Kolonlari_Var(string anahtar)
    {
        var tablo = _reports.Run(_admin, anahtar, new ReportRequest(Executed: true, FromDate: Gunes, ToDate: Batis));
        Assert.True(tablo.Headers.Count > 0, $"{anahtar}: rapor kolonsuz döndü.");
        Assert.All(tablo.Headers, h => Assert.False(string.IsNullOrWhiteSpace(h), $"{anahtar}: boş kolon başlığı var."));
    }

    /// <summary>⭐ Satır sayısı ile kolon sayısı tutmalı — tutmazsa ekranda veri kayar ve yanlış
    /// kolonda görünür (kullanıcı bunu "rapor yanlış" olarak yaşar).</summary>
    [Theory]
    [MemberData(nameof(TumRaporlar))]
    public void RPRT3_Satirlar_Kolon_Sayisiyla_Uyumlu(string anahtar)
    {
        var tablo = _reports.Run(_admin, anahtar, new ReportRequest(Executed: true, FromDate: Gunes, ToDate: Batis));
        foreach (var satir in tablo.Rows)
            Assert.True(satir.Count == tablo.Headers.Count,
                $"{anahtar}: satırda {satir.Count} hücre var ama {tablo.Headers.Count} kolon başlığı tanımlı.");
    }

    /// <summary>
    /// ⭐ Rapor TABLOSUNUN başlığı ile KATALOG adı aynı şeyi anlatmalı. Başlık kodda ayrı yazılıyor;
    /// katalogda ad değişip başlık unutulursa kullanıcı listede bir ad, tablonun üstünde BAŞKA bir ad
    /// görür. RPR-V3'te tam olarak bu oldu: katalog "Yakıt Depo Girişi" olurken tablo başlığı
    /// "Depo Girişi Raporu" kaldı. İlk kelime eşitliği bu kaymayı yakalar, üslup farkına karışmaz
    /// ("Personel Listesi" → "Personel Raporu" gibi meşru farklar serbest kalır).
    /// </summary>
    [Theory]
    [MemberData(nameof(TumRaporlar))]
    public void RPRT5_Tablo_Basligi_Katalog_Adiyla_Uyumlu(string anahtar)
    {
        var tablo = _reports.Run(_admin, anahtar, new ReportRequest(Executed: true, FromDate: Gunes, ToDate: Batis));
        var katalogAdi = ReportCatalog.ByKey(anahtar)!.Name;

        Assert.False(string.IsNullOrWhiteSpace(tablo.Title), $"{anahtar}: rapor tablosunun başlığı boş.");

        static string IlkKelime(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        Assert.True(IlkKelime(tablo.Title) == IlkKelime(katalogAdi),
            $"{anahtar}: katalogda «{katalogAdi}», tablo başlığında «{tablo.Title}» yazıyor — " +
            "ad değişikliği iki yerden birinde unutulmuş.");
    }

    /// <summary>⭐ MUAYENE: "sonraki tarih" GİRİLMEMİŞ belge de raporda görünmeli. Tarih süzgeci
    /// <c>next_date</c> üzerindedir ve NULL karşılaştırması daima false döner → böyle bir kayıt
    /// HİÇBİR tarih aralığında listelenmezdi, oysa ekranda duruyordu ve rapor sıralaması NULL'ları
    /// bilinçli olarak sona koyuyordu (yani var olmaları bekleniyordu).</summary>
    [Fact]
    public void RPRT4_Sonraki_Tarihi_Olmayan_Muayene_Raporda_Gorunur()
    {
        var arac = new VehicleService(_f, _clock).Create(_admin, new NewVehicle("ARC-2", "06ABC02", 2021, 5m, "km"));
        new InspectionService(_f, _clock).Save(_admin, new NewInspection(arac, "insurance", Simdi, null));

        var tablo = _reports.Run(_admin, "inspection", new ReportRequest(Executed: true, FromDate: Gunes, ToDate: Batis));

        Assert.Contains(tablo.Rows, r => r.Any(h => (h as string)?.Contains("ARC-2", StringComparison.Ordinal) == true));
    }
}
