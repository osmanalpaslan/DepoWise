namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ⭐ FAZ 4.3 (kullanıcı isteği 2026-09-06) — <b>"HER KAYDIN KENDİ LOG EKRANI" SÖZLEŞMESİ.</b>
///
/// Bir liste ekranı bu arayüzü uygularsa, kabuktaki <i>"Seçili Kaydın Geçmişi"</i> menüsü o ekranda
/// çalışır ve SEÇİLİ kaydın tüm geçmişini (alan bazlı farklarıyla) açar.
///
/// <b>Neden arayüz?</b> Her ekrana ayrı düğme/komut/pencere yazmak 20'den fazla dosyada tekrar demekti.
/// Tek sözleşme + tek pencere ile ekranlar yalnız "hangi kaydı seçtim" bilgisini bildirir; log okuma,
/// yetki ve gösterim tek yerde kalır.
///
/// <b>Yetki.</b> Bu arayüz yetki VERMEZ. Asıl kapı serviste (<c>AuditLogService.ForEntity</c>):
/// <c>btn-screen-log</c> düğme yetkisi + kaydın ait olduğu ekranda <c>View</c>.
/// </summary>
public interface IKayitLoguKaynagi
{
    /// <summary>Seçili kaydın log tipi (<c>audit_logs.entity_type</c>), ör. "vehicle".</summary>
    string? LogEntityType { get; }

    /// <summary>Seçili kaydın kimliği. Seçim yoksa <c>null</c> — kullanıcıya "önce kayıt seçin" denir.</summary>
    string? LogEntityId { get; }

    /// <summary>Pencere başlığında görünecek okunur ad (plaka, malzeme adı…). Boşsa tip adı kullanılır.</summary>
    string? LogKayitAdi { get; }
}
