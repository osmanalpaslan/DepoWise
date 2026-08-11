using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// İş verisi snapshot senkronu (Faz 2 — web görünürlüğü): masaüstü DB'den snapshot üret → sunucu DB'ye uygula.
/// Tenant zorlaması + LWW (updated_at) davranışı doğrulanır.
/// </summary>
public class BusinessSyncTests : IDisposable
{
    private readonly string _srcPath, _dstPath;
    private readonly SqliteConnectionFactory _src, _dst;
    private readonly TestClock _clock = new();

    public BusinessSyncTests()
    {
        _srcPath = Path.Combine(Path.GetTempPath(), "dw_bsync_src_" + Guid.NewGuid().ToString("N") + ".db");
        _dstPath = Path.Combine(Path.GetTempPath(), "dw_bsync_dst_" + Guid.NewGuid().ToString("N") + ".db");
        _src = new SqliteConnectionFactory(_srcPath);
        _dst = new SqliteConnectionFactory(_dstPath);
        new MigrationRunner(_src).Run();
        new MigrationRunner(_dst).Run();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static void Exec(SqliteConnectionFactory f, string sql, params (string, object?)[] ps)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void SeedCompany(SqliteConnectionFactory f, string id)
        => Exec(f, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@n,1,1,1,0);",
            ("@i", id), ("@n", id));

    private static void InsertPersonnel(SqliteConnectionFactory f, string id, string company, string name, long updatedAt)
        => Exec(f, "INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) " +
                   "VALUES(@i,@c,@n,1,1,@u,1,0);",
            ("@i", id), ("@c", company), ("@n", name), ("@u", updatedAt));

