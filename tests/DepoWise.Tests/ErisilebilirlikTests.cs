using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-62 (AR-GE 2026-10-25, F-096) — EKRAN OKUYUCU ETİKETİ: YALNIZ SİMGELİ DÜĞMELER ADSIZ KALMAZ ═══
/// "×", "+", "‹", çöp kutusu simgesi gibi düğmeler ekran okuyucuda "düğme" diye okunuyordu (ToolTip ad değildir).
///  ER1 — masaüstü: simgeli her Button'da AutomationProperties.Name
///  ER2 — web: her MudIconButton'da aria-label (ya da Title)
/// İstisna: ChatDock.razor — kullanıcının commit'lenmemiş sohbet çalışması (dokunulmaz); o iş birleşince istisna kaldırılır.
/// </summary>
public class ErisilebilirlikTests
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

    private static int Satir(string s, int i) => s[..i].Count(c => c == '\n') + 1;

    [Fact]
    public void ER1_Masaustu_Simge_Dugmeleri_Adli()
    {
        var dugme = new Regex(@"<Button\b([^>]*?)(/>|>([\s\S]*?)</Button>)");
        var harfRakam = new Regex(@"^[A-Za-zÇĞİÖŞÜçğıöşü0-9]+$");
        var eksik = new List<string>();
        foreach (var f in Dosyalar("src/DepoWise.Desktop", "*.axaml"))
        {
            var s = File.ReadAllText(f);
            foreach (Match m in dugme.Matches(s))
            {
                var a = m.Groups[1].Value; var ic = m.Groups[3].Value;
                var cont = Regex.Match(a, @"\bContent=""([^""]*)""");
                var simge = Regex.IsMatch(a, @"Classes=""[^""]*\bIcon\b")
                    || (cont.Success && cont.Groups[1].Value.Length <= 2 && !harfRakam.IsMatch(cont.Groups[1].Value))
                    || (!cont.Success && Regex.IsMatch(ic, @"<(PathIcon|Path|Image)\b") && !Regex.IsMatch(ic, @"<(TextBlock|SelectableTextBlock)\b"));
                if (simge && !a.Contains("AutomationProperties.Name"))
                    eksik.Add($"{Path.GetFileName(f)}:{Satir(s, m.Index)}");
            }
        }
        Assert.True(eksik.Count == 0, "Ekran okuyucu adı olmayan simge düğmesi (AutomationProperties.Name ekleyin):\n  " + string.Join("\n  ", eksik));
    }

    [Fact]
    public void ER2_Web_Simge_Dugmeleri_Adli()
    {
        var eksik = new List<string>();
        foreach (var f in Dosyalar("src/DepoWise.Web/Components", "*.razor"))
        {
            if (Path.GetFileName(f) == "ChatDock.razor") continue;   // kullanıcının commit'lenmemiş işi — bkz. sınıf açıklaması
            var s = File.ReadAllText(f);
            foreach (Match m in Regex.Matches(s, @"<MudIconButton\b"))
            {
                // etiket sonu: Razor @(...) içindeki '>' atlanır
                int i = m.Index + 14, derin = 0; bool tirnak = false;
                for (; i < s.Length; i++)
                {
                    var c = s[i];
                    if (c == '"') tirnak = !tirnak;
                    else if (c == '(') derin++;
                    else if (c == ')') derin--;
                    else if (c == '>' && derin <= 0 && !tirnak) break;
                }
                var attr = s[m.Index..i];
                if (!Regex.IsMatch(attr, @"aria-label=|\bTitle=|\btitle="))
                    eksik.Add($"{Path.GetFileName(f)}:{Satir(s, m.Index)}");
            }
        }
        Assert.True(eksik.Count == 0, "Ekran okuyucu adı olmayan MudIconButton (aria-label ekleyin):\n  " + string.Join("\n  ", eksik));
    }
}
