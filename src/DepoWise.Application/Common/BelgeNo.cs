namespace DepoWise.Application.Common;

/// <summary>
/// ═══ FAZ K (2026-09-05) — BELGE NUMARASI ALANI: ORTAK NORMALLEŞTİRME + SINIR ═══
///
/// <b>Bulgu (uçtan uca denetim, protokol §5 "karakter limiti backend'de de korunuyor mu"):</b>
/// belge/fatura/irsaliye numarası alanlarının <b>hiçbirinde uzunluk sınırı yoktu</b> — ne bu gece
/// eklenenlerde (yakıt dağıtımı, araç ve ekipman bakımı) ne de daha eskilerde (stok belgesi).
/// Kullanıcı yanlışlıkla uzun bir metni bu alana yapıştırırsa:
/// <list type="bullet">
///   <item>satır gereksiz yere şişer ve senkron paketine her turda girer,</item>
///   <item>liste ve Excel çıktısında hücre okunamaz hâle gelir,</item>
///   <item>hiçbir yerde uyarı çıkmaz — sessizce kabul edilir.</item>
/// </list>
///
/// <b>Neden 100 karakter:</b> Türkiye'de belge numarası seri + sıra biçimindedir
/// (ör. <c>ABC2026000000123</c>, 16 karakter). 100, gerçek hiçbir belgeyi kesmeyecek kadar geniş,
/// yanlışlıkla yapıştırılan bir paragrafı yakalayacak kadar dar. Biçim <b>DAYATILMAZ</b> — satıcıya
/// göre değişir; yalnız uzunluk sınırlanır.
///
/// <b>Neden ortak yardımcı:</b> aynı kural beş ayrı serviste tekrarlanacaktı; tekrarlanan kural
/// zamanla ayrışır (biri kırpar, biri kırpmaz). Tek kaynak.
///
/// <b>Kapı SERVİS katmanındadır</b> (arayüzde değil): masaüstü servisleri ÇEVRİMDIŞI da çağırır —
/// yalnız API'de olsaydı o yol korumasız kalırdı. (STK-03/BKM-04'te alınan aynı karar.)
/// </summary>
public static class BelgeNo
{
    /// <summary>Gerçek belge numaralarının çok üstünde, yanlışlıkla yapıştırılan metnin altında.</summary>
    public const int EnFazlaUzunluk = 100;

    /// <summary>
    /// Belge numarasını normalleştirir: kenar boşlukları kırpılır, boş metin <c>null</c> olur
    /// (böylece <c>""</c> ile <c>NULL</c> iki ayrı "boş" hâline gelmez ve raporlar ikisini farklı saymaz).
    /// Sınırı aşarsa <see cref="ArgumentException"/> — <b>sessizce kırpılmaz</b>: kullanıcı yanlış
    /// alana yazdığını öğrenmelidir, verisi habersiz budanmamalıdır.
    /// </summary>
    public static string? Normalize(string? value, string alanAdi = "Belge numarası")
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (v.Length > EnFazlaUzunluk)
            throw new ArgumentException($"{alanAdi} en fazla {EnFazlaUzunluk} karakter olabilir (girilen: {v.Length}).");
        return v;
    }
}
