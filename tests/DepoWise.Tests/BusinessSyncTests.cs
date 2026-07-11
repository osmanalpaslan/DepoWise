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
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void SeedCompany(SqliteConnectionFactory f, string id)
        => Exec(f, "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES($i,$n,1,1,1,0);",
            ("$i", id), ("$n", id));

    private static void InsertPersonnel(SqliteConnectionFactory f, string id, string company, string name, long updatedAt)
        => Exec(f, "INSERT INTO personnel(id,company_id,full_name,is_active,created_at,updated_at,version,is_deleted) " +
                   "VALUES($i,$c,$n,1,1,$u,1,0);",
            ("$i", id), ("$c", company), ("$n", name), ("$u", updatedAt));

    private static string? Scalar(SqliteConnectionFactory f, string sql, params (string, object?)[] ps)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in ps) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        var v2 = cmd.ExecuteScalar();
        return v2 is null || v2 is DBNull ? null : Convert.ToString(v2, System.Globalization.CultureInfo.InvariantCulture);
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
        Exec(_src, "INSERT INTO stock_balances(company_id,material_id,quantity,updated_at) VALUES('ACME','m1',5,100);");

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
        // stock_balances PK'si material_id (id değil) → generic upsert PK'yi doğru bulmalı.
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        // Ebeveyn malzeme + bakiye (Tables sırası: materials önce, stock_balances sonra → FK çözülür).
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','Malzeme','0','0',1,50,1,0);");
        Exec(_src, "INSERT INTO stock_balances(company_id,material_id,quantity,updated_at) VALUES('ACME','m1','5',50);");

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

    [Fact]
    public void Apply_NegatifStokBakiyesi_Reddedilir()
    {
        SeedCompany(_src, "ACME");
        SeedCompany(_dst, "ACME");
        Exec(_src, "INSERT INTO materials(id,company_id,code,name,min_stock,unit_price,created_at,updated_at,version,is_deleted) " +
                   "VALUES('m1','ACME','K1','Malzeme','0','0',1,100,1,0);");
        // Bozuk snapshot: negatif bakiye
        Exec(_src, "INSERT INTO stock_balances(company_id,material_id,quantity,updated_at) VALUES('ACME','m1','-9',50);");

        var snap = new BusinessSyncService(_src, _clock).BuildSnapshot("ACME");
        using var doc = JsonDocument.Parse(snap);
        var admin = Session("ACME", new[] { DepoWise.Application.Security.RoleKeys.CompanyAdmin });
        var res = new BusinessSyncService(_dst, _clock).Apply(admin, doc.RootElement);

        // materials uygulandı, negatif bakiye reddedildi
        Assert.Equal("Malzeme", Scalar(_dst, "SELECT name FROM materials WHERE id='m1';"));
        Assert.Null(Scalar(_dst, "SELECT quantity FROM stock_balances WHERE material_id='m1';"));
        Assert.Contains(res.Errors, e => e.Contains("negatif"));
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); File.Delete(_srcPath); File.Delete(_dstPath); } catch { }
    }
}
