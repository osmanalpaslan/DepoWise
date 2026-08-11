using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-05 (FAZ C, 2026-08-11) — MASAÜSTÜ + ÇEVRİMDIŞI LOKASYON DESTEĞİ.
///
/// Masaüstü stok işlemlerini <b>API'ye uğramadan</b>, doğrudan yerel SQLite üzerinde yapar
/// (çevrimdışı çalışma Alpnex'in temel gereksinimidir). Bu testler tam olarak o yolu koşturur:
/// hiçbir HTTP çağrısı yoktur — <see cref="ApiTestHost"/> kullanılmaz.
///
/// Kapsam: lokasyonlu giriş/çıkış/transfer/sayım/açılış · firma toplamı ↔ lokasyon ayrımı ·
/// ATANMAMIŞ · şirket izolasyonu · çevrimdışı→senkron sonrası lokasyonun KORUNMASI ·
/// online→offline→online döngüsünde kopya hareket oluşmaması.
/// </summary>
public class DesktopOfflineLocationTests : IDisposable
{
    private readonly string _localPath, _serverPath;
    private readonly SqliteConnectionFactory _local, _server;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;
    private readonly SessionContext _depoAOturum;
    private readonly string _depoA, _depoB, _mat;

    public DesktopOfflineLocationTests()
    {
        _localPath = Path.Combine(Path.GetTempPath(), "dw_desktop_" + Guid.NewGuid().ToString("N") + ".db");
        _serverPath = Path.Combine(Path.GetTempPath(), "dw_server_" + Guid.NewGuid().ToString("N") + ".db");
        _local = new SqliteConnectionFactory(_localPath);
        _server = new SqliteConnectionFactory(_serverPath);
        new MigrationRunner(_local).Run();
        new MigrationRunner(_server).Run();
        SeedCompany(_local, "A"); SeedCompany(_server, "A");

        _materials = new MaterialService(_local, _clock);
        _stock = new StockService(_local, _clock);
        _opening = new OpeningStockService(_local, _clock);

        var users = new UserService(_local, _clock);
        var uid = users.EnsureInitialAdmin("A", "depocu", "admin123", RoleKeys.CompanyAdmin);
        var yonetici = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_local, _clock);
        _depoA = branches.Create(yonetici, new NewBranch("Depo A"));
        _depoB = branches.Create(yonetici, new NewBranch("Depo B"));
        _mat = _materials.Create(yonetici, new NewMaterial("MASA-1", "Masaüstü malzemesi"));

        // Masaüstü gerçeği: kullanıcı BİR ŞUBEYE giriş yapar; o şube stok lokasyonudur.
        _depoAOturum = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static void SeedCompany(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>ÇEVRİMDIŞI → SENKRON: yerel hareketler sunucuya taşınır, sunucu bakiyeyi DEFTERDEN kurar.
    /// (Gerçek push/pull yolunun kullandığı <see cref="BusinessSyncService"/> ile birebir aynı.)</summary>
    private StockService SyncToServer()
    {
        var snapshot = new BusinessSyncService(_local, _clock).BuildSnapshot("A");
        using (var doc = JsonDocument.Parse(snapshot))
            new BusinessSyncService(_server, _clock).Apply("A", doc.RootElement);
        var srv = new StockService(_server, _clock);
        srv.RecomputeBalances("A");   // sunucu-otoriteli: bakiye hareketlerden yeniden kurulur
        return srv;
    }

    private long ServerMovementCount()
    {
        using var conn = _server.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id='A';";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    // ── 1-3. Çevrimdışı yazma yolları ─────────────────────────────────────────────────────

    /// <summary>1 + 2 — ÇEVRİMDIŞI GİRİŞ ve ÇIKIŞ oturumun deposuna yazılır; başka depo etkilenmez.
    /// Bu test boyunca hiçbir ağ çağrısı yoktur — masaüstü internetsiz de böyle çalışır.</summary>
    [Fact]
    public void Cevrimdisi_Giris_ve_Cikis_Oturumun_Deposuna_Yazilir()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.IssueOut(_depoAOturum, new[] { new StockLine(_mat, 3m) }, Op(), branchId: _depoA);

        Assert.Equal(7m, _stock.GetBalanceAt(_depoAOturum, _mat, _depoA));
        Assert.Equal(0m, _stock.GetBalanceAt(_depoAOturum, _mat, _depoB));
        Assert.Equal(7m, _stock.GetBalance(_depoAOturum, _mat));
    }

    /// <summary>3 + 4 — ÇEVRİMDIŞI TRANSFER: kaynak azalır, hedef artar, firma toplamı sabit kalır.</summary>
    [Fact]
    public void Cevrimdisi_Transfer_Kaynak_ve_Hedefi_Korur()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.Transfer(_depoAOturum, _mat, 4m, _depoA, _depoB, Op());

        Assert.Equal(6m, _stock.GetBalanceAt(_depoAOturum, _mat, _depoA));
        Assert.Equal(4m, _stock.GetBalanceAt(_depoAOturum, _mat, _depoB));
        Assert.Equal(10m, _stock.GetBalance(_depoAOturum, _mat));
    }

