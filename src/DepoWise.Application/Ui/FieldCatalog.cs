using System.Collections.Generic;
using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ ALAN KATALOĞU — TEK DOĞRU KAYNAK (kullanıcı isteği 2026-09-03) ═══
///
/// "Alan Ayarları" ekranının listelediği form alanları: hangi ekranda hangi alanlar var, hangileri
/// SİSTEM zorunlusu (asla gevşetilemez), hangileri firma isteğiyle zorunlu yapılabilir.
///
/// Kurallar:
///  • <b>SystemRequired=true</b> → iş kuralı alanıdır; listede kilitli gösterilir, DEĞİŞTİRİLEMEZ
///    (servis de reddeder). Gevşetilseydi mevcut servis doğrulamaları patlar, veri bütünlüğü bozulurdu.
///  • SystemRequired=false → varsayılan OPSİYONELDİR; firma "zorunlu" işaretleyebilir. Yapı yalnız
///    SIKILAŞTIRIR — hiçbir mevcut davranışı gevşetmez (yayın günü hiçbir form değişmez).
///  • KALICI KURAL (2026-09-03): forma yeni bir alan eklendiğinde buraya da satır eklenir → Alan
///    Ayarları ekranı kendiliğinden güncel kalır. İki platform da bu dosyayı derler.
///
/// FieldKey'ler ekran kodundaki alanlarla eşleşen SABİT anahtarlardır (DB kolon adı değil, form alanı
/// kimliğidir); doğrulama katmanları bu anahtarla okur.
/// </summary>
public static class FieldCatalog
{
    public sealed record FieldDef(string ScreenKey, string ScreenLabel, string FieldKey, string Label, bool SystemRequired);

    /// <summary>V1 kapsamı (2026-09-03): Araçlar · Malzemeler · Yakıt Dağıtımı. Yeni ekran/alan
    /// eklendikçe bu liste büyür (kalıcı kural) — ekran otomatik olarak Alan Ayarları'nda görünür.</summary>
    public static readonly IReadOnlyList<FieldDef> All = new[]
    {
        // ── ARAÇLAR ──────────────────────────────────────────────────────────────────────────────
        new FieldDef("vehicles", "Araçlar", "internal_code", "İç Kod", SystemRequired: true),
        new FieldDef("vehicles", "Araçlar", "branch", "Şantiye / Şube", SystemRequired: true),
        new FieldDef("vehicles", "Araçlar", "plate", "Plaka", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "production_year", "Üretim Yılı", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "vehicle_type", "Makine Tipi", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "category", "Kategori", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "brand", "Marka", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "model", "Model", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "driver", "Sürücü", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "chassis_no", "Şasi No", SystemRequired: false),
        new FieldDef("vehicles", "Araçlar", "engine_no", "Motor No", SystemRequired: false),

        // ── MALZEMELER ───────────────────────────────────────────────────────────────────────────
        new FieldDef("materials", "Malzemeler", "code", "Malzeme Kodu", SystemRequired: true),
        new FieldDef("materials", "Malzemeler", "name", "Malzeme Adı", SystemRequired: true),
        new FieldDef("materials", "Malzemeler", "category", "Kategori", SystemRequired: false),
        new FieldDef("materials", "Malzemeler", "sub_category", "Alt Kategori", SystemRequired: false),
        new FieldDef("materials", "Malzemeler", "unit", "Birim", SystemRequired: false),
        new FieldDef("materials", "Malzemeler", "brand", "Marka", SystemRequired: false),
        new FieldDef("materials", "Malzemeler", "supplier", "Tedarikçi", SystemRequired: false),
        new FieldDef("materials", "Malzemeler", "min_stock", "Kritik Stok", SystemRequired: false),
        new FieldDef("materials", "Malzemeler", "unit_price", "Birim Fiyat", SystemRequired: false),
        new FieldDef("materials", "Malzemeler", "description", "Açıklama", SystemRequired: false),

        // ── YAKIT DAĞITIMI ───────────────────────────────────────────────────────────────────────
        new FieldDef("fuel", "Yakıt Dağıtımı", "vehicle", "Araç", SystemRequired: true),
        new FieldDef("fuel", "Yakıt Dağıtımı", "liters", "Litre", SystemRequired: true),
        new FieldDef("fuel", "Yakıt Dağıtımı", "personnel", "Yakıtı Veren", SystemRequired: true),
        new FieldDef("fuel", "Yakıt Dağıtımı", "recipient", "Yakıtı Alan", SystemRequired: false),
    };

    /// <summary>Ekran bazında gruplanmış görünüm (Alan Ayarları ekranının listesi).</summary>
    public static IEnumerable<IGrouping<(string ScreenKey, string ScreenLabel), FieldDef>> ByScreen()
        => All.GroupBy(f => (f.ScreenKey, f.ScreenLabel));

    /// <summary>Katalogda böyle bir (ekran, alan) var mı ve firma tarafından değiştirilebilir mi?</summary>
    public static FieldDef? Find(string screenKey, string fieldKey)
        => All.FirstOrDefault(f => f.ScreenKey == screenKey && f.FieldKey == fieldKey);
}
