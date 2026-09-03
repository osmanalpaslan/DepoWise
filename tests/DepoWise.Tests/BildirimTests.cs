using DepoWise.Application.Common;
using DepoWise.Application.Files;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.WorkOrders;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ BLD-01 (ADR-172, 2026-08-28) — BİLDİRİM MERKEZİ TESTLERİ ═══
///
/// Kilitler: PK-I1 üç yeni TÜRETİLMİŞ kaynak (evrak geçerlilik · geciken iş emri · bekleyen talep;
/// fiziksel bildirim kaydı YOK) · yan kapı yok (kaynak modül yetkisi olmadan o kategori sızmaz) ·
/// BranchAccess · tenant · okundu-imza döngüsü (kötüleşince yeniden görünür) · tümünü-okundu +
/// idempotency (kopya satır/kopya bildirim imkânsız) · kaynak kayıtlar bit-bit değişmez ·
/// offline (belge servisi yokken evrak kategorisi sessiz boş) · mevcut 4 kaynağın davranışı korunur.
/// MIGRATION YOK (PK-I4: alert_reads'e dokunulmadı) — bu turda şema 80'de kalır.
/// </summary>
public class BildirimTests : IDisposable
{
    private const string Co = "BLD";
    private readonly string _dbPath, _storeRoot;
    private readonly SqliteConnectionFactory _f;
    private readonly DashboardService _svc;
    private readonly DocumentService _docs;
    private readonly WorkOrderService _wo;
    private readonly string _uid, _sube1, _sube2, _mat;
    private readonly SessionContext _admin;
    private static readonly long NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private const long GunMs = 86_400_000;
    private static readonly byte[] Pdf = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\ntest\n%%EOF");

    public BildirimTests()
    {
        var n = Guid.NewGuid().ToString("N");
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_bld_" + n + ".db");
        _storeRoot = Path.Combine(Path.GetTempPath(), "dw_bld_store_" + n);
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new DepoWise.Infrastructure.Organization.BranchService(_f);
        _sube1 = branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento"));
        _docs = new DocumentService(_f, new LocalFileStorageProvider(_storeRoot));
        _wo = new WorkOrderService(_f);
        _svc = new DashboardService(_f, new MaintenanceService(_f), new InspectionService(_f), _docs);
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
        try { Directory.Delete(_storeRoot, recursive: true); } catch { }
    }

    private SessionContext Personel(string[]? kapsam = null, params (string Mod, bool V, bool C, bool E, bool D)[] izinler)
        => new SessionContext(_uid, Co, new[] { RoleKeys.Staff },
            new PermissionSet(izinler.Select(x => new ModulePermission(x.Mod, x.V, x.C, x.E, x.D))))
        { ScopeBranchIds = kapsam };

    private IReadOnlyList<DashboardAlert> Alerts(SessionContext? s = null)
        => _svc.GetSummary(s ?? _admin).Alerts;

    /// <summary>Pending talep — mevcut talep zinciri üzerinden değil doğrudan satırla kurulur
    /// (bildirim kaynağı yalnız status='pending' satırını okur; zincire dokunmaz).</summary>
    private string Talep(string docNo, string? branchId = null, string status = "pending")
    {
        var id = Guid.NewGuid().ToString("N");
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO material_requests(id,company_id,doc_no,request_date,branch_id,status,created_at,updated_at,version,is_deleted)
VALUES(@id,@c,@no,@d,@b,@st,@d,@d,1,0);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", Co);
        cmd.AddWithValue("@no", docNo);
        cmd.AddWithValue("@d", NowMs);
        cmd.AddWithValue("@b", (object?)branchId ?? DBNull.Value);
        cmd.AddWithValue("@st", status);
        cmd.ExecuteNonQuery();
        return id;
    }

    // ══════════════ YENİ KAYNAKLAR (PK-I1) ══════════════

