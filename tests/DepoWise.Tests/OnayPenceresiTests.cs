using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 4.13 — ONAY/BİLGİ PENCERELERİNİN BUTONLARI (2026-09-06) ═══
///
/// <b>Bulunan hata (kullanıcı bildirdi).</b> Manuel senkrondan sonra senkron alanına tıklanınca açılan
/// pencerede <b>iki adet "Tamam"</b> butonu çıkıyordu. Kök neden: bilgi amaçlı pencereler
/// <c>AskAsync(..., okText: "Tamam", cancelText: "Tamam")</c> ile açılıyordu; iki buton da aynı metni
/// taşıyor ve kullanıcıya hangisinin ne yaptığı anlaşılmıyordu. Aynı hata <b>beş</b> çağrıda vardı.
///
/// <b>Çözüm.</b> Tek butonlu <c>ConfirmService.InfoAsync</c> eklendi (cancel butonu hiç çizilmez).
/// Bu test, hatanın koda geri sızmasını engeller — Avalonia pencereleri için başsız UI testi yoktur,
/// bu yüzden KAYNAK SÖZLEŞMESİ sınanır (aynı desen MasaustuTabloHizaTests'te de kullanılıyor).
///
///  OP1 — Hiçbir çağrıda ok ve cancel metni AYNI olamaz
///  OP2 — Tek butonlu bilgi penceresi yardımcısı vardır ve cancel'ı boş geçer
///  OP3 — Onay penceresi boş cancel metninde butonu GİZLER (altyapı sözleşmesi)
/// </summary>
public class OnayPenceresiTests
{
    private static string Kok()
    {
        var dizin = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dizin is not null; i++)
        {
            if (File.Exists(Path.Combine(dizin, "src", "DepoWise.Desktop", "ConfirmService.cs"))) return dizin;
            dizin = Path.GetDirectoryName(dizin);
        }
        throw new DirectoryNotFoundException("Proje kökü bulunamadı.");
    }

    private static IEnumerable<string> MasaustuKaynaklari()
        => Directory.EnumerateFiles(Path.Combine(Kok(), "src", "DepoWise.Desktop"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    // ══════════════════ OP1 ══════════════════

    /// <summary>
    /// 🔴 REGRESYON: <c>AskAsync(..., "X", "X")</c> — iki butona aynı metin. Kullanıcının bildirdiği
    /// "2 adet Tamam" hatasının tam kaynağı budur.
    /// </summary>
    [Fact]
    public void OP1_Onay_Penceresinde_Iki_Buton_Ayni_Metni_Tasiyamaz()
    {
        // "..." , "..." biçiminde art arda gelen İKİ AYNI metin (ok + cancel) yakalanır.
        var desen = new Regex("\"(?<m>[^\"]{1,40})\"\\s*,\\s*\"\\k<m>\"", RegexOptions.Compiled);
        var bulgular = new List<string>();

        foreach (var dosya in MasaustuKaynaklari())
        {
            var metin = File.ReadAllText(dosya);
            foreach (var satir in metin.Split('\n'))
            {
                if (!satir.Contains("AskAsync(")) continue;
                // Yorum satırları hariç: eski hatayı ANLATAN belgeler örnek metin içerir (kod değil).
                var kirpik = satir.TrimStart();
                if (kirpik.StartsWith("//") || kirpik.StartsWith("*")) continue;
                var m = desen.Match(satir);
                if (m.Success) bulgular.Add($"{Path.GetFileName(dosya)}: {satir.Trim()}");
            }
        }

        Assert.True(bulgular.Count == 0,
            "Onay penceresinde iki buton aynı metni taşıyor (bilgi penceresi için ConfirmService.InfoAsync kullanın):\n"
            + string.Join("\n", bulgular));
    }

    // ══════════════════ OP2 ══════════════════

    [Fact]
    public void OP2_Tek_Butonlu_Bilgi_Penceresi_Yardimcisi_Var()
    {
        var kaynak = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "ConfirmService.cs"));

        Assert.Contains("public static Task InfoAsync(", kaynak);
        Assert.Contains("AskAsync(message, title, okText, \"\", danger)", kaynak);   // cancel BOŞ geçilir
    }

    // ══════════════════ OP3 ══════════════════

    /// <summary>Altyapı sözleşmesi: cancel metni boşsa buton çizilmez. Bu bozulursa InfoAsync
    /// sessizce boş metinli ikinci bir buton göstermeye başlar.</summary>
    [Fact]
    public void OP3_Bos_Cancel_Metninde_Buton_Gizlenir()
    {
        var kaynak = File.ReadAllText(Path.Combine(Kok(), "src", "DepoWise.Desktop", "Views", "ConfirmWindow.axaml.cs"));

        Assert.Contains("cancel.IsVisible = !string.IsNullOrEmpty(cancelText);", kaynak);
    }
}
