using System.Collections.Generic;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// REGRESYON (2026-07-16): Malzeme içe aktarımı Türk Excel'inin virgüllü ondalığını 10 KAT BOZUYORDU.
///
/// Sebep: <c>Money.Parse</c> InvariantCulture + NumberStyles.Number ile çalışır; orada virgül BİNLİK
/// AYIRICIDIR. "12,5" → 125 (sessizce, hata vermeden). Money.Parse veritabanı okumaları içindir (değerler
/// orada daima nokta ile saklanır) — kullanıcı Excel'i için uygun değildir. İçe aktarım artık virgülü
/// noktaya çevirip parse eder.
/// </summary>
public class MaterialImportDecimalTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly MaterialImportService _import;
    private readonly SessionContext _admin;

    public MaterialImportDecimalTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_mimp_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _import = new MaterialImportService(_materials);
        var users = new UserService(_factory, _clock);
        var uid = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static ImportRow Row(int n, params (string Col, string? Val)[] cells)
        => new(n, cells.ToDictionary(c => c.Col, c => c.Val));

    /// <summary>"12,5" TL → 12,5 olmalı; eskiden 125 oluyordu (fiyat 10 kat şişiyordu).</summary>
    [Fact]
    public void VirgulluFiyat_OnKatBozulmaz()
    {
        _import.Commit(_admin, new[]
        {
            Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Birim Fiyat", "12,5"), ("Min Stok", "3,5")),
        });

        var m = _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single();
        Assert.Equal(12.5m, m.UnitPrice);   // 125 DEĞİL
        Assert.Equal(3.5m, m.MinStock);     // 35 DEĞİL
    }

    /// <summary>Nokta ile yazılmış ondalık da (İngiliz Excel'i / elle yazım) doğru okunmalı.</summary>
    [Fact]
    public void NoktaliFiyat_DogruOkunur()
    {
        _import.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Birim Fiyat", "12.5")) });

        Assert.Equal(12.5m, _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single().UnitPrice);
    }

    [Fact]
    public void TamSayiFiyat_DogruOkunur()
    {
        _import.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Birim Fiyat", "100")) });

        Assert.Equal(100m, _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single().UnitPrice);
    }

    /// <summary>Gerçekten sayı olmayan değer REDDEDİLMELİ (sessizce 0 yazılmamalı).</summary>
    [Fact]
    public void SayiOlmayanFiyat_SatirReddedilir()
    {
        var dry = _import.DryRun(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Birim Fiyat", "abc")) });

        Assert.Equal(0, dry.Valid);
        Assert.Equal(1, dry.Failed);
        Assert.Contains(dry.Errors, e => e.Message.Contains("sayısal olmalı"));
    }

    /// <summary>Min Stok da doğrulanmalı — eskiden hiç kontrol edilmiyordu, Money.Parse sessizce 0 veriyordu.</summary>
    [Fact]
    public void SayiOlmayanMinStok_SatirReddedilir()
    {
        var dry = _import.DryRun(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Min Stok", "yok")) });

        Assert.Equal(0, dry.Valid);
        Assert.Equal(1, dry.Failed);
    }

    [Fact]
    public void BosFiyat_SifirKabulEdilir()
    {
        _import.Commit(_admin, new[] { Row(2, ("Kod", "M-1"), ("Ad", "Filtre"), ("Birim Fiyat", "")) });

        Assert.Equal(0m, _materials.List(_admin, new PageRequest { Limit = 10 }).Items.Single().UnitPrice);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
