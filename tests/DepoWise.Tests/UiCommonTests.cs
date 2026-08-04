using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Theming;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Settings;
using Xunit;

namespace DepoWise.Tests;

public class UiCommonTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;

    public UiCommonTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_ui_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
    }

    private static SessionContext Session(string company = "A", IEnumerable<string>? roles = null, IEnumerable<ModulePermission>? perms = null)
        => new("u1", company, roles ?? Array.Empty<string>(), new PermissionSet(perms ?? Array.Empty<ModulePermission>()));

    // ---- Menü ----
    [Fact]
    public void Menu_YetkisizModulGizli_DashboardHerZaman()
    {
        var menu = MenuBuilder.Build(Session());
        Assert.Contains(menu, m => m.Key == AppModules.Dashboard);
        Assert.DoesNotContain(menu, m => m.Key == "materials");
    }

    [Fact]
    public void Menu_YetkiliModulGorunur()
    {
        var menu = MenuBuilder.Build(Session(perms: new[]
        {
            new ModulePermission("materials", true, false, false, false),
        }));
        Assert.Contains(menu, m => m.Key == "materials");
    }

    [Fact]
    public void Menu_Admin_TumModulleriGorur()
    {
        var menu = MenuBuilder.Build(Session(roles: new[] { RoleKeys.CompanyAdmin }));
        // Firma Admini, yalnız-Süper-Admin modülleri (companies/releases) HARİÇ tümünü görür.
        var superOnly = AppModules.All.Count(m => AppModules.IsSuperAdminOnly(m.Key));
        Assert.True(menu.Count >= AppModules.All.Count - superOnly - 1);
        Assert.Contains(menu, m => m.Key == "users");
        Assert.DoesNotContain(menu, m => m.Key == "companies");
        Assert.DoesNotContain(menu, m => m.Key == "releases");
    }

    // ---- Tarih ----
    [Theory]
    [InlineData("15/06/2026", true)]
    [InlineData("29/02/2024", true)]   // artık yıl
    [InlineData("29/02/2025", false)]  // artık yıl değil
    [InlineData("31/02/2026", false)]  // şubat 31
    [InlineData("00/01/2026", false)]
    [InlineData("13/13/2026", false)]
    [InlineData("1/1/2026", false)]    // maske ihlali
    [InlineData("2026-06-15", false)]
    [InlineData("", false)]
    public void Tarih_GercekTakvim(string input, bool ok)
        => Assert.Equal(ok, DateInput.Validate(input).Ok);

    // ---- Numerik ----
    [Fact]
    public void Numerik_Negatif_Reddedilir()
    {
        Assert.False(NumericInput.Validate(-1).Ok);
        Assert.True(NumericInput.Validate(-1, allowNegative: true).Ok);
    }

    [Fact]
    public void Numerik_SinirDisi_Reddedilir()
    {
        Assert.False(NumericInput.Validate(5, min: 10).Ok);
        Assert.False(NumericInput.Validate(150, max: 100).Ok);
        Assert.True(NumericInput.Validate(50, min: 10, max: 100).Ok);
        Assert.False(NumericInput.Validate(null).Ok);
    }

    // ---- Çoklu seçim ----
    [Fact]
    public void MultiSelect_Arama_SecimleriKaybetmez()
    {
        var ms = new MultiSelectState<string>(new[] { "Ankara", "İstanbul", "İzmir", "Bursa" }, x => x);
        ms.Toggle("Bursa", true);
        ms.Search("iz");                      // Bursa filtre dışı
        Assert.DoesNotContain("Bursa", ms.Filtered());
        Assert.True(ms.IsSelected("Bursa"));  // seçim korunur
        Assert.Equal(1, ms.SelectedCount);
    }

    [Fact]
    public void MultiSelect_TumunuSec_YalnizFiltreyiEkler()
    {
        var ms = new MultiSelectState<string>(new[] { "Ankara", "İstanbul", "İzmir", "Bursa" }, x => x);
        ms.Search("i");                        // İstanbul, İzmir (Türkçe duyarsız)
        ms.SelectAllFiltered();
        Assert.True(ms.IsSelected("İstanbul"));
        Assert.True(ms.IsSelected("İzmir"));
        Assert.False(ms.IsSelected("Ankara")); // filtre dışı eklenmez
        Assert.False(ms.IsSelected("Bursa"));
    }

    [Fact]
    public void MultiSelect_TumunuKaldir_YalnizFiltreyiCikarir()
    {
        var ms = new MultiSelectState<string>(new[] { "Ankara", "İstanbul", "İzmir" }, x => x,
            initialSelected: new[] { "Ankara", "İstanbul", "İzmir" });
        ms.Search("ank");
        ms.ClearFiltered();
        Assert.False(ms.IsSelected("Ankara"));
        Assert.True(ms.IsSelected("İstanbul")); // filtre dışı korunur
    }

    // ---- Alan görünürlüğü ----
    [Fact]
    public void Alan_ArtiButonu_YetkiYoksaGizli()
    {
        var f = new FieldDefinition("brand", "Marka", FieldType.Lookup, "materials", IsLookup: true, AllowAdd: true);
        var reader = Session(perms: new[] { new ModulePermission("materials", true, false, false, false) });
        var writer = Session(perms: new[] { new ModulePermission("materials", true, true, false, false) });

        Assert.True(FieldVisibility.IsVisible(reader, f));
        Assert.False(FieldVisibility.CanShowAddButton(reader, f)); // yazma yok → "+" gizli
        Assert.True(FieldVisibility.CanShowAddButton(writer, f));
    }

    [Fact]
    public void Alan_Dogrulama_TarihVeNumerik()
    {
        var date = new FieldDefinition("d", "Tarih", FieldType.Date, "materials");
        Assert.False(FieldVisibility.ValidateValue(date, "31/02/2026", null).Ok);

        var qty = new FieldDefinition("q", "Miktar", FieldType.Numeric, "materials", Required: true, Min: 0);
        Assert.False(FieldVisibility.ValidateValue(qty, null, -5).Ok);
        Assert.True(FieldVisibility.ValidateValue(qty, null, 3).Ok);
    }

    // ---- Tema / branding (sabit yazılmaz) ----
    [Fact]
    public void Tema_Varsayilan_VeFirmaOverride_AuditLenir()
    {
        new MigrationRunner(_factory).Run();
        var svc = new SettingsService(_factory);

        // Varsayılan (override yok)
        Assert.Equal(ThemeTokens.Default.Primary, svc.GetTheme("A").Primary);
        Assert.Equal("Alpnex", svc.GetBranding("A").AppName);

        // Firma override
        svc.Set("A", SettingKeys.ThemePrimary, "#FF0000", userId: "u1");
        svc.Set("A", SettingKeys.BrandAppName, "Acme Depo", userId: "u1");
        Assert.Equal("#FF0000", svc.GetTheme("A").Primary);
        Assert.Equal("Acme Depo", svc.GetBranding("A").AppName);
        // Başka firma etkilenmez (global varsayılan)
        Assert.Equal(ThemeTokens.Default.Primary, svc.GetTheme("B").Primary);

        // Ayar değişikliği audit'lendi
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_logs WHERE entity_type='app_setting';";
        Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 2);
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
