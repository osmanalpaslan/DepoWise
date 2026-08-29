using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Settings;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ ADR-182 · S2 (PK-V1=A) — "YAKITI VEREN" SON SEÇİMİ HATIRLANIR ═══
///
/// Kullanıcı isteği: Yakıt Dağıtımı'nda en son seçilen "Yakıtı Veren" kişi bir sonraki kayıtta
/// otomatik gelsin; kullanıcı değiştirirse yeni seçim geçerli olsun.
/// ⚠️ <b>"Yakıtı Alan" bu davranışın DIŞINDADIR</b> — her işlemde değişken kalır (kullanıcı kuralı).
///
/// <b>MIGRATION YOK:</b> değer mevcut <c>user_list_preferences</c> tablosunda, ayrılmış bir anahtar
/// (<see cref="UserPrefKeys.FuelGiver"/>) altında saklanır. Masaüstü yerel SQLite'a, web mevcut
/// <c>/api/me/list-columns/{key}</c> ucuyla sunucuya AYNI biçimde yazar.
/// </summary>
public class YakitVerenTercihTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly UserListPreferenceService _prefs;
    private readonly SessionContext _ali, _veli;

    public YakitVerenTercihTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_yktveren_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _prefs = new UserListPreferenceService(_factory, new SabitSaat());
        _ali = new SessionContext("u-ali", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        _veli = new SessionContext("u-veli", "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);
    }

    private sealed class SabitSaat : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    [Fact]
    public void YVT1_Hic_Kaydedilmemisse_Null_Doner()
        => Assert.Null(_prefs.GetLastChoice(_ali, UserPrefKeys.FuelGiver));   // ilk açılış → ön-seçim yok

    [Fact]
    public void YVT2_Son_Secim_Geri_Okunur()
    {
        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, "personel-1");
        Assert.Equal("personel-1", _prefs.GetLastChoice(_ali, UserPrefKeys.FuelGiver));
    }

    [Fact]
    public void YVT3_Yeni_Secim_Oncekini_Ezer()
    {
        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, "personel-1");
        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, "personel-2");   // kullanıcı elle değiştirdi
        Assert.Equal("personel-2", _prefs.GetLastChoice(_ali, UserPrefKeys.FuelGiver));
    }

    [Fact]
    public void YVT4_Bos_Deger_Tercihi_Temizler()
    {
        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, "personel-1");
        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, null);
        Assert.Null(_prefs.GetLastChoice(_ali, UserPrefKeys.FuelGiver));
    }

    /// <summary>⭐ Tercih KİŞİSELDİR — başka kullanıcıya taşmaz (anahtar user_id, oturumdan gelir).</summary>
    [Fact]
    public void YVT5_Kullanicilar_Arasinda_Tasmaz()
    {
        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, "ali-nin-secimi");
        Assert.Null(_prefs.GetLastChoice(_veli, UserPrefKeys.FuelGiver));

        _prefs.SaveLastChoice(_veli, UserPrefKeys.FuelGiver, "veli-nin-secimi");
        Assert.Equal("ali-nin-secimi", _prefs.GetLastChoice(_ali, UserPrefKeys.FuelGiver));    // Ali etkilenmedi
        Assert.Equal("veli-nin-secimi", _prefs.GetLastChoice(_veli, UserPrefKeys.FuelGiver));
    }

    /// <summary>⭐ Ayrılmış anahtar: hiçbir LİSTE EKRANININ kolon tercihiyle çakışmaz (ikisi bir arada yaşar).</summary>
    [Fact]
    public void YVT6_Liste_Ekrani_Kolon_Tercihiyle_Cakismaz()
    {
        _prefs.SaveColumns(_ali, "materials", new[] { "code", "name", "stock" });
        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, "personel-1");

        Assert.Equal(new[] { "code", "name", "stock" }, _prefs.GetColumns(_ali, "materials"));   // kolonlar bozulmadı
        Assert.Equal("personel-1", _prefs.GetLastChoice(_ali, UserPrefKeys.FuelGiver));
        // Ayrışma ANAHTAR bazlıdır (aynı tabloyu paylaşırlar): tercih kaydı tek elemanlıdır ve
        // liste ekranının kolon kaydına karışmaz.
        Assert.Single(_prefs.GetColumns(_ali, UserPrefKeys.FuelGiver)!);
    }

    /// <summary>⭐ WEB PARİTESİ: web, mevcut <c>/api/me/list-columns/{key}</c> ucunu (SaveColumns) kullanır;
    /// masaüstünün okuduğu biçimle BİREBİR aynı kaydı üretir — iki platform aynı tercihi görür.</summary>
    [Fact]
    public void YVT7_Webin_Yazdigi_Bicimi_Masaustu_Okur()
    {
        _prefs.SaveColumns(_ali, UserPrefKeys.FuelGiver, new[] { "personel-9" });   // web yolu
        Assert.Equal("personel-9", _prefs.GetLastChoice(_ali, UserPrefKeys.FuelGiver));   // masaüstü yolu

        _prefs.SaveLastChoice(_ali, UserPrefKeys.FuelGiver, "personel-7");          // masaüstü yolu
        Assert.Equal(new[] { "personel-7" }, _prefs.GetColumns(_ali, UserPrefKeys.FuelGiver));   // web yolu
    }

    /// <summary>⭐ "Yakıtı Alan" HATIRLANMAZ — katalogda anahtarı yoktur ve iki platform da onu kaydetmez.</summary>
    [Fact]
    public void YVT8_YakitiAlan_Hatirlanmaz()
    {
        var kok = new DirectoryInfo(AppContext.BaseDirectory);
        while (kok is not null && !File.Exists(Path.Combine(kok.FullName, "DepoWise.sln"))) kok = kok.Parent;
        Assert.NotNull(kok);

        var vm = File.ReadAllText(Path.Combine(kok!.FullName, "src", "DepoWise.Desktop", "ViewModels", "FuelViewModel.cs"));
        var web = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Web", "Components", "Pages", "Fuel.razor"));

        // İki platform da AYNI paylaşımlı anahtarı kullanır (elle yazılmış metin sürüklenmesi olamaz).
        Assert.Contains("UserPrefKeys.FuelGiver", vm, StringComparison.Ordinal);
        Assert.Contains("UserPrefKeys.FuelGiver", web, StringComparison.Ordinal);

        // "Yakıtı Alan" hiçbir tercih çağrısına girmez.
        Assert.DoesNotContain("SaveLastChoice(_session, UserPrefKeys.FuelGiver, DistRecipient", vm, StringComparison.Ordinal);
        Assert.DoesNotContain("SonVereniKaydet(_recipient", web, StringComparison.Ordinal);

        // Katalogda "alan" için anahtar TANIMLI DEĞİL — ileride yanlışlıkla eklenirse bu kilit düşer.
        var katalog = File.ReadAllText(Path.Combine(kok.FullName, "src", "DepoWise.Application", "Ui", "UserPrefKeys.cs"));
        Assert.DoesNotContain("FuelRecipient", katalog, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(katalog, @"public const string"));
    }

    public void Dispose()
    {
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(_dbPath + ext); } catch { }
        }
    }
}
