using System.Collections.Generic;
using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

public class MaintenanceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceDefinitionService _defs;
    private readonly MaintenanceService _maint;
    private readonly SessionContext _admin;

    public MaintenanceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mnt_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _maint = new MaintenanceService(_factory, _clock);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    // ---- Eşik testleri ----
    [Theory]
    [InlineData(80, AlertLevel.Normal)]
    [InlineData(85, AlertLevel.Approaching)]
    [InlineData(94, AlertLevel.Approaching)]
    [InlineData(95, AlertLevel.Critical)]
    [InlineData(99, AlertLevel.Critical)]
    [InlineData(100, AlertLevel.Overdue)]
    [InlineData(130, AlertLevel.Overdue)]
    public void Esik_Yuzdeleri(int consumed, AlertLevel expected)
        => Assert.Equal(expected, AlertRules.Level(AlertRules.Progress(consumed, 100)));

    // ---- Tanım CRUD + alt bakım + araç kapsamı (Bakım Tanımları sekmesi) ----
    [Fact]
    public void Tanim_ListeGuncelleSil_AltBakim_AracKapsami()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("Yağ Değişimi", 5000m, "km"), new[] { v });

        var mains = _defs.List(_admin);
        Assert.Single(mains);
        Assert.Equal("Yağ Değişimi", mains[0].Name);

        // Araç kapsamı
        Assert.Contains(v, _defs.GetVehicleIds(_admin, def));
        _defs.SetVehicles(_admin, def, System.Array.Empty<string>());
        Assert.Empty(_defs.GetVehicleIds(_admin, def));

        // Alt bakım (parent)
        _defs.Create(_admin, new NewMaintenanceDefinition("Ön Balata", 0m, "km", ParentDefId: def));
        var subs = _defs.List(_admin, def);
        Assert.Single(subs);
        Assert.Equal("Ön Balata", subs[0].Name);
        Assert.Single(_defs.List(_admin)); // alt bakım ana listede görünmez

        // Güncelle + sil
        _defs.Update(_admin, def, new NewMaintenanceDefinition("Yağ Değişimi 2", 6000m, "hour"));
        Assert.Equal("Yağ Değişimi 2", _defs.List(_admin)[0].Name);
        _defs.Delete(_admin, def);
        Assert.Empty(_defs.List(_admin));
    }

    [Fact]
    public void Import_Arac_Muayene_Bakim_Excelden()
    {
        var lookups = new LookupService(_factory, _clock);

        // Araç import
        var vimp = new VehicleImportService(_vehicles, lookups);
        var r1 = vimp.Commit(_admin, new[] { new ImportRow(2, new Dictionary<string, string?>
            { ["İç Kod"] = "IM-1", ["Plaka"] = "34A", ["Üretim Yılı"] = "2020", ["Durum"] = "Aktif" }) });
        Assert.Equal(1, r1.Added);
        Assert.Contains(_vehicles.List(_admin), v => v.InternalCode == "IM-1");

        // Muayene import (araç İç Kod ile eşlenir)
        var iimp = new InspectionImportService(new InspectionService(_factory, _clock), _vehicles);
        var r2 = iimp.Commit(_admin, new[] { new ImportRow(2, new Dictionary<string, string?>
            { ["Araç"] = "IM-1", ["Belge Tipi"] = "Muayene", ["Sonraki Tarih"] = "01.01.2030" }) });
        Assert.Equal(1, r2.Added);

        // Bakım import (tanım yoksa oluşturulur)
        var mimp = new MaintenanceImportService(_maint, _defs, _vehicles, lookups);
        var r3 = mimp.Commit(_admin, new[] { new ImportRow(2, new Dictionary<string, string?>
            { ["Araç"] = "IM-1", ["Bakım Tanımı"] = "Yağ Değişimi", ["Yapılma KM"] = "1000" }) });
        Assert.Equal(1, r3.Added);
        Assert.Contains(_maint.ListMaintenances(_admin), m => m.VehicleCode == "IM-1");

        // Bilinmeyen araç → hata
        var bad = iimp.Commit(_admin, new[] { new ImportRow(2, new Dictionary<string, string?>
            { ["Araç"] = "YOK", ["Belge Tipi"] = "Sigorta" }) });
        Assert.Equal(1, bad.Failed);
    }

    [Fact]
    public void Muayene_Kaydet_Listele()
    {
        var insp = new InspectionService(_factory, _clock);
        var v = _vehicles.Create(_admin, new NewVehicle("MV-1", Plate: "34X"));
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        insp.Save(_admin, new NewInspection(v, "inspection", now, now + 10L * 86_400_000, Place: "TÜVTÜRK"));
        var list = insp.List(_admin);
        Assert.Single(list);
        Assert.Equal("Muayene", list[0].DocTypeText);
        Assert.Equal("MV-1 - 34X", list[0].VehicleText);
        Assert.Equal("TÜVTÜRK", list[0].Place);
    }

    // ---- Bakım malzemesi tek düşüm ----
    [Fact]
    public void Bakim_Malzeme_TekDusum_FiyatSnapshot()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre", UnitPrice: 50m));
        _opening.RecordOpening(_admin, m, 10m, "op-open");
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 5000m, "km"));

        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m) }), "op-mnt-1");

        // Stok 10 → 8 (tek düşüm)
        Assert.Equal(8m, _opening.GetBalance(_admin, m));
        // Fiyat snapshot maintenance_materials'ta
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT quantity, unit_price FROM maintenance_materials WHERE material_id=@m;";
        cmd.AddWithValue("@m", m);
        using var r = cmd.ExecuteReader();
        Assert.True(r.Read());
        Assert.Equal(2m, Money.Parse(r.GetString(0)));
        Assert.Equal(50m, Money.Parse(r.GetString(1)));

        // Liste + kullanılan malzeme (Araç Bakımları sekmesi)
        var rows = _maint.ListMaintenances(_admin);
        Assert.Single(rows);
        Assert.Equal("V-1", rows[0].VehicleCode);
        Assert.Equal("Periyodik", rows[0].DefinitionName);
        Assert.False(rows[0].IsCancelled);
        var mats = _maint.GetMaintenanceMaterials(_admin, rows[0].Id);
        Assert.Single(mats);
        Assert.Equal("M-1", mats[0].Code);
        Assert.Equal(2m, mats[0].Quantity);
    }

    [Fact]
    public void Bakim_Idempotent_AyniOperation_CiftDusmez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(_admin, m, 10m, "op-open");
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));

        var id1 = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m) }), "dup");
        var id2 = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 2m) }), "dup");
        Assert.Equal(id1, id2);
        Assert.Equal(8m, _opening.GetBalance(_admin, m)); // 6 değil
    }

    // madde 5.3 (kullanıcı isteği 2026-08-06): Bakım Takibi'nde YETERSİZ STOK ENGELLENMEZ — kayıt oluşur,
    // stok eksiye düşebilir (kullanıcı stok girişini sonradan yapabilir; iş akışı durmaz). Uyarı + opsiyonel
    // "Taslak Talep" istemci tarafındadır. Eski davranış (engelle+rollback) BİLİNÇLİ olarak değiştirildi.
    [Fact]
    public void Bakim_YetersizStok_EngellenMEZ_KayitOlusur_StokEksiyeDuser()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(_admin, m, 1m, "op-open");
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));

        var id = _maint.Save(_admin,
            new NewMaintenance(v, def, PerformedKm: 1000m, Materials: new[] { new MaintenanceMaterialLine(m, 5m) }), "op");

        Assert.False(string.IsNullOrEmpty(id));       // kayıt oluştu (engellenmedi)
        Assert.Equal(-4m, _opening.GetBalance(_admin, m)); // 1 - 5 = -4 (defter tüketimi kayıtlı)
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM vehicle_maintenances;";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
    }

    [Fact]
    public void Bakim_Iptal_StoguGeriAlir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Filtre"));
        _opening.RecordOpening(_admin, m, 10m, "op-open");
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));
        var id = _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m,
            Materials: new[] { new MaintenanceMaterialLine(m, 3m) }), "op");
        Assert.Equal(7m, _opening.GetBalance(_admin, m));

        _maint.Cancel(_admin, id, "Yanlış kayıt");
        Assert.Equal(10m, _opening.GetBalance(_admin, m)); // geri eklendi

        _maint.Cancel(_admin, id, "tekrar"); // idempotent
        Assert.Equal(10m, _opening.GetBalance(_admin, m));
    }

    // ---- Sayaç ileri ----
    [Fact]
    public void Bakim_SayaciIleriTasir()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 5000m, "km"));
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1500m), "op");
        Assert.Equal(1500m, _vehicles.GetMeter(_admin, v));
    }

    // ---- Uyarı döngüsü ----
    [Fact]
    public void Uyari_KritikSeviye_YeniBakim_Temizler()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        // performed 1000, next 1100 (interval 100)
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m), "op-1");
        // sayacı 1098'e taşı → tüketilen 98 → %98 kritik
        _vehicles.SetMeter(_admin, v, 1098m);

        var alert = _maint.GetAlerts(_admin).Single();
        Assert.Equal(AlertLevel.Critical, alert.Level);

        // Yeni bakım (performed 1098) → en-son kayıt değişir, tüketilen ~0 → Normal
        _clock.Advance(60_000); // created_at farklı olsun (MAX deterministik)
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1098m), "op-2");
        var after = _maint.GetAlerts(_admin).Single();
        Assert.Equal(AlertLevel.Normal, after.Level);
    }

    /// <summary>2026-10-01 kullanıcı bildirimi: "yeni bakım girsem de eski uyarı silinmiyor". Sonradan girilen
    /// (eşitlenen/aktarılan) ESKİ km'li kayıt, daha yeni yapılmış bakımı gölgelememeli.</summary>
    [Fact]
    public void Uyari_Sonradan_Girilen_Eski_Kayit_Yeni_Bakimi_Golgelemez()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m), "op-1");
        _vehicles.SetMeter(_admin, v, 1098m);
        _clock.Advance(60_000);
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1098m), "op-2");   // yeni bakım
        _clock.Advance(60_000);
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 900m), "op-3");    // ESKİ bakım sonradan girildi

        var a = _maint.GetAlerts(_admin).Single();
        Assert.Equal(AlertLevel.Normal, a.Level);   // eskiden 900 esas alınıp "Gecikti" kalıyordu
    }

    /// <summary>Aynı bildirim, muayene tarafı: sonradan girilen eski belge, geçerli yeni belgeyi gölgelememeli.</summary>
    [Fact]
    public void Muayene_Uyarisi_Sonradan_Girilen_Eski_Belgeyi_Esas_Almaz()
    {
        var insp = new InspectionService(_factory, _clock);
        var v = _vehicles.Create(_admin, new NewVehicle("MV-9", Plate: "34Y"));
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        const long gun = 86_400_000;
        insp.Save(_admin, new NewInspection(v, "inspection", now, now + 300 * gun));            // yeni, geçerli
        _clock.Advance(60_000);
        insp.Save(_admin, new NewInspection(v, "inspection", now - 400 * gun, now - 30 * gun));  // eski, süresi dolmuş

        var a = insp.GetAlerts(_admin).Single();
        Assert.Equal(DateAlertLevel.Normal, a.Level);
        Assert.Equal(now + 300 * gun, a.NextDate);
    }

    // ═══ 2026-10-04 — uyarılar 2. tur (kullanıcı: "aynı türde yeni bakım girsem de eski uyarı silinmiyor") ═══
    private const long Gun = 86_400_000;

    /// <summary>Canlı vaka (GREY 010): SAAT bazlı tanımda yeni bakımın sayacı KM alanına girilmiş. Yeni kayıt
    /// esas alınmalı (sayaç km ?? saat) — eskiden eski saatli kayıt seçilip "Gecikti" kalıyordu.</summary>
    [Fact]
    public void Saat_Tanimi_Sayaci_Km_Alanina_Girilen_Yeni_Bakim_Uyariyi_Kaldirir()
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var v = _vehicles.Create(_admin, new NewVehicle("GREY-10", CurrentMeter: 1829m, MeterUnit: "hour"));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("MOTOR BAKIMI", 250m, "hour"));
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedHour: 1240m, PerformedDate: now - 300 * Gun), "op-1");
        Assert.Equal(AlertLevel.Overdue, _maint.GetAlerts(_admin).Single().Level);

        _clock.Advance(60_000);
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1789m, PerformedDate: now - 10 * Gun), "op-2");
        var a = _maint.GetAlerts(_admin).Single();
        Assert.Equal(AlertLevel.Normal, a.Level);
        Assert.Equal(40m, a.Consumed);
        Assert.Null(a.Note);
    }

    /// <summary>Canlı vaka (EKS-P 019): yeni bakım SAYAÇSIZ girildi → eski sayaçlı kayıt esas alınıp uyarı
    /// sürüyordu. Artık yeni kayıt esas alınır; periyot hesaplanamadığı için kullanıcıya NOT verilir ve
    /// ana ekranda "Kontrol gerekli" bilgi kalemi görünür.</summary>
    [Fact]
    public void Sayacsiz_Yeni_Bakim_Esas_Alinir_Ve_Not_Verir()
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var v = _vehicles.Create(_admin, new NewVehicle("EKS-19", CurrentMeter: 7600m, MeterUnit: "hour"));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("MOTOR BAKIMI", 250m, "hour"));
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedHour: 7251m, PerformedDate: now - 60 * Gun), "op-1");
        Assert.Equal(AlertLevel.Overdue, _maint.GetAlerts(_admin).Single().Level);

        _maint.Save(_admin, new NewMaintenance(v, def, PerformedDate: now - 1 * Gun), "op-2");
        var a = _maint.GetAlerts(_admin).Single();
        Assert.Equal(AlertLevel.Normal, a.Level);
        Assert.Contains("girilmeden", a.Note);

        var dash = new DepoWise.Infrastructure.Reporting.DashboardService(_factory, _maint, new InspectionService(_factory, _clock));
        var d = dash.GetSummary(_admin).Alerts.Single(x => x.Kind == DepoWise.Application.Reports.AlertKind.Maintenance);
        Assert.Contains("Kontrol gerekli", d.Detail);
        Assert.False(d.IsCritical);
        Assert.True(d.HasNote);
    }

    /// <summary>Canlı vaka (EKS-P 004): araç sayacı 122045 (fazladan hane), bakım 12404 saat → uyarı sürer ama
    /// not "sayaç hatalı girilmiş olabilir" der.</summary>
    [Fact]
    public void Supheli_Arac_Sayaci_Notla_Bildirilir()
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var v = _vehicles.Create(_admin, new NewVehicle("EKS-04", CurrentMeter: 122045m, MeterUnit: "hour"));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("MOTOR BAKIMI", 250m, "hour"));
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedHour: 12404m, PerformedDate: now - 5 * Gun), "op-1");
        var a = _maint.GetAlerts(_admin).Single();
        Assert.Equal(AlertLevel.Overdue, a.Level);
        Assert.Contains("hatalı", a.Note);
    }

    /// <summary>Sonradan girilen kayıt DAHA ESKİ tarihli → esas alınmaz; uyarı sürerse nedeni nota yazılır.</summary>
    [Fact]
    public void Eski_Tarihli_Sonradan_Girilen_Kayit_Esas_Alinmaz_Notla_Aciklanir()
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var v = _vehicles.Create(_admin, new NewVehicle("V-7", CurrentMeter: 1200m));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m, PerformedDate: now - 10 * Gun), "op-1");
        _clock.Advance(60_000);
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1150m, PerformedDate: now - 40 * Gun), "op-2");
        var a = _maint.GetAlerts(_admin).Single();
        Assert.Equal(AlertLevel.Overdue, a.Level);
        Assert.Contains("daha eski tarihli", a.Note);
    }

    /// <summary>Muayene listesi: yenilenen eski belge artık "Süresi geçti" değil "Yenilendi" görünür
    /// (kullanıcı: "yeni muayene girsem de eski uyarı silinmiyor").</summary>
    [Fact]
    public void Muayene_Listesi_Yenilenen_Belgeyi_Yenilendi_Gosterir()
    {
        var insp = new InspectionService(_factory, _clock);
        var v = _vehicles.Create(_admin, new NewVehicle("KAM-7", Plate: "34K"));
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        insp.Save(_admin, new NewInspection(v, "inspection", now - 400 * Gun, now - 30 * Gun));   // eski, süresi geçmiş
        _clock.Advance(60_000);
        insp.Save(_admin, new NewInspection(v, "inspection", now, now + 365 * Gun));             // yeni

        var rows = insp.List(_admin);
        Assert.Equal("Yenilendi", rows.Single(r => r.NextDate == now - 30 * Gun).StatusText);
        Assert.Equal("Normal", rows.Single(r => r.NextDate == now + 365 * Gun).StatusText);
        Assert.Equal(DateAlertLevel.Normal, insp.GetAlerts(_admin).Single().Level);
    }

    /// <summary>Yeni belge bitiş tarihsiz girildi → uyarı eski belgeye göre sürer; not ne yapılacağını söyler.</summary>
    [Fact]
    public void Muayene_Bitis_Tarihsiz_Yeni_Belge_Notla_Aciklanir()
    {
        var insp = new InspectionService(_factory, _clock);
        var v = _vehicles.Create(_admin, new NewVehicle("KAM-8"));
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        insp.Save(_admin, new NewInspection(v, "inspection", now - 400 * Gun, now - 30 * Gun));
        _clock.Advance(60_000);
        insp.Save(_admin, new NewInspection(v, "inspection", now, null));
        var a = insp.GetAlerts(_admin).Single();
        Assert.Equal(DateAlertLevel.Expired, a.Level);
        Assert.Contains("bitiş tarihi boş", a.Note);
    }

    /// <summary>Yeni belge, bitişi ZATEN geçmiş tarihle girildi → uyarı sürer; not tarihleri kontrol ettirir.</summary>
    [Fact]
    public void Muayene_Gecmis_Bitisle_Girilen_Yeni_Belge_Notla_Aciklanir()
    {
        var insp = new InspectionService(_factory, _clock);
        var v = _vehicles.Create(_admin, new NewVehicle("KAM-9"));
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        insp.Save(_admin, new NewInspection(v, "inspection", now - 365 * Gun, now - 2 * Gun));
        var a = insp.GetAlerts(_admin).Single();
        Assert.Equal(DateAlertLevel.Expired, a.Level);
        Assert.Contains("geçmiş", a.Note);
    }

    [Fact]
    public void Uyari_Gecikti_Yuzde100Ustu()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 100m, "km"));
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m), "op-1");
        _vehicles.SetMeter(_admin, v, 1150m); // tüketilen 150 → %150
        Assert.Equal(AlertLevel.Overdue, _maint.GetAlerts(_admin).Single().Level);
    }

    [Fact]
    public void Uyari_AtandiAmaHicYapilmadi_IlkBakimBekliyor()
    {
        // Bakım tanımı araca ATANIR ama HİÇ yapılmaz → "ilk bakım bekliyor" (Overdue) uyarısı çıkmalı
        // (kullanıcı bulgusu 2026-07-25: "bakım periyodu doldu ama uyarı listelenmedi").
        var v = _vehicles.Create(_admin, new NewVehicle("V-1", CurrentMeter: 1000m));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("Yağ", 5000m, "km"), new[] { v });

        var alert = _maint.GetAlerts(_admin).Single(a => a.DefinitionId == def);
        Assert.True(alert.NeverPerformed);
        Assert.Equal(AlertLevel.Overdue, alert.Level);

        // İlk bakım yapılınca "hiç yapılmadı" düşer (artık normal takip): performed 1000 → tüketilen ~0 → Normal
        _maint.Save(_admin, new NewMaintenance(v, def, PerformedKm: 1000m), "op-1");
        var after = _maint.GetAlerts(_admin).Single(a => a.DefinitionId == def);
        Assert.False(after.NeverPerformed);
        Assert.Equal(AlertLevel.Normal, after.Level);
    }

    [Fact]
    public void Bakim_DenyByDefault()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        var def = _defs.Create(_admin, new NewMaintenanceDefinition("P", 100m, "km"));
        var noPerm = new SessionContext("u", "A", Array.Empty<string>(), PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _maint.Save(noPerm, new NewMaintenance(v, def, PerformedKm: 1m), "op"));
    }

    // ---- Muayene/sigorta tarih uyarısı ----
    [Fact]
    public void Muayene_TarihUyarisi()
    {
        var v = _vehicles.Create(_admin, new NewVehicle("V-1"));
        var insp = new InspectionService(_factory, _clock);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var soon = _clock.UtcNow.AddDays(10).ToUnixTimeMilliseconds();
        var past = _clock.UtcNow.AddDays(-5).ToUnixTimeMilliseconds();

        insp.Save(_admin, new NewInspection(v, "inspection", now, soon));
        Assert.Equal(DateAlertLevel.Approaching, insp.GetAlerts(_admin).Single(a => a.DocType == "inspection").Level);

        insp.Save(_admin, new NewInspection(v, "insurance", now, past));
        Assert.Equal(DateAlertLevel.Expired, insp.GetAlerts(_admin).Single(a => a.DocType == "insurance").Level);
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
