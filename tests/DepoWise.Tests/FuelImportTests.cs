using System.Collections.Generic;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Yakıt içe aktarımı (Excel → sistem). Kullanıcı şartı: "elde tutulan Excel'de alanlar eksik, veriler
/// sıkıntı olmadan girmeli". Bu testler eksik alanların makul varsayılanlara düştüğünü, araç eşlemesinin
/// plaka ile de çalıştığını, depo yetersizliğinin ÖNCEDEN bildirildiğini ve aynı dosyanın ikinci kez
/// aktarılmasının kayıt TEKRARLAMADIĞINI sabitler.
/// </summary>
public class FuelImportTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly VehicleService _vehicles;
    private readonly FuelService _fuel;
    private readonly LookupService _lookups;
    private readonly FuelImportService _import;
    private readonly FuelDepotImportService _depotImport;
    private readonly SessionContext _admin;

    public FuelImportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_fimp_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _vehicles = new VehicleService(_factory, _clock);
        _fuel = new FuelService(_factory, _clock);
        _lookups = new LookupService(_factory, _clock);
        _import = new FuelImportService(_fuel, _vehicles, _lookups);
        _depotImport = new FuelDepotImportService(_fuel, _lookups);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static ImportRow Row(int n, params (string Col, string? Val)[] cells)
        => new(n, cells.ToDictionary(c => c.Col, c => c.Val));

    /// <summary>Depoya yakıt koyar (dağıtımların çalışabilmesi için şart).</summary>
    private void FillDepot(decimal liters, decimal price = 40m)
        => _fuel.AddDepotEntry(_admin, new NewDepotEntry(liters, price), Guid.NewGuid().ToString("N"));

    // ── Depo girişi ────────────────────────────────────────────────────────────────────────
    [Fact]
    public void DepoGirisi_Aktarilir_BakiyeArtar()
    {
        var rows = new[]
        {
            Row(2, ("Tarih", "01.05.2026"), ("Litre", "1000"), ("Birim Fiyat", "42,50"), ("Fatura No", "F-1")),
            Row(3, ("Tarih", "02.05.2026"), ("Litre", "500"), ("Birim Fiyat", "43")),
        };

        var res = _depotImport.Commit(_admin, rows);

        Assert.Equal(2, res.Added);
        Assert.Equal(0, res.Failed);
        Assert.Equal(1500m, _fuel.GetDepotBalance(_admin));
        // Güncel fiyat = EN SON tarihli giriş (sıralama doğru çalışıyor mu).
        Assert.Equal(43m, _fuel.GetCurrentFuelPrice(_admin));
    }

    [Fact]
    public void DepoGirisi_LitreVeFiyat_Zorunlu()
    {
        var rows = new[]
        {
            Row(2, ("Litre", ""), ("Birim Fiyat", "42")),
            Row(3, ("Litre", "100"), ("Birim Fiyat", "")),
            Row(4, ("Litre", "-5"), ("Birim Fiyat", "42")),
        };

        var dry = _depotImport.DryRun(_admin, rows);

        Assert.Equal(0, dry.Valid);
        Assert.Equal(3, dry.Failed);
    }

    // ── Dağıtım: eksik alanlar ─────────────────────────────────────────────────────────────
    /// <summary>Kullanıcının asıl derdi: Excel'de yalnız araç + litre var. Kayıt GİRMELİ.</summary>
    [Fact]
    public void Dagitim_YalnizAracVeLitre_Yeterli()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", "34 ABC 123", CurrentMeter: 5000m));

        var res = _import.Commit(_admin, new[] { Row(2, ("Araç", "ARAC-1"), ("Litre", "50")) });

        Assert.Equal(1, res.Added);
        Assert.Equal(0, res.Failed);
        var d = _fuel.ListDistributions(_admin).Single();
        Assert.Equal(50m, d.Liters);
        Assert.Equal(40m, d.UnitPrice);        // fiyat boştu → depo fiyatı kullanıldı
        Assert.Equal(950m, _fuel.GetDepotBalance(_admin));
    }

    /// <summary>Sayaç boşsa aracın MEVCUT sayacı yazılır ve sayaç DEĞİŞMEZ (geçmiş kayıt sayacı bozmaz).</summary>
    [Fact]
    public void Dagitim_SayacBos_AracinSayaciDegismez()
    {
        FillDepot(1000m);
        var vid = _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        _import.Commit(_admin, new[] { Row(2, ("Araç", "ARAC-1"), ("Litre", "50")) });

        Assert.Equal(5000m, _vehicles.List(_admin).Single(v => v.Id == vid).CurrentMeter);
    }

    /// <summary>Sayaç doluysa ve ileriyse aracın sayacı güncellenir (canlı ekranla aynı kural).</summary>
    [Fact]
    public void Dagitim_SayacIleri_AracinSayaciGuncellenir()
    {
        FillDepot(1000m);
        var vid = _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        _import.Commit(_admin, new[] { Row(2, ("Araç", "ARAC-1"), ("Litre", "50"), ("Sayaç", "5400")) });

        Assert.Equal(5400m, _vehicles.List(_admin).Single(v => v.Id == vid).CurrentMeter);
    }

    /// <summary>Araç PLAKA ile de eşlenmeli (Excel'de iç kod değil plaka yazar). Boşluk/harf duyarsız.</summary>
    [Fact]
    public void Dagitim_PlakaIleEslesir_BoslukDuyarsiz()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", "34 ABC 123", CurrentMeter: 5000m));

        var res = _import.Commit(_admin, new[]
        {
            Row(2, ("Araç", "34abc123"), ("Litre", "10")),      // boşluksuz + küçük harf
            Row(3, ("Araç", "34 ABC 123"), ("Litre", "10")),    // birebir
        });

        Assert.Equal(2, res.Added);
        Assert.Equal(0, res.Failed);
    }

    [Fact]
    public void Dagitim_TanimsizArac_SatirReddedilir_DigerleriGirer()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        var res = _import.Commit(_admin, new[]
        {
            Row(2, ("Araç", "YOK-BOYLE"), ("Litre", "10")),
            Row(3, ("Araç", "ARAC-1"), ("Litre", "20")),
        });

        Assert.Equal(1, res.Added);
        Assert.Equal(1, res.Failed);
        Assert.Contains(res.Errors, e => e.RowNumber == 2 && e.Message.Contains("Araç bulunamadı"));
        Assert.Equal(20m, _fuel.ListDistributions(_admin).Single().Liters);   // geçerli satır girdi
    }

    [Fact]
    public void Dagitim_LitreZorunlu_VeVirgullüOndalikOkunur()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        var dry = _import.DryRun(_admin, new[]
        {
            Row(2, ("Araç", "ARAC-1"), ("Litre", "")),        // zorunlu
            Row(3, ("Araç", "ARAC-1"), ("Litre", "12,5")),    // Türk Excel'i virgül yazar
        });

        Assert.Equal(1, dry.Valid);
        Assert.Equal(1, dry.Failed);
    }

    // ── Depo yetersizliği: kullanıcı ÖNCEDEN uyarılmalı ────────────────────────────────────
    /// <summary>Yalnız "araca yakıt verdim" Excel'i varsa depo boştur → dağıtım servisi reddeder.
    /// DryRun bunu satır satır patlamadan ÖNCE, tek net mesajla söylemeli.</summary>
    [Fact]
    public void Dagitim_DepoYetersiz_DryRunOncedenUyarir()
    {
        FillDepot(30m);   // depoda 30 L var
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        var dry = _import.DryRun(_admin, new[]
        {
            Row(2, ("Araç", "ARAC-1"), ("Litre", "50")),
            Row(3, ("Araç", "ARAC-1"), ("Litre", "40")),   // toplam 90 L > 30 L
        });

        Assert.Equal(2, dry.Valid);   // satırların kendisi geçerli
        Assert.Contains(dry.Errors, e => e.Message.Contains("DEPO YETERSİZ") && e.Message.Contains("60"));
    }

    [Fact]
    public void Dagitim_DepoYetersiz_YetenSatirlarGirer_DigerleriHataVerir()
    {
        FillDepot(60m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        var res = _import.Commit(_admin, new[]
        {
            Row(2, ("Araç", "ARAC-1"), ("Tarih", "01.05.2026"), ("Litre", "50")),
            Row(3, ("Araç", "ARAC-1"), ("Tarih", "02.05.2026"), ("Litre", "50")),   // bakiye 10 L kaldı → reddedilir
        });

        Assert.Equal(1, res.Added);
        Assert.Equal(1, res.Failed);
        Assert.Contains(res.Errors, e => e.Message.Contains("Depo yakıtı yetersiz"));
        Assert.Equal(10m, _fuel.GetDepotBalance(_admin));   // negatife DÜŞMEDİ
    }

    // ── Tekrar aktarım (idempotency) ──────────────────────────────────────────────────────
    /// <summary>Aynı dosya ikinci kez aktarılırsa kayıt TEKRARLANMAMALI.</summary>
    [Fact]
    public void Dagitim_AyniDosyaIkiKez_KayitTekrarlanmaz()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));
        var rows = new[]
        {
            Row(2, ("Araç", "ARAC-1"), ("Tarih", "01.05.2026"), ("Litre", "50"), ("Sayaç", "5100")),
            Row(3, ("Araç", "ARAC-1"), ("Tarih", "02.05.2026"), ("Litre", "60"), ("Sayaç", "5200")),
        };

        var first = _import.Commit(_admin, rows);
        var second = _import.Commit(_admin, rows);

        Assert.Equal(2, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Updated);                       // "zaten vardı, atlandı"
        Assert.Equal(2, _fuel.ListDistributions(_admin).Count);
        Assert.Equal(890m, _fuel.GetDepotBalance(_admin));     // bakiye İKİ KEZ düşmedi
    }

    /// <summary>Aynı araca aynı gün aynı litre AYRI satırlarda meşru olabilir — ikisi de korunmalı
    /// (satır numarası deterministik anahtara dahil edildiği için).</summary>
    [Fact]
    public void Dagitim_AyniGunAyniLitreIkiSatir_IkisiDeGirer()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        var res = _import.Commit(_admin, new[]
        {
            Row(2, ("Araç", "ARAC-1"), ("Tarih", "01.05.2026"), ("Litre", "50")),
            Row(3, ("Araç", "ARAC-1"), ("Tarih", "01.05.2026"), ("Litre", "50")),
        });

        Assert.Equal(2, res.Added);
        Assert.Equal(2, _fuel.ListDistributions(_admin).Count);
    }

    [Fact]
    public void DepoGirisi_AyniDosyaIkiKez_KayitTekrarlanmaz()
    {
        var rows = new[] { Row(2, ("Tarih", "01.05.2026"), ("Litre", "1000"), ("Birim Fiyat", "42")) };

        _depotImport.Commit(_admin, rows);
        var second = _depotImport.Commit(_admin, rows);

        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Updated);
        Assert.Equal(1000m, _fuel.GetDepotBalance(_admin));   // 2000 OLMADI
    }

    // ── Tarih sırası ──────────────────────────────────────────────────────────────────────
    /// <summary>Excel karışık sırada olabilir. Commit tarihe göre sıralar → prev_meter zinciri doğru kurulur.</summary>
    [Fact]
    public void Dagitim_KarisikTarihSirasi_SayacZinciriDogruKurulur()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        _import.Commit(_admin, new[]
        {
            Row(2, ("Araç", "ARAC-1"), ("Tarih", "03.05.2026"), ("Litre", "10"), ("Sayaç", "5300")),
            Row(3, ("Araç", "ARAC-1"), ("Tarih", "01.05.2026"), ("Litre", "10"), ("Sayaç", "5100")),
            Row(4, ("Araç", "ARAC-1"), ("Tarih", "02.05.2026"), ("Litre", "10"), ("Sayaç", "5200")),
        });

        // En eskiden en yeniye: 5000→5100→5200→5300. prev_meter zinciri kopmamalı.
        var list = _fuel.ListDistributions(_admin).OrderBy(d => d.DistributionDate).ToList();
        Assert.Equal(3, list.Count);
        Assert.Equal((5000m, 5100m), (list[0].PrevMeter, list[0].CurrentMeter));
        Assert.Equal((5100m, 5200m), (list[1].PrevMeter, list[1].CurrentMeter));
        Assert.Equal((5200m, 5300m), (list[2].PrevMeter, list[2].CurrentMeter));
    }

    /// <summary>REGRESYON: Türk Excel'i "12,5" yazar. Money.Parse virgülü BİNLİK ayırıcı sayıp 125 üretiyordu
    /// (10 kat sessiz hata). Yakıt import'u kendi ParseDecimal'ını kullanır — litre birebir girmeli.</summary>
    [Fact]
    public void Dagitim_VirgulluLitre_OnKatBozulmaz()
    {
        FillDepot(1000m);
        _vehicles.Create(_admin, new NewVehicle("ARAC-1", CurrentMeter: 5000m));

        _import.Commit(_admin, new[] { Row(2, ("Araç", "ARAC-1"), ("Litre", "12,5"), ("Birim Fiyat", "42,75")) });

        var d = _fuel.ListDistributions(_admin).Single();
        Assert.Equal(12.5m, d.Liters);        // 125 DEĞİL
        Assert.Equal(42.75m, d.UnitPrice);
        Assert.Equal(987.5m, _fuel.GetDepotBalance(_admin));
    }

    [Fact]
    public void DepoGirisi_VirgulluDeger_OnKatBozulmaz()
    {
        _depotImport.Commit(_admin, new[] { Row(2, ("Litre", "100,5"), ("Birim Fiyat", "42,75")) });

        Assert.Equal(100.5m, _fuel.GetDepotBalance(_admin));   // 1005 DEĞİL
        Assert.Equal(42.75m, _fuel.GetCurrentFuelPrice(_admin));
    }

    // ── Yetki + tenant ────────────────────────────────────────────────────────────────────
    [Fact]
    public void Yetkisiz_AktarimYapamaz()
    {
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "personel", "p12345", RoleKeys.Staff);
        var staff = new SessionContext(uid, "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);   // yetki verilmedi

        Assert.Throws<ForbiddenException>(() => _import.Commit(staff, new[] { Row(2, ("Araç", "X"), ("Litre", "5")) }));
        Assert.Throws<ForbiddenException>(() => _depotImport.Commit(staff, new[] { Row(2, ("Litre", "5"), ("Birim Fiyat", "1")) }));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
