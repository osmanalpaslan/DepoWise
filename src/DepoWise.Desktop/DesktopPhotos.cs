using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DepoWise.Application.Security;

namespace DepoWise.Desktop;

/// <summary>
/// ═══ MASAÜSTÜ FOTOĞRAF KATMANI — SUNUCU OTORİTELİ ═══ (ADR-182 · ARA İŞ 2 / S5, 2026-08-29)
///
/// <b>Kullanıcının bildirdiği sorun:</b> "Malzeme/araç fotoğrafı başka bir makineden, başka bir kullanıcı
/// eklediğinde ben aynı kaydı açtığımda fotoğrafı göremiyorum."
///
/// <b>Kök neden:</b> masaüstü fotoğrafı YALNIZ kendi diskine ve kendi yerel <c>file_records</c> tablosuna
/// yazıyordu. Bu tablo iş senkronunda YOKTUR ve ikili (binary) içerik hiçbir senkron paketinde taşınmaz →
/// ortada ÜÇ ayrı silo vardı (A makinesi diski · B makinesi diski · sunucu diski) ve birbirini görmeleri
/// bugünkü mimaride imkânsızdı. Web ise fotoğrafı zaten SUNUCUYA yüklüyordu.
///
/// <b>Çözüm (PK-F1=A):</b> Evrak modülünde (EVR-01) zaten kurulmuş olan "içerik sunucuda durur, iki platform
/// aynı API'yi çağırır" deseni fotoğraflara da uygulanır. Sunucu uçları HAZIRDI; masaüstü hiç çağırmıyordu.
/// Yeni tablo, yeni migration ve senkron sözleşmesi değişikliği GEREKMEZ.
///
/// <b>Çevrimdışı (PK-F4=A):</b> fotoğraf EKLEME çevrimiçi gerektirir ve kullanıcıya NET uyarı verilir
/// (kayıt yine kaydedilir). GÖRÜNTÜLEME çevrimdışıyken yereldeki eski fotoğraflara düşer — kullanıcı
/// bilgisiz kalmaz, ekranda "çevrimdışı" notu görür.
///
/// <b>Eski yerel fotoğraflar (PK-F5=A):</b> kayıt açıldığında yereldeki fotoğraflar sunucuya BİR KEZ
/// taşınır. Bu YALNIZ EKLEMEDİR: hiçbir kayıt silinmez/değiştirilmez ve içerik özeti (sha256) sunucuda
/// zaten varsa atlanır → mükerrer yükleme olmaz.
/// </summary>
public static class DesktopPhotos
{
    /// <summary>Ekranda gösterilecek fotoğraf: kimlik + içerik.</summary>
    public sealed record Yuklenen(string FileId, byte[] Bytes);

    /// <summary>Yükleme sonucu — çağıran ekran duruma göre kullanıcıya mesaj gösterir.</summary>
    public sealed record YuklemeSonucu(int Eklenen, bool Cevrimdisi, string? Hata);

    /// <summary>Varlık türü → API yol parçası. Malzeme ve araç AYNI altyapıyı kullanır (tek FileService).</summary>
    public static string ApiEntity(string entityType) => entityType == "vehicle" ? "vehicles" : "materials";

    /// <summary>
    /// Kaydın fotoğraflarını getirir. Sıra: (1) sunucu listesi → (2) yerelde kalmış eskiler varsa BİR KEZ
    /// sunucuya taşı (PK-F5=A) → (3) içerikleri sunucudan indir. Sunucuya ulaşılamazsa YERELE düşer ve
    /// <c>Cevrimdisi=true</c> döner (çağıran kullanıcıyı bilgilendirir).
    /// </summary>
    public static async Task<(List<Yuklenen> Fotograflar, bool Cevrimdisi)> YukleAsync(
        SessionContext s, string entityType, string entityId)
    {
        var api = ApiEntity(entityType);
        var uzak = await OrgServerClient.ListPhotosAsync(api, entityId);
        if (uzak is null) return (YerelOku(s, entityType, entityId), true);   // çevrimdışı → yerel kopya

        if (await TasiEskileriAsync(s, entityType, entityId, uzak) > 0)
            uzak = await OrgServerClient.ListPhotosAsync(api, entityId) ?? uzak;

        var liste = new List<Yuklenen>();
        foreach (var p in uzak)
        {
            var bytes = await OrgServerClient.DownloadPhotoAsync(api, entityId, p.Id);
            if (bytes is not null) liste.Add(new Yuklenen(p.Id, bytes));
        }
        return (liste, false);
    }

