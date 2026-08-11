using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// SNK-12 (2026-08-11) — MASAÜSTÜNDE DEPO LİSTESİNİN TAZELENMESİ.
///
/// SORUN: şubeler iş-senkronunda (business-push/pull) TAŞINMAZ — web-otoriteli olduğu için ayrı yoldan
/// (/api/public/branches → masaüstündeki BranchMirror) iner. Bu aynalama eskiden YALNIZ GİRİŞTE
/// çalışıyordu: oturum açıkken web'de yeni bir depo açılırsa masaüstü onu öğrenmiyor ve o depoya stok
/// işlemi YAPAMIYORDU (<c>EnsureLocationOwned</c> yerelde bilinmeyen depoyu reddeder).
///
/// ÇÖZÜM: aynı mekanizma normal senkron turunda da çağrılır (yeni protokol YOK), sık çağrıyı önlemek
/// için zaman kısıtlamalı. Bu testler aynalamanın VERİ davranışını kilitler (ağdan bağımsız saf yol).
///
/// 🔒 ÇEVRİMDIŞI: aynalama sunucuya ulaşamazsa yerel listeye DOKUNMAZ → daha önce inmiş depolarla
/// çevrimdışı stok işlemi sürer. (Bu davranış masaüstündeki BranchMirror.RefreshAsync içindedir: sunucu null dönerse
/// BranchMirrorApply.Run HİÇ çağrılmaz.)
/// </summary>
public class BranchMirrorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _local;
    private readonly TestClock _clock = new();
    private readonly SessionContext _admin;
    private readonly StockService _stock;
    private readonly string _mat;

    public BranchMirrorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_brmirror_" + Guid.NewGuid().ToString("N") + ".db");
        _local = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_local).Run();
        Seed("A"); Seed("B");

        var users = new UserService(_local, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _stock = new StockService(_local, _clock);
        _mat = new MaterialService(_local, _clock).Create(_admin, new NewMaterial("SNK12-1", "Malzeme"));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private void Seed(string companyId)
    {
        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@i,@i,1,1,1,0);";
        cmd.AddWithValue("@i", companyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Sunucudan gelmiş gibi bir liste uygular (ağ yok — saf aynalama yolu).</summary>
    private void Mirror(string companyId, params (string Id, string Name, string? Code)[] rows)
        => BranchMirrorApply.Run(_local, companyId, rows);

    private List<(string Id, string Name, long Deleted)> LocalBranches(string companyId)
    {
        var list = new List<(string, string, long)>();
        using var conn = _local.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, is_deleted FROM branches WHERE company_id=@c ORDER BY name;";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetInt64(2)));
        return list;
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    // ── 1. Yeni depo senaryosu (SNK-12'nin temel kabul kriteri) ───────────────────────────

    /// <summary>
    /// 1 — YENİ DEPO UÇTAN UCA: web'de açılan depo masaüstünde YOKKEN o depoya stok işlemi
    /// REDDEDİLİR; aynalama sonrası depo yerelde görünür ve işlem ÇEVRİMDIŞI yapılabilir.
    /// Bu, SNK-12'nin temel kabul kriteridir.
    /// </summary>
    [Fact]
    public void Yeni_Depo_Aynalanmadan_Kullanilamaz_Aynalandiktan_Sonra_Kullanilir()
    {
        // Web'de yeni depo açıldı; masaüstü henüz bilmiyor.
        Assert.Throws<ForbiddenException>(() =>
            _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 5m) }, Op(), branchId: "yeni-depo"));

        // Senkron turu şube listesini tazeledi.
        Mirror("A", ("yeni-depo", "Yeni Şantiye", null));

        Assert.Contains(LocalBranches("A"), b => b.Id == "yeni-depo" && b.Deleted == 0);

        // Artık ÇEVRİMDIŞI stok işlemi yapılabiliyor (ağ çağrısı yok — yerel SQLite).
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 5m) }, Op(), branchId: "yeni-depo");
        Assert.Equal(5m, _stock.GetBalanceAt(_admin, _mat, "yeni-depo"));

        // Yeni depoda transfer ve sayım da çalışır.
        Mirror("A", ("yeni-depo", "Yeni Şantiye", null), ("depo-b", "Depo B", null));
        _stock.Transfer(_admin, _mat, 2m, "yeni-depo", "depo-b", Op());
        _stock.Count(_admin, new[] { new CountLine(_mat, 4m) }, "sayım", Op(), branchId: "yeni-depo");

        Assert.Equal(4m, _stock.GetBalanceAt(_admin, _mat, "yeni-depo"));
        Assert.Equal(2m, _stock.GetBalanceAt(_admin, _mat, "depo-b"));
    }

    // ── 2-4. Mevcut depolar · kopya · isim güncellemesi ───────────────────────────────────

    /// <summary>2 + 3 — MEVCUT DEPOLAR KAYBOLMAZ, aynalama tekrarlansa bile KOPYA oluşmaz.</summary>
    [Fact]
    public void Tekrarlanan_Aynalama_Kopya_Uretmez_ve_Mevcutlari_Korur()
    {
        Mirror("A", ("d1", "Depo 1", "K1"), ("d2", "Depo 2", null));
        Mirror("A", ("d1", "Depo 1", "K1"), ("d2", "Depo 2", null));
        Mirror("A", ("d1", "Depo 1", "K1"), ("d2", "Depo 2", null));

        var rows = LocalBranches("A");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, b => Assert.Equal(0, b.Deleted));
    }

    /// <summary>4 — İSİM/KOD GÜNCELLEMESİ yerele yansır (web-otoriteli).</summary>
    [Fact]
    public void Isim_Degisikligi_Yerele_Yansir()
    {
        Mirror("A", ("d1", "Eski Ad", "K1"));
        Mirror("A", ("d1", "Yeni Ad", "K2"));

        var row = Assert.Single(LocalBranches("A"));
        Assert.Equal("Yeni Ad", row.Name);
    }

    // ── 5. Pasif/silinmiş depo ───────────────────────────────────────────────────────────

    /// <summary>5 — Sunucuda ARTIK OLMAYAN depo yerelde PASİFE alınır, FİZİKSEL silinmez
    /// (stok hareketleri o kimliğe bağlı; silmek geçmişi kopartırdı). Yeniden açılırsa aktifleşir.</summary>
    [Fact]
    public void Sunucuda_Olmayan_Depo_Pasife_Alinir_Fiziksel_Silinmez()
    {
        Mirror("A", ("d1", "Depo 1", null), ("d2", "Depo 2", null));
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 7m) }, Op(), branchId: "d2");

        Mirror("A", ("d1", "Depo 1", null));   // d2 sunucudan kaldırıldı

        var rows = LocalBranches("A");
        Assert.Equal(2, rows.Count);                                        // satır DURUYOR
        Assert.Equal(1, rows.Single(b => b.Id == "d2").Deleted);            // ama pasif
        Assert.Equal(7m, _stock.GetBalanceAt(_admin, _mat, "d2"));          // geçmiş stok KAYBOLMADI

        Mirror("A", ("d1", "Depo 1", null), ("d2", "Depo 2", null));        // yeniden açıldı
        Assert.Equal(0, LocalBranches("A").Single(b => b.Id == "d2").Deleted);
    }

    // ── 6. Firma izolasyonu ──────────────────────────────────────────────────────────────

    /// <summary>6 — FİRMA İZOLASYONU: A firmasının aynalaması B firmasının depolarına DOKUNMAZ
    /// (ne pasife alır ne değiştirir). İki firmanın listesi birbirini silemez.</summary>
    [Fact]
    public void Bir_Firmanin_Aynalamasi_Digerinin_Depolarina_Dokunmaz()
    {
        Mirror("A", ("a1", "A Depo", null));
        Mirror("B", ("b1", "B Depo", null));

        // A'nın listesi değişti (a1 kaldırıldı, a2 eklendi) — B etkilenmemeli.
        Mirror("A", ("a2", "A Depo 2", null));

        var b = Assert.Single(LocalBranches("B"));
        Assert.Equal("b1", b.Id);
        Assert.Equal(0, b.Deleted);                                          // B'nin deposu HÂLÂ aktif

        Assert.Equal(1, LocalBranches("A").Single(x => x.Id == "a1").Deleted);
        Assert.Equal(0, LocalBranches("A").Single(x => x.Id == "a2").Deleted);
    }

    /// <summary>7 — YETKİ/SAHİPLİK BYPASS EDİLMEZ: aynalama yalnız kendi firmasının satırlarını yazar;
    /// başka firmanın deposuna stok işlemi yine reddedilir.</summary>
    [Fact]
    public void Aynalama_Sahiplik_Kontrolunu_Bypass_Etmez()
    {
        Mirror("A", ("a1", "A Depo", null));
        Mirror("B", ("b1", "B Depo", null));

        Assert.Throws<ForbiddenException>(() =>
            _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 5m) }, Op(), branchId: "b1"));
    }

    /// <summary>8 — BOŞ LİSTE koruması: sunucu boş liste dönerse bu "hepsi silindi" demektir ve
    /// yalnız İLGİLİ firmanın depoları pasife alınır. (Çevrimdışı durumda liste zaten <c>null</c>
    /// döner ve <see cref="BranchMirror.Apply"/> hiç çağrılmaz — yerel liste korunur.)</summary>
    [Fact]
    public void Bos_Liste_Yalniz_Kendi_Firmasini_Pasife_Alir()
    {
        Mirror("A", ("a1", "A Depo", null));
        Mirror("B", ("b1", "B Depo", null));

        Mirror("A");   // sunucuda A'nın hiç şubesi kalmadı

        Assert.Equal(1, LocalBranches("A").Single().Deleted);
        Assert.Equal(0, LocalBranches("B").Single().Deleted);
    }

    /// <summary>9 — ÇEVRİMDIŞI KORUMASI: aynalama sunucuya ulaşamazsa hiç çağrılmaz ve yerel liste
    /// AYNEN kalır. Burada o sözleşmeyi kilitliyoruz: mevcut kayıtlar dokunulmadan duruyor ve
    /// çevrimdışı stok işlemi çalışmaya devam ediyor.</summary>
    [Fact]
    public void Cevrimdisi_Aynalama_Yapilmazsa_Yerel_Liste_Korunur()
    {
        Mirror("A", ("d1", "Depo 1", null));
        _stock.ReceiveIn(_admin, new[] { new StockLine(_mat, 9m) }, Op(), branchId: "d1");

        // ÇEVRİMDIŞI dönem: aynalama HİÇ çağrılmaz (sunucu null döndü) → liste değişmez.
        Assert.Single(LocalBranches("A"));
        Assert.Equal(0, LocalBranches("A").Single().Deleted);

        // Ve çevrimdışı stok işlemi sürer.
        _stock.IssueOut(_admin, new[] { new StockLine(_mat, 4m) }, Op(), branchId: "d1");
        Assert.Equal(5m, _stock.GetBalanceAt(_admin, _mat, "d1"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }
}