    private static string? Scalar(SqliteConnectionFactory f, string sql, params (string, object?)[] ps)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.AddWithValue(k, v ?? DBNull.Value);
        var v2 = cmd.ExecuteScalar();
        return v2 is null || v2 is DBNull ? null : Convert.ToString(v2, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Delta_Snapshot_YalnizDegisenleri_Icerir_CompanyVersion_MaxDoner()
    {
        // DELTA (kullanıcı bulgusu 2026-07-19: 2508 kayıtta tam snapshot zaman aşımına uğruyordu).
        SeedCompany(_src, "ACME");
        InsertPersonnel(_src, "P1", "ACME", "Ali", updatedAt: 1000);
        InsertPersonnel(_src, "P2", "ACME", "Veli", updatedAt: 2000);
        InsertPersonnel(_src, "P3", "ACME", "Can", updatedAt: 3000);

        var svc = new BusinessSyncService(_src, _clock);
        // CompanyVersion = en büyük updated_at
        Assert.Equal(3000, svc.CompanyVersion("ACME"));

        // sinceVersion=1500 → yalnız P2(2000) ve P3(3000) gelir; P1(1000) GELMEZ.
        using var delta = JsonDocument.Parse(svc.BuildSnapshot("ACME", null, sinceVersion: 1500));
        var personnel = delta.RootElement.GetProperty("tables").GetProperty("personnel");
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (var row in personnel.EnumerateArray()) ids.Add(row.GetProperty("id").GetString()!);
        Assert.Equal(2, ids.Count);
        Assert.Contains("P2", ids);
        Assert.Contains("P3", ids);
        Assert.DoesNotContain("P1", ids);

        // sinceVersion=0 → TAM (üçü de)
        using var full = JsonDocument.Parse(svc.BuildSnapshot("ACME", null, sinceVersion: 0));
        Assert.Equal(3, full.RootElement.GetProperty("tables").GetProperty("personnel").GetArrayLength());
    }

    private static System.Collections.Generic.HashSet<string> Ids(JsonDocument doc, string table)
    {
        var set = new System.Collections.Generic.HashSet<string>();
        foreach (var row in doc.RootElement.GetProperty("tables").GetProperty(table).EnumerateArray())
            set.Add(row.GetProperty("id").GetString()!);
        return set;
    }

    [Fact]
    public void Z4_Push_SunucuGlobalMax_Yerine_Watermark_Kullaninca_KayitAtlanmaz()
    {
        // ⚠️ Z4 KÖK NEDEN (kanıtlı 94-araç / personel bug'ı): eski push "since = SUNUCU global max(updated_at)"
        // kullanıyordu. Başka bir kaydın (toplu import / başka makine / başka tablo) YÜKSEK zaman damgası, bu
        // makinenin KENDİ yeni kaydını "gönderilmiş gibi" ATLATIYORDU. Yeni push, bu makinenin KENDİ
        // watermark'ını kullanır → atlama imkânsız. (Mekanizma tablo-bağımsız: BuildSnapshot her tabloya AYNI
        // "updated_at > since" filtresini uygular; personel temsilîdir — malzeme/araç/bakım/yakıt/talep aynıdır.)
        SeedCompany(_src, "ACME");
        InsertPersonnel(_src, "P_NEW", "ACME", "Yeni-henuz-gonderilmemis", updatedAt: 1000);
        InsertPersonnel(_src, "P_HIGH", "ACME", "Global-max-yukselten", updatedAt: 5000);

        var svc = new BusinessSyncService(_src, _clock);
        long serverGlobalMax = svc.CompanyVersion("ACME"); // = 5000 (başka kayıt yüzünden yüksek)
        Assert.Equal(5000, serverGlobalMax);

        // (A) ESKİ MANTIK: since = sunucu global max (5000) → P_NEW(1000 ≤ 5000) ATLANIR = BUG.
        using (var eski = JsonDocument.Parse(svc.BuildSnapshot("ACME", null, sinceVersion: serverGlobalMax)))
            Assert.DoesNotContain("P_NEW", Ids(eski, "personnel"));

        // (B) YENİ MANTIK: since = bu makinenin watermark'ı (henüz push yok → 0) → P_NEW GÖNDERİLİR = DÜZELTME.
        using (var yeni = JsonDocument.Parse(svc.BuildSnapshot("ACME", null, sinceVersion: 0)))
            Assert.Contains("P_NEW", Ids(yeni, "personnel"));

        // (C) "Her şeyi tekrar gönderme" YASAĞI korunur: watermark P_NEW'i kapsayınca (1000), sonraki push
        //     yalnız GERÇEKTEN yeni kaydı (P_NEXT) taşır; P_NEW tekrar gönderilmez.
        InsertPersonnel(_src, "P_NEXT", "ACME", "Sonraki", updatedAt: 6000);
        using (var delta = JsonDocument.Parse(svc.BuildSnapshot("ACME", null, sinceVersion: 1000)))
        {
            var ids = Ids(delta, "personnel");
            Assert.Contains("P_NEXT", ids);
            Assert.DoesNotContain("P_NEW", ids);
        }
    }

    [Fact]
    public void Webte_Silinen_Kayit_Yerelde_De_Silinir_SUNUCU_OTORITER()
    {
        // WEB TAM OTORİTER: sunucuda (web) silinen kayıt, makinenin yerel DB'sinde de silinmeli —
        // makinede daha YENİ bir düzenleme olsa bile. (Eskiden LWW yüzünden silme atlanıyor, kayıt "diriliyordu".)
        SeedCompany(_src, "ACME");   // _src = SUNUCU (web)
        SeedCompany(_dst, "ACME");   // _dst = MAKİNE (yerel)

        InsertPersonnel(_src, "P1", "ACME", "Ali", updatedAt: 1000);
        InsertPersonnel(_dst, "P1", "ACME", "Ali", updatedAt: 1000);

        // 1) Web'de SİLİNDİ (soft delete, updated_at=2000)
        Exec(_src, "UPDATE personnel SET is_deleted=1, updated_at=2000 WHERE id='P1';");
        // 2) Makinede kayıt DAHA SONRA düzenlendi (updated_at=3000) → LWW'ye göre yerel "daha yeni"
        Exec(_dst, "UPDATE personnel SET full_name='Ali (yerelde degisti)', updated_at=3000 WHERE id='P1';");

        // 3) Geri-çekme: sunucu snapshot'ı makineye uygulanır
        using var snap = JsonDocument.Parse(new BusinessSyncService(_src, _clock).BuildSnapshot("ACME"));
        new BusinessSyncService(_dst, _clock).ApplyPull("ACME", snap.RootElement);

        // Silme KAZANMALI (yerel daha yeni olmasına rağmen)
        Assert.Equal("1", Scalar(_dst, "SELECT is_deleted FROM personnel WHERE id='P1';"));
    }

    [Fact]
    public void Sunucuda_Silinen_Kayit_Cihaz_Pushuyla_Diriltilemez()
    {
        // Masaüstü girişte ÖNCE PUSH sonra PULL yapar. Web'de silinmiş bir kaydı makine "silinmemiş" ve daha yeni
        // updated_at ile push ederse, eskiden sunucuda kayıt DİRİLİYOR, sonra pull ile tüm makinelere geri yayılıyordu.
        SeedCompany(_src, "ACME");   // _src = MAKİNE (push eden)
        SeedCompany(_dst, "ACME");   // _dst = SUNUCU (web)

        InsertPersonnel(_dst, "P9", "ACME", "Ayse", updatedAt: 1000);
        Exec(_dst, "UPDATE personnel SET is_deleted=1, updated_at=2000 WHERE id='P9';");   // web'de SİLİNDİ

        // Makinede kayıt hâlâ canlı ve DAHA YENİ (henüz silmeyi görmemiş)
        InsertPersonnel(_src, "P9", "ACME", "Ayse (makinede canli)", updatedAt: 9000);

        // Makine sunucuya push eder (server-side apply)
        using var snap = JsonDocument.Parse(new BusinessSyncService(_src, _clock).BuildSnapshot("ACME", "MPC"));
        new BusinessSyncService(_dst, _clock).Apply("ACME", snap.RootElement);

        // Sunucudaki silme KORUNMALI (diriltilmemeli)
        Assert.Equal("1", Scalar(_dst, "SELECT is_deleted FROM personnel WHERE id='P9';"));
    }

    [Fact]
    public void GeriCekmede_SilinmemisKayitta_LWW_Korunur()
    {
        // Karşı kontrol: silme SÖZ KONUSU DEĞİLSE eski LWW davranışı aynen sürer —
        // makinedeki daha yeni düzenleme, sunucunun eski sürümüyle EZİLMEZ.
        SeedCompany(_src, "ACME"); SeedCompany(_dst, "ACME");
        InsertPersonnel(_src, "P2", "ACME", "Veli (sunucu eski)", updatedAt: 1000);
        InsertPersonnel(_dst, "P2", "ACME", "Veli (yerel yeni)", updatedAt: 5000);

        using var snap = JsonDocument.Parse(new BusinessSyncService(_src, _clock).BuildSnapshot("ACME"));
        new BusinessSyncService(_dst, _clock).ApplyPull("ACME", snap.RootElement);

        Assert.Equal("Veli (yerel yeni)", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='P2';"));
        Assert.Equal("0", Scalar(_dst, "SELECT is_deleted FROM personnel WHERE id='P2';"));
    }

    [Fact]
    public void CokMakineli_GeriCekme_BMakinesi_ANinVerisiniGorur()
    {
        // A makinesi (_src) + Sunucu (_dst) + B makinesi (3. DB)
        var bPath = Path.Combine(Path.GetTempPath(), "dw_bsync_b_" + Guid.NewGuid().ToString("N") + ".db");
        var bFactory = new SqliteConnectionFactory(bPath);
        try
        {
            new MigrationRunner(bFactory).Run();
            SeedCompany(_src, "ACME"); SeedCompany(_dst, "ACME"); SeedCompany(bFactory, "ACME");

            // A makinesi personel girer → sunucuya PUSH
            InsertPersonnel(_src, "pA", "ACME", "Ahmet (A makinesi)", 100);
            using (var snapA = JsonDocument.Parse(new BusinessSyncService(_src, _clock).BuildSnapshot("ACME")))
                new BusinessSyncService(_dst, _clock).Apply("ACME", snapA.RootElement);

            // B makinesi başta A'nın verisini GÖRMEZ
            Assert.Null(Scalar(bFactory, "SELECT full_name FROM personnel WHERE id='pA';"));

            // B makinesi GERİ-ÇEKER (server → B) → artık A'nın verisini görür
            using (var snapS = JsonDocument.Parse(new BusinessSyncService(_dst, _clock).BuildSnapshot("ACME")))
                new BusinessSyncService(bFactory, _clock).ApplyPull("ACME", snapS.RootElement,
                    new HashSet<string>(StringComparer.Ordinal) { "stock_balances" });

            Assert.Equal("Ahmet (A makinesi)", Scalar(bFactory, "SELECT full_name FROM personnel WHERE id='pA';"));
        }
        finally { try { SqliteConnection.ClearAllPools(); File.Delete(bPath); } catch { } }
    }

    [Fact]
    public void GeriCekme_HaricTutulanTablo_Uygulanmaz()
    {
        SeedCompany(_src, "ACME"); SeedCompany(_dst, "ACME");
        // Sunucuda bir stock_balances satırı (türetilmiş) olsun
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,unit_price,currency_code,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','Malzeme',0,'TRY',1,100,1,0);");
        Exec(_src, "INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES('ACME','m1','',5,100);");

        using var snap = JsonDocument.Parse(new BusinessSyncService(_src, _clock).BuildSnapshot("ACME"));
        new BusinessSyncService(_dst, _clock).ApplyPull("ACME", snap.RootElement,
            new HashSet<string>(StringComparer.Ordinal) { "stock_balances" });

        Assert.Equal("Malzeme", Scalar(_dst, "SELECT name FROM materials WHERE id='m1';")); // malzeme uygulandı
        Assert.Null(Scalar(_dst, "SELECT quantity FROM stock_balances WHERE material_id='m1';")); // stock_balances HARİÇ
    }

    [Fact]
    public void Snapshot_SunucudaKayitOlusturur()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        InsertPersonnel(_src, "p1", "ACME", "Ali Veli", 100);

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        var res = new BusinessSyncService(_dst, _clock).Apply("ACME", doc.RootElement);

        Assert.True(res.Upserted >= 1);
        Assert.Equal("Ali Veli", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';"));
    }

    [Fact]
    public void Apply_CompanyId_OturumdanZorlanir()
    {
        // Kaynakta farklı firma id'si olsa bile sunucu oturumun firmasını yazar (tenant güvenliği).
        SeedCompany(_src, "EVIL");
        SeedCompany(_dst, "ACME");
        InsertPersonnel(_src, "p9", "EVIL", "Sızıntı", 100);

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("EVIL");
        using var doc = JsonDocument.Parse(snap);
        new BusinessSyncService(_dst, _clock).Apply("ACME", doc.RootElement);

        Assert.Equal("ACME", Scalar(_dst, "SELECT company_id FROM personnel WHERE id='p9';"));
    }

    [Fact]
    public void Apply_LWW_EskiYazmaYeniyiEzmez()
    {
        SeedCompany(_dst, "ACME");
        SeedCompany(_src, "ACME");
        // Sunucuda YENİ sürüm (updated_at=200)
        InsertPersonnel(_dst, "p2", "ACME", "Yeni İsim", 200);
        // Kaynakta ESKİ sürüm (updated_at=100)
        InsertPersonnel(_src, "p2", "ACME", "Eski İsim", 100);

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        new BusinessSyncService(_dst, _clock).Apply("ACME", doc.RootElement);

        // Eski yazma yeniyi EZMEMELİ
        Assert.Equal("Yeni İsim", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p2';"));
    }

    [Fact]
    public void Apply_IdOlmayanPk_StockBalances_Calisir()
    {
        // stock_balances PK'si 'id' DEĞİL, üç kolonlu BİLEŞİK anahtardır (company_id, material_id, location_id)
        // → generic upsert PK'yi DbIntrospect'ten okuyup ON CONFLICT hedefini üç kolonla kurmalı (STK-02).
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        // Ebeveyn malzeme + bakiye (Tables sırası: materials önce, stock_balances sonra → FK çözülür).
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','Malzeme','0','0',1,50,1,0);");
        Exec(_src, "INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES('ACME','m1','','5',50);");

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        var res = new BusinessSyncService(_dst, _clock).Apply("ACME", doc.RootElement);

        Assert.True(res.Upserted >= 2);
        Assert.Equal("Malzeme", Scalar(_dst, "SELECT name FROM materials WHERE id='m1';"));
        Assert.Equal("5", Scalar(_dst, "SELECT quantity FROM stock_balances WHERE material_id='m1';"));
    }

    [Fact]
    public void Cakisma_AdminVePersonelAyniKaydiDegistirirse_Tespit()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        // Cihaz sunucuda kayıtlı, son push baseline = 100
        Exec(_dst, "INSERT INTO sync_devices(id,company_id,device_name,status,last_business_push_at,created_at,updated_at,version) " +
                   "VALUES('dev1','ACME','MPC','active',100,1,1,1);");
        // Admin (web) sunucuda p1'i düzenledi (updated_at=200) + audit
        InsertPersonnel(_dst, "p1", "ACME", "Admin İsmi", 200);
        Exec(_dst, "INSERT INTO users(id,company_id,username,password_hash,is_active,created_at,updated_at,version,is_deleted) " +
                   "VALUES('u1','ACME','admin','x',1,1,1,1,0);");
        Exec(_dst, "INSERT INTO audit_logs(id,company_id,user_id,entity_type,entity_id,action,created_at) " +
                   "VALUES('a1','ACME','u1','personnel','p1','update',210);");
        // Personel (masaüstü) aynı kaydı düzenledi (updated_at=150) — ikisi de baseline 100 sonrası
        InsertPersonnel(_src, "p1", "ACME", "Personel İsmi", 150);

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME", "MPC");
        using var doc = JsonDocument.Parse(snap);
        new BusinessSyncService(_dst, _clock).Apply("ACME", doc.RootElement);

        // Çakışma kaydı oluştu, kazanan admin (200 > 150), LWW admin ismini korudu
        Assert.Equal("Admin İsmi", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';"));
        Assert.Equal("1", Scalar(_dst, "SELECT COUNT(*) FROM data_conflicts WHERE entity_id='p1' AND status='open';"));
        Assert.Equal("admin", Scalar(_dst, "SELECT winner FROM data_conflicts WHERE entity_id='p1';"));
        Assert.Equal("admin", Scalar(_dst, "SELECT admin_name FROM data_conflicts WHERE entity_id='p1';")); // users.username (full_name yok)
    }

    [Fact]
    public void Cakisma_YokEgerYalnizBirTarafDegistiyse()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        Exec(_dst, "INSERT INTO sync_devices(id,company_id,device_name,status,last_business_push_at,created_at,updated_at,version) " +
                   "VALUES('dev1','ACME','MPC','active',100,1,1,1);");
        // Sadece personel değiştirdi (150>100); sunucu eski (updated_at=90 <= baseline)
        InsertPersonnel(_dst, "p1", "ACME", "Eski", 90);
        InsertPersonnel(_src, "p1", "ACME", "Personel Yeni", 150);

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME", "MPC");
        using var doc = JsonDocument.Parse(snap);
        new BusinessSyncService(_dst, _clock).Apply("ACME", doc.RootElement);

        Assert.Equal("Personel Yeni", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';")); // device kazandı
        Assert.Equal("0", Scalar(_dst, "SELECT COUNT(*) FROM data_conflicts WHERE entity_id='p1';")); // çakışma YOK
    }

    // ── Yetki + içerik doğrulaması (Y3) ──

    private static DepoWise.Application.Security.SessionContext Session(
        string company, string[] roles, params (string module, bool create, bool edit)[] perms)
    {
        var mods = perms.Select(p => new DepoWise.Application.Security.ModulePermission(
            p.module, CanView: true, CanCreate: p.create, CanEdit: p.edit, CanDelete: false));
        return new DepoWise.Application.Security.SessionContext(
            "u1", company, roles, new DepoWise.Application.Security.PermissionSet(mods));
    }

    [Fact]
    public void Apply_YetkisizModul_TablosuUygulanmaz()
    {
        // Personel kullanıcısı yalnız 'personnel' yazabiliyor; materials yazma yetkisi YOK.
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        InsertPersonnel(_src, "p1", "ACME", "Ali", 100);
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','İzinsiz Malzeme','0','0',1,100,1,0);");

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        var s = Session("ACME", new[] { DepoWise.Application.Security.RoleKeys.Staff }, ("personnel", true, true));
        new BusinessSyncService(_dst, _clock).Apply(s, doc.RootElement);

        // personnel uygulandı, materials (yetkisiz) uygulanmadı
        Assert.Equal("Ali", Scalar(_dst, "SELECT full_name FROM personnel WHERE id='p1';"));
        Assert.Null(Scalar(_dst, "SELECT name FROM materials WHERE id='m1';"));
    }

    [Fact]
    public void Apply_Admin_TumTablolariYazabilir()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','Malzeme','0','0',1,100,1,0);");

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        var admin = Session("ACME", new[] { DepoWise.Application.Security.RoleKeys.CompanyAdmin });
        new BusinessSyncService(_dst, _clock).Apply(admin, doc.RootElement);

        Assert.Equal("Malzeme", Scalar(_dst, "SELECT name FROM materials WHERE id='m1';"));
    }

    /// <summary>ADR-086: açılış stoğu negatif olabildiğinden türetilmiş BAKİYE de negatif olabilir →
    /// stock_balances negatif değeri artık REDDEDİLMEZ (senkronda uygulanır). Ledger kalkanı hareket
    /// düzeyinde korunur (bkz. Apply_NegatifHareketMiktari_Reddedilir).</summary>
    [Fact]
    public void Apply_NegatifStokBakiyesi_Uygulanir()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','Malzeme','0','0',1,100,1,0);");
        // Negatif açılış → negatif bakiye (devralınan eksik stok). Artık geçerli bir durumdur.
        Exec(_src, "INSERT INTO stock_balances(company_id,material_id,location_id,quantity,updated_at) VALUES('ACME','m1','','-9',50);");

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        var admin = Session("ACME", new[] { DepoWise.Application.Security.RoleKeys.CompanyAdmin });
        var res = new BusinessSyncService(_dst, _clock).Apply(admin, doc.RootElement);

        Assert.Equal("Malzeme", Scalar(_dst, "SELECT name FROM materials WHERE id='m1';"));
        Assert.Equal("-9", Scalar(_dst, "SELECT quantity FROM stock_balances WHERE material_id='m1';"));
        Assert.DoesNotContain(res.Errors, e => e.Contains("negatif"));
    }

    /// <summary>Ledger kalkanı KORUNUR: stock_movements.quantity negatif snapshot'ı REDDEDİLİR — negatif açılış
    /// dahi hareket düzeyinde DAİMA pozitif quantity + direction=-1 olarak saklanır (ADR-086), ham negatif miktar
    /// yalnız bozuk/kötü niyetli snapshot'tan gelebilir.</summary>
    [Fact]
    public void Apply_NegatifHareketMiktari_Reddedilir()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','Malzeme','0','0',1,100,1,0);");
        Exec(_src, "INSERT INTO stock_movements(id,company_id,material_id,movement_type,direction,quantity,operation_id,created_at) " +
                   "VALUES('mv1','ACME','m1','opening',1,'-9','op-bad',50);");

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        var admin = Session("ACME", new[] { DepoWise.Application.Security.RoleKeys.CompanyAdmin });
        var res = new BusinessSyncService(_dst, _clock).Apply(admin, doc.RootElement);

        Assert.Null(Scalar(_dst, "SELECT quantity FROM stock_movements WHERE id='mv1';"));
        Assert.Contains(res.Errors, e => e.Contains("negatif"));
    }