    /// <summary>13 (kısmi) — Kaynak = hedef transferi ENGELLENİR (Web ile aynı iş kuralı).</summary>
    [Fact]
    public void Ayni_Depoya_Transfer_Engellenir()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        Assert.Throws<ArgumentException>(() => _stock.Transfer(_depoAOturum, _mat, 1m, _depoA, _depoA, Op()));
    }

    /// <summary>5 + 6 — ÇEVRİMDIŞI SAYIM, SAYILAN DEPONUN bakiyesiyle karşılaştırılır.
    /// 🔴 Masaüstünde bulunan iki hatanın nöbetçisi: sayım eskiden hem lokasyonsuz gönderiliyor
    /// (fark ATANMAMIŞ'a yazılıyor) hem de sistem miktarı firma genelinden okunuyordu.</summary>
    [Fact]
    public void Cevrimdisi_Sayim_Sayilan_Deponun_Bakiyesini_Kullanir()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoB);

        // Ekranın gösterdiği "sistem stoğu" = SAYILAN DEPONUN bakiyesi (firma toplamı 15 DEĞİL).
        Assert.Equal(10m, _stock.GetBalanceAt(_depoAOturum, _mat, _depoA));

        _stock.Count(_depoAOturum, new[] { new CountLine(_mat, 12m) }, "sayım", Op(), branchId: _depoA);

        Assert.Equal(12m, _stock.GetBalanceAt(_depoAOturum, _mat, _depoA));
        Assert.Equal(5m, _stock.GetBalanceAt(_depoAOturum, _mat, _depoB));
        Assert.Equal(0m, _stock.GetBalanceAt(_depoAOturum, _mat, StockBalanceWriter.Unassigned));   // ATANMAMIŞ'a YAZILMADI
        Assert.Equal(17m, _stock.GetBalance(_depoAOturum, _mat));
    }

    /// <summary>7 — ÇEVRİMDIŞI AÇILIŞ STOĞU oturumun deposuna yazılır (ATANMAMIŞ'a düşmez).</summary>
    [Fact]
    public void Cevrimdisi_Acilis_Stogu_Oturumun_Deposuna_Yazilir()
    {
        var m2 = _materials.Create(_depoAOturum, new NewMaterial("MASA-2", "Açılışlı"));
        _opening.RecordOpening(_depoAOturum, m2, 25m, Op(), branchId: _depoA);

        Assert.Equal(25m, _stock.GetBalanceAt(_depoAOturum, m2, _depoA));
        Assert.Equal(0m, _stock.GetBalanceAt(_depoAOturum, m2, StockBalanceWriter.Unassigned));
        Assert.Equal(25m, _opening.GetBalance(_depoAOturum, m2));
    }

    // ── 8-10. Kırılım · firma toplamı · ATANMAMIŞ ─────────────────────────────────────────

    /// <summary>8 + 9 + 10 — Malzeme kartı: TOPLAM (firma geneli) ile KIRILIM toplamı kopmaz;
    /// ATANMAMIŞ ayrı bir satırdır ve EN SONDA gelir (gerçek bir depo gibi gösterilmez).</summary>
    [Fact]
    public void Kart_Kirilimi_Toplamla_Kopmaz_ve_ATANMAMIS_Sonda()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);
        // Lokasyonsuz (geçmiş) hareket — idari/eski kayıt gibi.
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 1m) }, Op());

        var rows = _stock.GetLocationBalances(_depoAOturum, _mat);
        Assert.Equal(3, rows.Count);
        Assert.Equal(15m, rows.Sum(x => x.Quantity));
        Assert.Equal(_stock.GetBalance(_depoAOturum, _mat), rows.Sum(x => x.Quantity));
        Assert.Equal("", rows[^1].LocationId);
        Assert.Equal("Atanmamış", rows[^1].LocationName);
        // ⚠️ Firma toplamı (15) ile ATANMAMIŞ (1) FARKLI kavramlardır.
        Assert.NotEqual(_stock.GetBalance(_depoAOturum, _mat), rows[^1].Quantity);
    }

    /// <summary>18 — ESKİ TEK-SATIR VARSAYIMI KALMADI: aynı malzemenin iki deposu varken
    /// malzeme listesi satırı ÇOĞALTMAZ ve stok kolonu TOPLAMI gösterir.</summary>
    [Fact]
    public void Malzeme_Listesi_Iki_Depolu_Malzemeyi_Cogaltmaz()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);

        var grid = _materials.SearchGrid(_depoAOturum, new MaterialGridFilter(Code: "MASA-1"), 1, 50);
        Assert.Equal(1, grid.TotalCount);
        Assert.Equal(14m, Assert.Single(grid.Items).Stock);
        Assert.Equal(14m, _materials.GetDetail(_depoAOturum, _mat).Stock);
    }

    // ── 11-14. Senkron ────────────────────────────────────────────────────────────────────

    /// <summary>11 + 14 — ÇEVRİMDIŞI → SENKRON: lokasyon bilgisi senkronda KAYBOLMAZ.
    /// Sunucu bakiyeyi defterden kurar ve masaüstüyle AYNI kırılımı üretir.
    /// (Bir hareket senkron sonrası lokasyonunu kaybederse bu STK-05'in kritik hatasıdır.)</summary>
    [Fact]
    public void Cevrimdisi_Hareketler_Senkronda_Lokasyonunu_Kaybetmez()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);
        _stock.IssueOut(_depoAOturum, new[] { new StockLine(_mat, 3m) }, Op(), branchId: _depoA);
        _stock.Transfer(_depoAOturum, _mat, 2m, _depoA, _depoB, Op());
        _stock.Count(_depoAOturum, new[] { new CountLine(_mat, 6m) }, "sayım", Op(), branchId: _depoA);

        var beklenenA = _stock.GetBalanceAt(_depoAOturum, _mat, _depoA);
        var beklenenB = _stock.GetBalanceAt(_depoAOturum, _mat, _depoB);

        var srv = SyncToServer();

        Assert.Equal(beklenenA, srv.GetBalanceAt(_depoAOturum, _mat, _depoA));
        Assert.Equal(beklenenB, srv.GetBalanceAt(_depoAOturum, _mat, _depoB));
        Assert.Equal(_stock.GetBalance(_depoAOturum, _mat), srv.GetBalance(_depoAOturum, _mat));
        Assert.Equal(0m, srv.GetBalanceAt(_depoAOturum, _mat, StockBalanceWriter.Unassigned));
    }

    /// <summary>12 + 13 — ONLINE → OFFLINE → ONLINE → OFFLINE → ONLINE döngüsü:
    /// tekrar tekrar senkron KOPYA hareket üretmez, bakiye lokasyon bazında doğru kalır.</summary>
    [Fact]
    public void Online_Offline_Dongusu_Kopya_Hareket_Uretmez()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        SyncToServer();                                   // 1. senkron
        var ilkSayi = ServerMovementCount();

        _stock.IssueOut(_depoAOturum, new[] { new StockLine(_mat, 3m) }, Op(), branchId: _depoA);
        SyncToServer();                                   // 2. senkron (offline dönem sonrası)
        _stock.Transfer(_depoAOturum, _mat, 2m, _depoA, _depoB, Op());
        var srv = SyncToServer();                         // 3. senkron
        SyncToServer();                                   // 4. senkron — YENİ hareket yok, tekrar gönderim

        // Yerel defterdeki hareket sayısı ile sunucudaki EŞİT olmalı (kopya yok).
        using (var conn = _local.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id='A';";
            Assert.Equal(Convert.ToInt64(cmd.ExecuteScalar()), ServerMovementCount());
        }
        Assert.True(ServerMovementCount() > ilkSayi);

        Assert.Equal(_stock.GetBalanceAt(_depoAOturum, _mat, _depoA), srv.GetBalanceAt(_depoAOturum, _mat, _depoA));
        Assert.Equal(_stock.GetBalanceAt(_depoAOturum, _mat, _depoB), srv.GetBalanceAt(_depoAOturum, _mat, _depoB));
        Assert.Equal(5m, srv.GetBalanceAt(_depoAOturum, _mat, _depoA));   // 10 − 3 − 2
        Assert.Equal(2m, srv.GetBalanceAt(_depoAOturum, _mat, _depoB));
    }

    /// <summary>26 — Aynı malzemenin FARKLI DEPOLARDAKİ hareketleri birbirinden bağımsızdır;
    /// senkronda çakışma (conflict) olarak görülmez — ikisi de uygulanır.</summary>
    [Fact]
    public void Ayni_Malzemenin_Farkli_Depolari_Cakisma_Uretmez()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);

        var srv = SyncToServer();
        Assert.Equal(10m, srv.GetBalanceAt(_depoAOturum, _mat, _depoA));
        Assert.Equal(4m, srv.GetBalanceAt(_depoAOturum, _mat, _depoB));
        Assert.Equal(14m, srv.GetBalance(_depoAOturum, _mat));
    }

    /// <summary>
    /// 21 — DOĞRULUK KAYNAĞI DEFTERDİR. <c>stock_balances</c> senkron paketinde **taşınır**
    /// (tablo listesinde yer alıyor, <c>BusinessSyncService.Tables</c>) ama **otoriter DEĞİLDİR**:
    /// sunucu push sonrası bakiyeyi <c>stock_movements</c>'tan yeniden hesaplar ve masaüstü pull'u
    /// bakiyeyi bilinçli olarak HARİÇ tutar (<c>BusinessSyncPullService</c>).
    ///
    /// Bu test o sözleşmenin nöbetçisidir: pakette KASTEN YANLIŞ bir bakiye olsa bile sonuç
    /// defterin söylediğidir. Kural bozulursa iki makine birbirinin bakiyesini ezer.
    /// </summary>
    [Fact]
    public void Senkronda_Bakiye_Otoriter_Degil_Defter_Kazanir()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);

        // Yerel bakiyeyi KASTEN boz (bozuk/eski istemci snapshot'ı gibi). Defter DOĞRU kalır.
        using (var conn = _local.Create())
        using (var bad = conn.CreateCommand())
        {
            bad.CommandText = "UPDATE stock_balances SET quantity='999' WHERE material_id=@m AND location_id=@l;";
            bad.AddWithValue("@m", _mat); bad.AddWithValue("@l", _depoA);
            bad.ExecuteNonQuery();
        }

        var snapshot = new BusinessSyncService(_local, _clock).BuildSnapshot("A");
        using (var doc = JsonDocument.Parse(snapshot))
        {
            var tables = doc.RootElement.GetProperty("tables");
            Assert.True(tables.TryGetProperty("stock_movements", out _), "Hareket defteri senkronda TAŞINMALI.");
        }

        var srv = SyncToServer();   // push + sunucu-otoriteli yeniden hesaplama
        Assert.Equal(10m, srv.GetBalanceAt(_depoAOturum, _mat, _depoA));   // 999 DEĞİL → defter kazandı
        Assert.Equal(10m, srv.GetBalance(_depoAOturum, _mat));
    }

    // ── 17. Şirket izolasyonu ─────────────────────────────────────────────────────────────

    /// <summary>17 — ŞİRKETLER ARASI LOKASYON: başka firmanın deposu ÇEVRİMDIŞI da reddedilir
    /// (koruma serviste, STK-03). Masaüstü API'ye uğramadığı için bu tek savunma hattıdır.</summary>
    [Fact]
    public void Baska_Firmanin_Deposu_Cevrimdisi_de_Reddedilir()
    {
        SeedCompany(_local, "B");
        var users = new UserService(_local, _clock);
        var bUid = users.EnsureInitialAdmin("B", "b_admin", "admin123", RoleKeys.CompanyAdmin);
        var bOturum = new SessionContext(bUid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var bDepo = new BranchService(_local, _clock).Create(bOturum, new NewBranch("B Deposu"));

        Assert.Throws<ForbiddenException>(() =>
            _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: bDepo));
        Assert.Throws<ForbiddenException>(() =>
            _stock.Count(_depoAOturum, new[] { new CountLine(_mat, 5m) }, "sayım", Op(), branchId: bDepo));
        Assert.Throws<ForbiddenException>(() =>
            _opening.RecordOpening(_depoAOturum, _mat, 5m, Op(), branchId: bDepo));

        Assert.Equal(0m, _stock.GetBalance(_depoAOturum, _mat));   // hiçbir kayıt oluşmadı
    }

    // ── 20. Hareket lokasyonları ──────────────────────────────────────────────────────────

    /// <summary>20 — Hareket listesi ekranda lokasyonu gösterebilmeli: transferde
    /// <c>Kaynak → Hedef</c>, diğerlerinde tek depo adı, lokasyonsuzda "Atanmamış".</summary>
    [Fact]
    public void Hareket_Listesi_Lokasyon_Metnini_Uretir()
    {
        _stock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _stock.Transfer(_depoAOturum, _mat, 4m, _depoA, _depoB, Op());

        // Hareket listesi oturumun ŞUBE KAPSAMINA göre süzülür (mevcut kural) → hedef depodaki
        // transfer girişini görmek için "Tüm Şubeler" oturumu kullanılır (okuma serbesttir).
        var tumSubeler = new SessionContext(_depoAOturum.UserId, "A",
            new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var rows = _stock.SearchMovements(tumSubeler, null, null, null);
        var giris = rows.First(r => r.MovementType == "in");
        Assert.Equal("Depo A", giris.LocationFlowText);

        var transferGiris = rows.First(r => r.MovementType == "transfer" && r.Direction > 0);
        Assert.Equal("Depo A → Depo B", transferGiris.LocationFlowText);

        var transferCikis = rows.First(r => r.MovementType == "transfer" && r.Direction < 0);
        Assert.Equal("Depo A", transferCikis.LocationFlowText);   // kaynak = hedef → tek ad
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_localPath); } catch { }
        try { File.Delete(_serverPath); } catch { }
    }
}
