namespace DepoWise.Infrastructure.Organization;

/// <summary>
/// SIF-03 (2026-08-18) — İŞ VERİSİ TEMİZLİĞİNİN EKSİK KALAN PARÇALARI.
///
/// <b>NEDEN VAR:</b> "firma iş verisini sıfırla" ve "yereli sıfırla" akışları silinecek tabloları
/// <see cref="Sync.BusinessSyncService.Tables"/> listesinden okuyordu. O liste ise <b>SENKRON</b>
/// sözleşmesidir — taşınacak tabloları sayar, silinecekleri değil. Aradaki fark yüzünden şu iki grup
/// temizlikte ATLANIYORDU:
///
/// <list type="number">
///   <item><see cref="CompanyScopedExtras"/> — <c>company_id</c> kolonu OLAN ama senkronda taşınmayan
///     tablolar (bakiye türetilmiş, sayaç/muayene/log makineye özel…). Sıfırlama sonrası ekranda
///     "eski stok bakiyesi / eski muayene kaydı" olarak görünüyordu.</item>
///   <item><see cref="OrphanChildren"/> — <c>company_id</c> kolonu OLMAYAN satır/bağlantı tabloları.
///     Temizlik <c>WHERE company_id=@c</c> ile yürüdüğü ve SQLite yolunda yabancı anahtarlar
///     kapatıldığı (<c>PRAGMA foreign_keys=OFF</c>) için bunlara HİÇ dokunulmuyordu.</item>
/// </list>
///
/// <b>PostgreSQL'de sorun yoktu</b> (DialectPurge yabancı-anahtar zinciriyle çocukları da siler);
/// bu katman SQLite yolunu (masaüstü + SQLite'a düşmüş sunucu) aynı sonuca getirir.
///
/// ⚠️ Bu liste <see cref="Sync.BusinessSyncService.Tables"/>'ın YERİNE GEÇMEZ, ONA EK'tir.
/// Senkron sözleşmesi değişmez — bir tabloyu taşınır yapmak istiyorsan orayı düzenle.
/// </summary>
public static class BusinessDataExtras
{
    /// <summary>Firma iş verisidir, <c>company_id</c> taşır, ama senkron listesinde YOKTUR.</summary>
    public static readonly string[] CompanyScopedExtras =
    {
        "stock_balances",        // SNK-11: türetilmiş bakiye — senkronda taşınmaz, ama sıfırlamada SİLİNMELİ
        "vehicle_inspections",   // muayene / sigorta / kasko
        "vehicle_meter_logs",    // araç sayaç geçmişi
        "stock_change_logs",     // doğrudan stok değişikliği uyarı kaydı
        "file_records",          // dosya / fotoğraf künyeleri
        "material_templates",    // malzeme şablonları
        "vehicle_templates",     // araç genel tanımları
    };

    /// <summary>
    /// <c>company_id</c> kolonu OLMAYAN çocuk tablolar: (çocuk, çocuktaki yabancı anahtar, ebeveyn).
    /// Temizlik bunları <b>öksüz kalma</b> ölçütüyle siler — ebeveyni artık var olmayan satırlar.
    /// Bu ölçüt firma-güvenlidir: başka firmanın ebeveyni durduğu sürece onun çocuğuna dokunulmaz.
    /// </summary>
    public static readonly (string Child, string Fk, string Parent)[] OrphanChildren =
    {
        ("maintenance_materials", "maintenance_id", "vehicle_maintenances"),
        ("material_request_items", "request_id", "material_requests"),
        ("request_status_history", "request_id", "material_requests"),
        ("stock_count_lines", "document_id", "stock_documents"),
        ("material_equivalents", "material_id", "materials"),
        ("material_compatible_vehicles", "material_id", "materials"),
        ("maintenance_definition_vehicles", "definition_id", "maintenance_definitions"),
        ("vehicle_template_materials", "template_id", "vehicle_templates"),
    };
}
