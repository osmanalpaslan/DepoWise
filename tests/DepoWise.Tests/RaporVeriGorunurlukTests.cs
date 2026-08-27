using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RPR-V — "VERİ GİRDİM AMA RAPORDA YOK" ═══ (kullanıcı bildirimi 2026-08-27)
///
/// <b>KULLANICININ GÖRDÜĞÜ.</b> "Giriş-Çıkış ekranından bir sürü depo girişi yaptım ama depo girişi
/// raporunda hiçbiri listelenmiyor."
///
/// Bu sınıf raporun "çalıştığını" değil, <b>normal yoldan girilen verinin raporda GÖRÜNDÜĞÜNÜ</b>
/// sınar — yani kullanıcının şikâyet ettiği sınıfı doğrudan hedefler. İki ayrı kusur bulundu:
///
/// <list type="number">
///   <item><b>BAKİYE ÇOK MAKİNEDE SIFIR KALIYOR.</b> <c>stock_balances</c> TÜRETİLMİŞ veridir ve
///   SNK-11 ile senkron paketinden çıkarılmıştır (sunucu push sonrası defterden yeniden hesaplar).
///   Ama <b>masaüstü GERİ-ÇEKME sonrası yeniden hesaplamıyordu</b> → başka bir makinede (ya da web'de)
///   girilen hareketler cihaza iniyor, hareket ekranında görünüyor, fakat <b>bakiye 0 kalıyordu</b>.
///   Etkilenen: Stok Durumu raporu · malzeme listesinin STOK kolonu · düşük stok uyarıları.</item>
///
///   <item><b>"Depo Girişi" adı yanıltıcıydı.</b> O rapor yalnız <c>fuel_depot_entries</c> okur —
///   yani YAKIT deposuna alınan yakıttır. Malzeme deposuna yapılan girişler <c>stock_movements</c>'a
///   yazılır ve <b>Stok Hareketleri</b> raporunda listelenir. Adı "Yakıt Depo Girişi" yapıldı.</item>
/// </list>
///
/// 🔒 Testler tamamen yerel SQLite üzerindedir; canlı veriye dokunmaz.
/// </summary>
public class RaporVeriGorunurlukTests : IDisposable
{
    private const string Co = "RPRV";
    private readonly List<string> _dosyalar = new();
    private readonly TestClock _clock = new();

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    /// <summary>Tüm test verisini kapsayan geniş tarih aralığı (raporlar tarih ister).</summary>
    private const long Gunes = 1_690_000_000_000, Batis = 1_710_000_000_000;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in _dosyalar) { try { File.Delete(f); } catch { } }
    }

    private SqliteConnectionFactory YeniMakine()
    {
        var p = Path.Combine(Path.GetTempPath(), "dw_rprv_" + Guid.NewGuid().ToString("N") + ".db");
        _dosyalar.Add(p);
        var f = new SqliteConnectionFactory(p);
        new MigrationRunner(f).Run();
        using (var conn = f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
            cmd.AddWithValue("@i", Co);
            cmd.ExecuteNonQuery();
        }
        return f;
    }

    private SessionContext Oturum(SqliteConnectionFactory f)
    {
        var users = new UserService(f, _clock);
        var uid = users.EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    private static ReportRequest Istek() => new(Executed: true, FromDate: Gunes, ToDate: Batis);

    private static int K(TableModel t, string baslik)
    {
        for (int i = 0; i < t.Headers.Count; i++) if (t.Headers[i] == baslik) return i;
        throw new InvalidOperationException($"'{baslik}' kolonu yok. Kolonlar: {string.Join(", ", t.Headers)}");
    }

    // ══════════════ 1) ÇOK MAKİNE: BAŞKA CİHAZDA GİRİLEN STOK, BU CİHAZDA GÖRÜNMELİ ══════════════

    /// <summary>
    /// ⭐ ASIL KUSUR. Makine A'da giriş yapılır → sunucu anlık görüntüsü üretilir → Makine B onu çeker.
    /// B'de <b>hareketler</b> görünüyordu ama <b>bakiye 0</b> kalıyordu: <c>stock_balances</c> senkron
    /// paketinde YOK (türetilmiş veri) ve geri-çekme sonrası yeniden hesaplanmıyordu.
    /// </summary>
    [Fact]
    public void RPRV1_Baska_Makinede_Girilen_Stok_Bu_Makinede_Bakiyeye_Yansir()
    {
        // ── Makine A: malzeme + depo girişi
        var fA = YeniMakine();
        var sA = Oturum(fA);
        var depoA = new BranchService(fA, _clock).Create(sA, new NewBranch("Merkez Depo"));
        var matA = new MaterialService(fA, _clock).Create(sA, new NewMaterial("M-1", "Çimento"));
        new StockService(fA, _clock).ReceiveIn(sA, new[] { new StockLine(matA, 40m) }, Op(), branchId: depoA);

        var aRapor = new ReportService(fA, _clock).Run(sA, "stock", Istek());
        Assert.Equal(40d, Convert.ToDouble(aRapor.Rows.Single()[K(aRapor, "Stok")]));   // A'da doğru

        // ── Sunucu anlık görüntüsü → Makine B (yeni cihaz, aynı firma)
        var paket = new BusinessSyncService(fA, _clock).BuildSnapshot(Co);
        var fB = YeniMakine();
        var sB = Oturum(fB);
        using (var doc = JsonDocument.Parse(paket))
            new BusinessSyncService(fB, _clock).ApplyPull(Co, doc.RootElement);

        // Hareket defteri B'ye ULAŞMIŞ olmalı (bu zaten çalışıyordu).
        var hareketler = new ReportService(fB, _clock).Run(sB, "stock-movements", Istek());
        Assert.True(hareketler.Rows.Count > 0, "Hareketler B'ye hiç inmemiş — senkron paketinde sorun var.");

        // ⭐ ASIL İDDİA: bakiye de doğru olmalı. Türetilmiş veri taşınmıyorsa DEFTERDEN hesaplanmalı.
        var bRapor = new ReportService(fB, _clock).Run(sB, "stock", Istek());
        Assert.True(bRapor.Rows.Count > 0, "Stok Durumu raporu B'de BOŞ — malzeme inmiş olmalıydı.");
        Assert.Equal(40d, Convert.ToDouble(bRapor.Rows.Single()[K(bRapor, "Stok")]));
    }

    /// <summary>Yeniden hesaplama defterden türetir: ters kayıt (iptal) sonrası bakiye de doğru düşer.
    /// Böylece "hesapla" adımı veriyi UYDURMUYOR, defteri yansıtıyor olur.</summary>
    [Fact]
    public void RPRV2_Iptal_Edilen_Giris_Bakiyeden_Dusulur()
    {
        var fA = YeniMakine();
        var sA = Oturum(fA);
        var depo = new BranchService(fA, _clock).Create(sA, new NewBranch("Depo"));
        var mat = new MaterialService(fA, _clock).Create(sA, new NewMaterial("M-2", "Demir"));
        var stokA = new StockService(fA, _clock);
        var belge = stokA.ReceiveIn(sA, new[] { new StockLine(mat, 10m) }, Op(), branchId: depo);
        _clock.Advance(60_000);
        stokA.ReceiveIn(sA, new[] { new StockLine(mat, 7m) }, Op(), branchId: depo);
        _clock.Advance(60_000);
        stokA.ReverseDocument(sA, belge.DocumentId, "test iptali");

        var paket = new BusinessSyncService(fA, _clock).BuildSnapshot(Co);
        var fB = YeniMakine();
        var sB = Oturum(fB);
        using (var doc = JsonDocument.Parse(paket))
            new BusinessSyncService(fB, _clock).ApplyPull(Co, doc.RootElement);

        var bRapor = new ReportService(fB, _clock).Run(sB, "stock", Istek());
        Assert.Equal(7d, Convert.ToDouble(bRapor.Rows.Single()[K(bRapor, "Stok")]));   // 10 + 7 − 10
    }

    // ══════════════ 2) "DEPO GİRİŞİ" ADI ══════════════

    /// <summary>⭐ Kullanıcı malzeme deposu girişi yaptı, "Depo Girişi" raporuna baktı ve boş buldu.
    /// O rapor YAKIT deposunundur; adı bunu söylemeliydi. Ad artık yakıt olduğunu belirtir ve
    /// açıklama malzeme girişlerinin hangi raporda olduğunu YAZAR.</summary>
    [Fact]
    public void RPRV3_Yakit_Deposu_Raporunun_Adi_Yaniltmaz()
    {
        var d = ReportCatalog.ByKey("fuel-depot")!;

        Assert.Contains("Yakıt", d.Name);                        // ad yakıt olduğunu söylüyor
        Assert.Equal(ReportCategory.Fuel, d.Category);
        Assert.Contains("Stok Hareketleri", d.InfoNote ?? "");   // doğru rapora yönlendiriyor
    }

    /// <summary>Malzeme deposuna yapılan giriş, <b>Stok Hareketleri</b> raporunda görünür — "Depo Girişi"
    /// raporunda DEĞİL. İkisi ayrı veri kaynağıdır; bu ayrım kasıtlıdır ve kilitlenir.</summary>
    [Fact]
    public void RPRV4_Malzeme_Girisi_Stok_Hareketlerinde_Gorunur_Yakit_Raporunda_Gorunmez()
    {
        var f = YeniMakine();
        var s = Oturum(f);
        var depo = new BranchService(f, _clock).Create(s, new NewBranch("Depo"));
        var mat = new MaterialService(f, _clock).Create(s, new NewMaterial("M-3", "Kum"));
        new StockService(f, _clock).ReceiveIn(s, new[] { new StockLine(mat, 5m) }, Op(), branchId: depo);

        var reports = new ReportService(f, _clock);
        Assert.NotEmpty(reports.Run(s, "stock-movements", Istek()).Rows);   // burada görünür
        Assert.Empty(reports.Run(s, "fuel-depot", Istek()).Rows);           // yakıt raporunda görünmez
    }

    // ══════════════ 3) GİRİŞ EKRANININ YAZDIĞI ŞUBEYLE RAPORUN SÜZDÜĞÜ ŞUBE AYNI OLMALI ══════════════

    /// <summary>Giriş-Çıkış ekranı girişi <c>_session.OperatingBranchId</c> ile yazar. "Tüm Şubeler" ile
    /// giriş yapıldığında bu NULL olur → hareket "atanmamış" kaydedilir. Rapor bu satırları GİZLEMEMELİ,
    /// yoksa kullanıcı girdiği kaydı hiçbir yerde göremez.</summary>
    [Theory]
    [InlineData(true)]    // ekrandaki gibi: çalışma şubesi seçili
    [InlineData(false)]   // "Tüm Şubeler" ile giriş → branchId null
    public void RPRV5_Girisin_Yazildigi_Sube_Raporda_Gorunur(bool subeli)
    {
        var f = YeniMakine();
        var s = Oturum(f);
        var depo = new BranchService(f, _clock).Create(s, new NewBranch("Depo"));
        var mat = new MaterialService(f, _clock).Create(s, new NewMaterial("M-4", "Tuğla"));

        // Ekranın yaptığının aynısı: branchId = oturumun çalışma şubesi.
        var oturum = subeli
            ? new SessionContext(s.UserId, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = depo }
            : s;
        new StockService(f, _clock).ReceiveIn(oturum, new[] { new StockLine(mat, 3m) }, Op(),
            branchId: oturum.OperatingBranchId);

        var reports = new ReportService(f, _clock);
        Assert.NotEmpty(reports.Run(oturum, "stock-movements", Istek()).Rows);   // girenin kendisi görür
        Assert.NotEmpty(reports.Run(s, "stock-movements", Istek()).Rows);        // "Tüm Şubeler" de görür
    }
}
