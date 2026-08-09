using DepoWise.Application.Ui;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// KOLON KATALOĞU (İş #10, 2026-08-09).
///
/// Katalog artık TEK dosyadır: <c>DepoWise.Application/Ui/ListColumns.cs</c>. Web projesi bu dosyayı
/// <c>Compile Include</c> ile PAYLAŞIR (proje referansı değil) → eskiden elle senkron tutulan ayna
/// kopya kaldırıldı. Bu testler kataloğun kendi iç tutarlılığını ve <c>Sanitize</c> davranışını korur.
///
/// <b>Kullanıcı tercihi ≠ sistem kataloğu.</b> "Bu kolon sistemde VAR" bilgisi burada (kod);
/// "bu KULLANICI bu kolonu görmek istiyor" bilgisi <c>user_list_preferences</c> tablosunda.
/// <c>Sanitize</c> ikisini birleştiren tek yerdir: tercihi kataloğa göre süzer.
/// </summary>
public class ListColumnCatalogTests
{
    public static TheoryData<string, IReadOnlyList<ListColumn>, IReadOnlyList<string>> Catalogs => new()
    {
        { "materials", MaterialListColumns.All, MaterialListColumns.DefaultVisible },
        { "vehicles", VehicleListColumns.All, VehicleListColumns.DefaultVisible },
        { "daily_activity", DailyActivityListColumns.All, DailyActivityListColumns.DefaultVisible },
    };

    [Theory]
    [MemberData(nameof(Catalogs))]
    public void Katalog_tutarli(string listKey, IReadOnlyList<ListColumn> all, IReadOnlyList<string> defaults)
    {
        Assert.NotEmpty(all);

        // Anahtar TEKRARI olamaz: kolon seçici aynı kolonu iki kez gösterir, tercih kaydı bozulur.
        var keys = all.Select(c => c.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());

        // Anahtar ve etiket boş olamaz (boş etiket = kolon seçicide görünmez kutucuk).
        Assert.All(all, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Key), $"{listKey}: boş anahtar");
            Assert.False(string.IsNullOrWhiteSpace(c.Label), $"{listKey}: '{c.Key}' için boş etiket");
        });

        // Varsayılan görünür kolonların HEPSİ katalogda olmalı; yoksa ilk açılışta hayalet kolon çizilir.
        Assert.NotEmpty(defaults);
        Assert.All(defaults, k => Assert.Contains(k, keys, StringComparer.Ordinal));
    }

    // ── Sanitize: kullanıcı tercihi × sistem kataloğu ─────────────────────────────────────

    [Fact]
    public void Sanitize_KATALOGDA_OLMAYAN_anahtari_atar()
    {
        // Gerçek senaryo: bir kolon sürüm yükseltmesinde kaldırıldı/yeniden adlandırıldı ama
        // kullanıcının kaydında duruyor → başlığı ham anahtar, içi boş bir "hayalet kolon" çizilirdi.
        var sonuc = MaterialListColumns.Sanitize(new[]
        {
            MaterialListColumns.Code, "artik_olmayan_kolon", MaterialListColumns.Name,
        });

        Assert.Equal(new[] { MaterialListColumns.Code, MaterialListColumns.Name }, sonuc);
    }

    [Fact]
    public void Sanitize_HICBIRI_gecerli_degilse_VARSAYILANA_doner()
    {
        // Tamamı geçersizse boş tablo göstermek yerine varsayılana düşülür (kullanıcı kilitlenmez).
        var sonuc = VehicleListColumns.Sanitize(new[] { "yok1", "yok2" });
        Assert.Equal(VehicleListColumns.DefaultVisible, sonuc);
    }

    [Fact]
    public void Sanitize_tercih_YOKSA_varsayilani_verir()
    {
        Assert.Equal(DailyActivityListColumns.DefaultVisible, DailyActivityListColumns.Sanitize(null));
    }

    [Fact]
    public void Sanitize_KATALOG_SIRASINI_korur()
    {
        // Kolon seçici zaten katalog sırasında döndürür (ColumnPickerDialog: Available.Where(...)).
        // Sanitize de aynı sırayı verir → mevcut sıralama davranışı DEĞİŞMEZ (regresyon yok).
        var karisik = new[] { MaterialListColumns.Stock, MaterialListColumns.Code, MaterialListColumns.Name };
        var sonuc = MaterialListColumns.Sanitize(karisik);

        var beklenen = MaterialListColumns.All
            .Where(c => karisik.Contains(c.Key)).Select(c => c.Key).ToList();
        Assert.Equal(beklenen, sonuc);
    }

    [Fact]
    public void Sanitize_gecerli_secimi_AYNEN_korur()
    {
        // En sık durum: kullanıcının seçimi tamamen geçerli → hiçbir şey değişmemeli.
        var secim = new[] { MaterialListColumns.Code, MaterialListColumns.Name, MaterialListColumns.Stock };
        Assert.Equal(secim, MaterialListColumns.Sanitize(secim));
    }
}
