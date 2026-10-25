using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-61 (AR-GE 2026-10-25, F-060 · K-10) — FORM DÜĞME SIRASI: "İptal … Kaydet" (birincil EN SAĞDA) ═══
/// Kullanıcı seçimi K-10 = a. Bir formda Kaydet'in hemen ardından İptal/Vazgeç/Kapat gelirse kas hafızası bozulur.
///  DS1 — masaüstü (.axaml): Kaydet/…Onayla/Uygula düğmesinin ARDINDAN İptal/Vazgeç/Kapat gelmez
///  DS2 — web (.razor): aynı kural
///  DS3 — onay/gerekçe/kolon pencerelerinde Enter (IsDefault) ve Esc (IsCancel) bağlı
///  DS4 — karar çifti "Reddet … Onayla" (iki ortam)
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
    // B8 denetimi (2026-10-25): "Hareketi Kaydet / Değişiklikleri Kaydet" (eylem sonda) ve "Değişiklikleri İptal Et" de sayılır.
    private const string Birincil = "^(Kaydet|Güncelle|Oluştur|Onayla|Uygula|Gönder|Tamamla|Evet)|(Onayla|Kaydet)$";
    // Yalın "İptal Et" SAYILMAZ: çoğu ekranda kaydı iptal eden bir İŞLEMDİR (fatura/bakım iptali), formdan vazgeçme değil.
    private const string Vazgecme = "^(İptal|Vazgeç|Kapat|Hayır|Değişiklikleri İptal Et)$";
    private static readonly Regex MasaustuDugme = new(@"<Button\b[^>]*?Content=""([^""]+)""");
    // B8 denetimi: metni "@(_saving ? "Kaydediliyor…" : "Kaydet")" biçiminde yazılan düğmeler GÖRÜLMÜYORDU (Cari formu ters sıradaydı) →
    // ikinci seçenek: @(...) içindeki SON tırnaklı metin (meşgul olmayan hâlin yazısı).
    private static readonly Regex WebDugme = new(@">\s*(?:@\([^<]*?""([^""<]{1,40})""\s*\)|([^<>@]{1,40}?))\s*</MudButton>");

    private static List<string> TersSira(string kok, string desen, Regex dugme, string once = Birincil, string sonra = Vazgecme)
    {
        var bulgu = new List<string>();
        foreach (var f in Dosyalar(kok, desen))
        {
            var s = File.ReadAllText(f);
            var d = dugme.Matches(s).Select(m => (Metin: (m.Groups[1].Success ? m.Groups[1] : m.Groups[2]).Value.Trim(), m.Index)).ToList();
            for (int i = 0; i + 1 < d.Count; i++)
                // M-64 ara bulgu (2026-10-25): yalnız Kaydet aranıyordu; "İptali Onayla / Ters Kaydı Onayla / Reddi Onayla" gibi onay
                // düğmeleri Vazgeç'in solunda kalmıştı → onay/uygula/gönder/tamamla da birincil sayılır. ("Sil" sayılmaz: tek başına
                // EN SOLDA, ayraçla ayrılmış durur — web diyaloglarındaki bilinçli düzen.)
                if (Regex.IsMatch(d[i].Metin, once) && Regex.IsMatch(d[i + 1].Metin, sonra)
                    && d[i + 1].Index - d[i].Index < 500)
                    bulgu.Add($"{Path.GetFileName(f)}:{s[..d[i].Index].Count(c => c == '\n') + 1} «{d[i].Metin}» → «{d[i + 1].Metin}»");
        }
        return bulgu;
    }

    [Fact]
    public void DS1_Masaustu_Iptal_Kaydet_Sirasi()
    {
        var b = TersSira("src/DepoWise.Desktop", "*.axaml", MasaustuDugme);
        Assert.True(b.Count == 0, "Kaydet'ten SONRA vazgeçme düğmesi (K-10: İptal … Kaydet):\n  " + string.Join("\n  ", b));
    }

    [Fact]
    public void DS2_Web_Iptal_Kaydet_Sirasi()
    {
        var b = TersSira("src/DepoWise.Web/Components", "*.razor", WebDugme);
        Assert.True(b.Count == 0, "Kaydet'ten SONRA vazgeçme düğmesi (K-10: İptal … Kaydet):\n  " + string.Join("\n  ", b));
    }

    /// <summary>DS4 — ⭐ M-64 ara bulgu: karar çiftinde "Reddet … Onayla" (birincil EN SAĞDA); "Onayla"nın hemen ardından
    /// "Reddet" gelmez — iki ortamda (Talepler, Onaylamalarım ters sıradaydı).</summary>
    [Fact]
    public void DS4_Reddet_Onayla_Sirasi()
    {
        const string onay = "Onayla$", ret = "Reddet$";
        // Yalnız AYNI çift: "Onayla"/"Reddet", "İşaretlileri Onayla"/"İşaretlileri Reddet" (tekli Onayla ile toplu Reddet ayrı gruptur).
        static string Onek(string m) => Regex.Replace(m, "(Onayla|Reddet)$", "").Trim();
        var b = TersSira("src/DepoWise.Desktop", "*.axaml", MasaustuDugme, onay, ret)
            .Concat(TersSira("src/DepoWise.Web/Components", "*.razor", WebDugme, onay, ret))
            .Where(x => Regex.Match(x, "«(.*)» → «(.*)»") is { Success: true } m && Onek(m.Groups[1].Value) == Onek(m.Groups[2].Value))
            .ToList();
        Assert.True(b.Count == 0, "Onayla'dan SONRA Reddet (K-10: Reddet … Onayla):\n  " + string.Join("\n  ", b));
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
