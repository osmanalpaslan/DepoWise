using System.Text.RegularExpressions;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ WEB-01 · BLAZOR DEVRESİNİ DÜŞÜREN KORUMASIZ İLK YÜKLEME ═══
///
/// <b>Sorunun kendisi:</b> Blazor Server'da <c>OnInitializedAsync</c> içinde YAKALANMAYAN bir istisna
/// yalnız o ekranı bozmaz — <b>kullanıcının devresini (SignalR bağlantısını) tamamen düşürür</b>.
/// Kullanıcı bembeyaz bir ekranla ve "bağlantı kesildi" mesajıyla kalır; sayfayı yenilemeden hiçbir
/// şey yapamaz. Sunucudan 401 (oturum düştü) ya da 500 (ör. disk doldu — R30) dönmesi bunu tetikler.
///
/// <b>Geçmiş:</b> aynı hata iki ayrı turda yaşandı — YET-C4'te dört yetki/kullanıcı ekranında,
/// 2026-08-25 denetiminde ise lokasyon önbelleğini kullanan üç stok ekranında (Sayım, Dağıtım,
/// Hareketler). Her seferinde tek tek düzeltildi ama kuralı KORUYAN bir şey yoktu.
///
/// <b>Bu test kuralı kalıcı hâle getirir:</b> bir sayfanın ilk yüklemesinde, istisna FIRLATABİLEN bir
/// çağrı <c>try</c> bloğunun DIŞINDA olamaz. Hata yutan yardımcılar (ör. <c>ApiClient.OptionsAsync</c>
/// kendi içinde yakalar) serbesttir — kural gereksiz yere sıkı değildir.
///
/// <b>Çözümleme dosya-içidir:</b> önce sayfanın kendi metotları, sonra ortak servisler bakılır.
/// Böylece iki ayrı sayfadaki aynı adlı metot (ör. birden çok <c>LoadList</c>) birbirine karışmaz.
/// </summary>
public class WebCircuitGuardTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("DepoWise.sln bulunamadı.");
    }

    // ── söz dizimi yardımcıları ───────────────────────────────────────────────────────────────
    private static int BlockEnd(string s, int open)
    {
        int d = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '{') d++;
            else if (s[i] == '}') { d--; if (d == 0) return i; }
        }
        return s.Length;
    }

    /// <summary>try{...}catch/finally{...} bloklarının kapsadığı aralıklar.</summary>
    private static List<(int A, int B)> TryRanges(string body)
    {
        var list = new List<(int, int)>();
        foreach (Match m in Regex.Matches(body, @"\btry\b\s*\{"))
        {
            int open = m.Index + m.Length - 1;
            int end = BlockEnd(body, open);
            int j = end + 1;
            while (j < body.Length)
            {
                var rest = body.Substring(j, Math.Min(140, body.Length - j));
                var cm = Regex.Match(rest, @"^\s*(catch|finally)[^{}]*\{");
                if (!cm.Success) break;
                int copen = j + cm.Length - 1;
                int cend = BlockEnd(body, copen);
                j = cend + 1; end = cend;
            }
            list.Add((m.Index, end));
        }
        return list;
    }

    private static bool InTry(List<(int A, int B)> r, int i) => r.Any(x => i > x.A && i < x.B);

    // ⚠️ `)` ile `{` arasında SATIR SONU olabilir (C# yaygın stili). İlk sürüm yalnız boşluk/tab
    // kabul ediyordu ve bu yüzden ApiClient/LocationOptions metotlarının HİÇBİRİNİ göremiyordu —
    // test "her zaman yeşil" bir kabuk oluyordu. Kasten bozma denemesiyle yakalandı.
    private static readonly Regex MethodRe = new(
        @"(?:^|\n)[ \t]*(?:public|private|protected|internal)[^\n;{}=]*?[ \t]([A-Za-z_]\w*)[ \t]*\([^;{}()]*\)[ \t\r\n]*\{",
        RegexOptions.Compiled);

    /// <summary>Bir dosyadaki metot adı → gövde(ler).</summary>
    private static Dictionary<string, List<string>> Methods(string src)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        int from = 0;
        while (true)
        {
            var m = MethodRe.Match(src, from);
            if (!m.Success) break;
            int open = src.IndexOf('{', m.Index + m.Length - 1);
            if (open < 0) break;
            int end = BlockEnd(src, open);
            if (!map.TryGetValue(m.Groups[1].Value, out var l)) map[m.Groups[1].Value] = l = new List<string>();
            l.Add(src.Substring(open + 1, end - open - 1));
            from = open + 1;
        }
        return map;
    }

    /// <summary>Gövdedeki, try DIŞINDA kalan <c>await X(</c> çağrılarının son adları.</summary>
    private static List<(string Call, int Index)> UnguardedCalls(string body)
    {
        var r = TryRanges(body);
        var list = new List<(string, int)>();
        foreach (Match m in Regex.Matches(body, @"await\s+([A-Za-z_][\w\.]*)\s*\("))
            if (!InTry(r, m.Index))
                list.Add((m.Groups[1].Value.Split('.').Last(), m.Index));
        return list;
    }

    /// <summary>Gövde, try DIŞINDA istisna fırlatabilecek bir çağrı içeriyor mu (taban kural)?</summary>
    private static bool ThrowsAtBase(string body)
    {
        var r = TryRanges(body);
        foreach (Match m in Regex.Matches(body, @"EnsureSuccessStatusCode|ReadFromJsonAsync|throw new"))
            if (!InTry(r, m.Index)) return true;
        return false;
    }

    /// <summary>Bir metot kümesinde "fırlatan" adları sabit noktaya kadar yay.</summary>
    private static HashSet<string> Throwing(Dictionary<string, List<string>> methods, HashSet<string>? seed = null)
    {
        var t = new HashSet<string>(seed ?? new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (name, bodies) in methods)
            if (bodies.Any(ThrowsAtBase)) t.Add(name);

        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;
            foreach (var (name, bodies) in methods)
            {
                if (t.Contains(name)) continue;
                if (bodies.Any(b => UnguardedCalls(b).Any(c => t.Contains(c.Call))))
                { t.Add(name); changed = true; }
            }
            if (!changed) break;
        }
        return t;
    }

    /// <summary>
    /// Bir yaşam döngüsü kancasının gövde(ler)i. İKİ yazım da desteklenir:
    /// blok gövdeli (<c>… OnInitializedAsync() { … }</c>) ve ifade gövdeli
    /// (<c>… OnInitializedAsync() =&gt; await Load();</c>). İkincisi eski taramada hiç görülmüyordu.
    /// </summary>
    private static IEnumerable<string> YasamDongusuGovdeleri(string src, string ad)
    {
        int from = 0;
        while (true)
        {
            int idx = src.IndexOf(ad, from, StringComparison.Ordinal);
            if (idx < 0) yield break;
            from = idx + ad.Length;

            // Tanım mı, yoksa yorum/çağrı mı? Ad ile parantez arasında yalnız boşluk olmalı.
            int par = src.IndexOf('(', idx);
            if (par < 0) yield break;
            if (src.Substring(idx + ad.Length, par - idx - ad.Length).Trim().Length > 0) continue;

            int kapa = src.IndexOf(')', par);
            if (kapa < 0) yield break;
            var sonra = src.Substring(kapa + 1, Math.Min(40, src.Length - kapa - 1));
            var m = Regex.Match(sonra, @"^\s*(\{|=>)");
            if (!m.Success) continue;                      // bildirim/çağrı — gövde değil

            if (m.Groups[1].Value == "{")
            {
                int open = src.IndexOf('{', kapa);
                yield return src.Substring(open + 1, BlockEnd(src, open) - open - 1);
            }
            else
            {
                // İfade gövdesi: "=> ifade;" — noktalı virgüle kadar.
                int ok = src.IndexOf("=>", kapa, StringComparison.Ordinal);
                int nokta = src.IndexOf(';', ok);
                if (nokta > ok) yield return src.Substring(ok + 2, nokta - ok - 2);
            }
        }
    }

    /// <summary>
    /// ⭐ WEB-01 — hiçbir Blazor sayfasının/bileşeninin YAŞAM DÖNGÜSÜNDE korumasız fırlatan çağrı olmamalı.
    /// </summary>
    [Fact]
    public void Hicbir_Sayfa_Ilk_Yuklemede_Devreyi_Dusurmemeli()
    {
        var root = RepoRoot();
        var servicesDir = Path.Combine(root, "src", "DepoWise.Web", "Services");
        var pagesDir = Path.Combine(root, "src", "DepoWise.Web", "Components", "Pages");
        Assert.True(Directory.Exists(pagesDir), "Blazor sayfa klasörü bulunamadı: " + pagesDir);

        // 1) Ortak servislerdeki (ApiClient, LocationOptions …) fırlatan metotlar.
        var serviceMethods = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(servicesDir, "*.cs"))
            foreach (var (k, v) in Methods(File.ReadAllText(f)))
            {
                if (!serviceMethods.TryGetValue(k, out var l)) serviceMethods[k] = l = new List<string>();
                l.AddRange(v);
            }
        // İfade gövdeli devretmeler (ör. `public Task<...> WriteTargets() => AllAsync();`) da sayılır.
        foreach (var f in Directory.GetFiles(servicesDir, "*.cs"))
            foreach (Match m in Regex.Matches(File.ReadAllText(f),
                     @"(?:public|internal)[^\n;{}=]*?\s([A-Za-z_]\w*)\s*\([^;{}()]*\)\s*=>\s*([A-Za-z_][\w\.]*)\s*\("))
            {
                if (!serviceMethods.TryGetValue(m.Groups[1].Value, out var l)) serviceMethods[m.Groups[1].Value] = l = new List<string>();
                l.Add("await " + m.Groups[2].Value + "();");   // devretmeyi korumasız çağrı gibi ele al
            }
        var serviceThrows = Throwing(serviceMethods);

        var bulgular = new List<string>();
        // ⭐ DENETİM 2026-08-26 — KAPSAM GENİŞLETMESİ. Eski tarama iki büyük deliği açık bırakıyordu:
        //   (a) İFADE GÖVDELİ yaşam döngüsü (OnInitializedAsync() => await Load();) TAMAMEN atlanıyordu —
        //       web'de 10 sayfa/bileşen böyle yazılmış ve hiçbiri denetlenmiyordu.
        //   (b) YALNIZ OnInitializedAsync bakılıyordu. Blazor Server'da OnParametersSetAsync ve
        //       OnAfterRenderAsync içindeki yakalanmayan istisna da devreyi AYNI şekilde düşürür
        //       (9 sayfa OnAfterRenderAsync kullanıyor).
        // Ayrıca ortak BİLEŞENLER (Components/*.razor, Layout/*.razor) de tarandı: bir bileşenin
        // devreyi düşürmesi, onu kullanan HER sayfayı düşürür.
        var dosyalar = Directory.GetFiles(pagesDir, "*.razor")
            .Concat(Directory.GetFiles(Path.Combine(root, "src", "DepoWise.Web", "Components"), "*.razor"))
            .Concat(Directory.GetFiles(Path.Combine(root, "src", "DepoWise.Web", "Components", "Layout"), "*.razor"))
            .OrderBy(x => x, StringComparer.Ordinal);

        var yasamDongusu = new[] { "OnInitializedAsync", "OnParametersSetAsync", "OnAfterRenderAsync" };

        foreach (var page in dosyalar)
        {
            var src = File.ReadAllText(page);

            // Sayfanın KENDİ metotları — ad çözümlemesi ÖNCE burada (dosyalar arası çakışma yok).
            var local = Methods(src);
            var localThrows = Throwing(local, serviceThrows);

            foreach (var kanca in yasamDongusu)
                foreach (var govde in YasamDongusuGovdeleri(src, kanca))
                    foreach (var (call, _) in UnguardedCalls(govde))
                    {
                        bool yerel = local.ContainsKey(call);
                        bool firlatir = yerel ? localThrows.Contains(call) : serviceThrows.Contains(call);
                        if (firlatir)
                            bulgular.Add($"{Path.GetFileName(page)} · {kanca} → korumasız '{call}(...)'");
                    }
        }

        Assert.True(bulgular.Count == 0,
            "OnInitializedAsync içinde try/catch DIŞINDA istisna fırlatabilen çağrı var. Blazor Server'da " +
            "bu, kullanıcının devresini düşürür (bembeyaz ekran). Çağrıyı try/catch içine alın ve hatayı " +
            "ekranda gösterin:\n  " + string.Join("\n  ", bulgular.Distinct()));
    }

    /// <summary>
    /// Kuralın kendisi çalışıyor mu? Kasten korumasız bir gövde ÜRETİLİR ve tespit edilmesi beklenir —
    /// test "her zaman yeşil" bir kabuk olmasın (yanlış güven vermesin).
    /// </summary>
    [Fact]
    public void Kural_Gercekten_Yakaliyor_Mu()
    {
        var kotu = "\n    private async Task Yukle() { var x = await Api.GetArrayAsync(\"/api/x\"); }\n";
        var iyi = "\n    private async Task Yukle() { try { var x = await Api.GetArrayAsync(\"/api/x\"); } catch { } }\n";

        var taban = new HashSet<string>(new[] { "GetArrayAsync" }, StringComparer.Ordinal);

        Assert.Contains("Yukle", Throwing(Methods(kotu), taban));
        Assert.DoesNotContain("Yukle", Throwing(Methods(iyi), taban));
    }
}
