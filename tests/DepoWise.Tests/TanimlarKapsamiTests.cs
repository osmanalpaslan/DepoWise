using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.5 — "+" İLE EKLENEN HER TANIM, TANIMLAR EKRANINDA DA OLMALI (2026-09-06) ═══
///
/// <b>Kullanıcı isteği.</b> <i>"Her ekranın yeni kayıt formunu detaylı analiz et; hangi alanın yanında
/// '+' ile ekleme butonu var ise bu ekrana eksik alanları ekle. Bu şekilde yeni bir alana '+' butonu
/// eklenirse OTOMATİK bu alana da ekle."</i>
///
/// "Otomatik"in kod karşılığı budur: bir alana "+" eklendiği anda bu test kırılır ve geliştirici
/// tanımı "Tanım Düzenle" ekranına eklemek ZORUNDA kalır. Böylece kural zamanla aşınmaz.
///
/// <b>Bulunan gerçek eksik.</b> Personel formunda "+" ile UNVAN eklenebiliyordu ama unvan Tanımlar
/// ekranında yoktu → yanlış eklenen unvan hiçbir yerden düzeltilemiyordu. Bu turda eklendi.
///
///  TK1 — Web: "+" ile eklenebilen her tanım tablosu Tanım Düzenle ekranında var
///  TK2 — Masaüstü ve web AYNI tanımları listeler (parite)
///  TK3 — Personel unvanı iki platformda da yönetilebilir (bulunan eksiğin regresyonu)
/// </summary>
public class TanimlarKapsamiTests
{
    private static string Kok()
    {
        var dizin = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dizin is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dizin, "src", "DepoWise.Web"))) return dizin;
            dizin = Path.GetDirectoryName(dizin);
        }
        throw new DirectoryNotFoundException("Proje kökü bulunamadı.");
    }

    private static string Oku(params string[] p) => File.ReadAllText(Path.Combine(Kok(), Path.Combine(p)));

    private static string TanimEkrani() => Oku("src", "DepoWise.Web", "Components", "Pages", "Definitions.razor");
    private static string MasaustuTanimlar() => Oku("src", "DepoWise.Desktop", "ViewModels", "SettingsViewModel.cs");

    /// <summary>
    /// Tanımlar ekranında YÖNETİLMESİ beklenmeyen "+" hedefleri — her biri gerekçeli.
    /// Bunlar tanım listesi değil, KENDİ EKRANI olan modüllerdir.
    /// </summary>
    private static readonly HashSet<string> Muaf = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/branches",                  // Şube/Şantiye: admin-kısıtlı ayrı ekran (2026-08-09 kararı)
        "/api/personnel",                 // Personel: tam modül (sürücü/teknisyen/talep eden seçimleri)
        "/api/maintenance/definitions",   // Bakım tanımları: kendi ekranı var (maintenance:defs)
        "/api/materials/subcategories",   // Alt kategori: Tanımlar'da ÖZEL bölüm (SubCategoryEditor)
        "/api/vehicles/models",           // Araç modeli: Tanımlar'da ÖZEL bölüm (VehicleModelEditor)
        "/api/lookups/equipment_types",   // Zaten DefEditor ile listede
        "/api/lookups/suppliers",         // Zaten DefEditor ile listede
    };

    // ══════════════════ TK1 ══════════════════

    /// <summary>
    /// 🔴 Web'de "+" ile ekleme yapan her <c>LookupSelect AddTable="..."</c> hedefinin Tanım Düzenle
    /// ekranında bir düzenleyicisi olmalı. Aksi hâlde kullanıcı ekleyebildiği bir tanımı düzeltemez.
    /// </summary>
    [Fact]
    public void TK1_Artiyla_Eklenen_Her_Tanim_Ekranda_Var()
    {
        var ekran = TanimEkrani();
        var eksikler = new List<string>();

        foreach (var dosya in Directory.EnumerateFiles(Path.Combine(Kok(), "src", "DepoWise.Web", "Components"), "*.razor", SearchOption.AllDirectories))
        {
            var s = File.ReadAllText(dosya);
            foreach (Match m in Regex.Matches(s, @"AddTable=""(?<t>[a-z_]+)"""))
            {
                var tablo = m.Groups["t"].Value;
                if (ekran.Contains($@"Table=""{tablo}""")) continue;
                eksikler.Add($"{Path.GetFileName(dosya)} → {tablo}");
            }
        }

        Assert.True(eksikler.Count == 0,
            "Bu tanımlar \"+\" ile eklenebiliyor ama Tanım Düzenle ekranında YOK "
            + "(kullanıcı ekleyebildiği tanımı düzeltemez):\n" + string.Join("\n", eksikler.Distinct()));
    }

    // ══════════════════ TK2 ══════════════════

    /// <summary>Web ve masaüstü aynı tanım kümesini yönetmeli (CLAUDE.md §4 işlevsel eşitlik).</summary>
    [Theory]
    [InlineData("units", "units")]
    [InlineData("material_categories", "material_categories")]
    [InlineData("brands", "brands")]
    [InlineData("suppliers", "suppliers")]
    [InlineData("vehicle_types", "vehicle_types")]
    [InlineData("vehicle_categories", "vehicle_categories")]
    [InlineData("equipment_types", "equipment_types")]
    public void TK2_Masaustu_Ve_Web_Ayni_Tanimlari_Listeler(string webTablo, string masaustuTablo)
    {
        Assert.Contains($@"Table=""{webTablo}""", TanimEkrani());
        Assert.Contains($@"""{masaustuTablo}""", MasaustuTanimlar());
    }

    // ══════════════════ TK3 ══════════════════

    /// <summary>🔴 Bu turda bulunan gerçek eksiğin regresyonu.</summary>
    [Fact]
    public void TK3_Personel_Unvani_Iki_Platformda_Yonetilebilir()
    {
        Assert.Contains("PersonnelTitleEditor", TanimEkrani());
        Assert.Contains("personnel_titles", MasaustuTanimlar());
        Assert.Contains("Personel — Unvanlar", MasaustuTanimlar());
    }
}
