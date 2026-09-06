using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.2 — DÜZENLE / SİL / İPTAL ONAYLARI (2026-09-06) ═══
///
/// <b>Kullanıcı isteği.</b> <i>"Bütün düzenleme ve silme butonlarında işlem yapılmadan önce ...
/// 'Kaydı düzenlemek istediğinize emin misiniz?', silme ise 'Kaydı silmek istediğinize emin
/// misiniz?' tarzında uyarılar vermeli. ... bazı butonlarda belki buna benzer kontroller vardır,
/// o yüzden aynı koşula sahip olan butonları pas geç."</i>
///
/// Bu test, KAYIT üzerinde işlem yapan komutların onaysız kalmasını engeller. Avalonia/Blazor için
/// başsız UI testi olmadığından KAYNAK SÖZLEŞMESİ sınanır (projede kanıtlanmış desen).
///
/// <b>MUAF LİSTESİ — neden var:</b> bazı "Remove/Cancel" komutları KAYDA değil, henüz kaydedilmemiş
/// FORMA dokunur (satır çıkarma, "+" kutusunu kapatma, sayfa/panel kapatma). Oraya onay koymak
/// kullanıcıyı yorar ve kullanıcının "pas geç" şartına aykırıdır. Muafiyetler tek tek gerekçelidir;
/// listeye körlemesine ekleme yapılmamalıdır.
///
///  IO1 — Masaüstü: kayıt işlemi yapan komutlar onaysız kalamaz
///  IO2 — Web: kayıt işlemi yapan işleyiciler onaysız kalamaz
///  IO3 — Standart metinler iki platformda AYNI
/// </summary>
public class IslemOnaylariTests
{
    private static string Kok()
    {
        var dizin = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dizin is not null; i++)
        {
            if (Directory.Exists(Path.Combine(dizin, "src", "DepoWise.Desktop"))) return dizin;
            dizin = Path.GetDirectoryName(dizin);
        }
        throw new DirectoryNotFoundException("Proje kökü bulunamadı.");
    }

    /// <summary>KAYDA değil FORMA dokunan komutlar — gerekçesiyle muaf.</summary>
    private static readonly HashSet<string> Muaf = new(StringComparer.Ordinal)
    {
        // Henüz kaydedilmemiş form satırını çıkarır (hiçbir şey silinmez):
        "RemoveLine", "RemoveMntLine", "RemoveExitLine", "RemoveCountLine", "RemoveItem",
        "RemoveEquivalentPick", "RemovePhoto",
        // Satır içi "+" ekleme kutusunu kapatır:
        "CancelAddSub", "CancelAddTechnician", "CancelAddMntSub", "CancelAddCategory",
        "CancelAddSubCategory", "CancelAddModel", "CancelAddDriver", "CancelAddPersonnel",
        "CancelAddTitle", "CancelAddType", "CancelAddCat", "CancelAddBrand", "CancelAddSupplier",
        "CancelAddUnit",
        // "Yeni kayıt" formunu kapatır — hiçbir kayda dokunmaz (11 ekranda aynı desen):
        "CancelAdd",
        // Düzenleme/panel kapatma (veri işlemi değil):
        "CancelEdit", "CancelMetaEdit", "CancelEntry", "CancelForm", "CancelDialog", "CancelNew", "CancelPick",
        // Onayı ORTAK yardımcıda olan yakıt iptalleri (CancelFuel → Dialog.AskReason):
        "CancelDist", "CancelDepot",
        // Navigasyon (düzenleme değil) — kullanıcı zaten "Tam Düzenleme" düğmesine bilinçli bastı:
        "EditNav", "EditNavNewTab",
    };

    private static readonly Regex Duzenle = new(@"^(BeginEdit|Edit|Duzenle|StartEdit|OpenEdit|EditRow|EditParty|EditAccount|BeginEditRequest|BeginEditDistribution|BeginEditDist|BeginEditDef)\w*$", RegexOptions.IgnoreCase);
    private static readonly Regex Sil = new(@"^(Delete|Sil|Remove)\w*$", RegexOptions.IgnoreCase);
    private static readonly Regex Iptal = new(@"^(Cancel|Iptal)\w*$", RegexOptions.IgnoreCase);

    private static List<string> OnaysizBul(string kokDizin, string uzanti, Regex onayDeseni)
    {
        var imza = new Regex(@"private\s+(?:async\s+)?(?:System\.Threading\.Tasks\.)?(?:Task|void)\s+(\w+)\s*\(", RegexOptions.Compiled);
        var bulgular = new List<string>();

        foreach (var dosya in Directory.EnumerateFiles(kokDizin, uzanti, SearchOption.AllDirectories))
        {
            if (dosya.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
             || dosya.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;

            var s = File.ReadAllText(dosya);
            foreach (Match m in imza.Matches(s))
            {
                var ad = m.Groups[1].Value;
                if (Muaf.Contains(ad)) continue;
                if (!Duzenle.IsMatch(ad) && !Sil.IsMatch(ad) && !Iptal.IsMatch(ad)) continue;

                var acilis = s.IndexOf('{', m.Index + m.Length);
                if (acilis < 0) continue;
                int derinlik = 0, son = acilis;
                for (; son < s.Length; son++)
                {
                    if (s[son] == '{') derinlik++;
                    else if (s[son] == '}') { derinlik--; if (derinlik == 0) break; }
                }
                var govde = s.Substring(acilis, Math.Min(son + 1, s.Length) - acilis);
                if (onayDeseni.IsMatch(govde)) continue;

                bulgular.Add($"{Path.GetFileName(dosya)}::{ad}");
            }
        }
        return bulgular;
    }

    // ══════════════════ IO1 — MASAÜSTÜ ══════════════════

    [Fact]
    public void IO1_Masaustu_Kayit_Islemleri_Onaysiz_Kalamaz()
    {
        var eksik = OnaysizBul(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ViewModels"), "*.cs",
            new Regex(@"ConfirmService\.(Ask|Confirm)"));

        Assert.True(eksik.Count == 0,
            "Onay sorulmayan kayıt işlemi(leri) var — ConfirmService.ConfirmEdit/Delete/CancelAsync kullanın "
            + "(form içi işlemse Muaf listesine GEREKÇESİYLE ekleyin):\n" + string.Join("\n", eksik));
    }

    // ══════════════════ IO2 — WEB ══════════════════

    [Fact]
    public void IO2_Web_Kayit_Islemleri_Onaysiz_Kalamaz()
    {
        var eksik = OnaysizBul(Path.Combine(Kok(), "src", "DepoWise.Web", "Components", "Pages"), "*.razor",
            new Regex(@"Dialog\.(Confirm|AskReason)"));

        Assert.True(eksik.Count == 0,
            "Onay sorulmayan kayıt işlemi(leri) var — Dialog.ConfirmEdit/ConfirmDelete/ConfirmCancelRecord kullanın "
            + "(form içi işlemse Muaf listesine GEREKÇESİYLE ekleyin):\n" + string.Join("\n", eksik));
    }

    // ══════════════════ IO3 — İKİ PLATFORM AYNI METİN ══════════════════

    /// <summary>Kullanıcı iki platformda aynı cümleyi görmeli (CLAUDE.md §4 işlevsel eşitlik).</summary>
    [Fact]
    public void IO3_Standart_Metinler_Iki_Platformda_Ayni()
    {
        var masaustu = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ConfirmService.cs"));
        var web = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Web", "Services", "DialogExtensions.cs"));

        foreach (var cumle in new[]
        {
            "Kaydı düzenlemek istediğinize emin misiniz?",
            "Kaydı silmek istediğinize emin misiniz?",
            "Kaydı iptal etmek istediğinize emin misiniz?",
        })
        {
            Assert.Contains(cumle, masaustu);
            Assert.Contains(cumle, web);
        }
    }
}
