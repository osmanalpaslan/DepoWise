using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ŞB-04 (2026-08-18) — ÜST ŞUBE ARTIK İŞLEVSEL.
///
/// <b>Önceki durum:</b> <c>branches.parent_id</c> ilk günden beri vardı ama YALNIZ saklanıp
/// gösteriliyordu. <see cref="BranchAccess"/>, raporlar ve hiçbir filtre onu okumuyordu →
/// "Üst Şube" alanı sadece bir etiketti: Merkez'e yetkili kullanıcı Merkez'in altındaki şantiyeleri
/// GÖREMİYOR, Merkez seçildiğinde rapor altları TOPLAMIYORDU.
///
/// <b>Yeni davranış:</b> kapsam ve rapor ağaca uyar. İki kural değişmedi:
/// • <b>Fail-closed:</b> genişletme İZİNLİ kümeyi AŞAMAZ — kesişim aynen uygulanır.
/// • <b>Fail-safe:</b> ağaç yüklenmemişse (<c>BranchDescendants == null</c>) davranış ŞB-04 öncesiyle
///   birebir aynıdır → kapsam kazara genişlemez.
/// </summary>
public class BranchParentScopeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly BranchService _branches;
    private readonly SessionContext _admin;
    private readonly string _merkez, _santiye1, _santiye2, _altSantiye, _bagimsiz;
    private const string Co = "DEPOWISE";

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public BranchParentScopeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ustsube_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        new CompanyService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);

        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) " +
                              "VALUES(@c,'Test',1,1,1,0) ON CONFLICT(id) DO NOTHING;";
            cmd.AddWithValue("@c", Co);
            cmd.ExecuteNonQuery();
        }

        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);

        //  MERKEZ
        //    ├── ŞANTİYE 1
        //    │     └── ALT ŞANTİYE
        //    └── ŞANTİYE 2
        //  BAĞIMSIZ (ağaç dışı)
        _merkez = _branches.Create(_admin, new NewBranch("MERKEZ"));
        _santiye1 = _branches.Create(_admin, new NewBranch("ŞANTİYE 1", "site", _merkez));
        _santiye2 = _branches.Create(_admin, new NewBranch("ŞANTİYE 2", "site", _merkez));
        _altSantiye = _branches.Create(_admin, new NewBranch("ALT ŞANTİYE", "site", _santiye1));
        _bagimsiz = _branches.Create(_admin, new NewBranch("BAĞIMSIZ"));
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>>? Agac()
    {
        using var conn = _factory.Create();
        return BranchTree.LoadDescendants(conn, Co);
    }

    /// <summary>Personel oturumu (admin bypass YOK) — kapsam gerçekten uygulanır.</summary>
    private SessionContext Personel(IReadOnlyList<string>? kapsam, string? anaSube = null, bool agacYukle = true)
        => new("kul", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty)
        {
            ScopeBranchIds = kapsam,
            HomeBranchId = anaSube,
            BranchDescendants = agacYukle ? Agac() : null,
        };

    // ── Ağaç çözümü ──────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Agac_Gecisli_Kapanis_Uretir()
    {
        var agac = Agac();
        Assert.NotNull(agac);
        var merkezAlt = agac![_merkez];
        Assert.Equal(3, merkezAlt.Count);                       // 2 şantiye + 1 alt şantiye
        Assert.Contains(_santiye1, merkezAlt);
        Assert.Contains(_santiye2, merkezAlt);
        Assert.Contains(_altSantiye, merkezAlt);                // TORUN da dahil (geçişli)
        Assert.DoesNotContain(_bagimsiz, merkezAlt);
        Assert.Equal(new[] { _altSantiye }, agac[_santiye1]);
        Assert.False(agac.ContainsKey(_santiye2));              // yaprak → haritada yok
    }

    [Fact]
    public void Agac_Duz_Yapida_Null_Doner()
    {
        var db = Path.Combine(Path.GetTempPath(), "dw_duz_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(db);
            new MigrationRunner(f).Run();
            using var conn = f.Create();
            Assert.Null(BranchTree.LoadDescendants(conn, Co));   // hiç üst/alt ilişkisi yok
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>Eski veride döngü varsa (ŞB-02 öncesi) gezinme ASILMAZ.</summary>
    [Fact]
    public void Agac_Donguye_Dayanikli()
    {
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            // MERKEZ'i kendi torununun altına al — servis engelliyor, doğrudan yazıyoruz (bozuk eski veri).
            cmd.CommandText = "UPDATE branches SET parent_id=@p WHERE id=@id;";
            cmd.AddWithValue("@p", _altSantiye);
            cmd.AddWithValue("@id", _merkez);
            cmd.ExecuteNonQuery();
        }

        var agac = Agac();   // asılmamalı

        Assert.NotNull(agac);
        Assert.DoesNotContain(_merkez, agac![_merkez]);   // kendisi listesinde olmaz
    }

    // ── Kapsam ───────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void UstSube_Kapsami_AltSubeleri_Kapsar()
    {
        var s = Personel(new[] { _merkez });

        var izinli = BranchAccess.Allowed(s)!;

        Assert.Contains(_merkez, izinli);
        Assert.Contains(_santiye1, izinli);
        Assert.Contains(_santiye2, izinli);
        Assert.Contains(_altSantiye, izinli);
        Assert.DoesNotContain(_bagimsiz, izinli);

        Assert.True(BranchAccess.CanAccess(s, _santiye1));
        Assert.True(BranchAccess.CanAccess(s, _altSantiye));
        Assert.False(BranchAccess.CanAccess(s, _bagimsiz));
    }

    [Fact]
    public void AnaSube_De_AltSubeleri_Kapsar()
    {
        var s = Personel(null, anaSube: _santiye1);

        var izinli = BranchAccess.Allowed(s)!;

        Assert.Contains(_santiye1, izinli);
        Assert.Contains(_altSantiye, izinli);
        Assert.DoesNotContain(_merkez, izinli);      // YUKARI doğru genişleme YOK
        Assert.DoesNotContain(_santiye2, izinli);    // KARDEŞE genişleme YOK
    }

    [Fact]
    public void AltSubeye_Yetkili_UstSubeyi_Goremez()
    {
        var s = Personel(new[] { _santiye1 });

        Assert.True(BranchAccess.CanAccess(s, _altSantiye));
        Assert.False(BranchAccess.CanAccess(s, _merkez));      // fail-closed: yukarı çıkılmaz
        Assert.False(BranchAccess.CanAccess(s, _santiye2));
    }

    // ── Rapor / seçim ────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Rapor_UstSube_Secilince_Altlari_Toplar()
    {
        var s = Personel(new[] { _merkez });

        var etkin = BranchAccess.Effective(s, new[] { _santiye1 })!;
        Assert.Equal(new[] { _santiye1, _altSantiye }, etkin);   // ŞANTİYE 1 seçildi → altı da geldi

        var hepsi = BranchAccess.Effective(s, new[] { _merkez })!;
        Assert.Equal(4, hepsi.Count);                            // merkez + 2 şantiye + 1 alt şantiye
    }

    /// <summary>Genişletme İZİNLİ kümeyi AŞAMAZ — elle üst şube istenerek kapsam büyütülemez.</summary>
    [Fact]
    public void Genisletme_Izinli_Kumeyi_Asamaz()
    {
        var s = Personel(new[] { _santiye1 });   // yalnız ŞANTİYE 1 + altı

        var etkin = BranchAccess.Effective(s, new[] { _merkez })!;

        Assert.DoesNotContain(_merkez, etkin);
        Assert.DoesNotContain(_santiye2, etkin);
        Assert.Contains(_altSantiye, etkin);     // istenen MERKEZ'in altındaki, izinli olan tek şube
    }

    // ── Fail-safe ────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Agac_Yuklenmemisse_Eski_Davranis()
    {
        var s = Personel(new[] { _merkez }, agacYukle: false);

        Assert.Equal(new[] { _merkez }, BranchAccess.Allowed(s));
        Assert.False(BranchAccess.CanAccess(s, _santiye1));   // ŞB-04 öncesi davranış
    }

    /// <summary>Yazma yolu da ağaca uyar: üst şubeye yetkili kullanıcı alt şubeye kayıt yazabilir.</summary>
    [Fact]
    public void Yazma_Yolu_AltSubeye_Izin_Verir()
    {
        var s = Personel(new[] { _merkez });

        BranchAccess.Require(s, _altSantiye, "test");                       // hata ATMAMALI
        Assert.Throws<ForbiddenException>(() => BranchAccess.Require(s, _bagimsiz, "test"));
    }

    /// <summary>Devir tavanı da ağaca uyar: üst şubeye yetkili yönetici alt şubeleri devredebilir.</summary>
    [Fact]
    public void Devir_Tavani_AltSubeleri_Kapsar()
    {
        var s = Personel(new[] { _merkez });

        BranchAccess.RequireGrantable(s, new[] { _santiye1, _altSantiye });   // hata ATMAMALI
        Assert.Throws<ForbiddenException>(() => BranchAccess.RequireGrantable(s, new[] { _bagimsiz }));
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
