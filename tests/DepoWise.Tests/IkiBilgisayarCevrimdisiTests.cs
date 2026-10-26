using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Sync;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-68 (AR-GE 2026-10-26, F-019, F-145) — ÇEVRİMDIŞI UÇTAN UCA: İKİ BİLGİSAYAR + SUNUCU ═══
/// Parçalar ayrı ayrı test ediliyordu; "bilgisayar 1 çevrimdışı yazar → gerçek API'ye gönderir → bilgisayar 2 çeker" ZİNCİRİ
/// yoktu. Burada iki ayrı yerel veritabanı (iki masaüstü) GERÇEK HTTP senkron uçlarıyla (business-push / business-pull — masaüstünün
/// kullandığı yol) tek sunucuya bağlanır. Stok işlemleri API'ye uğramadan, çevrimdışı yazılır.
///  E1 — bilgisayar 1'in çevrimdışı girişi sunucuya ve bilgisayar 2'ye aynen ulaşır
///  E2 — iki bilgisayar AYNI stoğu çevrimdışı düşer (oversell): sunucu ikisini de kabul eder (K-06 — reddetmek veri kaybettirir),
///       bakiye eksiye düşer ve "eksi stok" listesinde görünür; iki bilgisayar çekince aynı bakiyede buluşur, kopya hareket yok
///  E3 — ağ kopması: aynı paketin tekrar gönderimi kopya üretmez; boş tur hiçbir şey değiştirmez
///  E4 — iki bilgisayarın farklı depolara kayıtları birleşir; üç tarafın defteri ve bakiyesi aynı
/// </summary>
[Collection("PostgresSchema")]   // ApiTestHost süreç-genelinde ortam değişkeni yazar → seri koşmalı
public class IkiBilgisayarCevrimdisiTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "IKI-PC";
    private const string User = "iki_pc";
    private const string Pass = "Test!2026";

    private Bilgisayar _pc1 = null!, _pc2 = null!;
    private string _depoA = "", _depoB = "", _mat = "", _uid = "";

    /// <summary>Bir masaüstü: kendi yerel SQLite'ı + kendi saati + sunucu "bağlantısı" (yalnız senkron anında kullanılır).</summary>
    private sealed class Bilgisayar : IDisposable
    {
        public readonly string Yol;
        public readonly SqliteConnectionFactory Db;
        public readonly TestClock Saat = new();
        public readonly StockService Stok;
        public HttpClient Baglanti = null!;
        public SessionContext Oturum = null!;

        public Bilgisayar(string ad)
        {
            Yol = Path.Combine(Path.GetTempPath(), $"dw_iki_pc_{ad}_" + Guid.NewGuid().ToString("N") + ".db");
            Db = new SqliteConnectionFactory(Yol);
            new MigrationRunner(Db).Run();
            Stok = new StockService(Db, Saat);
        }

        public async Task<JsonElement> GonderAsync()
        {
            var paket = new BusinessSyncService(Db, Saat).BuildSnapshot(Company);
            var r = await Baglanti.PostAsync("/api/sync/business-push", new StringContent(paket, System.Text.Encoding.UTF8, "application/json"));
            r.EnsureSuccessStatusCode();
            return await ApiTestHost.JsonAsync(r);
        }

        public async Task CekAsync()
        {
            var r = await Baglanti.GetAsync("/api/sync/business-pull");
            r.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
            new BusinessSyncService(Db, Saat).ApplyPull(Company, doc.RootElement, new HashSet<string>(StringComparer.Ordinal) { "stock_balances" });
        }

        public decimal Bakiye(string mat, string depo) => Stok.GetBalanceAt(Oturum, mat, depo);
        public long HareketSayisi() => Say(Db);

        public void Dispose() { try { File.Delete(Yol); } catch { } }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static long Say(IDbConnectionFactory f)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id = @c;";
        cmd.AddWithValue("@c", Company);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private ServerServices Sunucu => _host.Services.GetRequiredService<ServerServices>();
    private SessionContext SunucuOturumu => new(_uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = Sunucu;
        using (var conn = svc.Factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
                              "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@id", Company);
            cmd.ExecuteNonQuery();
        }
        _uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        _depoA = svc.Branches.Create(SunucuOturumu, new NewBranch("Depo A"));
        _depoB = svc.Branches.Create(SunucuOturumu, new NewBranch("Şantiye B"));
        _mat = svc.Materials.Create(SunucuOturumu, new NewMaterial("IKI-1", "İki bilgisayar malzemesi"));

        _pc1 = await KurAsync("1");
        _pc2 = await KurAsync("2");
    }

    private async Task<Bilgisayar> KurAsync(string ad)
    {
        var pc = new Bilgisayar(ad);
        using (var conn = pc.Db.Create())
        using (var cmd = conn.CreateCommand())
        {
            // Firma + şubeler AYNI kimliklerle (şubeler iş senkronunda değil, org yolundan gelir — gerçek masaüstünün kurulum sonrası hâli).
            cmd.CommandText =
                "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,@c,1,1,1,0);" +
                "INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) VALUES(@a,@c,'Depo A','branch',1,1,1,0);" +
                "INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) VALUES(@b,@c,'Şantiye B','branch',1,1,1,0);";
            cmd.AddWithValue("@c", Company);
            cmd.AddWithValue("@a", _depoA);
            cmd.AddWithValue("@b", _depoB);
            cmd.ExecuteNonQuery();
        }
        pc.Oturum = new SessionContext(_uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = _depoA };
        pc.Baglanti = await _host.LoginAsync(User, Pass, Company);
        await pc.CekAsync();   // ilk kurulum: malzeme kataloğu sunucudan iner
        return pc;
    }

    public async Task DisposeAsync()
    {
        _pc1?.Dispose(); _pc2?.Dispose();
        await ((IAsyncLifetime)_host).DisposeAsync();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");
    private decimal SunucuBakiye(string depo) => Sunucu.Stock.GetBalanceAt(SunucuOturumu, _mat, depo);

    private async Task HerkesEsitlensin()
    {
        await _pc1.GonderAsync(); await _pc2.GonderAsync();
        await _pc1.CekAsync(); await _pc2.CekAsync();
    }

    [Fact]
    public async Task E1_Bilgisayar1_Cevrimdisi_Giris_Sunucuya_Ve_Bilgisayar2ye_Ulasir()
    {
        _pc1.Stok.ReceiveIn(_pc1.Oturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        Assert.Equal(10m, _pc1.Bakiye(_mat, _depoA));
        Assert.Equal(0m, _pc2.Bakiye(_mat, _depoA));                 // henüz senkron yok
        Assert.Equal(0m, SunucuBakiye(_depoA));

        await _pc1.GonderAsync();
        Assert.Equal(10m, SunucuBakiye(_depoA));
        await _pc2.CekAsync();
        Assert.Equal(10m, _pc2.Bakiye(_mat, _depoA));
        Assert.Equal(_pc1.HareketSayisi(), _pc2.HareketSayisi());
        Assert.Equal(_pc1.HareketSayisi(), Say(Sunucu.Factory));
    }

    [Fact]
    public async Task E2_Iki_Bilgisayar_Ayni_Stogu_Dusurse_Kabul_Uyari_Ve_Ayni_Sonuc()
    {
        _pc1.Stok.ReceiveIn(_pc1.Oturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        await HerkesEsitlensin();
        Assert.Equal(10m, _pc2.Bakiye(_mat, _depoA));

        // İkisi de çevrimdışı: her biri 10 görüyor; 7 ve 6 düşüyorlar (yerelde ikisi de geçerli).
        _pc1.Stok.IssueOut(_pc1.Oturum, new[] { new StockLine(_mat, 7m) }, Op(), branchId: _depoA);
        _pc2.Stok.IssueOut(_pc2.Oturum, new[] { new StockLine(_mat, 6m) }, Op(), branchId: _depoA);
        Assert.Equal(3m, _pc1.Bakiye(_mat, _depoA));
        Assert.Equal(4m, _pc2.Bakiye(_mat, _depoA));

        await HerkesEsitlensin();
        Assert.Equal(-3m, SunucuBakiye(_depoA));                     // iki çıkış da kayıtlı — veri kaybı yok
        Assert.Equal(-3m, _pc1.Bakiye(_mat, _depoA));
        Assert.Equal(-3m, _pc2.Bakiye(_mat, _depoA));
        using (var conn = Sunucu.Factory.Create())                    // K-06: "eksi stok oluştu" listesinde görünür
        {
            var eksi = DashboardService.NegativeStockList(conn, Company);
            Assert.Contains(eksi, r => r.MaterialId == _mat && r.LocationId == _depoA && r.Quantity == -3m);
        }
        var sayi = Say(Sunucu.Factory);
        Assert.Equal(sayi, _pc1.HareketSayisi());
        Assert.Equal(sayi, _pc2.HareketSayisi());
        Assert.Equal(3, sayi);                                        // giriş + iki çıkış, kopya yok
    }

    [Fact]
    public async Task E3_Ag_Kopmasi_Tekrar_Gonderim_Kopya_Uretmez()
    {
        _pc1.Stok.ReceiveIn(_pc1.Oturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);
        _pc1.Stok.IssueOut(_pc1.Oturum, new[] { new StockLine(_mat, 2m) }, Op(), branchId: _depoA);
        await _pc1.GonderAsync();
        await _pc1.GonderAsync();                                     // yanıt kayboldu varsayımıyla aynı paket yeniden
        await _pc1.GonderAsync();
        Assert.Equal(2, Say(Sunucu.Factory));
        Assert.Equal(3m, SunucuBakiye(_depoA));

        await HerkesEsitlensin(); await HerkesEsitlensin();           // boş turlar
        Assert.Equal(2, Say(Sunucu.Factory));
        Assert.Equal(2, _pc2.HareketSayisi());
        Assert.Equal(3m, _pc2.Bakiye(_mat, _depoA));
    }

    [Fact]
    public async Task E4_Farkli_Depolara_Kayitlar_Birlesir_Uc_Taraf_Ayni()
    {
        _pc1.Stok.ReceiveIn(_pc1.Oturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);
        var pc2SantiyeOturumu = new SessionContext(_uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = _depoB };
        _pc2.Stok.ReceiveIn(pc2SantiyeOturumu, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);

        await HerkesEsitlensin();
        foreach (var (a, b) in new[] { (_pc1.Bakiye(_mat, _depoA), _pc1.Bakiye(_mat, _depoB)), (_pc2.Bakiye(_mat, _depoA), _pc2.Bakiye(_mat, _depoB)),
                                       (SunucuBakiye(_depoA), SunucuBakiye(_depoB)) })
        {
            Assert.Equal(5m, a);
            Assert.Equal(4m, b);
        }
        Assert.Equal(2, Say(Sunucu.Factory));
        Assert.Equal(2, _pc1.HareketSayisi());
        Assert.Equal(2, _pc2.HareketSayisi());
    }
}
