using System.Data.Common;
using System.Text.RegularExpressions;

namespace DepoWise.Infrastructure.Database;

/// <summary>
/// ═══ M-78 / K-21 (AR-GE 2026-10-17) — BİLGİSAYAR KODLU BELGE NUMARASI ═══
///
/// <c>Sonraki("TLP-2026-", "A3")</c> → <c>TLP-2026-A3-0042</c>: sıra yalnız BU bilgisayarın numaraları içinde artar, iki
/// çevrimdışı bilgisayar asla aynı numarayı üretmez. Kod yoksa (sunucu/web, ya da kodu henüz alınmamış eski kurulum) eski
/// biçim <c>TLP-2026-0042</c> sürer; eski biçimin sırası kodlu numaraları SAYMAZ (onları sayıya çevirmeye çalışmak
/// PostgreSQL'de hata verirdi).
/// </summary>
public static class BelgeNumarasi
{
    private static readonly Regex KodBicimi = new("^[A-Z]{1,2}[1-9]$", RegexOptions.CultureInvariant);

    /// <summary>Geçerli bilgisayar kodu mu (sunucunun ürettiği biçim: A1…Z9, AA1…).</summary>
    public static bool GecerliKod(string? kod) => kod is not null && KodBicimi.IsMatch(kod);

    /// <summary>
    /// <paramref name="onek"/> = "TLP-2026-" gibi (sonunda tire). <paramref name="tablo"/>/<paramref name="kolon"/> sabit
    /// (çağıranın kodundan; kullanıcı verisi değil). <paramref name="ekKosul"/> ör. "AND doc_type=@t" (+ parametreleri
    /// <paramref name="bagla"/> ile).
    /// </summary>
    public static string Sonraki(DbConnection conn, DbTransaction? tx, string tablo, string kolon, string companyId,
        string onek, string? makineKodu, string ekKosul = "", Action<DbCommand>? bagla = null, int dolgu = 4)
    {
        var kodlu = GecerliKod(makineKodu);
        var p = kodlu ? onek + makineKodu + "-" : onek;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Eski biçimde (kodsuz) yalnız "önek + rakamlar" sayılır; kodlu numaralar (içinde tire var) dışarıda kalır.
        cmd.CommandText =
            $"SELECT COALESCE(MAX(CAST(substr({kolon}, length(@p)+1) AS INTEGER)),0) FROM {tablo} " +
            $"WHERE company_id=@c AND {kolon} LIKE @like" +
            (kodlu ? "" : $" AND substr({kolon}, length(@p)+1) NOT LIKE '%-%'") +
            $" {ekKosul};";
        cmd.AddWithValue("@p", p);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@like", p + "%");
        bagla?.Invoke(cmd);
        var sonraki = Convert.ToInt64(cmd.ExecuteScalar()) + 1;
        return p + sonraki.ToString(new string('0', Math.Clamp(dolgu, 1, 18)));
    }

    /// <summary>n. bilgisayar kodu (0'dan): A1…A9, B1…Z9 (234 adet), sonra AA1… (iki harf).</summary>
    public static string Kod(int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n));
        if (n < 26 * 9) return $"{(char)('A' + n / 9)}{1 + n % 9}";
        n -= 26 * 9;
        return $"{(char)('A' + n / 9 / 26 % 26)}{(char)('A' + n / 9 % 26)}{1 + n % 9}";
    }
}