    /// <summary>Forma eklenen yeni fotoğrafları SUNUCUYA yükler. Çevrimdışıysa yerele YAZILMAZ
    /// (aksi hâlde yine yalnız bu makinede kalır ve kullanıcı yüklendiğini sanırdı) — çağıran uyarır.</summary>
    public static async Task<YuklemeSonucu> KaydetAsync(string entityType, string entityId, IEnumerable<string> yerelYollar)
    {
        var api = ApiEntity(entityType);
        int eklenen = 0;
        foreach (var yol in yerelYollar)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(yol); }
            catch (Exception ex) { return new YuklemeSonucu(eklenen, false, ex.Message); }

            var r = await OrgServerClient.UploadPhotoAsync(api, entityId, Path.GetFileName(yol), MimeTahmin(yol), bytes);
            if (r.Offline) return new YuklemeSonucu(eklenen, true, null);
            if (!r.Ok) return new YuklemeSonucu(eklenen, false, r.Error);
            eklenen++;
        }
        return new YuklemeSonucu(eklenen, false, null);
    }

    /// <summary>Kayıtlı fotoğrafı SUNUCUDAN siler. Yetki kapısı sunucudadır (Delete); arayüz kilidi
    /// güvenlik sayılmaz — arayüz ayrıca "yalnız Düzenle modunda" kuralını uygular (PK-F3).</summary>
    public static Task<OrgServerClient.Result> SilAsync(string entityType, string entityId, string fileId)
        => OrgServerClient.DeletePhotoAsync(ApiEntity(entityType), entityId, fileId);

    /// <summary>PK-F5=A — yereldeki eski fotoğrafları sunucuya BİR KEZ taşır. Yalnız EKLEME yapar;
    /// içerik özeti sunucuda varsa atlar. Başarısızlık sessizdir: görüntüleme akışı bozulmaz.</summary>
    private static async Task<int> TasiEskileriAsync(SessionContext s, string entityType, string entityId,
        IReadOnlyList<OrgServerClient.RemotePhoto> uzak)
    {
        var api = ApiEntity(entityType);
        var uzakOzet = uzak.Where(x => !string.IsNullOrEmpty(x.Sha256))
                           .Select(x => x.Sha256!)
                           .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int tasinan = 0;
        try
        {
            foreach (var f in DesktopServices.Files.GetPhotos(s, entityType, entityId))
            {
                if (!string.IsNullOrEmpty(f.Sha256) && uzakOzet.Contains(f.Sha256!)) continue;   // zaten sunucuda
                byte[] bytes;
                try { bytes = DesktopServices.Storage.Read(f.StorageKey); }
                catch { continue; }   // yerel dosya kayıpsa taşıyacak bir şey yok
                var r = await OrgServerClient.UploadPhotoAsync(api, entityId,
                    Path.GetFileName(f.StorageKey), f.Mime, bytes);
                if (r.Offline) break;
                if (r.Ok) tasinan++;
            }
        }
        catch { /* yerel okuma/yetki sorunu → taşıma atlanır, görüntüleme etkilenmez */ }
        return tasinan;
    }

    /// <summary>Çevrimdışı görüntüleme: bu makinede kalmış fotoğraflar.</summary>
    private static List<Yuklenen> YerelOku(SessionContext s, string entityType, string entityId)
    {
        var liste = new List<Yuklenen>();
        try
        {
            foreach (var f in DesktopServices.Files.GetPhotos(s, entityType, entityId))
            {
                try { liste.Add(new Yuklenen(f.Id, DesktopServices.Storage.Read(f.StorageKey))); }
                catch { }
            }
        }
        catch { }
        return liste;
    }

    private static string MimeTahmin(string yol)
        => Path.GetExtension(yol).ToLowerInvariant() is ".png" ? "image/png" : "image/jpeg";
}
