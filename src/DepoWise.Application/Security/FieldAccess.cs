using System;
using System.Collections.Generic;

namespace DepoWise.Application.Security;

/// <summary>
/// ═══ FAZ 3b-3 (ADR-223, 2026-09-05) — ALAN BAZLI ERİŞİM: TEK KARAR NOKTASI ═══
///
/// <b>İKİNCİ BİR YETKİ MOTORU DEĞİLDİR.</b> Karar, mevcut <see cref="AccessControl"/> üzerine
/// kurulur: alan izinleri <c>fld_&lt;ekran&gt;_&lt;alan&gt;</c> anahtarıyla mevcut
/// <c>user_permissions</c>/<c>role_permissions</c> satırlarında durur ve
/// <see cref="PermissionSnapshot"/> içinde zaten önbelleklenir. Yeni tablo, yeni önbellek,
/// yeni geçersiz kılma yolu YOKTUR.
///
/// <b>İki kademeli model (K1 bozulmadan gizleme):</b>
/// <list type="number">
///   <item><b>FİRMA</b>: alan "korumalı" mı? (<c>field_protections</c> → <see cref="SessionContext.ProtectedFields"/>)
///     Değilse → <b>bugünkü davranış</b>: görünür ve düzenlenebilir. Yetki hiç sorulmaz.</item>
///   <item><b>KULLANICI/ROL</b>: korumalı alanda deny-by-default; yalnız açık <c>fld_</c> izni açar.</item>
/// </list>
///
/// Böylece kısıtlama kararı yetki katmanında değil firma yapılandırmasında durur → rol izinleri
/// yalnız ALLOW üretmeye devam eder (K1) ve Faz 1 precedence sırası (K5) hiç değişmez.
///
/// <b>EDIT ⇒ VIEW (kullanıcı kararı D3):</b> göremediği alanı kimse düzenleyemez. Bu kural
/// <see cref="Duzenlenebilir"/> içinde uygulanır — "gizli ama düzenlenebilir" durumu OLUŞAMAZ.
/// Yazma yolunda ayrıca <see cref="GecerliMi"/> ile reddedilir.
///
/// <b>Performans (talimat §37):</b> karar iki sözlük aramasıdır — <b>O(1), veritabanı sorgusu YOK</b>.
/// Liste sorgularında satır başına değil, <b>sorgu başına bir kez</b> hesaplanmalıdır
/// (bkz. <see cref="IzinliAlanlar"/>). 10.000 satırda 10.000 kez çağırmak yapısal olarak gereksizdir.
///
/// <b>Admin bypass:</b> <see cref="AccessControl.Can"/> üzerinden gelir — admin ve süper admin
/// korumalı alanları görür. Bu bilinçlidir: firma yöneticisi kendi firmasının verisine erişir.
/// </summary>
public static class FieldAccess
{
    /// <summary>İzin anahtarı öneki. <c>rpt_</c> ve <c>datype_</c> ile aynı kanıtlanmış desen —
    /// serbest metin <c>module_key</c> sayesinde MIGRATION GEREKTİRMEZ.</summary>
    public const string Prefix = "fld_";

    /// <summary>Kanonik izin anahtarı. <b>Web ve masaüstü AYNI anahtarı kullanır</b> — aynı alanın
    /// iki platformda farklı anahtara sahip olmasına izin verilmez (kullanıcı şartı §6).</summary>
    public static string Key(string screenKey, string fieldKey) => Prefix + screenKey + "_" + fieldKey;

    public static bool IsFieldKey(string moduleKey) => moduleKey.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Korumalı alan kümesinin anahtar biçimi (<see cref="SessionContext.ProtectedFields"/>).</summary>
    public static string ProtectionKey(string screenKey, string fieldKey) => screenKey + "|" + fieldKey;

    /// <summary>
    /// Alan bu firmada KORUMALI mı? Değilse hiçbir yetki sorusu sorulmaz ve davranış bugünküyle
    /// birebir aynıdır — geri uyumluluğun tek cümlelik kaynağı budur.
    /// </summary>
    public static bool Korumali(SessionContext s, string screenKey, string fieldKey)
        => s.ProtectedFields is { Count: > 0 } p && p.Contains(ProtectionKey(screenKey, fieldKey));

    /// <summary>
    /// Kullanıcı bu alanı GÖREBİLİR mi?
    ///
    /// Korumasız alan → <c>true</c> (bugünkü davranış).
    /// Korumalı alan → yalnız açık <c>fld_</c> izni (View) ya da admin bypass.
    /// </summary>
    public static bool Gorunur(SessionContext s, string screenKey, string fieldKey)
    {
        if (!Korumali(s, screenKey, fieldKey)) return true;
        return AccessControl.Can(s, Key(screenKey, fieldKey), PermissionAction.View);
    }

