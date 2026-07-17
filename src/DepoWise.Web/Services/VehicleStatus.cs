namespace DepoWise.Web.Services;

/// <summary>
/// Araç durumu — TEK KAYNAK (2026-07-16). Önceden bu liste 5 ayrı yerde elle tekrarlanıyordu
/// (masaüstü VM, web formu, web listesi, import eşlemesi, rozet rengi) ve yeni bir durum eklemek
/// hepsini tek tek bulmayı gerektiriyordu — biri unutulursa durum ekranda ham kod olarak görünürdü.
///
/// Veritabanında serbest TEXT'tir (CHECK kısıtı YOK) → yeni değer eklemek migration gerektirmez.
///
/// NOT: Bu dosya, masaüstü/sunucu tarafındaki <c>DepoWise.Application/Ui/VehicleStatus.cs</c>'in
/// AYNASIDIR (web projesinin Application'a referansı yoktur). İkisi BİRLİKTE güncellenmelidir.
/// </summary>
public static class VehicleStatus
{
    public const string Active = "active";
    public const string Passive = "passive";
    public const string Maintenance = "maintenance";
    /// <summary>Arızalı — araç çalışmıyor ama henüz bakıma alınmamış (kullanıcı isteği 2026-07-16).</summary>
    public const string Faulty = "faulty";

    /// <summary>Seçim kutularının kaynağı: (kod, görünen ad). Sıra ekranda göründüğü sıradır.</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> All = new[]
    {
        (Active, "Aktif"),
        (Passive, "Pasif"),
        (Maintenance, "Bakımda"),
        (Faulty, "Arızalı"),
    };

    /// <summary>Kod → görünen ad. Bilinmeyen kod ham hâliyle döner (veri kaybolmasın).</summary>
    public static string Label(string? code)
    {
        foreach (var (c, l) in All) if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase)) return l;
        return code ?? "";
    }

    /// <summary>Durum, araç için "çalışmıyor" anlamına mı geliyor? (Bakımda + Arızalı)
    /// "Durum Açıklaması" alanı yalnız bu durumlarda anlamlıdır.</summary>
    public static bool NeedsNote(string? code)
        => string.Equals(code, Maintenance, StringComparison.OrdinalIgnoreCase)
        || string.Equals(code, Faulty, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Excel'den gelen serbest metni durum koduna çevirir (içe aktarım). Türkçe/İngilizce + yaygın
    /// yazımlar kabul edilir; BOŞ ise <see cref="Active"/> (araç varsayılan olarak çalışır durumdadır).
    /// Tanınmayan metin için null döner → çağıran satırı REDDEDER (sessizce "aktif" yazmaz).
    /// </summary>
    public static string? Parse(string? text)
    {
        var t = (text ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return Active;
        return t switch
        {
            "aktif" or "active" or "çalışıyor" or "calisiyor" => Active,
            "pasif" or "passive" or "pasive" => Passive,
            "bakımda" or "bakimda" or "bakım" or "bakim" or "maintenance" => Maintenance,
            "arızalı" or "arizali" or "arıza" or "ariza" or "bozuk" or "faulty" or "broken" => Faulty,
            _ => null,
        };
    }
}
