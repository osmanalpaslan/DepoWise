using System;
using System.Collections.Generic;
using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>
/// Malzeme "Tür" alanı — SERBEST METİN ama bilinen bir kanonik liste vardır (formda açılır liste olarak sunulur).
/// Kartta ayrı bir tanım/lookup DEĞİLDİR; doğrudan materials.type sütununa yazılır.
///
/// Kullanıcı isteği (2026-07-18): Excel ile içe aktarımda "YEDEK PARÇA" yalnız BÜYÜK harfle yazıldığı için
/// kanonik "Yedek Parça" ile eşleşmiyor, ayrı bir değer gibi görünüyordu. <see cref="Normalize"/>, bilinen
/// bir türe harf duyarsız uyanı KANONİK biçime çevirir; bilinmeyen (kullanıcının kendi eklediği) türü
/// olduğu gibi (kırpılmış) bırakır — tür serbest metindir, kısıtlanmaz.
///
/// NOT: Aynı kanonik liste web (Materials.razor `_types`) ve masaüstü (MaterialsViewModel.TypeOptions)
/// açılır listelerinde de vardır; oralar zaten kanonik değer üretir (düzeltme yalnız içe aktarım/eski veri
/// için gerekli). İleride tek kaynağa indirilebilir.
/// </summary>
public static class MaterialType
{
    public static readonly IReadOnlyList<string> Canonical = new[]
    {
        "Yedek Parça", "Sarf Malzeme", "Hammadde", "Lastik", "Diğer",
    };

    /// <summary>Boş → null. Bilinen türe harf duyarsız uyuyorsa KANONİK biçim; değilse kırpılmış giriş.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return Canonical.FirstOrDefault(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }
}
