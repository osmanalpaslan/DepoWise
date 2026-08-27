using System.Text.Json;

namespace DepoWise.Application.Common;

/// <summary>
/// ═══ TSN (kullanıcı bildirimi 2026-08-27) — JSON ALAN ADI TOLERANSI ═══
///
/// <b>Kapatılan hata.</b> <c>GET /api/lookups/sync</c> satırları sözlük (<c>Dictionary</c>) olarak
/// döner ve sözlük ANAHTARLARI veritabanı sütun adlarıdır: <c>brand_id</c>, <c>parent_id</c>,
/// <c>brand_type</c>. ASP.NET Core'un web varsayılanları <i>özellik</i> adlarını camelCase'e çevirir
/// ama <i>sözlük anahtarlarına dokunmaz</i> (<c>DictionaryKeyPolicy</c> ayarlı değildir). Masaüstü
/// tarafı ise camelCase (<c>brandId</c>) arıyordu; <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>
/// büyük-küçük harf duyarlı olduğu için alan HİÇ bulunamıyor, "boş geldi" sanılıyor ve tanım senkronu
/// ilgili sütunu <c>NULL</c>'a çekiyordu (araç modeli markasını kaybediyor, alt kategori üst seviyeye
/// çıkıyordu).
///
/// <b>Neden tolerans, neden tek adı düzeltmek değil:</b> masaüstü ve sunucu AYRI yayınlanır. Sahada
/// her zaman eski sunucu + yeni istemci (ya da tersi) karışımı olabilir. Okuyucu her iki yazımı da
/// kabul ederse hangi taraf önce güncellenirse güncellensin alan kaybolmaz.
///
/// Bu sınıf yalnız OKUR; hiçbir şey yazmaz ve JSON şemasını değiştirmez.
/// </summary>
public static class JsonAlan
{
    /// <summary>
    /// Bir JSON nesnesinden alanı, yazım farklarına toleranslı biçimde okur.
    /// Sırasıyla: verilen ad (<c>brand_id</c>) → camelCase (<c>brandId</c>) → PascalCase
    /// (<c>BrandId</c>) → alt çizgi/büyük-küçük harf yok sayılarak tarama.
    /// </summary>
    /// <param name="satir">JSON nesnesi (dizi elemanı).</param>
    /// <param name="kolonAdi">Veritabanı sütun adı, <c>snake_case</c> (ör. <c>brand_id</c>).</param>
    /// <returns>Değer; alan yoksa veya JSON <c>null</c> ise <c>null</c>. Boş metin de <c>null</c> döner
    /// (çağıranlar için "yok" ile "boş" aynı anlama gelir: sütun NULL kalır).</returns>
    public static string? AlanOku(JsonElement satir, string kolonAdi)
    {
        if (satir.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(kolonAdi)) return null;

        if (Dene(satir, kolonAdi, out var v)) return v;

        var camel = Camel(kolonAdi);
        if (camel != kolonAdi && Dene(satir, camel, out v)) return v;

        var pascal = camel.Length > 0 ? char.ToUpperInvariant(camel[0]) + camel[1..] : camel;
        if (pascal != kolonAdi && Dene(satir, pascal, out v)) return v;

        // Son çare: alt çizgileri ve harf büyüklüğünü yok sayarak tara (sunucu yeni bir yazım
        // kullanmaya başlarsa alan yine de kaybolmasın).
        var hedef = Sadeles(kolonAdi);
        foreach (var p in satir.EnumerateObject())
            if (Sadeles(p.Name) == hedef)
                return Metin(p.Value);

        return null;
    }

    /// <summary><c>brand_id</c> → <c>brandId</c>. Alt çizgi kaldırılır, sonraki harf büyütülür.</summary>
    private static string Camel(string s)
    {
        if (!s.Contains('_')) return s;
        var b = new System.Text.StringBuilder(s.Length);
        var buyut = false;
        foreach (var ch in s)
        {
            if (ch == '_') { buyut = true; continue; }
            b.Append(buyut ? char.ToUpperInvariant(ch) : ch);
            buyut = false;
        }
        return b.ToString();
    }

    private static string Sadeles(string s) => s.Replace("_", "").ToLowerInvariant();

    private static bool Dene(JsonElement satir, string ad, out string? deger)
    {
        if (satir.TryGetProperty(ad, out var el)) { deger = Metin(el); return true; }
        deger = null;
        return false;
    }

    /// <summary>JSON değerini metne çevirir; <c>null</c> ve boş metin ikisi de <c>null</c> döner.</summary>
    private static string? Metin(JsonElement el)
    {
        var s = el.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => el.GetString(),
            _ => el.ToString(),
        };
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
