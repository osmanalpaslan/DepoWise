using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Announcements;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ DYR-01 (ADR-173, 2026-08-28) — DUYURU TESTLERİ ═══
///
/// Kilitler: PK-J1 okuma HERKESE + yazma kapalı (yetkisiz yazamaz; yönetici-dışı aktif-dışını göremez) ·
/// PK-J2 şube hedefi kapsam izolasyonu (ekran + bildirim — yan kapı yok) · PK-J3 yayın penceresi
/// (aktiflik türetilir) · PK-J5 önem→kritik rozet · tenant · okundu-imza (düzenlenince yeniden
/// okunmamış — alert_reads, migration'sız) · soft delete + Çöp Kutusu · senkron uçtan uca idempotent ·
/// Migration081 bit-bit + statik CREATE-only · kaynak kayıtlar bit-bit (okuma salt-okunur).
/// </summary>
public class DuyuruTests : IDisposable
{
    private const string Co = "DYR";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AnnouncementService _svc;
    private readonly DashboardService _dash;
    private readonly string _uid, _sube1, _sube2;
    private readonly SessionContext _admin;
    private static readonly long NowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private const long GunMs = 86_400_000;

    public DuyuruTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_dyr_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();
        Firma(_f, Co);
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var branches = new BranchService(_f);
        _sube1 = branches.Create(_admin, new NewBranch("Şantiye A", "site"));
        _sube2 = branches.Create(_admin, new NewBranch("Şantiye B", "site"));
        _svc = new AnnouncementService(_f);
        _dash = new DashboardService(_f, new MaintenanceService(_f), new InspectionService(_f));
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

    // ══════════════ CRUD + PENCERE (PK-J3) ══════════════

    [Fact]
    public void DYR1_CRUD_Dogrulama_Ve_Yayin_Penceresi()
    {
        Assert.Throws<ArgumentException>(() => _svc.Create(_admin, new NewAnnouncement("")));                       // başlık zorunlu
        Assert.Throws<ArgumentException>(() => _svc.Create(_admin, new NewAnnouncement("X", PublishStart: NowMs, PublishEnd: NowMs - 1)));

        _svc.Create(_admin, new NewAnnouncement("Süresiz"));                                                        // hemen + süresiz
        _svc.Create(_admin, new NewAnnouncement("Gelecek", PublishStart: NowMs + 5 * GunMs));                       // yayında değil
        _svc.Create(_admin, new NewAnnouncement("Bitmiş", PublishStart: NowMs - 10 * GunMs, PublishEnd: NowMs - GunMs));

        var okuyucu = Personel();   // hiç yetkisi yok — yine de AKTİF duyuruları okur (PK-J1)
        var gorulen = _svc.List(okuyucu).Select(a => a.Title).ToList();
        Assert.Equal(new[] { "Süresiz" }, gorulen);                     // pencere dışılar kendiliğinden düştü

        // Yönetici tümünü görür (durum etiketiyle) — includeInactive.
        var hepsi = _svc.List(_admin, includeInactive: true);
        Assert.Equal(3, hepsi.Count);
        Assert.Equal("Yayında değil (gelecek)", hepsi.Single(a => a.Title == "Gelecek").StatusDisplay(NowMs));
        Assert.Equal("Yayın bitti", hepsi.Single(a => a.Title == "Bitmiş").StatusDisplay(NowMs));

        // Düzenleme kilidi:
        var s1 = hepsi.Single(a => a.Title == "Süresiz");
        _svc.Update(_admin, s1.Id, new NewAnnouncement("Süresiz 2"), expectedVersion: s1.Version);
        Assert.Throws<ConcurrencyException>(() =>
            _svc.Update(_admin, s1.Id, new NewAnnouncement("X"), expectedVersion: s1.Version));
    }

    // ══════════════ PK-J1 — OKUMA HERKESE, YAZMA KAPALI ══════════════

