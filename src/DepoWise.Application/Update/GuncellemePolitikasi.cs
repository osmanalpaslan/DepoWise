namespace DepoWise.Application.Update;

public enum GuncellemeKarari { Yok, Serbest, Zorunlu }

/// <summary>Güncelleme kararı. <c>KalanGun</c>: Serbest'te zorunlu olmasına kalan tam gün; <c>Neden</c>: Zorunlu'da kullanıcıya metin.</summary>
public sealed record GuncellemeDurumu(GuncellemeKarari Karar, int KalanGun, string? Neden);

/// <summary>
/// ⭐ M-12 (AR-GE 2026-10-18, 05 F-042/F-156, kullanıcı kararı K-17 a) — ZORUNLU GÜNCELLEME POLİTİKASI.
///
/// "Ertele" sınırsızdı: çok eski bir masaüstü yıllarca eski kurallarla veri yazabiliyordu. Kural:
/// • Program sürümü yayının EN DÜŞÜK DESTEKLENEN sürümünün altındaysa → <b>hemen zorunlu</b> (kritik güvenlik/veri düzeltmesi
///   yayınında yayıncı en düşük sürümü yeni sürümün kendisine çeker).
/// • Güncelleme bu bilgisayarda İLK GÖRÜLDÜĞÜNDEN beri <see cref="AzamiErtelemeGun"/> günü geçtiyse → zorunlu.
/// • Aksi hâlde ertelenebilir (kalan gün söylenir).
/// Sunucu tarafı: desteklenmeyen sürüm VERİ GÖNDEREMEZ (<see cref="YazmaIzinli"/>, 426) — okuma serbesttir; veri yerelde korunur
/// ve güncellemeden sonra gönderilir.
/// </summary>
public static class GuncellemePolitikasi
{
    public const int AzamiErtelemeGun = 7;

    /// <summary>Masaüstünün her isteğe eklediği sürüm başlığı.</summary>
    public const string SurumBasligi = "X-DepoWise-Surum";

    /// <summary>Başlığı GÖNDERMEYEN son sürüm (M-12'den önceki masaüstleri). Başlık yoksa istemci bu sürümde sayılır.</summary>
    public const string BasliksizSurum = "1.0.202";

    public static GuncellemeDurumu Karar(string? mevcut, UpdatePackage? enSon, DateTimeOffset? ilkGorulme, DateTimeOffset simdi)
    {
        if (enSon is null || !SemVer.TryParse(enSon.Version, out var son) || !SemVer.TryParse(mevcut, out var cv)
            || son.CompareTo(cv) <= 0)
            return new GuncellemeDurumu(GuncellemeKarari.Yok, 0, null);

        // ⭐ M-01: imzası geçersiz yayın "güncelleme yok" sayılır — kurulamayacak bir paket zorunlu güncellemeye dönüşüp
        // masaüstünü kilitlemesin (imzasız sürüm zaten kurulmaz; sunucu da artık kabul etmez).
        if (!PaketImzasi.Dogrula(enSon.Version, enSon.ChecksumSha256, enSon.Signature))
            return new GuncellemeDurumu(GuncellemeKarari.Yok, 0, null);

        if (SemVer.TryParse(enSon.MinSupportedVersion, out var min) && cv.CompareTo(min) < 0)
            return new GuncellemeDurumu(GuncellemeKarari.Zorunlu, 0,
                $"Bu sürüm ({cv}) artık desteklenmiyor (en düşük {min}). Güncelleme kritik bir düzeltme içeriyor ve zorunludur.");

        var gecen = ilkGorulme is { } ilk ? (simdi - ilk).TotalDays : 0;
        if (gecen >= AzamiErtelemeGun)
            return new GuncellemeDurumu(GuncellemeKarari.Zorunlu, 0,
                $"Güncelleme {AzamiErtelemeGun} günden uzun süredir erteleniyor; artık zorunludur.");
        return new GuncellemeDurumu(GuncellemeKarari.Serbest, Math.Max(1, (int)Math.Ceiling(AzamiErtelemeGun - gecen)), null);
    }

    /// <summary>Sunucu: bu istemci sürümü veri yazabilir mi? En düşük sürüm tanımsız/geçersizse herkes yazar (bugünkü davranış).
    /// Başlık yoksa istemci <see cref="BasliksizSurum"/> sayılır; başlık bozuksa reddedilmez (yanlışlıkla kimse kilitlenmesin).</summary>
    public static bool YazmaIzinli(string? istemciSurumu, string? enDusuk)
    {
        if (!SemVer.TryParse(enDusuk, out var min)) return true;
        var surum = string.IsNullOrWhiteSpace(istemciSurumu) ? BasliksizSurum : istemciSurumu;
        if (!SemVer.TryParse(surum, out var v)) return true;
        return v.CompareTo(min) >= 0;
    }
}