    /// <summary>Evrak: süresi dolan KRİTİK üretir, 30 gün içinde yaklaşan üretir; uzak/süresiz üretmez.</summary>
    [Fact]
    public void BLD1_Evrak_Gecerlilik_Esikleri()
    {
        _docs.Save(_admin, "material", _mat, new DocumentMeta("Dolmuş", null, null, NowMs - GunMs, null), "a.pdf", "application/pdf", Pdf);
        _docs.Save(_admin, "material", _mat, new DocumentMeta("Yaklaşan", null, null, NowMs + 10 * GunMs, null), "b.pdf", "application/pdf", Pdf);
        _docs.Save(_admin, "material", _mat, new DocumentMeta("Uzak", null, null, NowMs + 60 * GunMs, null), "c.pdf", "application/pdf", Pdf);
        _docs.Save(_admin, "material", _mat, new DocumentMeta("Süresiz", null, null, null, null), "d.pdf", "application/pdf", Pdf);

        var evrak = Alerts().Where(a => a.Kind == AlertKind.Document).ToList();
        Assert.Equal(2, evrak.Count);
        var dolmus = evrak.Single(a => a.Title.StartsWith("Dolmuş"));
        Assert.True(dolmus.IsCritical);
        // 2026-09-03 (kullanıcı isteği): detay artık asıl veriyi de taşır — geçerlilik TARİHİ + geçen gün.
        Assert.StartsWith("Geçerlilik: ", dolmus.Detail);
        Assert.Contains("süresi doldu", dolmus.Detail);
        var yaklasan = evrak.Single(a => a.Title.StartsWith("Yaklaşan"));
        Assert.False(yaklasan.IsCritical);
        Assert.Contains("gün kaldı", yaklasan.Detail);   // 2026-09-03: kalan gün görünür
        Assert.Equal("documents", yaklasan.NavigateKey);
    }

    /// <summary>İş emri: plan bitişi geçmiş AÇIK emir üretir; gelecektekiler ve TERMİNAL (tamamlanan) üretmez.</summary>
    [Fact]
    public void BLD2_Geciken_IsEmri()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-1", "Geciken", BranchId: _sube1, PlannedEnd: NowMs - GunMs));
        _wo.Create(_admin, new NewWorkOrder("IE-2", "Vakitli", BranchId: _sube1, PlannedEnd: NowMs + 5 * GunMs));
        var kapali = _wo.Create(_admin, new NewWorkOrder("IE-3", "Bitmiş", BranchId: _sube1, PlannedEnd: NowMs - GunMs));
        _wo.SetStatus(_admin, kapali, "in_progress");
        _wo.SetStatus(_admin, kapali, "completed");

