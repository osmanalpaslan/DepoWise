namespace DepoWise.Application.Reports;

/// <summary>Kullanıcının seçtiği tek bir filtre: kolon anahtarı + aranan metin.
/// ⚠️ <paramref name="ColumnKey"/> beyaz listede DOĞRULANIR; <paramref name="Value"/> asla SQL'e
/// birleştirilmez, mevcut <c>GridQuery</c> yolundan PARAMETRE olarak geçer.</summary>
public sealed record CustomReportFilter(string ColumnKey, string Value);

/// <summary>
/// ═══ ARA İŞ 4 — CUSTOM RAPOR TANIMI (ADR-186) ═══
///
/// Kullanıcının kaydettiği rapor tarifi. <b>Ham SQL taşımaz</b> (PK-CR-01=A): yalnız beyaz-listeli
/// kaynak anahtarı, kolon anahtarları, filtre değerleri ve sıralama tercihi bulunur.
///
/// ⚠️ Güvenlik meta verisi (DataModule · Category · IsManager) tanımda DEĞİL, kaynak kayıt
/// defterinde (<see cref="CustomReportSources"/>) durur — kullanıcı tanımı düzenleyerek yetki
/// kapısını gevşetemez.
/// </summary>
public sealed record CustomReportDefinition(
    string Id,
    string CompanyId,
    string Name,
    string SourceKey,
    IReadOnlyList<string> Columns,
    IReadOnlyList<CustomReportFilter> Filters,
    string? SortColumn,
    bool SortDesc,
    bool IsActive,
    long CreatedAt,
    long UpdatedAt)
{
    /// <summary>Rapor motorundaki katalog anahtarı — sabit raporlarla ÇAKIŞMAZ (önek ayırır).</summary>
    public string ReportKey => KeyOf(Id);

    /// <summary>Bu rapora özel DİNAMİK yetki anahtarı (PK-CR-04=A).
    /// <c>user_permissions.module_key</c> serbest metin olduğu için MIGRATION GEREKTİRMEZ.</summary>
    public string PermissionKey => PermissionKeyOf(Id);

    /// <summary>Katalog anahtarı öneki. ⚠️ Bilinçli olarak <c>:</c> DEĞİL <c>-</c> kullanılır:
    /// anahtar <c>/api/reports/{type}</c> yolunda bir URL segmenti olarak taşınır; tire her istemcide
    /// ve yönlendiricide kodlamasız güvenlidir. Mevcut sabit rapor anahtarları da tire kullanır
    /// (ör. <c>vehicle-daily</c>) → desen tutarlıdır ve çakışma testle kilitlidir (CR28).</summary>
    public const string KeyPrefix = "custom-";
    public const string PermissionPrefix = "report_custom_";

    public static string KeyOf(string id) => KeyPrefix + id;
    public static string PermissionKeyOf(string id) => PermissionPrefix + id;

    /// <summary>Katalog anahtarı custom rapora mı ait? Değilse null (sabit rapor yolu bozulmaz).</summary>
    public static string? IdFromKey(string? key)
        => key is not null && key.StartsWith(KeyPrefix, StringComparison.Ordinal)
            ? key[KeyPrefix.Length..]
            : null;
}

/// <summary>Tanım doğrulama sonucu — geçersizse <see cref="Error"/> kullanıcıya gösterilecek Türkçe metindir.</summary>
public sealed record CustomReportValidation(bool Ok, string? Error)
{
    public static readonly CustomReportValidation Success = new(true, null);
    public static CustomReportValidation Fail(string error) => new(false, error);
}

/// <summary>
/// Tanım doğrulayıcı — <b>TEK kapı</b>: hem kaydetme hem çalıştırma buradan geçer, böylece
/// kaydedilmiş bozuk/elle düzenlenmiş bir tanım çalıştırma anında da reddedilir (PK-CR-01=A).
/// Doğrulama <b>istisna atmaz</b>, sonuç döndürür — istisna üzerinden güvenlik kapısı atlatılamaz.
/// </summary>
public static class CustomReportRules
{
    /// <summary>Bir raporun getirebileceği azami satır (SQL'e inen tavan — PK-CR-06/10=A).</summary>
    public const int MaxRows = 5_000;

    /// <summary>Tek sorgu sayfası (mevcut <c>SearchGrid</c> tavanı 500'dür; tavan SQL'de uygulanır).</summary>
    public const int PageSize = 500;

    public static CustomReportValidation Validate(CustomReportDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Name))
            return CustomReportValidation.Fail("Rapor adı zorunludur.");

        var src = CustomReportSources.ByKey(def.SourceKey);
        if (src is null)
            return CustomReportValidation.Fail($"Bilinmeyen rapor kaynağı: «{def.SourceKey}».");

        if (def.Columns is null || def.Columns.Count == 0)
            return CustomReportValidation.Fail("En az bir kolon seçilmelidir.");

        // ⭐ BEYAZ LİSTE: kolon anahtarı katalogda yoksa REDDEDİLİR (SQL'e asla ulaşmaz).
        foreach (var c in def.Columns)
            if (!src.HasColumn(c))
                return CustomReportValidation.Fail($"«{src.Label}» kaynağında geçersiz kolon: «{c}».");

        if (def.Columns.Distinct(StringComparer.Ordinal).Count() != def.Columns.Count)
            return CustomReportValidation.Fail("Aynı kolon birden fazla kez seçilemez.");

        foreach (var f in def.Filters ?? Array.Empty<CustomReportFilter>())
            if (!src.HasColumn(f.ColumnKey))
                return CustomReportValidation.Fail($"«{src.Label}» kaynağında geçersiz filtre kolonu: «{f.ColumnKey}».");

        if (def.SortColumn is { Length: > 0 } sc && !src.HasColumn(sc))
            return CustomReportValidation.Fail($"«{src.Label}» kaynağında geçersiz sıralama kolonu: «{sc}».");

        return CustomReportValidation.Success;
    }

    /// <summary>Çalıştırma ön koşulları (PK-CR-10=A) — tanım geçerli olsa bile burada durabilir.</summary>
    public static CustomReportValidation ValidateRun(CustomReportDefinition def, long? fromDate, long? toDate)
    {
        var temel = Validate(def);
        if (!temel.Ok) return temel;

        var src = CustomReportSources.ByKey(def.SourceKey)!;

        // OLAY VERİSİ: iş günü tarih aralığı ZORUNLU (SQL'e iner).
        if (src.RequiresDate)
        {
            if (fromDate is null || toDate is null)
                return CustomReportValidation.Fail(
                    $"«{src.Label}» raporu için tarih aralığı zorunludur (başlangıç ve bitiş).");
            if (fromDate > toDate)
                return CustomReportValidation.Fail("Başlangıç tarihi bitiş tarihinden sonra olamaz.");
        }

        // ANA VERİ: tarih yoktur; sınırsız sorguyu engellemek için EN AZ BİR filtre zorunludur.
        if (src.RequiresFilter)
        {
            var doluFiltre = (def.Filters ?? Array.Empty<CustomReportFilter>())
                .Count(f => !string.IsNullOrWhiteSpace(f.Value));
            if (doluFiltre == 0)
                return CustomReportValidation.Fail(
                    $"«{src.Label}» raporu tarih aralığı kullanmaz; bu yüzden en az bir filtre girilmelidir.");
        }

        return CustomReportValidation.Success;
    }
}
