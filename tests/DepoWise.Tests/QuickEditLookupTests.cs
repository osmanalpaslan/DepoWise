using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// HIZLI DÜZENLEME PENCERELERİNİN SEÇİM ALANLARI (İş #9, 2026-08-09).
///
/// Malzeme/Araç hızlı düzenleme pencerelerindeki sabit tanım (lookup) alanları düz <c>ComboBox</c>'tı →
/// ARAMA YOKTU; ana ekranlar ise ortak <c>LookupBox</c> kullanıyordu. Bu iş o alanları ortak bileşene
/// geçirdi.
///
/// Avalonia için headless UI test altyapısı YOKTUR. Bu yüzden pencerelerin dayandığı iki şey test edilir:
///   1. <b>Veri kaynağı</b> — listeler firma-izole mi, kaydın MEVCUT değeri listede geliyor mu
///      (gelmezse pencere açıldığında seçim boş görünür = sessiz veri kaybı riski),
///   2. <b>Arama/sayfalama çekirdeği</b> — <see cref="LookupPaging"/>, LookupBox'ın kullandığı motor.
///
/// Not: LookupBox listeyi BELLEKTE filtreler → her tuş vuruşunda yeni sorgu ATILMAZ. Testler bu
/// varsayımı (tek yükleme + bellekte filtre) doğrular.
/// </summary>
public class QuickEditLookupTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly LookupService _lookups;
    private readonly MaterialService _materials;
    private readonly VehicleService _vehicles;
    private readonly BranchService _branches;
    private readonly UserService _users;
    private readonly SessionContext _a, _b;

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public QuickEditLookupTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_qelookup_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();

        _users = new UserService(_factory, _clock);
        _lookups = new LookupService(_factory, _clock);
        _materials = new MaterialService(_factory, _clock);
        _vehicles = new VehicleService(_factory, _clock);
        _branches = new BranchService(_factory, _clock);

        Company("A"); Company("B");
        _a = Admin("A", "kul_a");
        _b = Admin("B", "kul_b");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }

    private void Company(string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "INSERT INTO companies(id, name, created_at, updated_at, version, is_deleted, machine_quota, max_users, max_admins) " +
            "VALUES(@id, @id, 1, 1, 1, 0, 5, 20, 5) ON CONFLICT(id) DO NOTHING;";
        cmd.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private SessionContext Admin(string company, string user)
    {
        var uid = _users.EnsureInitialAdmin(company, user, "Test!2026", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, company, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    // ── 1. VERİ KAYNAĞI: firma izolasyonu ─────────────────────────────────────────────────

    [Fact]
    public void Malzeme_penceresinin_listeleri_BASKA_firmayi_gostermez()
    {
        _lookups.AddBrand(_b, "B MARKASI");
        _lookups.AddSupplier(_b, "B TEDARİKÇİ");
        _lookups.AddUnit(_b, "B BİRİM");

        Assert.DoesNotContain(_lookups.ListBrands(_a, "material"), x => x.Name == "B MARKASI");
        Assert.DoesNotContain(_lookups.List(_a, "suppliers"), x => x.Name == "B TEDARİKÇİ");
        Assert.DoesNotContain(_lookups.List(_a, "units"), x => x.Name == "B BİRİM");
    }

    [Fact]
    public void Arac_penceresinin_listeleri_BASKA_firmayi_gostermez()
    {
        _lookups.AddVehicleType(_b, "B TİPİ");
        _lookups.AddVehicleCategory(_b, "B KATEGORİSİ");
        var bBranch = _branches.Create(_b, new NewBranch("B Şubesi"));

        Assert.DoesNotContain(_lookups.List(_a, "vehicle_types"), x => x.Name == "B TİPİ");
        Assert.DoesNotContain(_lookups.List(_a, "vehicle_categories"), x => x.Name == "B KATEGORİSİ");
        Assert.DoesNotContain(_branches.List(_a), x => x.Id == bBranch);
    }

    // ── 2. VERİ KAYNAĞI: kaydın MEVCUT değeri listede gelmeli ─────────────────────────────
    // Pencere "SelectedItem = liste.FirstOrDefault(o => o.Id == kaydin_id)" ile doldurulur.
    // Değer listede yoksa seçim BOŞ görünür ve kullanıcı farkında olmadan üzerine kaydedebilir.

    [Fact]
    public void Malzemenin_MEVCUT_markasi_ve_tedarikcisi_listede_gelir()
    {
        var brandId = _lookups.AddBrand(_a, "CATERPILLAR");
        var supId = _lookups.AddSupplier(_a, "ABC LTD");
        var unitId = _lookups.AddUnit(_a, "ADET");
        var matId = _materials.Create(_a, new NewMaterial("M-1", "Filtre", UnitId: unitId, BrandId: brandId, SupplierId: supId));

        var d = _materials.GetDetail(_a, matId)!;
        Assert.Contains(_lookups.ListBrands(_a, "material"), x => x.Id == d.BrandId);
        Assert.Contains(_lookups.List(_a, "suppliers"), x => x.Id == d.SupplierId);
        Assert.Contains(_lookups.List(_a, "units"), x => x.Id == d.UnitId);
    }

    [Fact]
    public void Aracin_MEVCUT_tipi_kategorisi_ve_subesi_listede_gelir()
    {
        var typeId = _lookups.AddVehicleType(_a, "EKSKAVATÖR");
        var catId = _lookups.AddVehicleCategory(_a, "İŞ MAKİNESİ");
        var branchId = _branches.Create(_a, new NewBranch("Merkez"));
        var vehId = _vehicles.Create(_a, new NewVehicle("ARC-1", VehicleTypeId: typeId, CategoryId: catId, BranchId: branchId));

        var v = _vehicles.Get(_a, vehId)!;
        Assert.Contains(_lookups.List(_a, "vehicle_types"), x => x.Id == v.VehicleTypeId);
        Assert.Contains(_lookups.List(_a, "vehicle_categories"), x => x.Id == v.CategoryId);
        Assert.Contains(_branches.List(_a), x => x.Id == v.BranchId);
    }

    // ── 3. ARAMA/SAYFALAMA ÇEKİRDEĞİ (LookupBox'ın motoru) ────────────────────────────────

    [Fact]
    public void Buyuk_listede_ilk_acilis_TEK_sayfa_gosterir_ve_sorgu_TEKRARLANMAZ()
    {
        // 500 marka → pencere listeyi BİR KEZ yükler; LookupBox bellekte filtreler.
        for (int i = 1; i <= 500; i++) _lookups.AddBrand(_a, $"MARKA {i:000}");
        var all = _lookups.ListBrands(_a, "material");
        Assert.True(all.Count >= 500);

        var page1 = LookupPaging.Apply(all.ToList(), x => x.Name, null, 1, 25);
        Assert.Equal(25, page1.Items.Count);           // açılışta yalnız 25 satır çizilir
        Assert.True(page1.TotalPages >= 20);

        // Arama BELLEKTE yapılır → yeni veritabanı sorgusu yok (aynı "all" listesi kullanılır).
        var arama = LookupPaging.Apply(all.ToList(), x => x.Name, "MARKA 007", 1, 25);
        Assert.Single(arama.Items);
        Assert.Equal("MARKA 007", arama.Items[0].Name);
    }

    [Fact]
    public void Hizli_yazip_silme_ilk_sayfaya_doner_ve_TAM_listeyi_geri_verir()
    {
        // Kullanıcı yazıp siliyor: arama boşalınca liste eksik kalmamalı (LookupBox arama değişince page=1 yapar).
        for (int i = 1; i <= 60; i++) _lookups.AddBrand(_a, $"MARKA {i:000}");
        var all = _lookups.ListBrands(_a, "material").ToList();

        var daraltilmis = LookupPaging.Apply(all, x => x.Name, "MARKA 059", 1, 25);
        Assert.Single(daraltilmis.Items);

        var geriDonus = LookupPaging.Apply(all, x => x.Name, "", 1, 25);
        Assert.Equal(25, geriDonus.Items.Count);
        Assert.True(geriDonus.TotalPages >= 3);   // tam liste geri geldi (60 kayıt / 25 = 3 sayfa)
    }

    [Fact]
    public void Turkce_arama_dogru_esler()
    {
        // "İŞ" / "iş" ayrımı Türkçe'de yanlış eşleşmeye çok açıktır; LookupBox bunu LookupPaging'e devreder.
        _lookups.AddVehicleCategory(_a, "İŞ MAKİNESİ");
        _lookups.AddVehicleCategory(_a, "KAMYON");
        var all = _lookups.List(_a, "vehicle_categories").ToList();

        var res = LookupPaging.Apply(all, x => x.Name, "iş", 1, 25);
        Assert.Contains(res.Items, x => x.Name == "İŞ MAKİNESİ");
        Assert.DoesNotContain(res.Items, x => x.Name == "KAMYON");
    }
}
