using System.Net.Http.Json;
using System.Text.Json;
using DepoWise.Api;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// STK-07 (FAZ C, 2026-08-11) — SENKRON SERTİFİKASYONU (lokasyon bazlı stok).
///
/// Amaç YENİ senkron tasarlamak DEĞİL; mevcut senkronun STK-02…06 ile gelen depo bazlı stok
/// modeliyle <b>doğru, idempotent ve çevrimdışı uyumlu</b> çalıştığını UÇTAN UCA kanıtlamaktır.
///
/// Bu testler <b>GERÇEK HTTP senkron uçlarını</b> kullanır (<c>/api/sync/business-push</c>,
/// <c>business-pull</c>, <c>business-version</c>) — masaüstünün kullandığı yolun aynısı.
/// Masaüstü tarafı <b>yerel SQLite</b> ile temsil edilir; stok işlemleri API'ye uğramadan,
/// çevrimdışı yazılır ve yalnız "bağlantı geldiğinde" push edilir.
///
/// KABUL ÖLÇÜTLERİ: veri kaybı yok · kopya hareket yok · lokasyon kaybolmuyor ·
/// bakiyenin otoritesi <b>defter</b> (<c>stock_movements</c>) · delta pull gerçekten delta.
/// </summary>
[Collection("PostgresSchema")]
public class SyncStockLocationCertificationTests : IAsyncLifetime
{
    private readonly ApiTestHost _host = new();
    private const string Company = "SNK-A";
    private const string User = "snk_kullanici";
    private const string Pass = "Test!2026";

    private HttpClient _client = null!;          // "bağlantı" — yalnız senkron anında kullanılır
    private string _localPath = "";
    private SqliteConnectionFactory _local = null!;   // MASAÜSTÜ (çevrimdışı) veritabanı
    private readonly TestClock _clock = new();
    private StockService _localStock = null!;
    private OpeningStockService _localOpening = null!;
    private SessionContext _depoAOturum = null!;
    private string _depoA = "", _depoB = "", _mat = "";

