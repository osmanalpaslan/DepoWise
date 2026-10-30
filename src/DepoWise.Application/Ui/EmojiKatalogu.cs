using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DepoWise.Application.Ui;

/// <summary>Seçicide bir emoji: görünen karakter, Türkçe adı (aranır, <c>:ad:</c> kısayolu olur) ve yazı kısayolları (<c>:)</c> gibi).</summary>
public sealed record EmojiOgesi(string Emoji, string Ad, params string[] Kisayollar)
{
    /// <summary>İpucu: "gülümseme · :) :gülümseme:".</summary>
    public string Ipucu => Ad + " · " + string.Join(" ", Kisayollar.Append(":" + Ad + ":"));
}

public sealed record EmojiKategori(string Ad, IReadOnlyList<EmojiOgesi> Ogeler);

/// <summary>
/// ⭐ 2026-10-30 — SOHBET EMOJİLERİ (kullanıcı isteği: "mesaj kısmına emoji ve global emoji kısayollarını da ekle").
///
/// <para>TEK kaynak: masaüstü (ChatViewModel) ve web (ChatDock) aynı listeyi ve aynı kısayol kuralını kullanır (web bu dosyayı csproj
/// <c>Compile Include</c> ile derler). İki tür kısayol: yaygın yazı kısayolları (<c>:)</c> → 🙂, <c>&lt;3</c> → ❤️, <c>(y)</c> → 👍) ve
/// <c>:ad:</c> biçimi (<c>:kamyon:</c> → 🚚; Türkçe harfli ya da harfsiz yazılabilir: <c>:uyarı:</c> = <c>:uyari:</c>).</para>
///
/// <para><b>Kural:</b> kısayol yalnız TEK BAŞINA bir sözcükse dönüşür (önünde ve arkasında boşluk ya da metin başı/sonu) → adresler
/// (<c>http://</c>), saatler (<c>10:30</c>) ve kod gibi metinler bozulmaz. Yazarken yalnız arkasından boşluk gelmiş (bitmiş) kısayol
/// dönüşür; gönderirken metnin sonundaki kısayol da dönüşür.</para>
/// </summary>
public static class EmojiKatalogu
{
    public static readonly IReadOnlyList<EmojiKategori> Kategoriler = new EmojiKategori[]
    {
        new("Yüzler", new EmojiOgesi[]
        {
            new("🙂", "gülümseme", ":)", ":-)"), new("😀", "sırıtma", ":D", ":-D"), new("😄", "kahkaha", "xD"),
            new("😂", "gözyaşıyla gülme", ":'D"), new("😉", "göz kırpma", ";)", ";-)"), new("😊", "mutlu", "^^"),
            new("😍", "bayıldım"), new("😘", "öpücük", ":*"), new("😎", "havalı", "B-)"), new("🤔", "düşünüyorum"),
            new("😐", "ifadesiz", ":|"), new("😕", "kararsız", ":/"), new("🙁", "üzgün", ":(", ":-("), new("😢", "ağlıyor", ":'("),
            new("😮", "şaşkın", ":O", ":o"), new("😛", "dil", ":P", ":p"), new("😡", "kızgın", ">:("), new("😴", "uykulu"),
            new("🤗", "sarılma"), new("😅", "terledim"), new("🙄", "göz devirme"), new("😬", "gergin"),
        }),
        new("Eller", new EmojiOgesi[]
        {
            new("👍", "beğen", "(y)", ":+1:"), new("👎", "beğenme", "(n)", ":-1:"), new("👌", "tamam el", ":ok:"), new("👏", "alkış"),
            new("🙏", "teşekkür"), new("💪", "güçlü"), new("✋", "dur"), new("👋", "selam"), new("🤝", "anlaştık"),
            new("✌️", "zafer"), new("👉", "bak"), new("👀", "göz"),
        }),
        new("Duygu", new EmojiOgesi[]
        {
            new("❤️", "kalp", "<3"), new("💔", "kırık kalp", "</3"), new("🔥", "ateş"), new("⭐", "yıldız"),
            new("🎉", "kutlama"), new("💯", "yüz"), new("✨", "parıltı"), new("🎂", "pasta"),
        }),
        new("Şantiye", new EmojiOgesi[]
        {
            new("🚚", "kamyon"), new("🚛", "tır"), new("🚜", "traktör"), new("🏗️", "vinç"), new("🚧", "çalışma"),
            new("🔧", "tamir"), new("🔨", "çekiç"), new("🛠️", "alet"), new("🧰", "alet çantası"), new("🔩", "cıvata"),
            new("⚙️", "dişli"), new("⛽", "yakıt"), new("📦", "paket"), new("🏭", "fabrika"), new("🦺", "yelek"),
            new("⛑️", "baret"), new("🚗", "araba"), new("🛞", "lastik"),
        }),
        new("Ofis", new EmojiOgesi[]
        {
            new("📄", "belge"), new("📋", "liste"), new("📌", "iğne"), new("📎", "ataş"), new("📅", "takvim"),
            new("⏰", "saat"), new("📞", "telefon"), new("✉️", "zarf"), new("💰", "para"), new("🧾", "fatura"),
            new("📊", "grafik"), new("🔑", "anahtar"), new("📷", "fotoğraf"), new("🖨️", "yazıcı"),
        }),
        new("Durum", new EmojiOgesi[]
        {
            new("✅", "tamam", ":onay:"), new("❌", "hayır"), new("⚠️", "uyarı"), new("❗", "önemli"), new("❓", "soru"),
            new("⏳", "bekliyor"), new("🔴", "kırmızı"), new("🟡", "sarı"), new("🟢", "yeşil"), new("➕", "artı"),
            new("➖", "eksi"), new("➡️", "ileri"), new("🔁", "tekrar"), new("🆗", "ok yazısı"),
        }),
        new("Hava", new EmojiOgesi[]
        {
            new("☀️", "güneş"), new("⛅", "bulutlu"), new("🌧️", "yağmur"), new("⛈️", "fırtına"), new("❄️", "kar"),
            new("🌙", "gece"), new("💨", "rüzgar"),
        }),
    };

