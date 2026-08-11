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
/// STK-08 (FAZ C, 2026-08-11) — ATANMAMIŞ STOK DAĞITIMI (KARAR-8).
///
/// Geçmiş hareketlerde depo girilmediği için stok "Atanmamış" (<c>location_id=""</c>) kovasında duruyor.
/// Sistem hangi malzemenin hangi depoda olduğunu <b>bilmez ve tahmin etmez</b> — kullanıcı açıkça dağıtır.
///
/// KARAR T-1: mevcut <see cref="StockService.Transfer"/> GEVŞETİLMEDİ (boş kaynağı bilinçli reddeder ve
/// şubeye bağlı kullanıcıda kaynağı sessizce kendi şubesine çevirir). Dağıtım kendi DAR kapısından geçer:
/// <see cref="StockService.DistributeUnassigned"/> — kaynak DAİMA ATANMAMIŞ, hareket türü <b>transfer</b>.
///
/// Bu dosya masaüstünün çevrimdışı yolunu (yerel SQLite, API yok) ve senkron sonrası sunucu sonucunu
/// birlikte kilitler.
/// </summary>
public class StockDistributeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _local;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly StockService _stock;
    private readonly SessionContext _admin;
    private readonly SessionContext _subeliKullanici;
    private readonly string _depoA, _depoB, _mat, _mat2;

    public StockDistributeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_dist_" + Guid.NewGuid().ToString("N") + ".db");
        _local = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_local).Run();
        SeedCompany(_local, "A");

        _materials = new MaterialService(_local, _clock);
        _stock = new StockService(_local, _clock);

        var users = new UserService(_local, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        var branches = new BranchService(_local, _clock);
        _depoA = branches.Create(_admin, new NewBranch("Depo A"));
        _depoB = branches.Create(_admin, new NewBranch("Depo B"));
        _mat = _materials.Create(_admin, new NewMaterial("DAG-1", "Dağıtım malzemesi 1"));
        _mat2 = _materials.Create(_admin, new NewMaterial("DAG-2", "Dağıtım malzemesi 2"));

        // ŞUBEYE BAĞLI kullanıcı — kaynağın sessizce onun şubesine çevrilmediğini kanıtlamak için.
        _subeliKullanici = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty)
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
    private const string Unassigned = "";

    /// <summary>ATANMAMIŞ kovasına stok koyar (lokasyonsuz giriş = geçmiş kaydın eşi).</summary>
    private void SeedUnassigned(string materialId, decimal qty)
        => _stock.ReceiveIn(_admin, new[] { new StockLine(materialId, qty) }, Op());

    private decimal At(string loc, string? mat = null) => _stock.GetBalanceAt(_admin, mat ?? _mat, loc);
    private decimal Total(string? mat = null) => _stock.GetBalance(_admin, mat ?? _mat);

    // ── 1-3. Liste · kaynak daima ATANMAMIŞ ──────────────────────────────────────────────

    /// <summary>1 — ATANMAMIŞ stoğu olan malzemeler listelenir; depoya bağlı stok listeye GİRMEZ.</summary>
    [Fact]
    public void Atanmamis_Listesi_Yalniz_Lokasyonsuz_Stogu_Gosterir()
    {
        SeedUnassigned(_mat, 100m);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat2, 50m) }, Op(), branchId: _depoA);   // depoda → listede YOK

        var list = _stock.ListUnassigned(_admin);
        var row = Assert.Single(list);
        Assert.Equal("DAG-1", row.Code);
        Assert.Equal(100m, row.Quantity);
    }

    /// <summary>2 + 3 — 🔴 KAYNAK DAİMA ATANMAMIŞ. Şubeye bağlı kullanıcı dağıtım yaptığında sistem
    /// kaynağı SESSİZCE onun şubesine ÇEVİRMEZ (mevcut <c>Transfer</c>'in <c>EnforceOwnBranch</c>
    /// davranışı burada geçerli değildir). Depo A'nın kendi stoğu DOKUNULMADAN kalır.</summary>
    [Fact]
    public void Subeli_Kullanicida_Kaynak_Sessizce_Kendi_Subesine_Cevrilmez()
    {
        SeedUnassigned(_mat, 100m);
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 40m) }, Op(), branchId: _depoA);   // A'nın kendi stoğu

        // Şubesi Depo A olan kullanıcı, ATANMAMIŞ'tan Depo B'ye dağıtıyor.
        _stock.DistributeUnassigned(_subeliKullanici, new[] { new StockLine(_mat, 30m) }, _depoB, Op());

        Assert.Equal(70m, At(Unassigned));   // ATANMAMIŞ'tan düştü
        Assert.Equal(40m, At(_depoA));       // 🔴 Depo A'ya DOKUNULMADI (eski hata burada yakalanır)
        Assert.Equal(30m, At(_depoB));
        Assert.Equal(140m, Total());         // toplam DEĞİŞMEDİ
    }

    // ── 4-8. Hedef doğrulamaları ─────────────────────────────────────────────────────────

    /// <summary>4 — ATANMAMIŞ hedef olarak SEÇİLEMEZ (yeni belirsizlik üretilmez).</summary>
    [Fact]
    public void Atanmamis_Hedef_Olarak_Secilemez()
    {
        SeedUnassigned(_mat, 100m);
        Assert.Throws<ArgumentException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 10m) }, "", Op()));
        Assert.Equal(100m, At(Unassigned));
    }

    /// <summary>5 + 6 + 7 — BAŞKA FİRMANIN · BİLİNMEYEN · PASİF depo hedef olamaz (403).</summary>
    [Fact]
    public void Yabanci_Bilinmeyen_ve_Pasif_Depo_Hedef_Olamaz()
    {
        SeedUnassigned(_mat, 100m);

        // Başka firmanın deposu
        SeedCompany(_local, "B");
        var users = new UserService(_local, _clock);
        var bUid = users.EnsureInitialAdmin("B", "b_admin", "admin123", RoleKeys.CompanyAdmin);
        var bOturum = new SessionContext(bUid, "B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var yabanci = new BranchService(_local, _clock).Create(bOturum, new NewBranch("B Deposu"));
        Assert.Throws<ForbiddenException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 10m) }, yabanci, Op()));

        // Bilinmeyen depo
        Assert.Throws<ForbiddenException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 10m) }, "yok-boyle", Op()));

        // Pasif (silinmiş) depo
        var pasif = new BranchService(_local, _clock).Create(_admin, new NewBranch("Kapanan Depo"));
        using (var conn = _local.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE branches SET is_deleted=1 WHERE id=@id;";
            cmd.AddWithValue("@id", pasif);
            cmd.ExecuteNonQuery();
        }
        Assert.Throws<ForbiddenException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 10m) }, pasif, Op()));

        Assert.Equal(100m, At(Unassigned));   // hiçbiri stoğa dokunmadı
    }

    // ── 9-11. Miktar doğrulamaları ───────────────────────────────────────────────────────

    /// <summary>8 + 9 + 10 — SIFIR ve NEGATİF miktar reddedilir; MEVCUTTAN FAZLA dağıtım reddedilir.</summary>
    [Fact]
    public void Sifir_Negatif_ve_Asim_Reddedilir()
    {
        SeedUnassigned(_mat, 10m);

        Assert.Throws<ArgumentException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 0m) }, _depoA, Op()));
        Assert.Throws<ArgumentException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, -5m) }, _depoA, Op()));
        // 10 varken 11 → TAMAMEN reddedilir (kısmi işlem YOK)
        Assert.Throws<NegativeStockException>(() =>
            _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 11m) }, _depoA, Op()));

        Assert.Equal(10m, At(Unassigned));
        Assert.Equal(0m, At(_depoA));
    }

    /// <summary>Aynı malzeme iki satırda gelirse TOPLAM üzerinden yeterlilik kontrol edilir
    /// (6+6 ile 10 birimlik stoktan 12 dağıtılamaz).</summary>
    [Fact]
    public void Ayni_Malzeme_Iki_Satirda_Gelirse_Toplam_Kontrol_Edilir()
    {
        SeedUnassigned(_mat, 10m);
        Assert.Throws<NegativeStockException>(() => _stock.DistributeUnassigned(_admin,
            new[] { new StockLine(_mat, 6m), new StockLine(_mat, 6m) }, _depoA, Op()));
        Assert.Equal(10m, At(Unassigned));
    }

    // ── 12-14. Kısmi · tam · çok hedefe bölme ────────────────────────────────────────────

    /// <summary>11 + 12 — KISMİ ve TAM dağıtım. 100'ün 30'u aktarılır, kalan 70 sonra aktarılır.</summary>
    [Fact]
    public void Kismi_ve_Tam_Dagitim_Calisir()
    {
        SeedUnassigned(_mat, 100m);

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 30m) }, _depoA, Op());
        Assert.Equal(70m, At(Unassigned));
        Assert.Equal(30m, At(_depoA));

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 70m) }, _depoA, Op());
        Assert.Equal(0m, At(Unassigned));
        Assert.Equal(100m, At(_depoA));
        Assert.Equal(100m, Total());
    }

    /// <summary>13 — AYNI MALZEME FARKLI HEDEFLERE bölünebilir: 100 → A 40 · B 35 · kalan 25 ATANMAMIŞ.</summary>
    [Fact]
    public void Ayni_Malzeme_Farkli_Hedeflere_Bolunebilir()
    {
        SeedUnassigned(_mat, 100m);

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 40m) }, _depoA, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 35m) }, _depoB, Op());

        Assert.Equal(40m, At(_depoA));
        Assert.Equal(35m, At(_depoB));
        Assert.Equal(25m, At(Unassigned));
        Assert.Equal(100m, Total());   // 🔒 toplam DEĞİŞMEDİ
    }

    // ── 15-16. Çoklu malzeme · atomiklik ─────────────────────────────────────────────────

    /// <summary>14 — ÇOKLU MALZEME tek belgede aktarılır.</summary>
    [Fact]
    public void Coklu_Malzeme_Tek_Belgede_Aktarilir()
    {
        SeedUnassigned(_mat, 100m);
        SeedUnassigned(_mat2, 50m);

        var res = _stock.DistributeUnassigned(_admin,
            new[] { new StockLine(_mat, 60m), new StockLine(_mat2, 20m) }, _depoA, Op());

        Assert.False(string.IsNullOrEmpty(res.DocumentId));
        Assert.Equal(60m, At(_depoA));
        Assert.Equal(20m, At(_depoA, _mat2));
        Assert.Equal(40m, At(Unassigned));
        Assert.Equal(30m, At(Unassigned, _mat2));
    }

    /// <summary>15 — 🔒 ATOMİKLİK: bir satır yetersizse TAMAMI geri alınır — kısmi dağıtım KALMAZ.</summary>
    [Fact]
    public void Bir_Satir_Yetersizse_Tum_Islem_Geri_Alinir()
    {
        SeedUnassigned(_mat, 100m);
        SeedUnassigned(_mat2, 5m);

        Assert.Throws<NegativeStockException>(() => _stock.DistributeUnassigned(_admin,
            new[] { new StockLine(_mat, 60m), new StockLine(_mat2, 20m) }, _depoA, Op()));

        // İLK satır geçerliydi ama HİÇBİRİ uygulanmadı.
        Assert.Equal(100m, At(Unassigned));
        Assert.Equal(5m, At(Unassigned, _mat2));
        Assert.Equal(0m, At(_depoA));
        Assert.Equal(0m, At(_depoA, _mat2));
    }

    // ── 17-18. Ondalık · toplam korunumu ─────────────────────────────────────────────────

    /// <summary>16 + 17 — ONDALIK korunur ve firma toplamı DEĞİŞMEZ (float yuvarlaması yok).</summary>
    [Fact]
    public void Ondalik_Korunur_ve_Toplam_Degismez()
    {
        SeedUnassigned(_mat, 0.3m);

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 0.1m) }, _depoA, Op());

        Assert.Equal(0.1m, At(_depoA));
        Assert.Equal(0.2m, At(Unassigned));
        Assert.Equal(0.3m, Total());
    }

    // ── 19-21. Hareket · ters kayıt · audit ──────────────────────────────────────────────

    /// <summary>18 + 19 — GERÇEK TRANSFER HAREKETİ oluşur: iki bacak, kaynak ATANMAMIŞ (branch_id NULL),
    /// hedef seçilen depo. Yeni hareket türü açılmadı → rapor/senkron kendiliğinden çalışır.</summary>
    [Fact]
    public void Gercek_Transfer_Hareketi_Olusur()
    {
        SeedUnassigned(_mat, 100m);
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 30m) }, _depoA, Op());

        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT movement_type, direction, COALESCE(branch_id,'(NULL)'), quantity FROM stock_movements " +
            "WHERE company_id='A' AND movement_type='transfer' ORDER BY direction;";
        var legs = new List<(string Type, long Dir, string Branch, string Qty)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) legs.Add((r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3)));

        Assert.Equal(2, legs.Count);
        Assert.Equal(("transfer", -1L, "(NULL)", "30"), legs[0]);   // kaynak = ATANMAMIŞ
        Assert.Equal(("transfer", 1L, _depoA, "30"), legs[1]);      // hedef = Depo A
    }

    /// <summary>
    /// 20 — DÜZELTME YOLU: dağıtım bir TRANSFER'dir ve transferler bilinçli olarak GERİ ALINMAZ
    /// (2026-08-06 kullanıcı kararı: iki deponun stoğunu etkiler). Bu kural dağıtım için de aynen
    /// geçerlidir — STK-08 ona bir istisna AÇMAZ.
    ///
    /// Yanlış depoya dağıtıldıysa düzeltme, o depodan doğru depoya YENİ bir transferdir; her iki
    /// hareket de defterde kalır (geçmiş silinmez). Ekran metinleri kullanıcıya bunu açıkça söyler.
    /// </summary>
    [Fact]
    public void Dagitim_Ters_Kayitla_Geri_Alinmaz_Duzeltme_Yeni_Transferdir()
    {
        SeedUnassigned(_mat, 100m);
        var doc = _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 30m) }, _depoA, Op());

        // Transfer geri alınamaz — dağıtım da bir transferdir.
        var ex = Assert.Throws<ForbiddenException>(() => _stock.ReverseDocument(_admin, doc.DocumentId, "yanlış depo"));
        Assert.Contains("Transfer geri alınamaz", ex.Message);
        Assert.Equal(30m, At(_depoA));   // belge duruyor

        // DÜZELTME: yanlış depodan doğru depoya YENİ transfer.
        _stock.Transfer(_admin, _mat, 30m, _depoA, _depoB, Op());

        Assert.Equal(0m, At(_depoA));
        Assert.Equal(30m, At(_depoB));
        Assert.Equal(70m, At(Unassigned));
        Assert.Equal(100m, Total());     // 🔒 toplam yine DEĞİŞMEDİ
    }

    /// <summary>21 — AUDIT: dağıtım mevcut audit sistemine yazılır (yeni audit tablosu YOK).</summary>
    [Fact]
    public void Dagitim_Audit_Kaydi_Birakir()
    {
        SeedUnassigned(_mat, 100m);
        var doc = _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 30m) }, _depoA, Op());

        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE company_id='A' AND entity_id=@d;";
        cmd.AddWithValue("@d", doc.DocumentId);
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) > 0, "Dağıtım belgesi audit'e yazılmalı.");
    }

    // ── 22. Yetki ────────────────────────────────────────────────────────────────────────

    /// <summary>22 — YETKİSİZ kullanıcı dağıtım yapamaz (deny-by-default; yeni yetki düğümü açılmadı,
    /// mevcut "stock" + Create kapısı kullanılıyor).</summary>
    [Fact]
    public void Yetkisiz_Kullanici_Dagitim_Yapamaz()
    {
        SeedUnassigned(_mat, 100m);
        var yetkisiz = new SessionContext("u-yetkisiz", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);

        Assert.Throws<ForbiddenException>(() =>
            _stock.DistributeUnassigned(yetkisiz, new[] { new StockLine(_mat, 10m) }, _depoA, Op()));
        Assert.Throws<ForbiddenException>(() => _stock.ListUnassigned(yetkisiz));
        Assert.Equal(100m, At(Unassigned));
    }

    // ── 23-25. Çevrimdışı → senkron → sunucu ─────────────────────────────────────────────

    /// <summary>23 + 24 + 25 — ÇEVRİMDIŞI DAĞITIM SENKRONDA KORUNUR: masaüstünde (API'siz) yapılan
    /// dağıtım sunucuya taşındığında ATANMAMIŞ doğru azalır, hedef doğru artar; aynı paket TEKRAR
    /// gönderilse bile KOPYA hareket oluşmaz ve bakiye değişmez (yakınsama).</summary>
    [Fact]
    public void Cevrimdisi_Dagitim_Senkronda_Korunur_ve_Kopya_Uretmez()
    {
        SeedUnassigned(_mat, 100m);
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 30m) }, _depoA, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 20m) }, _depoB, Op());

        var srvPath = Path.Combine(Path.GetTempPath(), "dw_dist_srv_" + Guid.NewGuid().ToString("N") + ".db");
        var server = new SqliteConnectionFactory(srvPath);
        try
        {
            new MigrationRunner(server).Run();
            SeedCompany(server, "A");

            void Sync()
            {
                using var doc = JsonDocument.Parse(new BusinessSyncService(_local, _clock).BuildSnapshot("A"));
                new BusinessSyncService(server, _clock).Apply("A", doc.RootElement);
                new StockService(server, _clock).RecomputeBalances("A");
            }

            Sync();
            Sync();   // AYNI paket tekrar → kopya olmamalı

            var srv = new StockService(server, _clock);
            Assert.Equal(30m, srv.GetBalanceAt(_admin, _mat, _depoA));
            Assert.Equal(20m, srv.GetBalanceAt(_admin, _mat, _depoB));
            Assert.Equal(50m, srv.GetBalanceAt(_admin, _mat, Unassigned));
            Assert.Equal(100m, srv.GetBalance(_admin, _mat));

            // Hareket sayısı iki tarafta EŞİT (kopya yok).
            Assert.Equal(Count(_local), Count(server));

            static long Count(IDbConnectionFactory f)
            {
                using var c = f.Create();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id='A';";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(srvPath); } catch { }
        }
    }

    /// <summary>26 — LİSTE TAZELİĞİ: dağıtımdan sonra kalan ATANMAMIŞ miktar doğru görünür;
    /// tamamı dağıtılan malzeme listeden DÜŞER (sıfır satır gösterilmez).</summary>
    [Fact]
    public void Dagitim_Sonrasi_Liste_Dogru_Kalani_Gosterir()
    {
        SeedUnassigned(_mat, 100m);
        SeedUnassigned(_mat2, 40m);

        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat, 30m) }, _depoA, Op());
        _stock.DistributeUnassigned(_admin, new[] { new StockLine(_mat2, 40m) }, _depoA, Op());   // tamamı

        var list = _stock.ListUnassigned(_admin);
        var row = Assert.Single(list);
        Assert.Equal("DAG-1", row.Code);
        Assert.Equal(70m, row.Quantity);   // DAG-2 tamamen dağıtıldı → listede YOK
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