    /// <summary>
    /// Kullanıcı bu alanı DÜZENLEYEBİLİR mi?
    ///
    /// ⭐ D3 — <b>EDIT ⇒ VIEW</b>: göremediği alanı düzenleyemez. Yetki kaydında yanlışlıkla
    /// "view=0, edit=1" bulunsa bile etkin sonuç <c>false</c>'tur; okumadan yazma OLUŞMAZ.
    /// </summary>
    public static bool Duzenlenebilir(SessionContext s, string screenKey, string fieldKey)
    {
        if (!Korumali(s, screenKey, fieldKey)) return true;
        if (!Gorunur(s, screenKey, fieldKey)) return false;          // ⭐ EDIT ⇒ VIEW
        return AccessControl.Can(s, Key(screenKey, fieldKey), PermissionAction.Edit);
    }

    /// <summary>
    /// Yazma yolunun kapısı. Alan düzenlenemiyorsa <b>sessizce yok sayılmaz, REDDEDİLİR</b> —
    /// kullanıcı gönderdiği değerin kaybolduğunu bilmelidir (Faz K'de kapatılan "sessiz kusur"
    /// sınıfının aynısı). Çağıran servis, değeri hiç göndermemişse bu metodu çağırmamalıdır.
    /// </summary>
    public static void RequireEdit(SessionContext s, string screenKey, string fieldKey, string alanAdi)
    {
        if (!Duzenlenebilir(s, screenKey, fieldKey))
            throw new ForbiddenException($"'{alanAdi}' alanını değiştirme yetkiniz yok.");
    }

    /// <summary>
    /// ⭐ YAZMA YOLUNUN KANONİK KURALI (ADR-223 · D3 uygulaması) — servisler bunu çağırır.
    ///
    /// Formlar TÜM alanları birlikte gönderir. Bu yüzden "yetkin yoksa 403" demek, alanı hiç
    /// GÖREMEYEN bir kullanıcının malzeme adını değiştirmesini bile imkânsız kılardı; sıfır
    /// gönderip kaydetmek ise <b>sessiz veri kaybı</b> olurdu. Üç durum ayrılır:
    ///
    /// <list type="table">
    ///   <item><term>Alan korumasız</term><description>Gönderilen değer yazılır — <b>bugünkü davranış</b>.</description></item>
    ///   <item><term>Görünmüyor</term><description>Gönderilen değer YOK SAYILIR, <b>kayıttaki değer korunur</b>.
    ///     Kullanıcı değeri hiç görmediği için gönderdiği şey anlamlı değildir; veri kaybı olamaz.</description></item>
    ///   <item><term>Görünüyor ama düzenlenemez</term><description>Değer <b>değiştirilmişse 403</b>
    ///     (sessizce yutulmaz — kullanıcı gördüğü değeri değiştirmeye çalışmıştır); aynıysa geçer.</description></item>
    /// </list>
    ///
    /// Böylece hem "sessiz kusur" hem de "kullanılamaz ekran" sınıfları kapanır.
    /// </summary>
    /// <param name="gonderilen">İstemcinin gönderdiği değer.</param>
    /// <param name="mevcut">Kayıtta duran değer (aynı transaction içinde okunmalıdır).</param>
    /// <returns>Veritabanına yazılacak ETKİN değer.</returns>
    public static T YazmaDegeri<T>(SessionContext s, string screenKey, string fieldKey,
        T gonderilen, T mevcut, string alanAdi)
    {
        if (!Korumali(s, screenKey, fieldKey)) return gonderilen;
        if (!Gorunur(s, screenKey, fieldKey)) return mevcut;                  // gizli → kayıttaki değer korunur
        if (Duzenlenebilir(s, screenKey, fieldKey)) return gonderilen;
        if (EqualityComparer<T>.Default.Equals(gonderilen, mevcut)) return mevcut;   // dokunmamış → engelleme
        throw new ForbiddenException($"'{alanAdi}' alanını değiştirme yetkiniz yok.");
    }

    /// <summary>
    /// ⭐ LİSTE YOLU İÇİN: bir ekranın izinli alan kümesini <b>bir kez</b> hesaplar.
    ///
    /// 10.000 satırlık listede satır başına karar vermek gereksizdir — izin satır verisine değil
    /// OTURUMA bağlıdır. Servisler sorgudan ÖNCE bunu çağırıp sonucu tüm satırlara uygular.
    /// </summary>
    public static IReadOnlySet<string> IzinliAlanlar(SessionContext s, string screenKey,
        IEnumerable<string> alanAnahtarlari)
    {
        var sonuc = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in alanAnahtarlari)
            if (Gorunur(s, screenKey, f)) sonuc.Add(f);
        return sonuc;
    }

    /// <summary>
    /// Yetki kaydının geçerliliği (yazma anında kontrol edilir). "Görünmez ama düzenlenebilir"
    /// anlamsızdır; D3 gereği böyle bir satır yazılmamalıdır.
    /// </summary>
    public static bool GecerliMi(bool canView, bool canEdit) => canView || !canEdit;
}
