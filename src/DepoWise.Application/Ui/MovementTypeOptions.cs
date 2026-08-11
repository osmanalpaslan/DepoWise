namespace DepoWise.Application.Ui;

/// <summary>
/// STOK HAREKET TÜRÜ — TEK DOĞRU KAYNAK (`STK-B1`, 2026-08-11).
///
/// <b>Çözdüğü sorun.</b> Aynı <c>movement_type</c> değeri kullanıcıya ÜÇ ayrı yerde, ÜÇ FARKLI
/// biçimde gösteriliyordu ve üçü de eksikti:
/// <list type="bullet">
///   <item><c>StockService.StockMovementRow.TypeText</c> (masaüstü hareket listeleri) — 5/8 tür</item>
///   <item><c>StockService.RecentForMaterial</c> (malzeme kartı "Son Hareketler", İKİ platform) — 6/8 tür</item>
///   <item><c>Web/StockMovements.razor</c> (web hareket listesi) — 6/8 tür + ölü bir <c>count</c> dalı</item>
/// </list>
/// Sonuç: <c>adjustment</c> masaüstünde "Düzeltme", diğer ikisinde "Sayım Düzeltme" görünüyordu;
/// <c>reverse</c> üç yerde sırasıyla ham <c>"reverse"</c> / "İptal (ters)" / "İptal" idi;
/// <c>usage</c> ve <c>usage_reverse</c> ise ÜÇÜNDE DE ham İngilizce sızıyordu (BKM-04 bunları
/// görünür hâle getirdi — artık her bakım tüketimi gerçek depolu bir <c>usage</c> satırı üretiyor).
///
/// <b>Neden bu dosya hem Application hem Web tarafından derleniyor.</b> Web bilinçli olarak tek
/// başınadır (Application'a proje referansı YOKTUR, her şeyi API'den alır). Bu katalog SAF veridir —
/// hiçbir <c>using</c>'i, bağımlılığı veya davranışı yoktur — ve Razor'da <c>switch</c>/etiket olarak
/// derleme zamanında gerekir. Bu yüzden <c>ListColumns</c> ve <c>RequestOperationStatus</c> ile AYNI
/// yöntemle paylaşılır: <b>tek dosya, iki projede derlenir</b> (bkz. <c>DepoWise.Web.csproj</c>).
/// Ayna dosya tutulmaz → iki liste birbirinden ıraksayamaz.
///
/// ⚠️ <b>KAPSAM:</b> Bu yalnız GÖSTERİM katmanıdır. Veritabanındaki <c>movement_type</c> DEĞERLERİ
/// değişmez, migration yoktur, hareket üretim iş mantığı aynıdır (`STK-B1` sınırı).
/// ⚠️ <c>count</c> BU LİSTEDE YOKTUR ve olmamalıdır: o bir <c>stock_documents.doc_type</c> değeridir
/// (sayım belgesi), <c>movement_type</c> değil. Sayım belgesi defterde <c>adjustment</c> hareketi üretir.
/// </summary>
public static class MovementTypeOptions
{
    public const string Opening = "opening";
    public const string In = "in";
    public const string Out = "out";
    public const string Transfer = "transfer";
    /// <summary>Sayım farkı hareketi — YALNIZ <c>StockService.Count</c> üretir.</summary>
    public const string Adjustment = "adjustment";
    /// <summary>Bakımda tüketilen malzeme (BKM-04) — <c>MaintenanceService</c> üretir.</summary>
    public const string Usage = "usage";
    /// <summary>Bakım iptalinde tüketimin geri alınması (BKM-04). <see cref="Reverse"/> ile AYNI ŞEY DEĞİLDİR.</summary>
    public const string UsageReverse = "usage_reverse";
    /// <summary>Stok BELGESİNİN ters kaydı — <c>StockService.ReverseDocument</c> üretir.
    /// Proje terminolojisinde "Ters Kayıt" (bkz. <c>AuditLogService</c>, <c>AppModules.Reverse</c>).</summary>
    public const string Reverse = "reverse";

    /// <summary>
    /// Üretimde gerçekten üretilebilen TÜM hareket türleri (kaynak koddan doğrulandı, 2026-08-11).
    /// Sıra, kullanıcıya gösterilecek sıradır: önce stok akışı, sonra bakım, sonra iptaller.
    ///
    /// Üreten yollar: <c>opening</c> → OpeningStockService · <c>in/out/transfer/adjustment</c> →
    /// StockService.ApplyLine · <c>reverse</c> → StockService.ReverseDocument ·
    /// <c>usage/usage_reverse</c> → MaintenanceService.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string Label)> All = new[]
    {
        (Opening, "Açılış"),
        (In, "Giriş"),
        (Out, "Çıkış"),
        (Transfer, "Transfer"),
        (Adjustment, "Sayım Düzeltme"),
        (Usage, "Bakım Tüketimi"),
        (UsageReverse, "Bakım Tüketimi İptali"),
        (Reverse, "İptal (Ters Kayıt)"),
    };

    /// <summary>Hareket türü → kullanıcıya dönük Türkçe etiket.
    ///
    /// Bilinmeyen değer OLDUĞU GİBİ döner: sessizce "Diğer" demek, katalogdan atlanmış yeni bir türü
    /// GİZLERDİ. Ham değerin ekranda görünmesi istenmeyen ama FARK EDİLİR bir durumdur; kalıcı koruma
    /// testi (<c>MovementTypeCatalogTests</c>) üretim kodunu tarayıp katalogda olmayan tür bulursa kırılır.</summary>
    public static string Label(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        foreach (var (k, l) in All) if (k == key) return l;
        return key;
    }
}
