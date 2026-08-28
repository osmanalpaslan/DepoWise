using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Announcements;
using DepoWise.Infrastructure.Calendars;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Purchasing;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ PAN-01 (ADR-175, 2026-08-28) — DASHBOARD (ANA EKRAN) TESTLERİ ═══
///
/// Kilitler: PK-L1 yeni özet alanları (açık/geciken iş emri · açık sipariş · bugünün takvimi · aktif
/// duyurular) · <b>null = kaynak yetkisi yok → kart/şerit hiç gösterilmez</b> (yan kapı) · BranchAccess ·
/// tenant · eski KPI/uyarı davranışının DEĞİŞMEDİĞİ · salt-okunurluk (bit-bit) · MIGRATION YOK.
/// </summary>
public class PanoTests : IDisposable
{
    private const string Co = "PAN";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly DashboardService _svc;
    private readonly WorkOrderService _wo;
    private readonly string _uid, _sube1, _sube2;
    private readonly SessionContext _admin;
    private static readonly long NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private const long GunMs = 86_400_000;
    private static readonly long BugunMs = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public PanoTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_pan_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_f);
        _sube1 = branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _wo = new WorkOrderService(_f);
        _svc = new DashboardService(_f, new MaintenanceService(_f), new InspectionService(_f));
    }

    private static void Firma(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V, bool C, bool E, bool D)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, x.C, x.E, x.D))))
        { ScopeBranchIds = kapsam };

    /// <summary>Sipariş en az bir satır ister — tek malzemeli satırla açılır.</summary>
    private string Siparis(PurchaseOrderService po, string no)
    {
        _mat ??= new DepoWise.Infrastructure.Materials.MaterialService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Materials.NewMaterial("M-PAN", "Çimento"));
        return po.Create(_admin, new NewPurchaseOrder(no,
            Lines: new[] { new NewPurchaseOrderLine(_mat, 5m) }));
    }
    private string? _mat;

    // ══════════════ PK-L1 — YENİ SAYILAR ══════════════

    /// <summary>Açık/geciken iş emri sayıları: terminal (tamamlanan) SAYILMAZ; geciken = plan bitişi geçmiş açık.</summary>
    [Fact]
    public void PAN1_IsEmri_Sayilari()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-1", "Açık", PlannedEnd: NowMs + 5 * GunMs));
        _wo.Create(_admin, new NewWorkOrder("IE-2", "Geciken", PlannedEnd: NowMs - GunMs));
        var kapali = _wo.Create(_admin, new NewWorkOrder("IE-3", "Bitmiş", PlannedEnd: NowMs - GunMs));
        _wo.SetStatus(_admin, kapali, "in_progress");
        _wo.SetStatus(_admin, kapali, "completed");

        var s = _svc.GetSummary(_admin);
        Assert.Equal(2, s.OpenWorkOrderCount);
        Assert.Equal(1, s.OverdueWorkOrderCount);
    }

    /// <summary>Açık sipariş sayısı: yalnız 'open'; iptal edilen sayılmaz.</summary>
    [Fact]
    public void PAN2_Siparis_Sayisi()
    {
        var po = new PurchaseOrderService(_f);
        Siparis(po, "PO-1");
        var iptal = Siparis(po, "PO-2");
        po.Cancel(_admin, iptal);
        Assert.Equal(1, _svc.GetSummary(_admin).OpenPurchaseOrderCount);
    }

    // ══════════════ ⭐ YETKİ GÖRÜNÜRLÜĞÜ (yan kapı yok) ══════════════

    /// <summary>Kaynak yetkisi olmayan kullanıcıda alan NULL döner → kart/şerit HİÇ gösterilmez;
    /// yetki verilince değer gelir.</summary>
    [Fact]
    public void PAN3_Yetkisiz_Alan_Null()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-1", "Açık"));
        Siparis(new PurchaseOrderService(_f), "PO-1");
        new CalendarService(_f).Create(_admin, new NewCalendarEvent("Bugün Toplantı", BugunMs));

        var yetkisiz = Personel();
        var s1 = _svc.GetSummary(yetkisiz);
        Assert.Null(s1.OpenWorkOrderCount);
        Assert.Null(s1.OverdueWorkOrderCount);
        Assert.Null(s1.OpenPurchaseOrderCount);
        Assert.Null(s1.TodayCalendar);
        // Duyuru okuma HERKESE (PK-J1) → şerit alanı null DEĞİL (boş liste = aktif duyuru yok).
        Assert.NotNull(s1.ActiveAnnouncements);

        var yetkili = Personel(null,
            ("work_orders", true, false, false, false),
            ("purchasing", true, false, false, false),
            ("calendar", true, false, false, false));
        var s2 = _svc.GetSummary(yetkili);
        Assert.Equal(1, s2.OpenWorkOrderCount);
        Assert.Equal(1, s2.OpenPurchaseOrderCount);
        Assert.NotNull(s2.TodayCalendar);
        Assert.Contains(s2.TodayCalendar!, t => t.Title == "Bugün Toplantı");
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kapsam dışı şubenin açık iş emri sayıya GİRMEZ.</summary>
    [Fact]
    public void PAN4_Sube_Kapsami()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-A", "A işi", BranchId: _sube1));
        _wo.Create(_admin, new NewWorkOrder("IE-B", "B işi", BranchId: _sube2));
        var dar = Personel(new[] { _sube1 }, ("work_orders", true, false, false, false));
        Assert.Equal(1, _svc.GetSummary(dar).OpenWorkOrderCount);
    }

    /// <summary>⭐ TENANT: başka firmanın sayıları/şeritleri bu firmanın verisini içermez.</summary>
    [Fact]
    public void PAN5_Firma_Izolasyonu()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-1", "Bizim"));
        new AnnouncementService(_f).Create(_admin, new NewAnnouncement("Bizim Duyuru"));
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var s = _svc.GetSummary(yabanci);
        Assert.Equal(0, s.OpenWorkOrderCount);
        Assert.Empty(s.ActiveAnnouncements!);
    }

    // ══════════════ ŞERİTLER ══════════════

    /// <summary>Bugünün Takvimi: yalnız BUGÜNLE kesişen öğeler; dünkü/yarınki girmez.</summary>
    [Fact]
    public void PAN6_Bugunun_Takvimi()
    {
        var cal = new CalendarService(_f);
        cal.Create(_admin, new NewCalendarEvent("Bugün", BugunMs));
        cal.Create(_admin, new NewCalendarEvent("Dün", BugunMs - GunMs));
        cal.Create(_admin, new NewCalendarEvent("Yarın", BugunMs + GunMs));
        cal.Create(_admin, new NewCalendarEvent("Çok Günlü", BugunMs - GunMs, BugunMs + GunMs));   // bugünü kapsar
        var serit = _svc.GetSummary(_admin).TodayCalendar!;
        var basliklar = serit.Select(t => t.Title).ToList();
        Assert.Contains("Bugün", basliklar);
        Assert.Contains("Çok Günlü", basliklar);
        Assert.DoesNotContain("Dün", basliklar);
        Assert.DoesNotContain("Yarın", basliklar);
    }

    /// <summary>Aktif Duyurular şeridi: pencere içindekiler, önem bayrağıyla; pencere dışı girmez.</summary>
    [Fact]
    public void PAN7_Duyuru_Seridi()
    {
        var ann = new AnnouncementService(_f);
        ann.Create(_admin, new NewAnnouncement("Acil", Importance: "important"));
        ann.Create(_admin, new NewAnnouncement("Normal"));
        ann.Create(_admin, new NewAnnouncement("Gelecek", PublishStart: NowMs + 5 * GunMs));
        var serit = _svc.GetSummary(_admin).ActiveAnnouncements!;
        Assert.Equal(2, serit.Count);
        Assert.True(serit.Single(a => a.Title == "Acil").IsImportant);
        Assert.False(serit.Single(a => a.Title == "Normal").IsImportant);
    }

    // ══════════════ MEVCUT DAVRANIŞ + SALT-OKUNURLUK ══════════════

    /// <summary>Eski 5 KPI ve uyarı üretimi DEĞİŞMEDİ; yeni alanlar eklemeli (boş kurulumda admin için 0/boş).</summary>
    [Fact]
    public void PAN8_Eski_Davranis_Korundu()
    {
        var s = _svc.GetSummary(_admin);
        Assert.Equal(0, s.VehicleCount);
        Assert.Equal(0, s.MaterialCount);
        Assert.Equal(0, s.PendingRequestCount);
        Assert.Empty(s.Alerts);
        Assert.Equal(0, s.OpenWorkOrderCount);       // admin: yetki var → 0 (null değil)
        Assert.Equal(0, s.OpenPurchaseOrderCount);
        Assert.NotNull(s.TodayCalendar);
        Assert.Empty(s.TodayCalendar!);
        // Eski imzayla kurulan özet hâlâ geçerli (yeni alanlar default null — eklemeli kanıtı):
        var eski = new DashboardSummary(1, 2, 3, 4, 5, Array.Empty<DashboardAlert>());
        Assert.Null(eski.OpenWorkOrderCount);
    }

    /// <summary>⭐ Özet hesaplama SALT-OKUNURDUR: kaynak kayıtlar bit-bit değişmez.</summary>
    [Fact]
    public void PAN9_Kaynaklar_BitBit_Degismez()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-1", "İş", BranchId: _sube1, PlannedEnd: NowMs - GunMs));
        Siparis(new PurchaseOrderService(_f), "PO-1");
        new CalendarService(_f).Create(_admin, new NewCalendarEvent("Bugün", BugunMs));
        new AnnouncementService(_f).Create(_admin, new NewAnnouncement("Duyuru"));

        string Foto()
        {
            var sb = new System.Text.StringBuilder();
            using var conn = _f.Create();
            foreach (var t in new[] { "work_orders", "purchase_orders", "calendar_events", "announcements" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT * FROM {t} ORDER BY 1;";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    for (int i = 0; i < r.FieldCount; i++)
                        sb.Append(r.IsDBNull(i) ? "∅" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            }
            return sb.ToString();
        }
        var once = Foto();
        _ = _svc.GetSummary(_admin);
        _ = _svc.GetSummary(_admin);
        Assert.Equal(once, Foto());
    }
}
