using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-61 (AR-GE 2026-10-25, F-060 · K-10) — FORM DÜĞME SIRASI: "İptal … Kaydet" (birincil EN SAĞDA) ═══
/// Kullanıcı seçimi K-10 = a. Bir formda Kaydet'in hemen ardından İptal/Vazgeç/Kapat gelirse kas hafızası bozulur.
///  DS1 — masaüstü (.axaml): Kaydet/…Onayla/Uygula düğmesinin ARDINDAN İptal/Vazgeç/Kapat gelmez
///  DS2 — web (.razor): aynı kural
///  DS3 — onay/gerekçe/kolon pencerelerinde Enter (IsDefault) ve Esc (IsCancel) bağlı
/// </summary>
public class DugmeSirasiTests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static IEnumerable<string> Dosyalar(string rel, string desen)
        => Directory.EnumerateFiles(Path.Combine(Kok(), rel), desen, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>Dosyadaki düğme metinlerini sırayla çıkarır; "Kaydet…" hemen ardından vazgeçme düğmesi gelirse bulgu.</summary>
    private static List<string> TersSira(string kok, string desen, Regex dugme)
    {
        var bulgu = new List<string>();
        foreach (var f in Dosyalar(kok, desen))
        {
            var s = File.ReadAllText(f);
            var d = dugme.Matches(s).Select(m => (Metin: m.Groups[1].Value.Trim(), m.Index)).ToList();
            for (int i = 0; i + 1 < d.Count; i++)
                // M-64 ara bulgu (2026-10-25): yalnız Kaydet aranıyordu; "İptali Onayla / Ters Kaydı Onayla / Reddi Onayla" gibi onay
                // düğmeleri Vazgeç'in solunda kalmıştı → onay/uygula/gönder/tamamla da birincil sayılır. ("Sil" sayılmaz: tek başına
                // EN SOLDA, ayraçla ayrılmış durur — web diyaloglarındaki bilinçli düzen.)
                if (Regex.IsMatch(d[i].Metin, "^(Kaydet|Güncelle|Oluştur|Onayla|Uygula|Gönder|Tamamla|Evet)|Onayla$")
                    && Regex.IsMatch(d[i + 1].Metin, "^(İptal|Vazgeç|Kapat|Hayır)$")
                    && d[i + 1].Index - d[i].Index < 500)
                    bulgu.Add($"{Path.GetFileName(f)}:{s[..d[i].Index].Count(c => c == '\n') + 1} «{d[i].Metin}» → «{d[i + 1].Metin}»");
        }
        return bulgu;
    }

    [Fact]
    public void DS1_Masaustu_Iptal_Kaydet_Sirasi()
    {
        var b = TersSira("src/DepoWise.Desktop", "*.axaml", new Regex(@"<Button\b[^>]*?Content=""([^""]+)"""));
        Assert.True(b.Count == 0, "Kaydet'ten SONRA vazgeçme düğmesi (K-10: İptal … Kaydet):\n  " + string.Join("\n  ", b));
    }

    [Fact]
    public void DS2_Web_Iptal_Kaydet_Sirasi()
    {
        var b = TersSira("src/DepoWise.Web/Components", "*.razor", new Regex(@">\s*([^<>@]{1,40}?)\s*</MudButton>"));
        Assert.True(b.Count == 0, "Kaydet'ten SONRA vazgeçme düğmesi (K-10: İptal … Kaydet):\n  " + string.Join("\n  ", b));
    }

    [Fact]
    public void DS3_Diyaloglarda_Enter_Ve_Esc()
    {
        foreach (var ad in new[] { "ConfirmWindow", "ReasonWindow", "ColumnPickerWindow" })
        {
            var s = File.ReadAllText(Path.Combine(Kok(), "src/DepoWise.Desktop/Views", ad + ".axaml"));
            Assert.Contains("IsDefault=\"True\"", s);
            Assert.Contains("IsCancel=\"True\"", s);
        }
    }
}
