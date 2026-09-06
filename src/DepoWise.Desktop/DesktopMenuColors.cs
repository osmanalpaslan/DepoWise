using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media;
using DepoWise.Application.Security;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ FAZ 2 (ADR-221, 2026-09-05) — MENÜ RENK AİLESİ → AVALONIA FIRÇASI ═══
///
/// <see cref="MenuPalette"/> "hangi menü hangi AİLEYİ kullanır" sorusunu yanıtlar (platformdan
/// bağımsız, düz string). Bu sınıf yalnız <b>aile → fırça</b> çevirisini yapar; web tarafında
/// karşılığı <c>NavMenu.razor</c> içindeki CSS değişkeni eşlemesidir. <see cref="DesktopIcons"/>
/// ile birebir aynı desen.
///
/// <b>Neden fırçalar burada SABİT tutulmuyor:</b> renkler <c>Themes/Palette.axaml</c> içindedir ve
/// açık/koyu temaya göre değişir. Burada yalnız "hangi aile hangi anahtarı kullanır" bilgisi vardır.
///
/// <b>⚠️ Performans (kullanıcı şartı):</b> menü her açılıp kapandığında kaynak sözlüğü
/// taranmamalıdır. Çözülen fırçalar <b>önbelleğe alınır</b>; menü yeniden kurulduğunda arama
/// yapılmaz. Tema değişiminde <see cref="Temizle"/> çağrılır — aksi hâlde koyu temanın fırçaları
/// açık temada kalırdı.
/// </summary>
public static class DesktopMenuColors
{
    private static readonly Dictionary<string, string> AnahtarlarByFamily = new(StringComparer.Ordinal)
    {
        [MenuPalette.Stock]      = "MenuFamilyStockBrush",
        [MenuPalette.Operations] = "MenuFamilyOperationsBrush",
        [MenuPalette.Finance]    = "MenuFamilyFinanceBrush",
        [MenuPalette.Reports]    = "MenuFamilyReportsBrush",
        [MenuPalette.Corporate]  = "MenuFamilyCorporateBrush",
        [MenuPalette.System]     = "MenuFamilySystemBrush",
        [MenuPalette.Neutral]    = "MenuFamilyNeutralBrush",
    };

    // Önbellek anahtarı TEMAYI da taşır: aynı aile açık ve koyu temada FARKLI fırçadır.
    // (Tema değişiminde Temizle() de çağrılır; bu ikinci güvence.)
    private static readonly Dictionary<string, IBrush?> Onbellek = new(StringComparer.Ordinal);

    /// <summary>Aile → Palette.axaml anahtarı (test/tanı için; eşleşme yoksa null).</summary>
    public static string? KeyForFamily(string family)
        => AnahtarlarByFamily.TryGetValue(family, out var k) ? k : null;

    /// <summary>Tema değişiminde çağrılır — önbellekteki fırçalar eski temaya aittir.</summary>
    public static void Temizle()
    {
        lock (Onbellek) Onbellek.Clear();
    }

    private static IBrush? AileFircasi(string family)
    {
        var app = Avalonia.Application.Current;
        if (app is null) return null;

        // 🔴 GERÇEK KUSUR (2026-09-05, uygulama çalıştırılarak bulundu):
        // İlk sürüm DesktopIcons'taki gibi `TryFindResource(anahtar, out …)` kullanıyordu ve
        // menüde HİÇBİR renk çubuğu çıkmıyordu. Sebep: ikon geometrileri Icons.axaml içinde DÜZ bir
        // sözlükte durur, aile fırçaları ise Palette.axaml'de <ThemeDictionaries> ALTINDADIR.
        // Tema sözlüğündeki bir anahtar, tema varyantı VERİLMEDEN çözülemez → daima null dönüyordu.
        //
        // Derleme geçiyordu, testler geçiyordu (ikisi de metni/eşlemeyi ölçüyor), ama arayüzde
        // hiçbir şey görünmüyordu. Ancak uygulamayı GERÇEKTEN açınca ortaya çıktı.
        var variant = app.ActualThemeVariant;
        var cacheKey = family + "|" + variant;

        lock (Onbellek)
        {
            if (Onbellek.TryGetValue(cacheKey, out var hazir)) return hazir;

            IBrush? firca = null;
            if (AnahtarlarByFamily.TryGetValue(family, out var anahtar) &&
                app.TryGetResource(anahtar, variant, out var res) && res is IBrush b)
                firca = b;

            Onbellek[cacheKey] = firca;   // null da saklanır: kaynak yoksa her seferinde aranmasın
            return firca;
        }
    }

    /// <summary>Üst grup (section) başlığı → aile fırçası.</summary>
    public static IBrush? ForSection(string? sectionTitle) => AileFircasi(MenuPalette.ForSection(sectionTitle));

    /// <summary>Üst menü (group) başlığı → aile fırçası (üst grubundan MİRAS).</summary>
    public static IBrush? ForGroup(string? groupTitle) => AileFircasi(MenuPalette.ForGroup(groupTitle));

    /// <summary>Ekran anahtarı → aile fırçası (üst menüsünden MİRAS; ekranın kendi rengi yoktur).</summary>
    public static IBrush? ForScreenKey(string? screenKey) => AileFircasi(MenuPalette.ForScreen(screenKey));
}