    [Fact]
    public void DYR2_Okuma_Herkese_Yazma_Kapali()
    {
        var id = _svc.Create(_admin, new NewAnnouncement("Herkese"));
        var yetkisiz = Personel();
        Assert.Single(_svc.List(yetkisiz));                                            // okuma serbest
        Assert.Throws<ForbiddenException>(() => _svc.Create(yetkisiz, new NewAnnouncement("Sızma")));
        Assert.Throws<ForbiddenException>(() => _svc.Update(yetkisiz, id, new NewAnnouncement("Sızma")));
        Assert.Throws<ForbiddenException>(() => _svc.Delete(yetkisiz, id));

        // Yönetici-dışı, includeInactive İSTESE DE aktif-dışını GÖREMEZ (fail-closed):
        _svc.Create(_admin, new NewAnnouncement("Gelecek", PublishStart: NowMs + 5 * GunMs));
        Assert.Single(_svc.List(yetkisiz, includeInactive: true));
    }

    // ══════════════ PK-J2 — ŞUBE HEDEFİ (yan kapı yok) ══════════════

    [Fact]
    public void DYR3_Sube_Hedefi_Kapsam_Izolasyonu()
    {
        _svc.Create(_admin, new NewAnnouncement("Genel"));
        _svc.Create(_admin, new NewAnnouncement("A'ya", BranchId: _sube1));
        _svc.Create(_admin, new NewAnnouncement("B'ye", BranchId: _sube2));

        var dar = Personel(new[] { _sube1 });
        var basliklar = _svc.List(dar).Select(a => a.Title).ToList();
        Assert.Contains("Genel", basliklar);
        Assert.Contains("A'ya", basliklar);
        Assert.DoesNotContain("B'ye", basliklar);

        // BİLDİRİM yan kapısı da kapalı: B'nin duyurusu dar kapsamın çan/uyarı listesine SIZMAZ.
        var bildirimler = _dash.GetSummary(dar).Alerts.Where(a => a.Kind == AlertKind.Announcement)
            .Select(a => a.Title).ToList();
        Assert.Contains("Genel", bildirimler);
        Assert.DoesNotContain("B'ye", bildirimler);

        // Yazmada kapsam dışına duyuru AÇILAMAZ (yazma yetkisi olsa bile):
        var darYazar = Personel(new[] { _sube1 }, ("announcements", true, true, true, true));
        Assert.Throws<ForbiddenException>(() =>
            _svc.Create(darYazar, new NewAnnouncement("Sızma", BranchId: _sube2)));
        Assert.Throws<ArgumentException>(() =>
            _svc.Create(_admin, new NewAnnouncement("X", BranchId: "yok-boyle-sube")));
    }

