using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.4 — SENKRON ÇAKIŞMA EKRANI ═══ (kullanıcı isteği 2026-09-06)
///
/// <i>"Senkron çakışma uyarısı web'te var masaüstünde de olmalı. Kimin kazandığı kimin kaybettiği
/// belirtilmeli. Uyarıya tıklandığında yeni bir senkron çakışma ekranı açılmalı. Üzerine yazılan kaydı
/// iptal edip istenen kaydı kazanan yapabilmeli. Kayıtlar doğru güncellenmeli."</i>
///
/// <b>Neden şema değişikliği gerekti (kanıt).</b> <c>data_conflicts</c> yalnız "kim kazandı" ve zaman
/// damgalarını tutuyordu; <b>kaybeden sürümün verisi hiçbir yerde yoktu</b> → "üzerine yazılanı geri
/// getir" isteği mevcut şemayla teknik olarak karşılanamıyordu (Migration094).
///
///  CK1 — Çakışmada İKİ sürümün de anlık görüntüsü saklanır
///  CK2 — Kazanan/kaybeden metinleri doğru taraf gösterir
///  CK3 — 🔴 Üzerine yazılan sürüm geri getirilir ve KAYIT GERÇEKTEN güncellenir
///  CK4 — 🔴 Geri getirme senkrona yayılır: version artar, updated_at ilerler
///  CK5 — 🔴 YETKİ: btn-conflict-resolve olmadan kazanan değiştirilemez
///  CK6 — Aynı çakışma İKİ KEZ çözülemez (kapatılmış çakışma reddedilir)
///  CK7 — 🔴 FİRMA SINIRI: başka firmanın çakışması açılamaz/çözülemez
///  CK8 — Eski (görüntüsüz) çakışmada açık hata verilir — sessiz başarısızlık yok
///  CK9 — Çözüm kaydın KENDİ log ekranına düşer (FAZ 4.3 ile birlikte)
///  CK10 — Alan bazlı fark üretilir ("Ad Soyad: … → …")
///  CK11 — 🔴 GÜVENLİK: kimlik/firma/sürüm sütunları görüntüden GERİ YAZILMAZ
/// </summary>
public class SenkronCakismaEkraniTests : IDisposable
{
    private readonly string _srcPath, _dstPath;
    private readonly SqliteConnectionFactory _src, _dst;
    private readonly TestClock _clock = new();