    /// <summary>Tüm emojiler (kategori sırasıyla).</summary>
    public static IEnumerable<EmojiOgesi> Tumu => Kategoriler.SelectMany(k => k.Ogeler);

    private static readonly Dictionary<string, string> _yaziKisayollari = Tumu
        .SelectMany(o => o.Kisayollar.Where(k => !k.StartsWith(':') || !k.EndsWith(':') || k.Length < 3).Select(k => (k, o.Emoji)))
        .GroupBy(x => x.k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Emoji, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> _adKisayollari = Tumu
        .SelectMany(o => o.Kisayollar.Where(k => k.Length >= 3 && k.StartsWith(':') && k.EndsWith(':')).Select(k => (Ad: k[1..^1], o.Emoji))
            .Append((Ad: o.Ad, o.Emoji)))
        .GroupBy(x => Sade(x.Ad), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Emoji, StringComparer.Ordinal);

    /// <summary>Ad karşılaştırması: küçük harf, Türkçe harfler sade (ı→i, ş→s…), boşluk → alt çizgi.</summary>
    public static string Sade(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Trim())
            sb.Append(c switch
            {
                'ı' or 'I' or 'İ' or 'i' => 'i', 'ş' or 'Ş' => 's', 'ğ' or 'Ğ' => 'g', 'ü' or 'Ü' => 'u', 'ö' or 'Ö' => 'o',
                'ç' or 'Ç' => 'c', ' ' => '_', _ => char.ToLowerInvariant(c),
            });
        return sb.ToString();
    }

    /// <summary>Bir sözcük kısayolsa karşılığı (değilse null).</summary>
    public static string? KisayolKarsiligi(string sozcuk)
    {
        if (_yaziKisayollari.TryGetValue(sozcuk, out var e)) return e;
        if (sozcuk.Length >= 3 && sozcuk[0] == ':' && sozcuk[^1] == ':' && _adKisayollari.TryGetValue(Sade(sozcuk[1..^1]), out e)) return e;
        return null;
    }

    private static readonly Regex _sozcuk = new(@"\S+", RegexOptions.Compiled);

    /// <summary>
    /// Metindeki kısayolları emojiye çevirir. <paramref name="sonDahil"/> false ise (yazarken) metnin SONUNDAKİ, henüz bitmemiş
    /// (arkasından boşluk gelmemiş) sözcük dönüştürülmez — kullanıcı <c>:)</c> yerine <c>:))</c> yazıyor olabilir.
    /// </summary>
    public static string Donustur(string? metin, bool sonDahil)
    {
        if (string.IsNullOrEmpty(metin)) return metin ?? "";
        return _sozcuk.Replace(metin, m =>
        {
            if (!sonDahil && m.Index + m.Length == metin.Length) return m.Value;
            return KisayolKarsiligi(m.Value) ?? m.Value;
        });
    }

    /// <summary>Seçicide arama: ad, kısayol ya da emojinin kendisiyle. Boş arama = tüm kategoriler.</summary>
    public static IReadOnlyList<EmojiKategori> Ara(string? arama)
    {
        var q = Sade(arama ?? "");
        if (q.Length == 0) return Kategoriler;
        var bulunan = Tumu.Where(o => Sade(o.Ad).Contains(q, StringComparison.Ordinal)
                                      || o.Kisayollar.Any(k => k.Contains(arama!.Trim(), StringComparison.OrdinalIgnoreCase))
                                      || o.Emoji == arama!.Trim()).ToList();
        return bulunan.Count == 0 ? Array.Empty<EmojiKategori>() : new[] { new EmojiKategori("Sonuçlar", bulunan) };
    }
}
