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

/// <summary>Ortak rapor mimarisi (kullanıcı isteği 2026-08-07): katalog bütünlüğü + şube-yetki (btn-branch-select)
/// mantığı + Run dispatch/tarih-varsayılanı/maksimum-kayıt. Hesaplama metotları değişmedi.</summary>
public class ReportArchitectureTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly MaterialService _materials;
    private readonly ReportService _reports;
    private readonly SessionContext _admin;

    public ReportArchitectureTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_rep_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _materials = new MaterialService(_factory, _clock);
        _reports = new ReportService(_factory);

        var users = new UserService(_factory, _clock);
        var id = users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(id, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }

    // ── Katalog bütünlüğü ──
    [Fact]
    public void Katalog_TumAnahtarlar_RunTarafindanTaninir()
    {
        // Sayı, kataloğa BİLİNÇLİ eklemelerle birlikte güncellenir (12 → 13: STK-10a `stock-movements`;
        // 19 → 21: RPR-10 `inspection` + RPR-11 `personnel`, denetim 2026-08-26).
        // Gevşetme değil: aşağıdaki döngü HER rapor için ad/açıklama/kategori/RequiresDate ve `ByKey`
        // çözümlemesini sınamaya devam ediyor; sayı yalnız "sessizce rapor eklendi/silindi" nöbetçisidir.
        // 21 → 22: RPT-GUNLUK `vehicle-daily` (2026-08-29, PK-R1=A — Araç Raporu günlük kırılımı).
        // 21 → 22: RPT-GUNLUK (vehicle-daily, PK-R1=A) · 22 → 24: ADR-182 / ARA İŞ 2-S3
        // (fuel-daily PK-G1=A · stock-movements-daily PK-G2=A).
        // 24 → 25: ADR-182 / ARA İŞ 2-S4 (daily-activity, PK-D1=A — Günlük Faaliyet detay raporu).
        // 25 → 26: 2026-09-02 kullanıcı isteği (daily-activity-summary — Günlük Faaliyet dönem/toplam).
        Assert.Equal(26, ReportCatalog.All.Count);
        foreach (var d in ReportCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name));
            Assert.False(string.IsNullOrWhiteSpace(d.Description));                 // UI'ya-dönük açıklama dolu
            Assert.False(string.IsNullOrWhiteSpace(ReportCatalog.CategoryLabel(d.Category)));  // kategori etiketi var
            if (d.RequiresDate) Assert.True(d.UsesDate);          // RequiresDate ⊆ UsesDate
            Assert.NotNull(ReportCatalog.ByKey(d.Key));
        }
    }

    [Fact]
    public void Run_BilinmeyenAnahtar_Firlatir()
        => Assert.Throws<ArgumentException>(() => _reports.Run(_admin, "yok-boyle-rapor", new ReportRequest(true)));

    // ── Şube yetki mantığı (güvenlik-kritik) ──
    [Fact]
    public void SubeSecimi_YetkisizKullanici_Yoksayilir_OturumSubesineDuser()
    {
        var staff = new SessionContext("u", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty) { OperatingBranchId = "B1" };
        // Yetkisiz kullanıcı B2/B3 seçse bile → yalnız oturum şubesi B1 (fail-closed).
        var eff = ReportScope.Effective(staff, new ReportRequest(true, BranchIds: new[] { "B2", "B3" }));
        Assert.Equal(new[] { "B1" }, eff);
        Assert.False(ReportScope.CanSelectBranches(staff));
    }

    [Fact]
    public void SubeSecimi_YetkiliKullanici_AcikSecim_Uygulanir()
    {
        // Admin = yetkili (CanUseButton bypass). Açık seçim honor edilir.
        _admin.OperatingBranchId = "B1";
        var eff = ReportScope.Effective(_admin, new ReportRequest(true, BranchIds: new[] { "B2", "B3" }));
        Assert.Equal(new[] { "B2", "B3" }, eff);
        Assert.True(ReportScope.CanSelectBranches(_admin));
    }

    [Fact]
    public void SubeSecimi_YetkiliAmaBosSecim_MevcutDavranis_OturumSubesi()
    {
        // Non-breaking: boş seçim → mevcut davranış (oturum şubesi), "tüm şubeler" DEĞİL.
        _admin.OperatingBranchId = "B1";
        var eff = ReportScope.Effective(_admin, new ReportRequest(true, BranchIds: Array.Empty<string>()));
        Assert.Equal(new[] { "B1" }, eff);
    }

    [Fact]
    public void ButonYetkisi_PersonelSet_ile_Verilebilir()
    {
        var set = new PermissionSet(Array.Empty<ModulePermission>(), new[] { SpecialButtons.BranchSelect });
        var staff = new SessionContext("u", "A", new[] { RoleKeys.Staff }, set) { OperatingBranchId = "B1" };
        Assert.True(ReportScope.CanSelectBranches(staff));
        Assert.Equal(new[] { "B9" }, ReportScope.Effective(staff, new ReportRequest(true, BranchIds: new[] { "B9" })));
    }

    // ── Run: maksimum kayıt koruması ──
    [Fact]
    public void Run_MaksimumKayit_UsttenKeser()
    {
        _materials.Create(_admin, new NewMaterial("M1", "Bir"));
        _materials.Create(_admin, new NewMaterial("M2", "Iki"));
        _materials.Create(_admin, new NewMaterial("M3", "Uc"));

        var full = _reports.Run(_admin, "stock", new ReportRequest(true), maxRows: 0);   // 0 = sınırsız
        Assert.Equal(3, full.Rows.Count);
        var capped = _reports.Run(_admin, "stock", new ReportRequest(true), maxRows: 2);
        Assert.Equal(2, capped.Rows.Count);
    }

    [Fact]
    public void Run_TarihGerektiren_TarihSizCalisir_VarsayilanUygular()
    {
        // "maintenance" RequiresDate=true. Tarih verilmese bile Run içeride Bu Ay'a düşürür → hata atmadan çalışır.
        var t = _reports.Run(_admin, "maintenance", new ReportRequest(true));
        Assert.Equal("Bakım Raporu", t.Title);   // çalıştı (boş sonuç olabilir)
    }
}
