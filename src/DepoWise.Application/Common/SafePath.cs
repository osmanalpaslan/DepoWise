namespace DepoWise.Application.Common;

/// <summary>
/// Kullanıcıdan/istekten gelen bir kimliğin dosya yoluna girdiği YERLER için tek güvenlik kapısı
/// (YOL-01, 2026-08-26).
///
/// <para><b>Neden var:</b> firma kimliği gibi değerler klasör adı olarak kullanılıyor
/// (<c>{veriKökü}/files/{firmaKimliği}</c>). Kimlik <c>".."</c> olursa çözülen yol köke — hatta kökün
/// üstüne — çıkar; ardından gelen özyinelemeli silme <b>bütün firmaların</b> verisini götürür.</para>
///
/// <para><b>Kural:</b> çözülen tam yol, taban klasörün ALTINDA olmalıdır. Tabanın kendisi de kabul
/// edilmez (boş kimlik → taban klasör → toplu silme). Aksi hâlde <c>null</c> döner; çağıran yalnız
/// <c>null</c> değilse işlem yapar (fail-closed).</para>
///
/// <para>Aynı desen <c>LocalFileStorageProvider</c> içinde zaten uygulanıyordu; bu sınıf onu tek bir
/// yerde toplayıp API'daki silme çağrılarında da kullanılabilir hâle getirir.</para>
/// </summary>
public static class SafePath
{
    /// <summary>
    /// <paramref name="root"/> altındaki <paramref name="segments"/> yolunu çözer.
    ///
    /// <para><b>Taban</b>, son parça HARİÇ tüm parçalardır (<c>root/files</c> gibi). Çözülen yol bu tabanın
    /// ALTINDA değilse <c>null</c> döner. Yalnız "kökün altında" demek YETMEZ: <c>root/files/../ust</c>
    /// kökün altındadır ama <c>files</c> klasöründen ÇIKMIŞTIR ve başka bir klasörü silmeye yönlendirir.</para>
    /// </summary>
    public static string? UnderRoot(string root, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(root) || segments is null || segments.Length == 0) return null;
        foreach (var s in segments)
            if (string.IsNullOrWhiteSpace(s)) return null;   // boş parça → taban klasörün kendisi

        string tam, taban;
        try
        {
            var parcalar = new string[segments.Length + 1];
            parcalar[0] = root;
            Array.Copy(segments, 0, parcalar, 1, segments.Length);
            tam = Path.GetFullPath(Path.Combine(parcalar));

            // Taban = son parça hariç (son parça, dışarı çıkamayacağı klasörün İÇİNDE olmalı).
            taban = Path.GetFullPath(Path.Combine(parcalar[..^1]));
        }
        catch { return null; }   // geçersiz karakter / çok uzun yol vb.

        if (!taban.EndsWith(Path.DirectorySeparatorChar)) taban += Path.DirectorySeparatorChar;

        // Tabanın ALTINDA mı? (tabanın kendisi kabul edilmez)
        return tam.StartsWith(taban, StringComparison.Ordinal) ? tam : null;
    }

    /// <summary>
    /// Klasör/dosya adı olarak kullanılabilecek kimlik mi? (harf, rakam, <c>-</c>, <c>_</c>)
    /// Üretimdeki firma kimlikleri onaltılık GUID'dir; masaüstünün çevrimdışı ürettiği kimlikler de öyle.
    /// </summary>
    public static bool IsSafeId(string? id)
        => !string.IsNullOrWhiteSpace(id)
           && id!.Length <= 64
           && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_');
}