    private sealed class TestClock : IClock
    { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000); }

    public SenkronCakismaEkraniTests()
    {
        _srcPath = Path.Combine(Path.GetTempPath(), "dw_ck_src_" + Guid.NewGuid().ToString("N") + ".db");
        _dstPath = Path.Combine(Path.GetTempPath(), "dw_ck_dst_" + Guid.NewGuid().ToString("N") + ".db");
        _src = new SqliteConnectionFactory(_srcPath);
        _dst = new SqliteConnectionFactory(_dstPath);
        new MigrationRunner(_src).Run();
        new MigrationRunner(_dst).Run();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_srcPath); } catch { }
        try { File.Delete(_dstPath); } catch { }
    }

    /// <summary>JSON görüntüsünden tek alanın değeri (ASCII dışı kaçışlardan etkilenmez).</summary>
    private static string? Alan(string json, string alan)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(alan, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    }

    // ══════════════════ ortak kurulum ══════════════════

    private static void Exec(SqliteConnectionFactory f, string sql, params (string, object?)[] ps)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static string? Scalar(SqliteConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Firma(SqliteConnectionFactory f, string id)
        => Exec(f, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);", ("@i", id));

    private static void Personel(SqliteConnectionFactory f, string id, string company, string name, long updatedAt, string? phone = null)
        => Exec(f, "INSERT INTO personnel(id,company_id,full_name,phone,is_active,created_at,updated_at,version,is_deleted) " +
                   "VALUES(@i,@c,@n,@p,1,1,@u,1,0);",
            ("@i", id), ("@c", company), ("@n", name), ("@p", phone), ("@u", updatedAt));

    /// <summary>Admin (web, 200) ile personel (masaüstü, 150) AYNI kaydı değiştirir → admin kazanır.</summary>
    private string CakismaUret(string company = "ACME")
    {
        Firma(_src, company); Firma(_dst, company);
        Exec(_dst, "INSERT INTO sync_devices(id,company_id,device_name,status,last_business_push_at,created_at,updated_at,version) " +
                   "VALUES('dev1',@c,'MPC','active',100,1,1,1);", ("@c", company));
        Personel(_dst, "p1", company, "Web Sürümü", 200, "0500");
        Exec(_dst, "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
                   "VALUES('u1',@c,'admin','x',1,1,1,1,0);", ("@c", company));
        Personel(_src, "p1", company, "Masaüstü Sürümü", 150, "0600");

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot(company, "MPC");
        using var doc = JsonDocument.Parse(snap);
        new BusinessSyncService(_dst, _clock).Apply(company, doc.RootElement);

        return Scalar(_dst, $"SELECT id FROM data_conflicts WHERE company_id='{company}' AND entity_id='p1';")!;
    }

    /// <summary>Personel oturumu: çakışma ekranını görebilir; kazananı değiştirme yetkisi seçime bağlı.
    /// (İki kapı ayrıdır: ekran yetkisi "sync_conflicts" · eylem yetkisi btn-conflict-resolve.)</summary>
    private static SessionContext Oturum(string company = "ACME", bool cozmeYetkisi = true)
        => new("u1", company, new[] { RoleKeys.Staff },
            new PermissionSet(new[]
                {
                    new ModulePermission("personnel", true, true, true, false),
                    new ModulePermission("sync_conflicts", true, false, false, false),
                },
                cozmeYetkisi ? new[] { SpecialButtons.ConflictResolve } : Array.Empty<string>()));

    // ══════════════════ CK1 ══════════════════

    [Fact]
    public void CK1_Iki_Surumun_De_Anlik_Goruntusu_Saklanir()
    {
        var id = CakismaUret();
        var svc = new BusinessSyncService(_dst, _clock);

        var d = svc.ConflictDetail("ACME", id);
        Assert.NotNull(d);
        Assert.False(string.IsNullOrWhiteSpace(d!.WinnerJson));
        Assert.False(string.IsNullOrWhiteSpace(d.LoserJson));
        // JSON, ASCII dışı karakterleri kaçış dizisiyle yazar → metin araması değil ALAN DEĞERİ karşılaştırılır.
        Assert.Equal("Web Sürümü", Alan(d.WinnerJson!, "full_name"));      // kazanan: sunucudaki (web) sürüm
        Assert.Equal("Masaüstü Sürümü", Alan(d.LoserJson!, "full_name"));  // kaybeden: cihazdan gelen sürüm
        Assert.True(d.CanPromoteLoser);
    }

    // ══════════════════ CK2 ══════════════════

    [Fact]
    public void CK2_Kazanan_Ve_Kaybeden_Acikca_Yazar()
    {
        var id = CakismaUret();
        var d = new BusinessSyncService(_dst, _clock).ConflictDetail("ACME", id)!;

        Assert.Equal("admin", d.Winner);
        Assert.Contains("Admin", d.WinnerText);
        Assert.Contains("masaüstü", d.LoserText, StringComparison.OrdinalIgnoreCase);   // kaybeden taraf açıkça yazar
        Assert.Contains("web", d.WinnerWho, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Masaüstü (personel)", d.LoserWho);
        Assert.Equal("personnel", d.AuditEntityType);   // kaydın kendi log ekranına köprü
    }

    // ══════════════════ CK3 — EN ÖNEMLİ ══════════════════

    [Fact]
    public void CK3_Uzerine_Yazilan_Surum_Geri_Getirilir()
    {
        var id = CakismaUret();
        Assert.Equal("Web Sürümü", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';"));

        var svc = new BusinessSyncService(_dst, _clock);
        var alan = svc.PromoteLoser(Oturum(), id);

        Assert.True(alan > 0);
        Assert.Equal("Masaüstü Sürümü", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';"));
        Assert.Equal("0600", Scalar(_dst, "SELECT phone FROM personnel WHERE id='p1';"));
        Assert.Equal("resolved", Scalar(_dst, "SELECT status FROM data_conflicts WHERE id='" + id + "';"));
        Assert.Equal("loser_promoted", Scalar(_dst, "SELECT resolution FROM data_conflicts WHERE id='" + id + "';"));
    }

    // ══════════════════ CK4 ══════════════════

    /// <summary>Geri getirme yalnız sunucuda kalsaydı cihazlar eski değeri göstermeye devam ederdi.
    /// <c>version</c> + <c>updated_at</c> ilerlediği için değişiklik NORMAL senkron akışıyla yayılır.</summary>
    [Fact]
    public void CK4_Geri_Getirme_Senkrona_Yayilir()
    {
        var id = CakismaUret();
        var oncekiVersion = long.Parse(Scalar(_dst, "SELECT version FROM personnel WHERE id='p1';")!);

        _clock.UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(9_000_000_000_000);
        new BusinessSyncService(_dst, _clock).PromoteLoser(Oturum(), id);

        Assert.Equal(oncekiVersion + 1, long.Parse(Scalar(_dst, "SELECT version FROM personnel WHERE id='p1';")!));
        Assert.Equal("9000000000000", Scalar(_dst, "SELECT updated_at FROM personnel WHERE id='p1';"));
    }

    // ══════════════════ CK5 — YETKİ ══════════════════

    [Fact]
    public void CK5_Yetkisiz_Kullanici_Kazanani_Degistiremez()
    {
        var id = CakismaUret();
        var svc = new BusinessSyncService(_dst, _clock);

        // (a) Ekranı görebilir ama ÇÖZME yetkisi yok → reddedilir.
        Assert.Throws<ForbiddenException>(() => svc.PromoteLoser(Oturum(cozmeYetkisi: false), id));

        // (b) Çözme yetkisi VAR ama ekranı göremiyor → yine reddedilir (yan kapı kapalı).
        var ekransiz = new SessionContext("u1", "ACME", new[] { RoleKeys.Staff },
            new PermissionSet(new[] { new ModulePermission("personnel", true, true, true, false) },
                new[] { SpecialButtons.ConflictResolve }));
        Assert.Throws<ForbiddenException>(() => svc.PromoteLoser(ekransiz, id));

        Assert.Equal("Web Sürümü", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';")); // veri DOKUNULMAMIŞ
    }

    // ══════════════════ CK6 ══════════════════

    [Fact]
    public void CK6_Ayni_Cakisma_Iki_Kez_Cozulemez()
    {
        var id = CakismaUret();
        var svc = new BusinessSyncService(_dst, _clock);
        svc.PromoteLoser(Oturum(), id);

        var ex = Assert.Throws<InvalidOperationException>(() => svc.PromoteLoser(Oturum(), id));
        Assert.Contains("kapat", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════ CK7 — FİRMA SINIRI ══════════════════

    [Fact]
    public void CK7_Baska_Firmanin_Cakismasi_Acilamaz()
    {
        var id = CakismaUret();
        Firma(_dst, "OTHER");
        var svc = new BusinessSyncService(_dst, _clock);

        Assert.Null(svc.ConflictDetail("OTHER", id));
        Assert.Throws<InvalidOperationException>(() => svc.PromoteLoser(Oturum("OTHER"), id));
        Assert.Equal("Web Sürümü", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';"));
    }

    // ══════════════════ CK8 ══════════════════

    /// <summary>Migration094 ÖNCESİ oluşmuş çakışmalarda görüntü yoktur. Sessizce "başarılı" demek
    /// kullanıcıyı yanıltırdı; açık hata verilir.</summary>
    [Fact]
    public void CK8_Goruntusuz_Cakismada_Acik_Hata_Verilir()
    {
        var id = CakismaUret();
        Exec(_dst, "UPDATE data_conflicts SET loser_json=NULL WHERE id=@i;", ("@i", id));

        var svc = new BusinessSyncService(_dst, _clock);
        Assert.False(svc.ConflictDetail("ACME", id)!.CanPromoteLoser);
        var ex = Assert.Throws<InvalidOperationException>(() => svc.PromoteLoser(Oturum(), id));
        Assert.Contains("saklanmamış", ex.Message);
    }

    // ══════════════════ CK9 ══════════════════

    [Fact]
    public void CK9_Cozum_Kaydin_Kendi_Loguna_Duser()
    {
        var id = CakismaUret();
        new BusinessSyncService(_dst, _clock).PromoteLoser(Oturum(), id);

        Assert.Equal("1", Scalar(_dst,
            "SELECT COUNT(*) FROM audit_logs WHERE entity_type='personnel' AND entity_id='p1' AND action='restore';"));

        // Log satırı, geri getirilen sürümün anlık görüntüsünü de taşır (FAZ 4.3).
        var after = Scalar(_dst,
            "SELECT after_json FROM audit_logs WHERE entity_id='p1' AND action='restore';");
        Assert.Equal("Masaüstü Sürümü", Alan(after!, "full_name"));
    }

    // ══════════════════ CK10 ══════════════════

    [Fact]
    public void CK10_Alan_Bazli_Fark_Uretilir()
    {
        var id = CakismaUret();
        var d = new BusinessSyncService(_dst, _clock).ConflictDetail("ACME", id)!;

        var ad = Assert.Single(d.Differences, x => x.Label == "Ad Soyad");
        Assert.Equal("Masaüstü Sürümü", ad.Old);   // kaybeden
        Assert.Equal("Web Sürümü", ad.New);        // kazanan
        Assert.DoesNotContain(d.Differences, x => x.Field is "version" or "updated_at" or "id" or "company_id");
    }

    // ══════════════════ CK11 — GÜVENLİK ══════════════════

    /// <summary>Görüntüdeki <c>company_id</c>/<c>id</c>/<c>version</c> geri yazılsaydı, eski bir sürüm
    /// numarası senkron kararlarını bozar; firma kimliği ise kaydı başka firmaya TAŞIYABİLİRDİ.</summary>
    [Fact]
    public void CK11_Kimlik_Firma_Surum_Sutunlari_Geri_Yazilmaz()
    {
        var id = CakismaUret();
        // Kaybeden görüntüye kötü niyetli/eski değerler serpiştir.
        Exec(_dst, "UPDATE data_conflicts SET loser_json='{\"id\":\"BASKA\",\"company_id\":\"OTHER\",\"version\":\"1\",\"full_name\":\"Masaüstü Sürümü\"}' WHERE id=@i;", ("@i", id));

        var oncekiVersion = long.Parse(Scalar(_dst, "SELECT version FROM personnel WHERE id='p1';")!);
        new BusinessSyncService(_dst, _clock).PromoteLoser(Oturum(), id);

        Assert.Equal("ACME", Scalar(_dst, "SELECT company_id FROM personnel WHERE id='p1';"));
        Assert.Equal(oncekiVersion + 1, long.Parse(Scalar(_dst, "SELECT version FROM personnel WHERE id='p1';")!));
        Assert.Equal("1", Scalar(_dst, "SELECT COUNT(*) FROM personnel WHERE id='p1';"));
        Assert.Null(Scalar(_dst, "SELECT id FROM personnel WHERE id='BASKA';"));
    }
}
