using System.Diagnostics;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-22 / F-111 (AR-GE 2026-10-18) — TÜRKÇE ARAMA İŞLEVİ (SQLite like) HIZLANDI, SONUÇ AYNI ═══
///  ARH1 — Yeni eşleştirici, eski Regex uygulamasıyla BİREBİR aynı sonucu verir (Türkçe harfler, joker türleri, 20 bin rastgele örnek)
///  ARH2 — Gerçek SQLite sorgusunda (`x LIKE y`) Türkçe duyarsızlık korunur
///  ARH3 — 300 bin değerde "%arama%" en az 2 kat hızlı (ölçülen 4,5 kat; eski = satır başına desen kurulumu + Regex)
/// </summary>
public class AramaHiziTests
{
    private static readonly string[] Ornekler =
    {
        "İstanbul", "ıspanak", "ISPARTA", "Çınar Ağacı", "çinko", "ŞİŞE", "şişe", "Ğ harfi", "ÖLÇÜ", "ölçü aleti",
        "Filtre-100", "filtre_100", "a%b", "50% indirim", "", "a", "aa", "aaa", "ab", "ba", "abc", "cab", "abcabc",
        "Yağ Filtresi (CAT)", "yağ\nfiltresi", "MLZ-2026-A3-0042", "x.y*z", "[köşeli]", "^başa$", "İi Iı",
    };

    private static readonly string[] Desenler =
    {
        "%", "%%", "", "a", "%a", "a%", "%a%", "a%a", "a%%b", "%ab%", "%a%b%", "a%b%c", "%c", "ab_", "_b%", "%_%", "__",
        "%istanbul%", "%İSTANBUL%", "ispanak", "ISPANAK", "%ıspa%", "%çın%", "%ÇIN%", "%şişe%", "%ŞİŞE", "ğ%", "%ölçü%",
        "%filtre%100", "filtre_100", "%\\_%", "%.%", "%*%", "%[%", "^%", "%$", "%yağ%filt%", "%İi%", "%ıı%", "%mlz-%-0042",
    };

    [Fact]
    public void ARH1_Yeni_Eslestirici_Eskisiyle_Birebir_Ayni()
    {
        foreach (var d in Desenler)
            foreach (var v in Ornekler)
                Assert.True(SqliteConnectionFactory.SqlLikeTr(d, v) == SqliteConnectionFactory.SqlLikeTrEski(d, v), $"desen «{d}» değer «{v}»");

        // Rastgele: küçük alfabeyle (çakışma çok olsun) 20.000 desen/değer çifti
        var r = new Random(20261018);
        const string harf = "aAıIiİşŞçÇ%_b";
        string Rastgele(int max) { var n = r.Next(0, max); var c = new char[n]; for (int i = 0; i < n; i++) c[i] = harf[r.Next(harf.Length)]; return new string(c); }
        for (int k = 0; k < 20_000; k++)
        {
            var d = Rastgele(6); var v = Rastgele(8).Replace("%", "").Replace("_", "");
            Assert.True(SqliteConnectionFactory.SqlLikeTr(d, v) == SqliteConnectionFactory.SqlLikeTrEski(d, v), $"desen «{d}» değer «{v}»");
        }
        Assert.False(SqliteConnectionFactory.SqlLikeTr(null, "a"));
        Assert.False(SqliteConnectionFactory.SqlLikeTr("a", null));
    }

    [Fact]
    public void ARH2_Sqlite_Sorgusunda_Turkce_Duyarsiz()
    {
        var yol = Path.Combine(Path.GetTempPath(), "depowise_arh_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var f = new SqliteConnectionFactory(yol);
            using var conn = f.Create();
            bool Like(string v, string d)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT @v LIKE @d;";
                cmd.AddWithValue("@v", v);
                cmd.AddWithValue("@d", d);
                return Convert.ToInt64(cmd.ExecuteScalar()) == 1;
            }
            Assert.True(Like("İSTANBUL", "%istanbul%"));
            Assert.True(Like("ıspanak", "ISPANAK"));
            Assert.True(Like("Çınar Ağacı", "%ÇINAR%"));
            Assert.False(Like("Istanbul", "%istanbul%"));   // Türkçede I → ı (i değil): eski davranışla aynı
            Assert.True(Like("Yağ Filtresi", "yağ%"));
            Assert.False(Like("Yağ Filtresi", "filtre%"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var p in new[] { yol, yol + "-wal", yol + "-shm" }) try { File.Delete(p); } catch { }
        }
    }

    [Fact]
    public void ARH3_Buyuk_Veride_Hizli()
    {
        var r = new Random(7);
        var degerler = Enumerable.Range(0, 300_000).Select(i => $"Malzeme {i} {(char)('A' + r.Next(26))}ğış Filtre-{r.Next(1000)}").ToArray();
        const string desen = "%filtre-42%";

        int Say(Func<string, string, bool> f) { int n = 0; foreach (var v in degerler) if (f(desen, v)) n++; return n; }
        Assert.Equal(Say(SqliteConnectionFactory.SqlLikeTrEski), Say(SqliteConnectionFactory.SqlLikeTr));   // aynı sonuç

        var sw = Stopwatch.StartNew(); Say(SqliteConnectionFactory.SqlLikeTrEski); var eski = sw.Elapsed;
        sw.Restart(); Say(SqliteConnectionFactory.SqlLikeTr); var yeni = sw.Elapsed;
        // Ölçüm (2026-10-18, Release): "%x%" 4,5 kat, "x%" 3,1 kat. Eşik yük altında kırılmasın diye 2 kat.
        Assert.True(yeni.TotalMilliseconds * 2 < eski.TotalMilliseconds,
            $"beklenen en az 2 kat hız: eski {eski.TotalMilliseconds:0} ms, yeni {yeni.TotalMilliseconds:0} ms");
    }
}
