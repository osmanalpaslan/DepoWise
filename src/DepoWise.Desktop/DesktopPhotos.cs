using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Files;

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

    /// <summary>Toplu taşıma sonucu — ekranda tek cümlede özetlenir.</summary>
    public sealed record TopluSonuc(int Toplam, int Yuklenen, int Atlanan, int Basarisiz, bool Cevrimdisi, string? Hata);

    /// <summary>
    /// ⭐ TOPLU TAŞIMA (kullanıcı isteği 2026-09-02) — BU MAKİNEDEKİ tüm yerel fotoğrafları sunucuya taşır.
    ///
    /// <b>Neden:</b> <see cref="TasiEskileriAsync"/> yalnız AÇILAN kayıt için çalışır. Bir makinede
    /// onlarca aracın fotoğrafı varsa hepsini tek tek açmak gerekiyordu; kullanıcı diğer makinesinde
    /// hiçbirini göremiyordu (canlıda sunucuda yalnız 8 araç fotoğrafı vardı).
    ///
    /// <b>Güvenlik/veri:</b> YALNIZ EKLEME yapar. Hiçbir yerel dosya veya kayıt silinmez/değiştirilmez.
    /// İçerik özeti (sha256) sunucuda zaten varsa o dosya ATLANIR → mükerrer yükleme olmaz, tekrar
    /// çalıştırmak zararsızdır (kesintide kaldığı yerden devam eder).
    /// Çevrimdışıysa hiçbir şey yapılmaz ve kullanıcıya bu söylenir.
    /// </summary>
    /// <param name="ilerleme">(işlenen, toplam) — arayüz yüzdeyi buradan günceller.</param>
    public static async Task<TopluSonuc> TumunuSunucuyaTasiAsync(
        SessionContext s, Action<int, int>? ilerleme = null)
    {
        List<FileRecordDto> yereller;
        try { yereller = DesktopServices.Files.GetAllLocalPhotos(s).ToList(); }
        catch (Exception ex) { return new TopluSonuc(0, 0, 0, 0, false, ex.Message); }

        if (yereller.Count == 0) return new TopluSonuc(0, 0, 0, 0, false, null);

        // Sunucudaki içerik özetleri, KAYIT BAŞINA TEK listeleme ile toplanır (dosya başına sorgu yok).
        var uzakOzet = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        int yuklenen = 0, atlanan = 0, basarisiz = 0, islenen = 0;

        foreach (var f in yereller)
        {
            islenen++;
            ilerleme?.Invoke(islenen, yereller.Count);

            var api = ApiEntity(f.EntityType);
            var anahtar = f.EntityType + "/" + f.EntityId;
            if (!uzakOzet.TryGetValue(anahtar, out var ozetler))
            {
                var uzak = await OrgServerClient.ListPhotosAsync(api, f.EntityId);
                if (uzak is null) return new TopluSonuc(yereller.Count, yuklenen, atlanan, basarisiz, true, null);
                ozetler = uzak.Where(x => !string.IsNullOrEmpty(x.Sha256))
                              .Select(x => x.Sha256!)
                              .ToHashSet(StringComparer.OrdinalIgnoreCase);
                uzakOzet[anahtar] = ozetler;
            }

            if (!string.IsNullOrEmpty(f.Sha256) && ozetler.Contains(f.Sha256!)) { atlanan++; continue; }

            byte[] bytes;
            try { bytes = DesktopServices.Storage.Read(f.StorageKey); }
            catch { basarisiz++; continue; }   // yerel dosya kayıp → sayılır, akış durmaz

            var r = await OrgServerClient.UploadPhotoAsync(api, f.EntityId,
                Path.GetFileName(f.StorageKey), f.Mime, bytes);
            if (r.Offline) return new TopluSonuc(yereller.Count, yuklenen, atlanan, basarisiz, true, null);
            if (r.Ok) { yuklenen++; if (!string.IsNullOrEmpty(f.Sha256)) ozetler.Add(f.Sha256!); }
            else basarisiz++;
        }

        return new TopluSonuc(yereller.Count, yuklenen, atlanan, basarisiz, false, null);
    }

    /// <summary>
    /// ⭐ AÇILIŞTA OTOMATİK TAŞIMA (kullanıcı isteği 2026-09-03 — "sunucuya neden gitmediğinin kaynağını
    /// tespit et ve yapıyı ONAR").
    ///
    /// <b>Kök neden:</b> ADR-182 öncesi fotoğraflar yükleyen makinenin yerel diskinde kalır; taşıma yalnız
    /// o kayıt O MAKİNEDE AÇILINCA çalışır. Baba kullanıcı kayıtları tek tek açmadığı için fotoğraflar
    /// hiç taşınmadı (canlı ölçüm: sunucuda 8 araç fotoğrafı vs makinede "neredeyse tüm araçlar").
    ///
    /// <b>Onarım:</b> uygulama açılışında (girişten sonra) taşıma ARKA PLANDA ve SESSİZCE bir kez çalışır —
    /// kullanıcı hiçbir şey yapmak zorunda değildir. Kurallar:
    ///  • Başarıyla biten taşımadan sonra yerel küme İMZALANIR (dosya kimliklerinin özeti); küme
    ///    değişmedikçe sonraki açılışlar HİÇ ağa çıkmaz (sıfır maliyet).
    ///  • Çevrimdışı/yarım kalırsa imza YAZILMAZ → sonraki açılışta kaldığı yerden dener.
    ///  • YALNIZ EKLEME: hiçbir yerel dosya silinmez; sunucuda olan atlanır (sha256).
    ///  • Açılışı YAVAŞLATMAZ: çağıran ateşle-unut kullanır; hata sessizdir (girişi asla bozmaz).
    /// </summary>
    public static async Task AcilistaSessizTasiAsync(SessionContext s)
    {
        try
        {
            var yereller = DesktopServices.Files.GetAllLocalPhotos(s);
            if (yereller.Count == 0) return;

            var imza = KumeImzasi(yereller.Select(f => f.Id));
            var imzaDosyasi = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Alpnex", $"foto_tasima_{s.CompanyId}.txt");
            if (File.Exists(imzaDosyasi) && File.ReadAllText(imzaDosyasi).Trim() == imza) return;   // taşınmış

            var sonuc = await TumunuSunucuyaTasiAsync(s);
            if (!sonuc.Cevrimdisi && sonuc.Hata is null && sonuc.Basarisiz == 0)
                File.WriteAllText(imzaDosyasi, imza);
        }
        catch { /* açılış akışı asla bozulmaz; taşıma sonraki açılışta yeniden dener */ }
    }

    private static string KumeImzasi(IEnumerable<string> ids)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("|", ids.OrderBy(x => x, StringComparer.Ordinal)));
        return Convert.ToHexString(sha.ComputeHash(bytes));
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
