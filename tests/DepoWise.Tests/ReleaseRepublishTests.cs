using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Update;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ AYNI SÜRÜMÜ YENİDEN YAYINLAMA (2026-09-02 düzeltmesi) ═══
///
/// <b>Bulunan hata:</b> <c>app_releases(version)</c> UNIQUE'tir (Migration012). <c>Publish</c> koşulsuz
/// INSERT yapıyordu → aynı sürüm ikinci kez yayınlandığında unique ihlaliyle patlıyordu. Ama uç
/// (<c>POST /api/releases</c>) paket dosyasını BU ÇAĞRIDAN ÖNCE yazar ve sürüm adıyla EZER. Sonuç:
/// diskte YENİ paket, veritabanında ESKİ checksum/boyut → istemci checksum doğrulamasında paketi
/// BOZUK sayar ve kurmaz; yayın notu da düzeltilemez.
///
/// Kilitlenen davranış: yeniden yayın kaydı **GÜNCELLER**, ikizlemez, kimliği korur.
/// </summary>
public class ReleaseRepublishTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly ReleaseService _releases;
    private readonly SessionContext _super;

    private const string Sum1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sum2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public ReleaseRepublishTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_rel_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _releases = new ReleaseService(_factory);
        _super = new SessionContext("u1", "__global__", new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);
    }

    private long SatirSayisi(string version)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM app_releases WHERE version=@v;";
        cmd.AddWithValue("@v", version);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>RP1 — Aynı sürüm yeniden yayınlanabilir: kayıt GÜNCELLENİR, ikizlenmez, kimlik korunur.</summary>
    [Fact]
    public void RP1_Ayni_Surum_Yeniden_Yayinlanabilir_Kayit_Guncellenir()
    {
        var ilkId = _releases.Publish(_super, new NewRelease("1.0.168", Sum1, 100, "0.0.0", "eski not",
            DownloadUrl: "/api/releases/1.0.168/download"));

        var ikinciId = _releases.Publish(_super, new NewRelease("1.0.168", Sum2, 200, "0.0.0", "duzeltilmis not",
            DownloadUrl: "/api/releases/1.0.168/download"));

        Assert.Equal(ilkId, ikinciId);          // kimlik korunur
        Assert.Equal(1, SatirSayisi("1.0.168")); // ikizlenmedi

        var son = _releases.Latest()!;
        Assert.Equal("1.0.168", son.Version);
        Assert.Equal("duzeltilmis not", son.ReleaseNotes);
        Assert.Equal(Sum2.ToUpperInvariant(), son.ChecksumSha256);   // paketle kayıt artık TUTARLI
        Assert.Equal(200, son.SizeBytes);
    }

    /// <summary>RP2 — Farklı sürümler ayrı satır kalır; Latest() en yüksek SemVer'i döndürür.</summary>
    [Fact]
    public void RP2_Farkli_Surumler_Ayri_Kalir()
    {
        _releases.Publish(_super, new NewRelease("1.0.167", Sum1, 100));
        _releases.Publish(_super, new NewRelease("1.0.168", Sum2, 200, "0.0.0", "yeni"));

        Assert.Equal(1, SatirSayisi("1.0.167"));
        Assert.Equal(1, SatirSayisi("1.0.168"));
        Assert.Equal("1.0.168", _releases.Latest()!.Version);
    }

    /// <summary>RP3 — Yetki: yeniden yayın da yalnız Süper Admin'e açıktır (kapı gevşemedi).</summary>
    [Fact]
    public void RP3_Yeniden_Yayin_Yalniz_SuperAdmin()
    {
        _releases.Publish(_super, new NewRelease("1.0.168", Sum1, 100, "0.0.0", "ilk"));

        var normal = new SessionContext("u2", "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() =>
            _releases.Publish(normal, new NewRelease("1.0.168", Sum2, 200, "0.0.0", "yetkisiz")));

        Assert.Equal("ilk", _releases.Latest()!.ReleaseNotes);   // değişmedi
    }

    /// <summary>RP4 — Doğrulama kapıları yeniden yayında da geçerli (bozuk checksum kaydı BOZAMAZ).</summary>
    [Fact]
    public void RP4_Gecersiz_Checksum_Mevcut_Kaydi_Bozmaz()
    {
        _releases.Publish(_super, new NewRelease("1.0.168", Sum1, 100, "0.0.0", "ilk"));

        Assert.Throws<ArgumentException>(() =>
            _releases.Publish(_super, new NewRelease("1.0.168", "kisa", 200, "0.0.0", "bozuk")));

        var son = _releases.Latest()!;
        Assert.Equal(Sum1.ToUpperInvariant(), son.ChecksumSha256);
        Assert.Equal("ilk", son.ReleaseNotes);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