    // ---- QA (§7) 2026-07-22: eşitleme çekirdeği regresyon testleri ----

    /// <summary>
    /// REGRESYON — canlı hata (2026-07-19): 2508 kayıtlı push sunucuda zaman aşımına uğruyor,
    /// ÖNDEKİ tablolar (malzeme) uygulanıp ARKADAKİ tablolar (araçlar) hiç ulaşmıyordu; kök sebep
    /// ApplyCore'un transaction'sız olması → satır başına ayrı commit (fsync) → dakikalarca sürüyordu.
    /// Bu test hem "arkadaki tablo da uygulanır" hem de "toplu commit hâlâ yerinde" (süre) güvencesini verir:
    /// transaction kaldırılırsa aynı yük dakikalara çıkar ve eşik aşılır.
    /// </summary>
    [Fact]
    public void Apply_BuyukCokTabloluBatch_ArkadakiTablolarDaUygulanir_VeTekTransactionKalir()
    {
        const int N = 600; // 1200 satır: personel (ön tablo) + araç (arka tablo)
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");

        using (var conn = _src.Create())
        {
            using var tx = conn.BeginTransaction();
            for (int i = 0; i < N; i++)
            {
                using var c1 = conn.CreateCommand();
                c1.CommandText = "INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) " +
                                 "VALUES(@i,'ACME',@n,1,1,1000,1,0);";
                c1.AddWithValue("@i", "P" + i);
                c1.AddWithValue("@n", "Personel " + i);
                c1.ExecuteNonQuery();

                using var c2 = conn.CreateCommand();
                c2.CommandText = "INSERT INTO vehicles(id,company_id,internal_code,plate,current_meter,meter_unit,status," +
                                 "created_at,updated_at,version,is_deleted) VALUES(@i,'ACME',@k,@p,'0','km','active',1,1000,1,0);";
                c2.AddWithValue("@i", "V" + i);
                c2.AddWithValue("@k", "KOD" + i);
                c2.AddWithValue("@p", "06 FF " + i);
                c2.ExecuteNonQuery();
            }
            tx.Commit();
        }

        var json = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(json);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = new BusinessSyncService(_dst, _clock).Apply("ACME", doc.RootElement);
        sw.Stop();

        // ARKADAKİ tablo (araçlar) da tamamen uygulanmalı — canlıda kaybolan buydu.
        Assert.Equal(N.ToString(), Scalar(_dst, "SELECT COUNT(*) FROM vehicles WHERE company_id='ACME';"));
        Assert.Equal(N.ToString(), Scalar(_dst, "SELECT COUNT(*) FROM personnel WHERE company_id='ACME';"));
        Assert.Equal("06 FF 0", Scalar(_dst, "SELECT plate FROM vehicles WHERE id='V0';"));
        Assert.Empty(res.Errors);

        // Toplu commit koruması: transaction kaldırılırsa 1200 fsync ile bu eşik kesin aşılır.
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20),
            $"1200 satırlık apply {sw.Elapsed.TotalSeconds:F1}s sürdü — tek transaction kaldırılmış olabilir.");
    }

    /// <summary>
    /// GÜVENLİK (§7.12 tenant sızıntısı): bir firmanın snapshot'ı BAŞKA firmanın tek bir satırını bile
    /// içermemeli. Sızarsa o veri karşı tarafta uygulanır → firmalar arası veri sızıntısı.
    /// </summary>
    [Fact]
    public void Snapshot_BaskaFirmaninVerisini_Sizdirmaz()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_src, "RAKIP");
        InsertPersonnel(_src, "P-ACME", "ACME", "Bizim Ali", updatedAt: 1000);
        InsertPersonnel(_src, "P-RAKIP", "RAKIP", "Rakip Veli", updatedAt: 1000);

        using var doc = JsonDocument.Parse(new BusinessSyncService(_src, _clock).BuildSnapshot("ACME"));

        // Hiçbir tabloda RAKIP'e ait company_id bulunmamalı.
        foreach (var table in doc.RootElement.GetProperty("tables").EnumerateObject())
            foreach (var row in table.Value.EnumerateArray())
                if (row.TryGetProperty("company_id", out var cid) && cid.ValueKind == JsonValueKind.String)
                    Assert.Equal("ACME", cid.GetString());

        var personnel = doc.RootElement.GetProperty("tables").GetProperty("personnel");
        Assert.Equal(1, personnel.GetArrayLength());
        Assert.Equal("P-ACME", personnel[0].GetProperty("id").GetString());
    }

    /// <summary>
    /// REGRESYON — QA bulgusu (2026-07-22, canlı sunucuda tespit): stock_movements (append-only defter)
    /// updated_at taşımadığı için (a) delta filtresine HİÇ girmiyor, her eşitlemede tüm defter aktarılıyordu,
    /// (b) CompanyVersion onu atladığı için yeni hareket firma sürümünü yükseltmiyor, karşı makine çekmiyordu.
    /// Damga sütunu created_at'e düşünce ikisi de düzelir.
    /// </summary>
    [Fact]
    public void Defter_UpdatedAtsiz_Tablo_DeltayaGirer_VeSurumuYukseltir()
    {
        SeedCompany(_src, "ACME");
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,created_at,updated_at,version,is_deleted) " +
                   "VALUES('M1','ACME','K1','Cimento',1,1000,1,0);");
        // Eski hareket (created_at=1000) ve YENİ hareket (created_at=5000)
        void Movement(string id, long createdAt) => Exec(_src,
            "INSERT INTO stock_movements(id,company_id,material_id,movement_type,direction,quantity,currency_code," +
            "operation_id,created_at) VALUES(@i,'ACME','M1','in',1,'5','TRY',@o,@t);",
            ("@i", id), ("@o", "op-" + id), ("@t", createdAt));
        Movement("MV-ESKI", 1000);
        Movement("MV-YENI", 5000);

        var svc = new BusinessSyncService(_src, _clock);

        // (b) Yeni hareket firma SÜRÜMÜNÜ yükseltmeli — yoksa karşı makine "değişiklik yok" sanar.
        Assert.Equal(5000, svc.CompanyVersion("ACME"));

        // (a) since=2000 → yalnız YENİ hareket gelmeli; tüm defter değil.
        using var delta = JsonDocument.Parse(svc.BuildSnapshot("ACME", null, sinceVersion: 2000));
        var mv = delta.RootElement.GetProperty("tables").GetProperty("stock_movements");
        Assert.Equal(1, mv.GetArrayLength());
        Assert.Equal("MV-YENI", mv[0].GetProperty("id").GetString());

        // Tam çekmede ikisi de durur (veri kaybı yok).
        using var full = JsonDocument.Parse(svc.BuildSnapshot("ACME", null, sinceVersion: 0));
        Assert.Equal(2, full.RootElement.GetProperty("tables").GetProperty("stock_movements").GetArrayLength());
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_srcPath); File.Delete(_dstPath); } catch { }
    }
}