        var geciken = Assert.Single(Alerts(), a => a.Kind == AlertKind.WorkOrder);
        Assert.Contains("IE-1", geciken.Title);
        Assert.True(geciken.IsCritical);
        Assert.Equal("work_orders", geciken.NavigateKey);
    }

    /// <summary>Talep: yalnız 'pending' üretir; taslak/onaylı üretmez. KPI sayacı da değişmedi.</summary>
    [Fact]
    public void BLD3_Bekleyen_Talep()
    {
        Talep("T-1");
        Talep("T-2", status: "draft");
        Talep("T-3", status: "approved");
        var sum = _svc.GetSummary(_admin);
        var t = Assert.Single(sum.Alerts, a => a.Kind == AlertKind.Request);
        Assert.Equal("Talep T-1", t.Title);
        Assert.Equal("requests:approve", t.NavigateKey);
        Assert.Equal(1, sum.PendingRequestCount);   // mevcut KPI davranışı korunuyor
    }

    // ══════════════ YETKİ + KAPSAM + TENANT ══════════════

    /// <summary>⭐ YAN KAPI YOK: kaynak modül yetkisi olmayan oturumda o kategori bildirime SIZMAZ.</summary>
    [Fact]
    public void BLD4_Yan_Kapi_Yok()
    {
        _docs.Save(_admin, "material", _mat, new DocumentMeta("Gizli", null, null, NowMs - GunMs, null), "g.pdf", "application/pdf", Pdf);
        _wo.Create(_admin, new NewWorkOrder("IE-G", "Gizli iş", PlannedEnd: NowMs - GunMs));
        Talep("T-G");

        var yetkisiz = Personel();   // hiçbir modül yok
        var kinds = Alerts(yetkisiz).Select(a => a.Kind).ToHashSet();
        Assert.DoesNotContain(AlertKind.Document, kinds);
        Assert.DoesNotContain(AlertKind.WorkOrder, kinds);
        Assert.DoesNotContain(AlertKind.Request, kinds);

        // work_orders View verilince YALNIZ iş emri kategorisi açılır.
        var woYetkili = Personel(null, ("work_orders", true, false, false, false));
        var kinds2 = Alerts(woYetkili).Select(a => a.Kind).ToHashSet();
        Assert.Contains(AlertKind.WorkOrder, kinds2);
        Assert.DoesNotContain(AlertKind.Document, kinds2);
        Assert.DoesNotContain(AlertKind.Request, kinds2);
    }

    /// <summary>⭐ ŞUBE KAPSAMI: kapsam dışı şubenin geciken iş emri ve bekleyen talebi görünmez;
    /// şubesiz talep gizlenmez (sınıf kuralı).</summary>
    [Fact]
    public void BLD5_Sube_Kapsami()
    {
        _wo.Create(_admin, new NewWorkOrder("IE-A", "A işi", BranchId: _sube1, PlannedEnd: NowMs - GunMs));
        _wo.Create(_admin, new NewWorkOrder("IE-B", "B işi", BranchId: _sube2, PlannedEnd: NowMs - GunMs));
        Talep("T-A", _sube1);
        Talep("T-B", _sube2);
        Talep("T-0");   // şubesiz

        var dar = Personel(new[] { _sube1 },
            ("work_orders", true, false, false, false), ("requests", true, false, false, false));
        var basliklar = Alerts(dar).Select(a => a.Title).ToList();
        Assert.Contains(basliklar, t => t.Contains("IE-A"));
        Assert.DoesNotContain(basliklar, t => t.Contains("IE-B"));
        Assert.Contains("Talep T-A", basliklar);
        Assert.Contains("Talep T-0", basliklar);
        Assert.DoesNotContain("Talep T-B", basliklar);
    }

    /// <summary>⭐ TENANT: başka firmanın oturumu bu firmanın hiçbir bildirimini göremez.</summary>
    [Fact]
    public void BLD6_Firma_Izolasyonu()
    {
        _docs.Save(_admin, "material", _mat, new DocumentMeta("Bizim", null, null, NowMs - GunMs, null), "x.pdf", "application/pdf", Pdf);
        _wo.Create(_admin, new NewWorkOrder("IE-T", "Bizim iş", PlannedEnd: NowMs - GunMs));
        Talep("T-T");
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(Alerts(yabanci));
    }

    // ══════════════ OKUNDU DAVRANIŞI (mevcut alert_reads modeli — MIGRATION YOK) ══════════════

    /// <summary>⭐ Okundu işaretle → sayaç düşer; kaynak KÖTÜLEŞİNCE (imza değişir) yeniden okunmamış olur.</summary>
    [Fact]
    public void BLD7_Okundu_Imza_Dongusu()
    {
        var d = _docs.Save(_admin, "material", _mat,
            new DocumentMeta("Sözleşme", null, null, NowMs + 10 * GunMs, null), "s.pdf", "application/pdf", Pdf);
        var alert = Assert.Single(Alerts(), a => a.Kind == AlertKind.Document);
        Assert.False(alert.Read);
        Assert.Equal(1, _svc.UnreadAlertCount(_admin));

        _svc.MarkAlertRead(_admin, alert.Key, alert.Signature);
        Assert.True(Alerts().Single(a => a.Kind == AlertKind.Document).Read);
        Assert.Equal(0, _svc.UnreadAlertCount(_admin));

        // KÖTÜLEŞME: geçerlilik geçmişe düşer → Detail (imza) değişir → okundu OTOMATİK düşer.
        _docs.UpdateMeta(_admin, d.Id, new DocumentMeta("Sözleşme", null, null, NowMs - GunMs, null));
        var yeniden = Alerts().Single(a => a.Kind == AlertKind.Document);
        Assert.False(yeniden.Read);
        Assert.True(yeniden.IsCritical);
        Assert.Equal(1, _svc.UnreadAlertCount(_admin));
    }

    /// <summary>⭐ İDEMPOTENCY: bildirim üretimi türetilmiş → iki hesaplama AYNI Key kümesini verir
    /// (kopya bildirim imkânsız); tümünü-okundu tekrar çağrılınca alert_reads'te kopya satır oluşmaz.</summary>
    [Fact]
    public void BLD8_TumunuOkundu_Ve_Idempotency()
    {
        _docs.Save(_admin, "material", _mat, new DocumentMeta("D1", null, null, NowMs - GunMs, null), "1.pdf", "application/pdf", Pdf);
        _wo.Create(_admin, new NewWorkOrder("IE-1", "İş", PlannedEnd: NowMs - GunMs));
        Talep("T-1");

        var k1 = Alerts().Select(a => a.Key).OrderBy(x => x).ToList();
        var k2 = Alerts().Select(a => a.Key).OrderBy(x => x).ToList();
        Assert.Equal(k1, k2);   // üretim idempotent — aynı olay hep aynı kimlik

        _svc.MarkAllAlertsRead(_admin);
        Assert.Equal(0, _svc.UnreadAlertCount(_admin));
        long Say()
        {
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM alert_reads WHERE user_id=@u;";
            cmd.AddWithValue("@u", _uid);
            return Convert.ToInt64(cmd.ExecuteScalar());
        }
        var once = Say();
        _svc.MarkAllAlertsRead(_admin);   // tekrar → upsert, kopya satır YOK
        _svc.MarkAllAlertsRead(_admin);
        Assert.Equal(once, Say());
    }

    /// <summary>⭐ Bildirim hesaplama SALT-OKUNURDUR: kaynak kayıtlar bit-bit değişmez.</summary>
    [Fact]
    public void BLD9_Kaynak_Kayitlar_BitBit_Degismez()
    {
        _docs.Save(_admin, "material", _mat, new DocumentMeta("D1", null, null, NowMs - GunMs, null), "1.pdf", "application/pdf", Pdf);
        _wo.Create(_admin, new NewWorkOrder("IE-1", "İş", BranchId: _sube1, PlannedEnd: NowMs - GunMs));
        Talep("T-1", _sube1);

        string Foto()
        {
            var sb = new System.Text.StringBuilder();
            using var conn = _f.Create();
            foreach (var t in new[] { "file_records", "work_orders", "material_requests", "materials" })
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
        _ = Alerts();
        _svc.MarkAllAlertsRead(_admin);   // okundu YALNIZ alert_reads'e yazar
        _ = Alerts();
        Assert.Equal(once, Foto());
    }

    // ══════════════ OFFLINE + MEVCUT DAVRANIŞ ══════════════

    /// <summary>Masaüstü çevrimdışı temsilcisi: belge servisi YOKKEN (documents=null) evrak kategorisi
    /// SESSİZCE boş — hata yok; diğer kaynaklar çalışır.</summary>
    [Fact]
    public void BLD10_Offline_Evrak_Sessiz_Bos()
    {
        _docs.Save(_admin, "material", _mat, new DocumentMeta("D1", null, null, NowMs - GunMs, null), "1.pdf", "application/pdf", Pdf);
        _wo.Create(_admin, new NewWorkOrder("IE-1", "İş", PlannedEnd: NowMs - GunMs));

        var offline = new DashboardService(_f, new MaintenanceService(_f), new InspectionService(_f));   // documents YOK
        var kinds = offline.GetSummary(_admin).Alerts.Select(a => a.Kind).ToHashSet();
        Assert.DoesNotContain(AlertKind.Document, kinds);
        Assert.Contains(AlertKind.WorkOrder, kinds);
    }

    /// <summary>Masaüstünün uzaktan aldığı evrak bildirimlerine BU CİHAZIN yerel okundu işaretleri uygulanır
    /// (PK-I4: okundu cihaz-yerel).</summary>
    [Fact]
    public void BLD11_ApplyReads_Cihaz_Yerel()
    {
        var uzak = new DashboardAlert(AlertKind.Document, "Uzak Belge", "Geçerlilik yaklaşıyor", "documents", false, "D-1");
        Assert.False(_svc.ApplyReads(_admin, new[] { uzak }).Single().Read);
        _svc.MarkAlertRead(_admin, uzak.Key, uzak.Signature);
        Assert.True(_svc.ApplyReads(_admin, new[] { uzak }).Single().Read);
        // İmza değişirse (kötüleşme) okundu düşer:
        var kotu = uzak with { Detail = "Geçerlilik süresi doldu" };
        Assert.False(_svc.ApplyReads(_admin, new[] { kotu }).Single().Read);
    }

    /// <summary>Yeni kaynak verisi yokken yeni kategoriler ÜRETİLMEZ ve özet hatasız döner —
    /// mevcut 4 kaynağın davranışına dokunulmadığının hızlı kanıtı (tam kanıt: dashboard/rapor regresyonu).</summary>
    [Fact]
    public void BLD12_Bos_Kurulumda_Yeni_Kategori_Yok()
    {
        var sum = _svc.GetSummary(_admin);
        Assert.DoesNotContain(sum.Alerts, a => a.Kind is AlertKind.Document or AlertKind.WorkOrder or AlertKind.Request);
        Assert.Equal(0, _svc.UnreadAlertCount(_admin));
    }

    // ══════════ 2026-09-03 (kullanıcı isteği): her uyarı KENDİ varlığının asıl verisini taşır ══════════

    /// <summary>⭐ Bakım uyarısında ARAÇ KODU + PLAKA görünür ve seviye TÜRKÇEDİR — kullanıcı ekran
    /// görüntüsüyle "%2486 (Overdue)" bildirmişti: araçsız ve İngilizce satır artık ÜRETİLEMEZ.</summary>
    [Fact]
    public void BLD13_Bakim_Uyarisi_Arac_Kodu_Ve_Plaka_Tasir()
    {
        var vehicles = new DepoWise.Infrastructure.Vehicles.VehicleService(_f);
        var arac = vehicles.Create(_admin, new DepoWise.Infrastructure.Vehicles.NewVehicle("KMY-01", Plate: "06 GE 2812"));
        var defs = new MaintenanceDefinitionService(_f);
        var tanim = defs.Create(_admin, new NewMaintenanceDefinition("MOTOR BAKIMI", 30m, "day", null, null));
        defs.SetVehicles(_admin, tanim, new[] { arac });   // hiç yapılmamış → kesin uyarı üretir

        var bakim = Alerts().Single(a => a.Kind == AlertKind.Maintenance);
        Assert.Contains("KMY-01", bakim.Detail);
        Assert.Contains("06 GE 2812", bakim.Detail);
        Assert.DoesNotContain("Overdue", bakim.Detail);   // İngilizce enum adı basılamaz
    }

    /// <summary>⭐ Düşük stok uyarısında malzeme KODU + mevcut/kritik stok görünür.</summary>
    [Fact]
    public void BLD14_Dusuk_Stok_Uyarisi_Kod_Ve_Stok_Tasir()
    {
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE materials SET min_stock='5' WHERE id=@id;";   // stok 0 ≤ kritik 5 → uyarı
            cmd.AddWithValue("@id", _mat);
            cmd.ExecuteNonQuery();
        }

        var stok = Alerts().Single(a => a.Kind == AlertKind.LowStock);
        Assert.Equal("Çimento", stok.Title);
        Assert.Contains("M-1", stok.Detail);
        Assert.Contains("kritik 5", stok.Detail);
    }

    /// <summary>⭐ Bekleyen talep uyarısı tarihi taşır; iş emri uyarısı plan bitiş tarihini + gecikmeyi taşır.</summary>
    [Fact]
    public void BLD15_Talep_Ve_IsEmri_Uyarilari_Asil_Veriyi_Tasir()
    {
        Talep("TAL-9");
        _wo.Create(_admin, new NewWorkOrder("IE-9", "Geciken İş", BranchId: _sube1, PlannedEnd: NowMs - 3 * GunMs));

        var talep = Alerts().Single(a => a.Kind == AlertKind.Request);
        Assert.Contains("Onay bekliyor", talep.Detail);
        Assert.Contains(DateTimeOffset.FromUnixTimeMilliseconds(NowMs).LocalDateTime.ToString("dd.MM.yyyy"), talep.Detail);

        var emir = Alerts().Single(a => a.Kind == AlertKind.WorkOrder);
        Assert.Contains("Plan bitişi: ", emir.Detail);
        Assert.Contains("gün gecikti", emir.Detail);
    }
}