    public async Task InitializeAsync()
    {
        _ = _host.CreateClient();
        var svc = _host.Services.GetRequiredService<ServerServices>();
        SeedCompanyOnServer(svc, Company);
        var uid = svc.Users.EnsureInitialAdmin(Company, User, Pass, RoleKeys.CompanyAdmin);
        var s = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        // Sunucuda: şubeler + malzeme (masaüstü ilk kurulumda bunları PULL ile alır).
        _depoA = svc.Branches.Create(s, new NewBranch("Depo A"));
        _depoB = svc.Branches.Create(s, new NewBranch("Depo B"));
        _mat = svc.Materials.Create(s, new NewMaterial("SNK-1", "Senkron malzemesi"));

        _client = await _host.LoginAsync(User, Pass, Company);

        // MASAÜSTÜ veritabanı — ayrı SQLite dosyası (çevrimdışı çalışan gerçek istemcinin eşi).
        _localPath = Path.Combine(Path.GetTempPath(), "dw_snk_" + Guid.NewGuid().ToString("N") + ".db");
        _local = new SqliteConnectionFactory(_localPath);
        new MigrationRunner(_local).Run();
        SeedCompanyLocal(_local, Company);
        _localStock = new StockService(_local, _clock);
        _localOpening = new OpeningStockService(_local, _clock);
        _depoAOturum = new SessionContext(uid, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
        { OperatingBranchId = _depoA };

        // ⚠️ STK-07 BULGUSU: `branches` iş-senkronu (business-push/pull) kapsamında DEĞİLDİR
        // (BusinessSyncService.Tables — "web-otoriteli; kod/şifre taşır"). Masaüstü şubeleri AYRI bir
        // yoldan (org uçları) alır. Testte o yolu, şubeleri AYNI kimliklerle yerele yazarak temsil
        // ediyoruz — gerçek masaüstünün senkron sonrası sahip olduğu durumun aynısı.
        MirrorBranchLocally(_depoA, "Depo A");
        MirrorBranchLocally(_depoB, "Depo B");

        await PullAsync();   // ilk kurulum: sunucudan tam snapshot (malzeme vb. yerele iner)
    }

    public async Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_localPath); } catch { }
        await ((IAsyncLifetime)_host).DisposeAsync();
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private static void SeedCompanyOnServer(ServerServices svc, string id)
    {
        using var conn = svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void SeedCompanyLocal(SqliteConnectionFactory f, string id)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", id);
        cmd.ExecuteNonQuery();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Şubeyi yerel veritabanına AYNI kimlikle yazar (masaüstünün org senkronundan sonraki hâli).</summary>
    private void MirrorBranchLocally(string id, string name)
    {
        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO branches(id, company_id, name, kind, created_at, updated_at, version, is_deleted) " +
            "VALUES(@id, @c, @n, 'branch', 1, 1, 1, 0);";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", Company);
        cmd.AddWithValue("@n", name);
        cmd.ExecuteNonQuery();
    }

    // ── gerçek senkron yolu (masaüstünün kullandığı uçlar) ────────────────────────────────

    /// <summary>PUSH: yerel snapshot → <c>/api/sync/business-push</c>. Sunucu uygular ve bakiyeyi
    /// DEFTERDEN yeniden hesaplar (uç içinde <c>RecomputeBalances</c> çağrılır).</summary>
    private async Task<JsonElement> PushAsync()
    {
        var snapshot = new BusinessSyncService(_local, _clock).BuildSnapshot(Company);
        var r = await _client.PostAsync("/api/sync/business-push",
            new StringContent(snapshot, System.Text.Encoding.UTF8, "application/json"));
        r.EnsureSuccessStatusCode();
        return await ApiTestHost.JsonAsync(r);
    }

    /// <summary>PULL: <c>/api/sync/business-pull[?since=]</c> → yerele uygula.
    /// Masaüstü gerçeğiyle aynı: türetilmiş <c>stock_balances</c> geri-çekmede HARİÇ tutulur.</summary>
    private async Task<JsonElement> PullAsync(long since = 0)
    {
        var r = await _client.GetAsync("/api/sync/business-pull" + (since > 0 ? "?since=" + since : ""));
        r.EnsureSuccessStatusCode();
        var body = await r.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        new BusinessSyncService(_local, _clock).ApplyPull(Company, doc.RootElement,
            new HashSet<string>(StringComparer.Ordinal) { "stock_balances" });
        return JsonDocument.Parse(body).RootElement;
    }

    private async Task<long> ServerVersionAsync()
        => (await ApiTestHost.JsonAsync(await _client.GetAsync("/api/sync/business-version")))
            .GetProperty("version").GetInt64();

    private ServerServices Svc => _host.Services.GetRequiredService<ServerServices>();
    private SessionContext ServerSession => new(_depoAOturum.UserId, Company,
        new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

    private decimal ServerAt(string loc) => Svc.Stock.GetBalanceAt(ServerSession, _mat, loc);
    private decimal LocalAt(string loc) => _localStock.GetBalanceAt(_depoAOturum, _mat, loc);

    private static long Count(IDbConnectionFactory f, string sql)
    {
        using var conn = f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private long ServerMovements() => Count(Svc.Factory, $"SELECT COUNT(*) FROM stock_movements WHERE company_id='{Company}';");
    private long LocalMovements() => Count(_local, $"SELECT COUNT(*) FROM stock_movements WHERE company_id='{Company}';");

    // ── SENARYO 1-2: çevrimdışı giriş / çıkış ────────────────────────────────────────────

    /// <summary>1 + 2 — ÇEVRİMDIŞI GİRİŞ ve ÇIKIŞ: hiçbir ağ çağrısı olmadan yerel deftere yazılır,
    /// doğru depoyu etkiler, diğer depoya dokunmaz. Senkron sonrası sunucu AYNI sonucu üretir.</summary>
    [Fact]
    public async Task S1_S2_Cevrimdisi_Giris_ve_Cikis_Senkronda_Korunur()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);
        _localStock.IssueOut(_depoAOturum, new[] { new StockLine(_mat, 3m) }, Op(), branchId: _depoA);

        Assert.Equal(7m, LocalAt(_depoA));
        Assert.Equal(4m, LocalAt(_depoB));

        await PushAsync();

        Assert.Equal(7m, ServerAt(_depoA));
        Assert.Equal(4m, ServerAt(_depoB));
        Assert.Equal(11m, Svc.Stock.GetBalance(ServerSession, _mat));
        Assert.Equal(0m, ServerAt(StockBalanceWriter.Unassigned));   // ATANMAMIŞ'a hiçbir şey düşmedi
    }

    // ── SENARYO 3: çevrimdışı transfer ───────────────────────────────────────────────────

    /// <summary>3 + 5 (transfer özel testi) — TRANSFER senkronda İKİ BACAĞIYLA korunur:
    /// kaynak çıkışı ve hedef girişi ayrı hareketlerdir; <c>branch_id</c> / <c>branch_from_id</c>
    /// alanları sunucuda birebir aynıdır. Tek bacağı gelen transfer olamaz (tek belge, tek push).</summary>
    [Fact]
    public async Task S3_Cevrimdisi_Transfer_Iki_Bacagiyla_Senkronlanir()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _localStock.Transfer(_depoAOturum, _mat, 4m, _depoA, _depoB, Op());

        await PushAsync();

        Assert.Equal(6m, ServerAt(_depoA));
        Assert.Equal(4m, ServerAt(_depoB));
        Assert.Equal(10m, Svc.Stock.GetBalance(ServerSession, _mat));

        // Hareket düzeyinde kaynak/hedef bilgisi korunmuş mu?
        using var conn = Svc.Factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT direction, branch_id, COALESCE(branch_from_id,'') FROM stock_movements " +
            $"WHERE company_id='{Company}' AND movement_type='transfer' ORDER BY direction;";
        var legs = new List<(long Dir, string To, string From)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) legs.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));

        Assert.Equal(2, legs.Count);                                  // iki bacak da geldi
        Assert.Equal((-1, _depoA, _depoA), legs[0]);                  // kaynak çıkışı
        Assert.Equal((1, _depoB, _depoA), legs[1]);                   // hedef girişi, kaynağı biliyor
    }

    // ── SENARYO 4: çevrimdışı sayım ──────────────────────────────────────────────────────

    /// <summary>4 — ÇEVRİMDIŞI SAYIM sayılan deponun bakiyesiyle karşılaştırılır (firma toplamı DEĞİL),
    /// fark aynı depoya yazılır ve senkron sonrası sunucuda aynı sonuç görülür.</summary>
    [Fact]
    public async Task S4_Cevrimdisi_Sayim_Dogru_Depoda_Senkronlanir()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoB);
        _localStock.Count(_depoAOturum, new[] { new CountLine(_mat, 12m) }, "sayım", Op(), branchId: _depoA);

        await PushAsync();

        Assert.Equal(12m, ServerAt(_depoA));                          // 12 − 10 = +2 uygulandı
        Assert.Equal(5m, ServerAt(_depoB));                           // diğer depo DOKUNULMADI
        Assert.Equal(0m, ServerAt(StockBalanceWriter.Unassigned));    // fark ATANMAMIŞ'a YAZILMADI
    }

    // ── SENARYO 5: offline → online → offline → online ───────────────────────────────────

    /// <summary>5 — ÇEVRİMDIŞI/ÇEVRİMİÇİ DÖNGÜSÜ: iki turda da kopya hareket oluşmaz, lokasyon
    /// kaybolmaz, miktarlar bozulmaz. Yerel ve sunucu hareket sayısı EŞİT kalır.</summary>
    [Fact]
    public async Task S5_Offline_Online_Dongusu_Kopya_Uretmez()
    {
        // 1. çevrimdışı dönem
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        await PushAsync();
        var ilk = ServerMovements();

        // 2. çevrimdışı dönem
        _localStock.IssueOut(_depoAOturum, new[] { new StockLine(_mat, 3m) }, Op(), branchId: _depoA);
        _localStock.Transfer(_depoAOturum, _mat, 2m, _depoA, _depoB, Op());
        await PushAsync();

        // 3. tur: YENİ hareket yok, aynı paket TEKRAR gönderiliyor
        await PushAsync();

        Assert.Equal(LocalMovements(), ServerMovements());            // kopya YOK
        Assert.True(ServerMovements() > ilk);
        Assert.Equal(5m, ServerAt(_depoA));                           // 10 − 3 − 2
        Assert.Equal(2m, ServerAt(_depoB));
        Assert.Equal(LocalAt(_depoA), ServerAt(_depoA));
        Assert.Equal(LocalAt(_depoB), ServerAt(_depoB));
    }

    // ── SENARYO 6: idempotency ───────────────────────────────────────────────────────────

    /// <summary>6 — IDEMPOTENCY: AYNI push paketi arka arkaya üç kez gönderilse bile hareket sayısı
    /// ve bakiye değişmez. (Ağ zaman aşımında istemci güvenle yeniden gönderebilir.)</summary>
    [Fact]
    public async Task S6_Ayni_Paket_Tekrar_Gonderilince_Bakiye_Degismez()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _localStock.Transfer(_depoAOturum, _mat, 4m, _depoA, _depoB, Op());

        await PushAsync();
        var hareket = ServerMovements();
        var a = ServerAt(_depoA); var b = ServerAt(_depoB);

        await PushAsync();
        await PushAsync();

        Assert.Equal(hareket, ServerMovements());
        Assert.Equal(a, ServerAt(_depoA));
        Assert.Equal(b, ServerAt(_depoB));
    }

    // ── SENARYO 7: çoklu lokasyon + ATANMAMIŞ ────────────────────────────────────────────

    /// <summary>7 — ÇOKLU LOKASYON + ATANMAMIŞ kırılımı senkronda BİREBİR korunur:
    /// A=10 · B=20 · ATANMAMIŞ=5 → firma toplamı 35. Lokasyonlar birbirinin yerine geçmez.</summary>
    [Fact]
    public async Task S7_Uc_Lokasyonlu_Kirilim_Senkronda_Korunur()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoB);
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 5m) }, Op());   // ATANMAMIŞ

        await PushAsync();

        Assert.Equal(10m, ServerAt(_depoA));
        Assert.Equal(20m, ServerAt(_depoB));
        Assert.Equal(5m, ServerAt(StockBalanceWriter.Unassigned));
        Assert.Equal(35m, Svc.Stock.GetBalance(ServerSession, _mat));
    }

    // ── SENARYO 8: bakiyenin otoritesi DEFTERDİR ─────────────────────────────────────────

    /// <summary>
    /// 8 — BAKİYENİN OTORİTESİ DEFTERDİR. Yerel bakiye KASTEN bozulur (Depo A = 999) ve push edilir.
    /// <c>stock_balances</c> push paketinde TAŞINSA BİLE sunucu bakiyeyi <c>stock_movements</c>'tan
    /// yeniden hesapladığı için sonuç DEFTERİN değeridir (10).
    ///
    /// ⚠️ Bu, <c>SNK-11</c>'in de gerekçesidir: bakiye taşınıyor ama otoriter değil → gereksiz yük.
    /// Bu testin kırılması, iki makinenin birbirinin bakiyesini ezebileceği anlamına gelir.
    /// </summary>
    [Fact]
    public async Task S8_Kasten_Bozuk_Bakiye_Push_Edilse_de_Defter_Kazanir()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);

        using (var conn = _local.Create())
        using (var bad = conn.CreateCommand())
        {
            bad.CommandText = "UPDATE stock_balances SET quantity='999' WHERE material_id=@m AND location_id=@l;";
            bad.AddWithValue("@m", _mat); bad.AddWithValue("@l", _depoA);
            Assert.Equal(1, bad.ExecuteNonQuery());
        }

        await PushAsync();

        Assert.Equal(10m, ServerAt(_depoA));                          // 999 DEĞİL → defter kazandı
        Assert.Equal(10m, Svc.Stock.GetBalance(ServerSession, _mat));
    }

    // ── SENARYO 9: yakınsama (convergence) ───────────────────────────────────────────────

    /// <summary>9 — YAKINSAMA: art arda giriş + çıkış + transfer + sayım + ters kayıt sonrası
    /// masaüstü ile sunucu AYNI sonuca ulaşır — hareket sayısı, hareket kimlikleri, lokasyon
    /// bakiyeleri ve firma toplamı birebir.</summary>
    [Fact]
    public async Task S9_Karisik_Hareketler_Sonrasi_Iki_Taraf_Yakinsar()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 20m) }, Op(), branchId: _depoA);
        var iptalEdilecek = _localStock.IssueOut(_depoAOturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: _depoA);
        _localStock.Transfer(_depoAOturum, _mat, 6m, _depoA, _depoB, Op());
        _localStock.Count(_depoAOturum, new[] { new CountLine(_mat, 10m) }, "sayım", Op(), branchId: _depoA);
        _localStock.ReverseDocument(_depoAOturum, iptalEdilecek.DocumentId, "hatalı çıkış");

        await PushAsync();

        Assert.Equal(LocalMovements(), ServerMovements());
        Assert.Equal(LocalAt(_depoA), ServerAt(_depoA));
        Assert.Equal(LocalAt(_depoB), ServerAt(_depoB));
        Assert.Equal(_localStock.GetBalance(_depoAOturum, _mat), Svc.Stock.GetBalance(ServerSession, _mat));

        // Hareket KİMLİKLERİ de aynı olmalı (yeni kimlik üretilmiyor → kopya riski yok).
        Assert.Equal(LocalIds(), ServerIds());

        HashSet<string> LocalIds() => Ids(_local);
        HashSet<string> ServerIds() => Ids(Svc.Factory);
        HashSet<string> Ids(IDbConnectionFactory f)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            using var conn = f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT id FROM stock_movements WHERE company_id='{Company}';";
            using var r = cmd.ExecuteReader();
            while (r.Read()) set.Add(r.GetString(0));
            return set;
        }
    }

    // ── SENARYO 10: delta pull + kalıcı ilerleme ─────────────────────────────────────────

    /// <summary>
    /// 10 — DELTA PULL GERÇEKTEN DELTA (talimatın "çok önemli" dediği test).
    /// İkinci senkronda değişmemiş kayıtlar TEKRAR İNDİRİLMEZ: <c>?since=</c> ile yapılan çekim,
    /// yalnız o damgadan SONRA değişenleri getirir. Sürüm (cursor) ilerler.
    ///
    /// Bu bozulursa masaüstü her açılışta tüm veriyi indirir (2508 kayıtta zaman aşımı yaşanmıştı).
    /// </summary>
    [Fact]
    public async Task S10_Delta_Pull_Degismeyeni_Tekrar_Indirmez_ve_Surum_Ilerler()
    {
        var surum1 = await ServerVersionAsync();

        // Sunucuda YENİ bir malzeme oluşsun (başka bir makinenin işi gibi).
        _clock.Advance(60_000);
        var s = new SessionContext(_depoAOturum.UserId, Company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yeni = Svc.Materials.Create(s, new NewMaterial("SNK-DELTA", "Delta malzemesi"));

        var surum2 = await ServerVersionAsync();
        Assert.True(surum2 > surum1, "İş verisi değişince sürüm İLERLEMELİ (aksi halde istemci değişikliği fark edemez).");

        // DELTA çekim: yalnız surum1'den SONRA değişenler gelmeli.
        var delta = await PullAsync(since: surum1);
        var mats = delta.GetProperty("tables").GetProperty("materials").EnumerateArray().ToList();
        Assert.Single(mats);                                          // YALNIZ yeni malzeme
        Assert.Equal(yeni, mats[0].GetProperty("id").GetString());
        Assert.DoesNotContain(mats, m => m.GetProperty("id").GetString() == _mat);   // eski malzeme GELMEDİ

        // Tam çekim ise ikisini de getirir → delta'nın gerçekten daralttığının kanıtı.
        var tam = await PullAsync();
        Assert.True(tam.GetProperty("tables").GetProperty("materials").EnumerateArray().Count() >= 2);

        // Delta çekim yerele UYGULANDI mı? (veri kaybı olmadan)
        Assert.Equal(1, Count(_local, $"SELECT COUNT(*) FROM materials WHERE id='{yeni}';"));

        // Güncel sürümden sonrası BOŞ olmalı — ikinci senkronda hiçbir şey tekrar inmez.
        var bos = await PullAsync(since: await ServerVersionAsync());
        Assert.Empty(bos.GetProperty("tables").GetProperty("materials").EnumerateArray());
    }

    // ── SENARYO 11: şirket izolasyonu ────────────────────────────────────────────────────

    /// <summary>11 — VERİ İZOLASYONU: başka firmanın deposuna yazma çevrimdışı da reddedilir
    /// (koruma serviste, STK-03) ve senkron paketi yalnız OTURUMUN firmasının verisini taşır.</summary>
    [Fact]
    public async Task S11_Baska_Firma_Verisi_Senkronla_Karismaz()
    {
        SeedCompanyLocal(_local, "SNK-B");
        var users = new UserService(_local, _clock);
        var bUid = users.EnsureInitialAdmin("SNK-B", "b_admin", "admin123", RoleKeys.CompanyAdmin);
        var bOturum = new SessionContext(bUid, "SNK-B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var bDepo = new BranchService(_local, _clock).Create(bOturum, new NewBranch("B Deposu"));

        // Çevrimdışı yolda bile yabancı depoya yazılamaz.
        Assert.Throws<ForbiddenException>(() =>
            _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 5m) }, Op(), branchId: bDepo));

        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        await PushAsync();

        // Sunucuya YALNIZ SNK-A verisi gitti.
        Assert.Equal(0, Count(Svc.Factory, "SELECT COUNT(*) FROM branches WHERE company_id='SNK-B';"));
        Assert.Equal(10m, ServerAt(_depoA));
    }

    // ── SENARYO 12: yeniden hesaplama sonrası bakiye tablosu temiz ───────────────────────

    /// <summary>12 — SENKRON SONRASI BAKİYE TABLOSU TEMİZ: her (malzeme, lokasyon) için TEK satır,
    /// hayalet lokasyon satırı yok, bileşik anahtar korunuyor.</summary>
    [Fact]
    public async Task S12_Senkron_Sonrasi_Bakiye_Tablosu_Tekil_ve_Temiz()
    {
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 10m) }, Op(), branchId: _depoA);
        _localStock.ReceiveIn(_depoAOturum, new[] { new StockLine(_mat, 4m) }, Op(), branchId: _depoB);
        _localStock.Transfer(_depoAOturum, _mat, 4m, _depoA, _depoB, Op());

        await PushAsync();

        // Aynı (malzeme, lokasyon) için iki satır OLAMAZ.
        Assert.Equal(0, Count(Svc.Factory,
            $"SELECT COUNT(*) FROM (SELECT material_id, location_id FROM stock_balances WHERE company_id='{Company}' " +
            "GROUP BY material_id, location_id HAVING COUNT(*) > 1) t;"));

        // Hayalet lokasyon: defterde hiç geçmeyen bir lokasyonda bakiye satırı OLAMAZ.
        Assert.Equal(0, Count(Svc.Factory,
            $@"SELECT COUNT(*) FROM stock_balances sb WHERE sb.company_id='{Company}' AND NOT EXISTS (
                 SELECT 1 FROM stock_movements sm WHERE sm.company_id = sb.company_id
                   AND sm.material_id = sb.material_id
                   AND COALESCE(sm.branch_id,'') = sb.location_id);"));

        Assert.Equal(6m, ServerAt(_depoA));
        Assert.Equal(8m, ServerAt(_depoB));
    }
}
