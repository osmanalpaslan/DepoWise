using System.Text.Json;

namespace DepoWise.Application.Common;

/// <summary>Tek bir alanın değişimi: "Sayaç: 10.000 → 155.000".</summary>
public sealed record AuditChange(string Field, string Label, string Old, string New)
{
    public string Text => $"{Label}: {Old} → {New}";
}

/// <summary>
/// ═══ FAZ 4.3 — ALAN BAZLI FARK (kullanıcı isteği 2026-09-06) ═══
///
/// İki anlık görüntüyü (<c>before_json</c> / <c>after_json</c>) karşılaştırıp <b>hangi alanda neyin
/// neye döndüğünü</b> üretir. Kullanıcının isteği tam olarak buydu: "kayıta ait hangi alanda neyi
/// güncelledi ise görebilmeliyim".
///
/// <b>Kurallar.</b>
/// <list type="bullet">
///   <item>Teknik sütunlar (<c>version</c>, <c>updated_at</c>…) atlanır — her güncellemede değiştikleri
///   için gerçek değişikliği gizlerler.</item>
///   <item>Gizli sütunlar (parola özeti, jeton…) zaten anlık görüntüye hiç girmez; buraya bir yolla
///   gelirse yine atlanır (iki kat koruma).</item>
///   <item>Değer değişmemişse satır ÜRETİLMEZ. "Değişmedi" satırları listeyi okunmaz yapardı.</item>
///   <item>Yeni kayıtta (öncesi yok) yalnız DOLU alanlar listelenir: boş alanların "— → —" satırı
///   bilgi taşımaz.</item>
/// </list>
/// </summary>
public static class AuditDiff
{
    /// <summary>İki anlık görüntü arasındaki alan farkları. Görüntü yoksa boş liste döner.</summary>
    /// <param name="adlar">Kimlik → okunur ad (ör. şube kimliği → şube adı). Verilmezse ham değer
    /// gösterilir; uydurma ad ASLA yazılmaz.</param>
    public static IReadOnlyList<AuditChange> Hesapla(string? beforeJson, string? afterJson,
        IReadOnlyDictionary<string, string>? adlar = null)
    {
        var once = Coz(beforeJson);
        var sonra = Coz(afterJson);
        if (once.Count == 0 && sonra.Count == 0) return Array.Empty<AuditChange>();

        var sira = new List<string>();
        foreach (var k in sonra.Keys) sira.Add(k);
        foreach (var k in once.Keys) if (!sonra.ContainsKey(k)) sira.Add(k);

        var sonuc = new List<AuditChange>();
        foreach (var sutun in sira)
        {
            if (AuditFields.Teknik(sutun) || AuditFields.Gizli(sutun)) continue;
            once.TryGetValue(sutun, out var eskiHam);
            sonra.TryGetValue(sutun, out var yeniHam);
            if (string.Equals(eskiHam ?? "", yeniHam ?? "", StringComparison.Ordinal)) continue;

            var eski = Bic(sutun, eskiHam, adlar);
            var yeni = Bic(sutun, yeniHam, adlar);
            if (eski == yeni) continue;                       // biçimlemeden sonra fark kalmadıysa gösterme
            if (once.Count == 0 && yeni == "—") continue;     // yeni kayıtta boş alanı yazma
            sonuc.Add(new AuditChange(sutun, AuditFields.Etiket(sutun), eski, yeni));
        }
        return sonuc;
    }

    /// <summary>Değeri biçimler; bağlantı sütunlarında kimlik yerine ADI gösterir.</summary>
    private static string Bic(string sutun, string? ham, IReadOnlyDictionary<string, string>? adlar)
    {
        if (adlar is not null && !string.IsNullOrWhiteSpace(ham)
            && AuditFields.BagliTablo(sutun) is not null && adlar.TryGetValue(ham!, out var ad))
            return ad;
        return AuditFields.Deger(sutun, ham);
    }

    /// <summary>Liste ekranında tek satıra sığan özet: ilk birkaç değişiklik + "+N alan daha".</summary>
    public static string Ozet(IReadOnlyList<AuditChange> degisiklikler, int enFazla = 3)
    {
        if (degisiklikler.Count == 0) return "";
        var bas = degisiklikler.Take(enFazla).Select(d => d.Text);
        var metin = string.Join(" · ", bas);
        return degisiklikler.Count > enFazla
            ? metin + $" · +{degisiklikler.Count - enFazla} alan daha"
            : metin;
    }

    /// <summary>JSON nesnesini "sütun → ham metin" sözlüğüne çevirir. Bozuk/boş JSON → boş sözlük
    /// (log okunurken istisna fırlatmak, logu tamamen erişilemez yapardı).</summary>
    private static Dictionary<string, string?> Coz(string? json)
    {
        var d = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return d;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return d;
            foreach (var p in doc.RootElement.EnumerateObject())
                d[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => p.Value.GetString(),
                    JsonValueKind.True => "1",
                    JsonValueKind.False => "0",
                    _ => p.Value.ToString(),
                };
        }
        catch (JsonException) { /* bozuk kayıt: farkı gösteremeyiz, log satırı yine görünür */ }
        return d;
    }
}
