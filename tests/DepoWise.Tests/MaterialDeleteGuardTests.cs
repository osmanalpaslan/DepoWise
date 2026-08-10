using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Requests;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// MLZ-01 — Malzeme silmede stok/kullanım koruması (2026-08-10).
///
/// Kural: malzeme HİÇ kullanılmamışsa silinebilir; stoğu varsa veya operasyonel geçmişi varsa SİLİNEMEZ.
///
/// Neden önemli: malzeme kataloğu FİRMA-GENELİ ortak listedir (kullanıcı kararı 2026-07-26). Koruma
/// olmadan, silme yetkisi olan HERHANGİ bir şubedeki kullanıcı tüm firmanın kullandığı malzemeyi
/// listeden düşürebiliyordu.
///
/// Koruma <see cref="MaterialService"/> içindedir; masaüstü servisi DOĞRUDAN, web ise API üzerinden
/// aynı metodu çağırdığı için tek nokta her iki platformu ve doğrudan API çağrısını birlikte korur.
/// </summary>
public class MaterialDeleteGuardTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly OpeningStockService _opening;
    private readonly StockService _stock;
    private readonly RequestService _requests;

    public MaterialDeleteGuardTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mlz01_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _opening = new OpeningStockService(_factory, _clock);
        _stock = new StockService(_factory, _clock);
        _requests = new RequestService(_factory, _stock, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = UtcNow.AddMilliseconds(ms);
    }

    private SessionContext Admin(string company)
    {
        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin(company, "admin_" + company, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    /// <summary>Silme yetkisi OLMAYAN kullanıcı (deny-by-default: hiç izin verilmemiş).</summary>
    private SessionContext NoDeleteUser(string company)
        => new SessionContext("u-nodelete", company, new[] { "warehouse" }, PermissionSet.Empty);

    private bool Exists(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT is_deleted FROM materials WHERE id=@i;";
        cmd.AddWithValue("@i", materialId);
        var v = cmd.ExecuteScalar();
        return v is not null && Convert.ToInt64(v) == 0L;
    }

    // ───────────────────────── Test 1 — hiç kullanılmamış malzeme silinebilir ─────────────────────────

    [Fact]
    public void HicKullanilmamis_Malzeme_Silinebilir()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-CLEAN", "Kullanılmamış"));

        _materials.Delete(a, m);   // atmamalı

        Assert.False(Exists(m));
    }

    // ───────────────────────── Test 2 — stok bakiyesi varsa silinemez ─────────────────────────

    [Fact]
    public void StokBakiyesiVarsa_Silinemez()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-STOK", "Stoklu Malzeme"));
        _opening.RecordOpening(a, m, 45m, "op-acilis-1");

        var ex = Assert.Throws<InvalidOperationException>(() => _materials.Delete(a, m));

        Assert.Contains("silinemez", ex.Message);
        Assert.Contains("stokta", ex.Message);
        Assert.Contains("M-STOK", ex.Message);          // hangi malzeme olduğu mesajda
        Assert.True(Exists(m));                          // Test 7/8: kayıt korunur
        Assert.Equal(45m, _opening.GetBalance(a, m));    // Test 8: stok etkilenmez
    }

    // ───────── Test 3 — bakiye SIFIRA düşse bile geçmiş hareket varsa silinemez (geçmiş korunur) ─────────

    [Fact]
    public void BakiyeSifir_AmaGecmisHareketVar_Silinemez()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-GECMIS", "Geçmişi Olan"));
        _opening.RecordOpening(a, m, 10m, "op-acilis-2");
        _stock.IssueOut(a, new[] { new StockLine(m, 10m) }, "op-cikis-1");

        Assert.Equal(0m, _opening.GetBalance(a, m));     // bakiye sıfır

        var ex = Assert.Throws<InvalidOperationException>(() => _materials.Delete(a, m));

        Assert.Contains("stok hareketi", ex.Message);    // sebep geçmiş hareket
        Assert.True(Exists(m));
    }

    // ───────────────────────── Test 3b — operasyonel kayıt (talep kalemi) ─────────────────────────

    [Fact]
    public void TalepKalemindeKullanilmis_Silinemez()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-TALEP", "Talepte Geçen"));
        _requests.Create(a, new NewRequest(new[] { new RequestItemInput(m, 3m) }));

        var ex = Assert.Throws<InvalidOperationException>(() => _materials.Delete(a, m));

        Assert.Contains("talep kalemi", ex.Message);
        Assert.True(Exists(m));
    }

    // ───────────────────────── Test 4 — yetkisiz kullanıcı silemez ─────────────────────────

    [Fact]
    public void YetkisizKullanici_Silemez()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-YETKI", "Yetki Testi"));

        // Yetki kontrolü kullanım kontrolünden ÖNCE çalışmalı: yetkisiz kullanıcı, malzemenin
        // kullanılıp kullanılmadığını öğrenemez (bilgi sızıntısı olmaz).
        Assert.Throws<ForbiddenException>(() => _materials.Delete(NoDeleteUser("A"), m));

        Assert.True(Exists(m));
    }

    // ───────────────────────── Test 9 — firma izolasyonu: başka firma etkilenmez ─────────────────────────

    [Fact]
    public void BaskaFirmaninAyniKodluMalzemesi_Etkilenmez()
    {
        var a = Admin("A");
        var b = Admin("B");

        // İki firmada AYNI kodla malzeme; A'nınki stoklu (silinemez), B'ninki temiz (silinebilir).
        var ma = _materials.Create(a, new NewMaterial("ORTAK-KOD", "A Malzemesi"));
        var mb = _materials.Create(b, new NewMaterial("ORTAK-KOD", "B Malzemesi"));
        _opening.RecordOpening(a, ma, 5m, "op-acilis-3");

        Assert.Throws<InvalidOperationException>(() => _materials.Delete(a, ma));

        // B'ninki kullanılmamış → silinebilmeli; A'nın stoğu bundan etkilenmemeli.
        _materials.Delete(b, mb);

        Assert.False(Exists(mb));
        Assert.True(Exists(ma));
        Assert.Equal(5m, _opening.GetBalance(a, ma));
    }

    // ───────── Firma izolasyonu 2: A'nın kullanımı B'nin aynı kodlu malzemesini KİLİTLEMEZ ─────────

    [Fact]
    public void BirFirmaninKullanimi_DigerFirmayiKilitlemez()
    {
        var a = Admin("A");
        var b = Admin("B");
        var ma = _materials.Create(a, new NewMaterial("K-1", "A"));
        var mb = _materials.Create(b, new NewMaterial("K-1", "B"));

        _opening.RecordOpening(a, ma, 100m, "op-acilis-4");

        // B'nin malzemesinin hiç kullanımı yok → engellenmemeli (kontrol material_id bazlı, kod bazlı DEĞİL).
        _materials.Delete(b, mb);
        Assert.False(Exists(mb));
    }

    // ───────────────────────── Test 7 — engellenen silme sonrası geçmiş bozulmaz ─────────────────────────

    [Fact]
    public void EngellenenSilme_HicbirSeyiDegistirmez()
    {
        var a = Admin("A");
        var m = _materials.Create(a, new NewMaterial("M-ROLLBACK", "Rollback Testi"));
        _opening.RecordOpening(a, m, 7m, "op-acilis-5");

        var versionBefore = Version(m);

        Assert.Throws<InvalidOperationException>(() => _materials.Delete(a, m));

        // Transaction geri alındı: is_deleted, version ve bakiye AYNI kalmalı.
        Assert.True(Exists(m));
        Assert.Equal(versionBefore, Version(m));
        Assert.Equal(7m, _opening.GetBalance(a, m));
        Assert.Equal("Rollback Testi", _materials.GetDetail(a, m).Name);
    }

    private long Version(string materialId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM materials WHERE id=@i;";
        cmd.AddWithValue("@i", materialId);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
    }
}
