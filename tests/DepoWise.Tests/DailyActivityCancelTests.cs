using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// GÜNLÜK FAALİYET → STOK / BAKIM TUTARLILIĞI (kullanıcı kararları K1–K4, 2026-08-09 · İş 2).
///
///  K1 — Faaliyet + bağlı bakım + stok iptali TEK ATOMİK işlem; hata olursa hiçbiri uygulanmaz.
///  K2 — Yalnız daily_activity/Delete yetkisi aranır (bakım/Ters Kayıt yetkisi İSTENMEZ),
///       ama kontrol SERVİS katmanındadır → arayüz/uç nokta değiştirilerek atlatılamaz.
///  K3 — İptal edilenler varsayılan gizli; istenirse gösterilir ve ayırt edilir.
///  K4 — "İptal Et" terminolojisi (fiziksel silme yok).
///
/// Eski davranış: faaliyet gizleniyor ama bakım + stok yerinde kalıyordu → üç rapor birbirini tutmuyordu.
/// </summary>
public class DailyActivityCancelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly StockService _stock;
    private readonly VehicleService _vehicles;
    private readonly MaintenanceDefinitionService _defs;
    private readonly MaintenanceService _maint;
    private readonly DailyActivityService _daily;
    private readonly SessionContext _admin;

    public DailyActivityCancelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_dacancel_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        _defs = new MaintenanceDefinitionService(_factory, _clock);
        _maint = new MaintenanceService(_factory, _clock);
        _daily = new DailyActivityService(_factory, _maint, _clock, _defs);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private (string Vehicle, string Material, string Def) Seed(decimal opening = 100m)
    {
        var v = _vehicles.Create(_admin, new NewVehicle("ARC-" + Guid.NewGuid().ToString("N")[..6], CurrentMeter: 1000m));
        var m = _materials.Create(_admin, new NewMaterial("MAT-" + Guid.NewGuid().ToString("N")[..6], "Filtre"));
        _opening.RecordOpening(_admin, m, opening, "op-" + Guid.NewGuid().ToString("N"));
        var d = _defs.Create(_admin, new NewMaintenanceDefinition("Periyodik", 100m, "km"));
        return (v, m, d);
    }

    /// <summary>Bakım tipli faaliyet: bakım kaydı + stok düşümü üretir.</summary>
    private string MaintenanceActivity(string vehicle, string def, string material, decimal qty)
        => _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(vehicle, def, PerformedKm: 1100m,
            Materials: new[] { new MaintenanceMaterialLine(material, qty) }), "op-da-" + Guid.NewGuid().ToString("N"));

    private long AuditCount(string entity, string action)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type=@e AND action=@a;";
        cmd.AddWithValue("@e", entity);
        cmd.AddWithValue("@a", action);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private bool MaintenanceCancelled(string maintenanceId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_cancelled FROM vehicle_maintenances WHERE id=@id;";
        cmd.AddWithValue("@id", maintenanceId);
        return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
    }

    private string? MaintenanceIdOf(string activityId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT maintenance_id FROM daily_activities WHERE id=@id;";
        cmd.AddWithValue("@id", activityId);
        return cmd.ExecuteScalar() as string;
    }

    // ── 1. BİRLEŞİK İPTAL (senaryo 1) ───────────────────────────────────────────────────────

    [Fact]
    public void Bakim_tipli_faaliyet_iptalinde_FAALIYET_BAKIM_ve_STOK_birlikte_iptal_olur()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);

        Assert.Equal(90m, _stock.GetBalance(m));                 // 100 - 10
        var mnt = MaintenanceIdOf(act);
        Assert.NotNull(mnt);
        Assert.False(MaintenanceCancelled(mnt!));

        _daily.Delete(_admin, act);

        // Faaliyet iptal
        Assert.Empty(_daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 50).Items);
        // Bağlı bakım iptal
        Assert.True(MaintenanceCancelled(mnt!));
        // Stok GERİ DÖNDÜ
        Assert.Equal(100m, _stock.GetBalance(m));
    }

    [Fact]
    public void Iptal_DENETIM_kayitlarini_olusturur()
    {
        var (v, m, d) = Seed();
        var act = MaintenanceActivity(v, d, m, 5m);
        var beforeAct = AuditCount("daily_activity", AuditActions.Reverse);
        var beforeMnt = AuditCount("vehicle_maintenance", AuditActions.Reverse);

        _daily.Delete(_admin, act);

        Assert.Equal(beforeAct + 1, AuditCount("daily_activity", AuditActions.Reverse));   // eskiden HİÇ yazılmıyordu
        Assert.Equal(beforeMnt + 1, AuditCount("vehicle_maintenance", AuditActions.Reverse));
    }

    [Fact]
    public void Cok_malzemeli_bakimda_TUM_satirlar_geri_doner()
    {
        var (v, _, d) = Seed();
        var m1 = _materials.Create(_admin, new NewMaterial("M1-" + Guid.NewGuid().ToString("N")[..5], "Yağ"));
        var m2 = _materials.Create(_admin, new NewMaterial("M2-" + Guid.NewGuid().ToString("N")[..5], "Filtre"));
        var m3 = _materials.Create(_admin, new NewMaterial("M3-" + Guid.NewGuid().ToString("N")[..5], "Conta"));
        foreach (var m in new[] { m1, m2, m3 }) _opening.RecordOpening(_admin, m, 50m, "op-" + Guid.NewGuid().ToString("N"));

        var act = _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, d, PerformedKm: 1100m,
            Materials: new[]
            {
                new MaintenanceMaterialLine(m1, 5m),
                new MaintenanceMaterialLine(m2, 3m),
                new MaintenanceMaterialLine(m3, 7m),
            }), "op-multi");

        Assert.Equal(45m, _stock.GetBalance(m1));
        Assert.Equal(47m, _stock.GetBalance(m2));
        Assert.Equal(43m, _stock.GetBalance(m3));

        _daily.Delete(_admin, act);

        Assert.Equal(50m, _stock.GetBalance(m1));
        Assert.Equal(50m, _stock.GetBalance(m2));
        Assert.Equal(50m, _stock.GetBalance(m3));
    }

    [Fact]
    public void Bakim_ekibi_stogu_isaretli_satir_GERI_EKLENMEZ()
    {
        var (v, m, d) = Seed(100m);
        // İşaretli satır kayıt sırasında stoktan DÜŞMEZ → iptalde de geri EKLENMEMELİ (stok şişmesin).
        var act = _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, d, PerformedKm: 1100m,
            Materials: new[] { new MaintenanceMaterialLine(m, 10m, FromTeamStock: true) }), "op-team");

        Assert.Equal(100m, _stock.GetBalance(m));   // hiç düşmedi
        _daily.Delete(_admin, act);
        Assert.Equal(100m, _stock.GetBalance(m));   // ❗ şişmedi
    }

    // ── 2. HAREKET/SEVKİYAT — davranış DEĞİŞMEDİ (senaryo 4) ────────────────────────────────

    [Fact]
    public void Hareket_faaliyeti_iptalinde_BAKIM_ve_STOK_islemi_YAPILMAZ()
    {
        var (v, m, _) = Seed(100m);
        var act = _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, Description: "sevk"), "op-mv");

        Assert.Null(MaintenanceIdOf(act));
        var before = _stock.GetBalance(m);

        _daily.Delete(_admin, act);

        Assert.Equal(before, _stock.GetBalance(m));                 // stok hiç etkilenmedi
        Assert.Empty(_daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 50).Items);
    }

    // ── 3. TEKRAR İPTAL (senaryo 3) ─────────────────────────────────────────────────────────

    [Fact]
    public void Ayni_faaliyet_IKINCI_KEZ_iptal_edilemez()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);
        _daily.Delete(_admin, act);
        Assert.Equal(100m, _stock.GetBalance(m));

        var ex = Assert.Throws<InvalidOperationException>(() => _daily.Delete(_admin, act));
        Assert.Contains("zaten iptal", ex.Message);
        Assert.Equal(100m, _stock.GetBalance(m));                   // ❗ stok İKİNCİ KEZ geri eklenmedi
    }

    [Fact]
    public void Bakim_baska_yerden_iptal_edilmisse_faaliyet_iptali_stogu_IKI_KEZ_geri_vermez()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);
        var mnt = MaintenanceIdOf(act)!;

        _maint.Cancel(_admin, mnt, "önce bakım ekranından");        // stok 100'e döndü
        Assert.Equal(100m, _stock.GetBalance(m));

        _daily.Delete(_admin, act);                                  // faaliyet de iptal

        Assert.Equal(100m, _stock.GetBalance(m));                    // ❗ 110 OLMADI
        Assert.True(MaintenanceCancelled(mnt));
    }

    // ── 4. ATOMİKLİK (senaryo 2) ────────────────────────────────────────────────────────────

    [Fact]
    public void Islem_ortasinda_hata_olursa_HICBIRI_uygulanmaz()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);
        var mnt = MaintenanceIdOf(act)!;
        Assert.Equal(90m, _stock.GetBalance(m));

        // Faaliyet satırını "yok" gibi göstererek 3. adımı (UPDATE) başarısız kıl:
        // aynı transaction içinde bakım iptali ZATEN yapılmış olacak → rollback ile o da geri alınmalı.
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE daily_activities SET company_id='BASKA' WHERE id=@id;";
            cmd.AddWithValue("@id", act);
            cmd.ExecuteNonQuery();
        }

        Assert.ThrowsAny<Exception>(() => _daily.Delete(_admin, act));

        // ROLLBACK kanıtı: bakım İPTAL EDİLMEDİ ve stok GERİ DÖNMEDİ.
        Assert.False(MaintenanceCancelled(mnt));
        Assert.Equal(90m, _stock.GetBalance(m));
    }

    // ── 5. YETKİ (senaryo 5) ────────────────────────────────────────────────────────────────

    [Fact]
    public void Yetkisiz_kullanici_SERVIS_seviyesinde_iptal_edemez()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);

        // daily_activity'de Delete YOK (yalnız görüntüleme/oluşturma) → reddedilmeli.
        var perms = new PermissionSet(new[] { new ModulePermission("daily_activity", true, true, true, false) });
        var staff = new SessionContext("u2", "A", new[] { RoleKeys.Staff }, perms);

        Assert.Throws<ForbiddenException>(() => _daily.Delete(staff, act));
        Assert.Equal(90m, _stock.GetBalance(m));                     // hiçbir şey değişmedi
        Assert.False(MaintenanceCancelled(MaintenanceIdOf(act)!));
    }

    [Fact]
    public void Faaliyet_iptal_yetkisi_VARSA_bakim_yetkisi_ARANMAZ()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);

        // K2: yalnız daily_activity/Delete var; maintenance yetkisi YOK.
        var perms = new PermissionSet(new[] { new ModulePermission("daily_activity", true, true, true, true) });
        var user = new SessionContext("u3", "A", new[] { RoleKeys.Staff }, perms);

        _daily.Delete(user, act);                                    // geçmeli

        Assert.True(MaintenanceCancelled(MaintenanceIdOf(act)!));
        Assert.Equal(100m, _stock.GetBalance(m));
    }

    // ── 6. GÖRÜNÜRLÜK (senaryolar 6–7, K3) ──────────────────────────────────────────────────

    [Fact]
    public void Iptal_edilen_faaliyet_varsayilan_GIZLI_istenirse_GORUNUR()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);
        _daily.Delete(_admin, act);

        Assert.Empty(_daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 50).Items);

        var withCancelled = _daily.SearchGrid(_admin, new DailyActivityGridFilter(), 1, 50, includeCancelled: true).Items;
        var row = Assert.Single(withCancelled);
        Assert.True(row.IsCancelled);
        Assert.Equal("İptal edildi", row.StatusText);                // ekranda ayırt edilebilir
    }

    // ── 7. ONAY ÖZETİ (ekranların kullanıcıya gösterdiği bilgi) ─────────────────────────────

    [Fact]
    public void Iptal_etkisi_ozeti_bagli_bakim_ve_malzeme_sayisini_verir()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);

        var (hasMnt, lines, qty) = _daily.GetCancelImpact(_admin, act);
        Assert.True(hasMnt);
        Assert.Equal(1, lines);
        Assert.Equal(10m, qty);

        // Hareket tipinde etki YOK → onay metni sade olur.
        var mv = _daily.SaveMovement(_admin, new NewMovementActivity("movement", v), "op-mv2");
        var impact = _daily.GetCancelImpact(_admin, mv);
        Assert.False(impact.HasMaintenance);
        Assert.Equal(0, impact.MaterialLines);
    }

    // ── 8. REGRESYON (senaryo 9) ────────────────────────────────────────────────────────────

    [Fact]
    public void Mevcut_olusturma_ve_stok_davranisi_BOZULMADI()
    {
        var (v, m, d) = Seed(100m);

        var act = MaintenanceActivity(v, d, m, 10m);
        Assert.NotNull(act);
        Assert.Equal(90m, _stock.GetBalance(m));                     // stok düşümü aynı
        Assert.NotNull(MaintenanceIdOf(act));                        // bakım kaydı üretiliyor
        Assert.Equal(1100m, _vehicles.GetMeter(_admin, v));          // sayaç ilerledi

        // Aynı operation_id ile tekrar → idempotent (çift kayıt yok)
        var again = _daily.SaveMaintenanceActivity(_admin, new NewMaintenance(v, d, PerformedKm: 1100m,
            Materials: new[] { new MaintenanceMaterialLine(m, 10m) }), "op-idem");
        Assert.NotNull(again);

        // Hareket kaydı hâlâ çalışıyor
        var mv = _daily.SaveMovement(_admin, new NewMovementActivity("movement", v, Description: "x"), "op-mv3");
        Assert.NotNull(mv);
    }

    [Fact]
    public void Iptal_ARAC_SAYACINI_GERI_ALMAZ()
    {
        var (v, m, d) = Seed(100m);
        var act = MaintenanceActivity(v, d, m, 10m);
        Assert.Equal(1100m, _vehicles.GetMeter(_admin, v));

        _daily.Delete(_admin, act);

        Assert.Equal(1100m, _vehicles.GetMeter(_admin, v));          // ❗ sayaç geri alınmadı
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
