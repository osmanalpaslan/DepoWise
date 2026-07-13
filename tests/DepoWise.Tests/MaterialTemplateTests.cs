using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using Xunit;

namespace DepoWise.Tests;

/// <summary>Adım 5 — Malzeme şablonu: admin şablonu (is_global) herkese; diğer kullanıcının şablonu yalnız
/// OLUŞTURANA görünür. Genel şablonu yalnız admin, kişiseli yalnız sahibi/admin yönetir.</summary>
public class MaterialTemplateTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialTemplateService _svc;

    private readonly SessionContext _admin = new("admin", "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    private readonly SessionContext _u1 = new("u1", "A", new[] { RoleKeys.Staff },
        new PermissionSet(new[] { new ModulePermission("material_templates", true, true, true, true) }));
    private readonly SessionContext _u2 = new("u2", "A", new[] { RoleKeys.Staff },
        new PermissionSet(new[] { new ModulePermission("material_templates", true, true, true, true) }));

    public MaterialTemplateTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mtpl_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _svc = new MaterialTemplateService(_factory, _clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Fact]
    public void AdminSablonu_Global_HerkeseGorunur()
    {
        var id = _svc.Create(_admin, new NewMaterialTemplate("Standart Cıvata", Code: "CIV", MinStock: 10m));
        Assert.Contains(_svc.List(_u1), t => t.Id == id && t.IsGlobal && !t.Mine);
        Assert.Contains(_svc.List(_u2), t => t.Id == id);
        Assert.NotNull(_svc.Get(_u2, id)); // içerik de görünür
    }

    [Fact]
    public void KullaniciSablonu_YalnizOlusturana()
    {
        var id = _svc.Create(_u1, new NewMaterialTemplate("U1 Şablon"));
        Assert.Contains(_svc.List(_u1), t => t.Id == id && !t.IsGlobal && t.Mine);
        Assert.DoesNotContain(_svc.List(_u2), t => t.Id == id);   // başka kullanıcı GÖRMEZ
        Assert.Null(_svc.Get(_u2, id));                           // içerik de gizli
    }

    [Fact]
    public void Yonetim_GenelYalnizAdmin_KiseselYalnizSahibi()
    {
        var global = _svc.Create(_admin, new NewMaterialTemplate("Genel"));
        var u1p = _svc.Create(_u1, new NewMaterialTemplate("U1"));

        // Personel genel şablonu düzenleyemez
        Assert.Throws<ForbiddenException>(() => _svc.Update(_u1, global, new NewMaterialTemplate("X")));
        // Başka personel kişisel şablonu düzenleyemez
        Assert.Throws<ForbiddenException>(() => _svc.Update(_u2, u1p, new NewMaterialTemplate("X")));
        // Sahibi kendi şablonunu düzenler; admin tümünü düzenler
        _svc.Update(_u1, u1p, new NewMaterialTemplate("U1 v2"));
        _svc.Update(_admin, u1p, new NewMaterialTemplate("U1 v3"));
        _svc.Update(_admin, global, new NewMaterialTemplate("Genel v2"));
    }

    [Fact]
    public void Sablon_Icerik_DoldururPrefill()
    {
        var id = _svc.Create(_admin, new NewMaterialTemplate("Kaynak", Code: "KYN", Type: "sarf", MinStock: 5m, UnitPrice: 12.5m, Currency: "TRY"));
        var rec = _svc.Get(_u1, id)!;
        Assert.Equal("KYN", rec.Code);
        Assert.Equal("sarf", rec.Type);
        Assert.Equal(5m, rec.MinStock);
        Assert.Equal(12.5m, rec.UnitPrice);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
