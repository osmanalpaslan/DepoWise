using System.Collections.Generic;
using System.Linq;
using DepoWise.Application.Ui;

namespace DepoWise.Application.Security;

/// <summary>
/// ═══ GÜNLÜK FAALİYET KAYIT TİPİ YETKİSİ (kullanıcı isteği 2026-09-03) ═══
///
/// Kullanıcı: "kayıt tipine yetki verilmemiş ise kayıt tipi görünmemeli; yetki ağacında anlaşılır
/// şekilde kategorize ederek yetkiye bağla."
///
/// <b>GEÇİŞ GÜVENLİ kural (rapor kalemleriyle aynı felsefe):</b> kullanıcıya HİÇ tip anahtarı
/// verilmemişse TÜM tipler görünür (bugünkü davranış — yayında kimse bir şey kaybetmez). En az bir
/// tip anahtarı verildiği anda kullanıcı YALNIZ verilen tipleri görür/seçer. Admin mevcut bypass
/// kuralıyla her tipi görür. Anahtarlar <see cref="DailyActivityTypeOptions"/>'tan ÜRETİLİR →
/// yeni tip eklenince yetki kalemi kendiliğinden doğar (kalıcı kural). Migration GEREKMEZ.
///
/// ⚠️ Bu sınıf BİLEREK <c>DailyActivityTypeOptions</c>'ın dışındadır: o dosya web projesine
/// bağlanıp derlenir ve AccessControl'e bağımlılık alamaz; bu sınıf yalnız sunucu/masaüstü derler.
/// </summary>
public static class DailyActivityTypeGate
{
    public const string Prefix = "datype_";

    public static string Key(string typeKey) => Prefix + typeKey;

    public static bool IsTypeKey(string moduleKey) => moduleKey.StartsWith(Prefix, System.StringComparison.Ordinal);

    /// <summary>Yetki ağacı kalemleri (tip kataloğundan üretilir; etiket kullanıcı diliyle).</summary>
    public static IReadOnlyList<(string Key, string Label)> Items { get; } =
        DailyActivityTypeOptions.All.Select(t => (Key(t.Key), "Günlük Faaliyet › " + t.Label)).ToList();

    /// <summary>Kullanıcıya en az bir tip anahtarı AÇIKÇA verilmiş mi? (Admin bypass'ı SAYILMAZ —
    /// admin zaten her tipi görür; kısıt yalnız açık atamayla başlar.)</summary>
    private static bool AcikTipAtamasiVar(SessionContext s)
        => DailyActivityTypeOptions.All.Any(t =>
        {
            var p = s.Permissions.For(Key(t.Key));
            return p is not null && p.CanView;
        });

    /// <summary>Bu kullanıcı bu kayıt tipini görebilir/seçebilir mi?</summary>
    public static bool CanSeeType(SessionContext s, string typeKey)
    {
        if (!AcikTipAtamasiVar(s)) return true;                       // hiç atama yok → tüm tipler (mevcut davranış)
        return AccessControl.Can(s, Key(typeKey), PermissionAction.View);
    }

    /// <summary>Kullanıcının görebildiği tip anahtarları (UI seçim listeleri bunun üzerinden süzer).</summary>
    public static IReadOnlyList<string> AllowedTypes(SessionContext s)
        => DailyActivityTypeOptions.All.Select(t => t.Key).Where(k => CanSeeType(s, k)).ToList();

    /// <summary>Liste satırı görünür mü? Satırın DB tipi (activity_type + movement_kind) UI tipine çevrilir.</summary>
    public static bool CanSeeRow(SessionContext s, string activityType, string? movementKind)
    {
        var uiTip = activityType == "movement"
            ? (movementKind == DailyActivityTypeOptions.Transfer ? DailyActivityTypeOptions.Transfer : DailyActivityTypeOptions.Movement)
            : activityType;
        return CanSeeType(s, uiTip);
    }
}