    /// <summary>⭐ TENANT: başka firma göremez/yazamaz.</summary>
    [Fact]
    public void DYR4_Firma_Izolasyonu()
    {
        var id = _svc.Create(_admin, new NewAnnouncement("Bizim"));
        Firma(_f, "BASKA");
        var uid2 = new UserService(_f).EnsureInitialAdmin("BASKA", "admin2", "admin123", RoleKeys.CompanyAdmin);
        var yabanci = new SessionContext(uid2, "BASKA", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Empty(_svc.List(yabanci));
        Assert.Empty(_dash.GetSummary(yabanci).Alerts);
        Assert.Throws<ArgumentException>(() => _svc.Update(yabanci, id, new NewAnnouncement("Çalıntı")));
        Assert.Throws<ArgumentException>(() => _svc.Delete(yabanci, id));
    }

    // ══════════════ BİLDİRİM ENTEGRASYONU (PK-J4/J5) ══════════════

    [Fact]
    public void DYR5_Bildirim_Entegrasyonu()
    {
        _svc.Create(_admin, new NewAnnouncement("Normal duyuru"));
        _svc.Create(_admin, new NewAnnouncement("Acil durum", Importance: "important"));
        _svc.Create(_admin, new NewAnnouncement("Gelecek", PublishStart: NowMs + 5 * GunMs));   // bildirime GİRMEZ

        var kalemler = _dash.GetSummary(_admin).Alerts.Where(a => a.Kind == AlertKind.Announcement).ToList();
        Assert.Equal(2, kalemler.Count);
        var acil = kalemler.Single(a => a.Title == "Acil durum");
        Assert.True(acil.IsCritical);                                   // PK-J5: önemli = kritik rozet
        Assert.Equal("Önemli duyuru", acil.Detail);
        Assert.Equal("announcements", acil.NavigateKey);
        Assert.False(kalemler.Single(a => a.Title == "Normal duyuru").IsCritical);
        Assert.Equal(2, _dash.UnreadAlertCount(_admin));                // çan sayacına girer
    }

    /// <summary>⭐ OKUNDU-İMZA: okundu → sayaç düşer; duyuru DÜZENLENİNCE (version artar) yeniden okunmamış.</summary>
    [Fact]
    public void DYR6_Okundu_Imza_Dongusu()
    {
        var id = _svc.Create(_admin, new NewAnnouncement("Toplantı var"));
        var kalem = Assert.Single(_dash.GetSummary(_admin).Alerts, a => a.Kind == AlertKind.Announcement);
        Assert.False(kalem.Read);

        _dash.MarkAlertRead(_admin, kalem.Key, kalem.Signature);
        Assert.True(_dash.GetSummary(_admin).Alerts.Single(a => a.Kind == AlertKind.Announcement).Read);
        Assert.Equal(0, _dash.UnreadAlertCount(_admin));

        _svc.Update(_admin, id, new NewAnnouncement("Toplantı var", Body: "Saat değişti"));   // düzenleme → imza değişir
        var yeniden = _dash.GetSummary(_admin).Alerts.Single(a => a.Kind == AlertKind.Announcement);
        Assert.False(yeniden.Read);                                     // herkes için yeniden okunmamış
        Assert.Equal(1, _dash.UnreadAlertCount(_admin));
    }

    // ══════════════ SİLME + SENKRON ══════════════

    [Fact]
    public void DYR7_SoftDelete_Ve_CopKutusu()
    {
        var id = _svc.Create(_admin, new NewAnnouncement("Silinecek"));
        _svc.Delete(_admin, id);
        Assert.Empty(_svc.List(_admin, includeInactive: true));
        Assert.Empty(_dash.GetSummary(_admin).Alerts.Where(a => a.Kind == AlertKind.Announcement));
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT is_deleted FROM announcements WHERE id=@id;";
            cmd.AddWithValue("@id", id);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));     // satır DURUYOR (fiziksel silme yok)
        }
        var trash = new DepoWise.Infrastructure.Files.TrashService(_f);
        Assert.Contains(trash.List(_admin, reauthenticated: true), t => t.Table == "announcements" && t.Id == id);
        trash.Restore(_admin, "announcements", id, reauthenticated: true);
        Assert.Single(_svc.List(_admin));
    }

    [Fact]
    public void DYR8_Senkron_Listesi_Ve_Uctan_Uca_Idempotent()
    {
        var t = BusinessSyncService.Tables.ToList();
        Assert.Contains("announcements", t);
        Assert.Equal(AnnouncementService.Module, BusinessSyncService.ModuleOf("announcements"));

        var dstPath = Path.Combine(Path.GetTempPath(), "dw_dyr_dst_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var dst = new SqliteConnectionFactory(dstPath);
            new MigrationRunner(dst).Run();
            Firma(dst, Co);
            using (var conn = dst.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) " +
                                  "VALUES(@b,@c,'Şantiye A','site',1,1,1,0);";
                cmd.AddWithValue("@b", _sube1);
                cmd.AddWithValue("@c", Co);
                cmd.ExecuteNonQuery();
            }
            var id = _svc.Create(_admin, new NewAnnouncement("Senkron Duyuru", BranchId: _sube1, Importance: "important"));

            var clock = new SystemClock();
            var dstSvc = new BusinessSyncService(dst, clock);
            using (var snap = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co)))
            {
                Assert.Empty(dstSvc.ApplyPull(Co, snap.RootElement).Errors);
                long Say(string sql)
                {
                    using var conn = dst.Create();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    return Convert.ToInt64(cmd.ExecuteScalar());
                }
                Assert.Equal(1, Say("SELECT COUNT(*) FROM announcements WHERE title='Senkron Duyuru' AND is_deleted=0"));
                dstSvc.ApplyPull(Co, snap.RootElement);   // tekrar → kopya yok
                Assert.Equal(1, Say("SELECT COUNT(*) FROM announcements WHERE title='Senkron Duyuru'"));

                _svc.Delete(_admin, id);                  // silme de taşınır (version+1 LWW'yi kazanır)
                using var snap2 = JsonDocument.Parse(new BusinessSyncService(_f, clock).BuildSnapshot(Co));
                Assert.Empty(dstSvc.ApplyPull(Co, snap2.RootElement).Errors);
                Assert.Equal(1, Say("SELECT COUNT(*) FROM announcements WHERE title='Senkron Duyuru' AND is_deleted=1"));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dstPath); } catch { }
        }
    }

    // ══════════════ ⭐⭐ MIGRATION081 KANITI ══════════════

    [Fact]
    public void DYR9_Migration081_Mevcut_Veriye_Dokunmaz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "dw_dyr_mig_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            new MigrationRunner(f, MigrationCatalog.All().Where(m => m.Version <= 80)).Run();
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('C1','Firma',10,10,1,0);
INSERT INTO branches(id,company_id,name,kind,created_at,updated_at,version,is_deleted) VALUES('B1','C1','Şantiye','site',11,11,1,0);
INSERT INTO alert_reads(id,company_id,user_id,alert_key,signature,created_at) VALUES('AR1','C1','U1','K1','S1',12);
INSERT INTO calendar_events(id,company_id,title,start_date,created_by,created_at,updated_at,version,is_deleted)
    VALUES('CE1','C1','Toplantı',13,'U1',13,13,1,0);";
                cmd.ExecuteNonQuery();
            }
            string Foto(SqliteConnectionFactory ff)
            {
                var sb = new System.Text.StringBuilder();
                using var conn = ff.Create();
                foreach (var t in new[] { "companies", "branches", "alert_reads", "calendar_events" })
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
            var once = Foto(f);
            Assert.Equal(new[] { 81 }, new MigrationRunner(f, new IMigration[] { new Migration081_Announcements() }).Run());
            Assert.Equal(once, Foto(f));   // ⭐ mevcut veri (alert_reads DAHİL) BİT-BİT aynı
            using (var conn = f.Create())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM announcements;";
                Assert.Equal(0L, Convert.ToInt64(cmd.ExecuteScalar()));   // yeni tablo BOŞ doğar
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(yol); } catch { }
        }
    }

    [Fact]
    public void DYR10_Migration081_Yalniz_Ekleme_Icerir()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);
        var sql = File.ReadAllText(Path.Combine(kok!.FullName,
            "src", "DepoWise.Infrastructure", "Database", "Migrations", "Migration081_Announcements.cs"));
        var i = sql.IndexOf("cmd.CommandText", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = sql[i..].ToUpperInvariant();
        foreach (var yasak in new[] { "ALTER ", "UPDATE ", "DELETE ", "DROP ", "INSERT " })
            Assert.DoesNotContain(yasak, govde);
    }

    /// <summary>⭐ Okuma + bildirim + okundu işlemleri duyuru/şube satırlarını BİT-BİT değiştirmez.</summary>
    [Fact]
    public void DYR11_Kaynak_Kayitlar_BitBit_Degismez()
    {
        _svc.Create(_admin, new NewAnnouncement("D1", BranchId: _sube1, Importance: "important"));
        string Foto()
        {
            var sb = new System.Text.StringBuilder();
            using var conn = _f.Create();
            foreach (var t in new[] { "announcements", "branches" })
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
        _ = _svc.List(_admin, includeInactive: true);
        _ = _dash.GetSummary(_admin);
        _dash.MarkAllAlertsRead(_admin);   // okundu YALNIZ alert_reads'e yazar
        Assert.Equal(once, Foto());
    }

    /// <summary>Excel modeli (liste kuralı 2).</summary>
    [Fact]
    public void DYR12_Excel_Modeli()
    {
        _svc.Create(_admin, new NewAnnouncement("Duyuru 1", Importance: "important", BranchId: _sube1));
        var model = AnnouncementService.ToTableModel(_svc.List(_admin, includeInactive: true), NowMs);
        Assert.Equal(new[] { "Başlık", "Önem", "Hedef", "Yayın", "Durum", "Oluşturan" }, model.Headers);
        Assert.Contains(model.Rows, r => Equals(r[0], "Duyuru 1") && Equals(r[1], "Önemli") && Equals(r[4], "Yayında"));
    }
}
