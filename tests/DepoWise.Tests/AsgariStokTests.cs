using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-31 (AR-GE 2026-10-26, F-114) — DEPO BAZINDA ASGARİ STOK + ÖNERİ LİSTESİ ═══
///  AS1 — depoya özel asgarinin ALTINDA öneri çıkar, üstünde/eşitte çıkmaz; önerilen = asgari − mevcut
///  AS2 — genel (malzeme kartı) asgari yalnız malzemenin o depoda bakiyesi varsa uygulanır (boş şantiye selinden korur)
///  AS3 — depoya özel değer genel değeri EZER (iki yönde)
///  AS4 — kapsam: şube kullanıcısı yalnız kendi deposunun önerisini görür; başka depoya asgari yazamaz / öneri isteyemez
///  AS5 — değer kaldırma genel değere döner; tekrar yazma KOPYA üretmez (kimlik malzeme:depo)
///  AS6 — seçilen önerilerden TEK talep (depo + kalemler + açıklama); boş liste / sıfır miktar reddedilir
///  AS7 — eksi asgari reddedilir; başka firmanın deposu reddedilir
/// </summary>
public class AsgariStokTests : IDisposable
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_asgari_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SqliteConnectionFactory _f;
    private readonly AsgariStokService _asgari;
    private readonly StockService _stok;
    private readonly SessionContext _yonetici, _subeA;
    private readonly string _a, _b, _yabanci, _m1, _m2;

    public AsgariStokTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        foreach (var c in new[] { "AS", "AS-B" })
        {
            using var conn = _f.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES(@c,@c,1,1,1,0);";
            cmd.AddWithValue("@c", c);
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin("AS", "as_admin", "Test!2026", RoleKeys.CompanyAdmin);
        _yonetici = new SessionContext(uid, "AS", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var subeler = new BranchService(_f);
        _a = subeler.Create(_yonetici, new NewBranch("Merkez Depo"));
        _b = subeler.Create(_yonetici, new NewBranch("Şantiye B"));
        var uidB = new UserService(_f).EnsureInitialAdmin("AS-B", "asb_admin", "Test!2026", RoleKeys.CompanyAdmin);
        _yabanci = subeler.Create(new SessionContext(uidB, "AS-B", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty), new NewBranch("Yabancı"));
        var mz = new MaterialService(_f);
        _m1 = mz.Create(_yonetici, new NewMaterial("AS-1", "Filtre", MinStock: 10m));
        _m2 = mz.Create(_yonetici, new NewMaterial("AS-2", "Hortum"));
        _stok = new StockService(_f);
        var talepler = new RequestService(_f, _stok);
        _asgari = new AsgariStokService(_f, talepler);
        // Şube A kullanıcısı: yalnız Merkez Depo kapsamında.
        _subeA = new SessionContext(uid, "AS", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty) { OperatingBranchId = _a };
    }

    public void Dispose() { try { File.Delete(_yol); } catch { } }

    private void Giris(string m, string depo, decimal q) => _stok.ReceiveIn(_yonetici, new[] { new StockLine(m, q) }, Guid.NewGuid().ToString("N"), branchId: depo);

    [Fact]
    public void AS1_Depoya_Ozel_Asgarinin_Altinda_Oneri()
    {
        Giris(_m2, _a, 3m);
        _asgari.Ayarla(_yonetici, _m2, _a, 5m);
        var o = Assert.Single(_asgari.Oneriler(_yonetici), x => x.MaterialId == _m2);
        Assert.Equal((_a, 3m, 5m, 2m, "depo"), (o.LocationId, o.Mevcut, o.Asgari, o.Onerilen, o.Kaynak));
        Giris(_m2, _a, 2m);                                                     // 5 = asgari → öneri yok
        Assert.DoesNotContain(_asgari.Oneriler(_yonetici), x => x.MaterialId == _m2);
    }

    [Fact]
    public void AS2_Genel_Asgari_Yalniz_Malzemenin_Tutuldugu_Depoda()
    {
        Giris(_m1, _a, 4m);                                                     // m1 yalnız Merkez'de var; genel asgari 10
        var oneriler = _asgari.Oneriler(_yonetici).Where(x => x.MaterialId == _m1).ToList();
        var o = Assert.Single(oneriler);                                       // Şantiye B'de bakiye kaydı yok → öneri YOK
        Assert.Equal((_a, 6m, "genel"), (o.LocationId, o.Onerilen, o.Kaynak));
    }

    [Fact]
    public void AS3_Depoya_Ozel_Deger_Geneli_Ezer()
    {
        Giris(_m1, _a, 5m);
        _asgari.Ayarla(_yonetici, _m1, _a, 2m);                                 // genel 10 → bu depoda 2
        Assert.DoesNotContain(_asgari.Oneriler(_yonetici), x => x.MaterialId == _m1);
        _asgari.Ayarla(_yonetici, _m1, _a, 20m);                                // ters yön: genelden yüksek
        Assert.Equal(15m, Assert.Single(_asgari.Oneriler(_yonetici), x => x.MaterialId == _m1).Onerilen);
    }

    [Fact]
    public void AS4_Kapsam_Sube_Kullanicisi_Yalniz_Kendi_Deposu()
    {
        Giris(_m2, _a, 1m); Giris(_m2, _b, 1m);
        _asgari.Ayarla(_yonetici, _m2, _a, 5m);
        _asgari.Ayarla(_yonetici, _m2, _b, 5m);
        Assert.Equal(2, _asgari.Oneriler(_yonetici).Count(x => x.MaterialId == _m2));
        var benim = _asgari.Oneriler(_subeA).Where(x => x.MaterialId == _m2).ToList();
        Assert.Equal(_a, Assert.Single(benim).LocationId);
        Assert.Throws<ForbiddenException>(() => _asgari.Ayarla(_subeA, _m2, _b, 9m));
        Assert.Throws<ForbiddenException>(() => _asgari.Oneriler(_subeA, _b));
    }

    [Fact]
    public void AS5_Kaldirma_Genele_Doner_Tekrar_Yazma_Kopya_Uretmez()
    {
        Giris(_m1, _a, 5m);
        _asgari.Ayarla(_yonetici, _m1, _a, 2m);
        _asgari.Ayarla(_yonetici, _m1, _a, 3m);
        _asgari.Ayarla(_yonetici, _m1, _a, 4m);
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM material_location_mins WHERE material_id=@m;";
            cmd.AddWithValue("@m", _m1);
            Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        }
        Assert.Equal(4m, Assert.Single(_asgari.Liste(_yonetici, _m1)).Asgari);
        _asgari.Ayarla(_yonetici, _m1, _a, null);                               // kaldır → genel 10 geçerli
        Assert.Empty(_asgari.Liste(_yonetici, _m1));
        Assert.Equal("genel", Assert.Single(_asgari.Oneriler(_yonetici), x => x.MaterialId == _m1).Kaynak);
    }

    [Fact]
    public void AS6_Secilen_Onerilerden_Tek_Talep()
    {
        var h = _asgari.TalepOlustur(_yonetici, _a, new[] { new RequestItemInput(_m1, 6m), new RequestItemInput(_m2, 2m) });
        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT r.branch_id, r.description, (SELECT COUNT(*) FROM material_request_items i WHERE i.request_id=r.id) " +
                              "FROM material_requests r WHERE r.id=@id;";
            cmd.AddWithValue("@id", h.Id);
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal(_a, r.GetString(0));
            Assert.Equal("Asgari stok önerisinden oluşturuldu", r.GetString(1));
            Assert.Equal(2L, r.GetInt64(2));
        }
        Assert.Throws<ArgumentException>(() => _asgari.TalepOlustur(_yonetici, _a, Array.Empty<RequestItemInput>()));
        Assert.Throws<ArgumentException>(() => _asgari.TalepOlustur(_yonetici, _a, new[] { new RequestItemInput(_m1, 0m) }));
        Assert.Throws<ForbiddenException>(() => _asgari.TalepOlustur(_subeA, _b, new[] { new RequestItemInput(_m1, 1m) }));
    }

    [Fact]
    public void AS7_Eksi_Asgari_Ve_Yabanci_Depo_Reddedilir()
    {
        Assert.Throws<ArgumentException>(() => _asgari.Ayarla(_yonetici, _m1, _a, -1m));
        Assert.ThrowsAny<Exception>(() => _asgari.Ayarla(_yonetici, _m1, _yabanci, 5m));
        Assert.Empty(_asgari.Liste(_yonetici, _m1));
    }
}
