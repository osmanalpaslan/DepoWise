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
        if (uzak is null) return (CevrimdisiOku(s, entityType, entityId), true);   // çevrimdışı → cihazdaki kopya

        if (await TasiEskileriAsync(s, entityType, entityId, uzak) > 0)
            uzak = await OrgServerClient.ListPhotosAsync(api, entityId) ?? uzak;

        // 2026-10-04: içerik önce CİHAZ ÖNBELLEĞİNDEN okunur, yoksa sunucudan indirilip önbelleğe yazılır →
        // aynı fotoğraf tekrar tekrar indirilmez ve çevrimdışıyken de görünür.
        var liste = new List<Yuklenen>();
        foreach (var p in uzak)
        {
            var bytes = Onbellek.Oku(s.CompanyId, entityType, entityId, p.Id)
                        ?? await OrgServerClient.DownloadPhotoAsync(api, entityId, p.Id);
            if (bytes is null) continue;
            Onbellek.Yaz(s.CompanyId, entityType, entityId, p.Id, bytes);
            liste.Add(new Yuklenen(p.Id, bytes));
        }
        Onbellek.ListeYaz(s.CompanyId, entityType, entityId, uzak.Select(p => p.Id));
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

    /// <summary>
    /// Çevrimdışı görüntüleme (2026-10-04): önce sunucudan CİHAZA İNDİRİLMİŞ kopya (önbellek listesi); o kayıt
    /// için önbellek hiç oluşmamışsa bu makinede kalmış eski yerel fotoğraflar.
    /// </summary>
    private static List<Yuklenen> CevrimdisiOku(SessionContext s, string entityType, string entityId)
    {
        var ids = Onbellek.ListeOku(s.CompanyId, entityType, entityId);
        if (ids is null) return YerelOku(s, entityType, entityId);
        var liste = new List<Yuklenen>();
        foreach (var id in ids)
            if (Onbellek.Oku(s.CompanyId, entityType, entityId, id) is { } b) liste.Add(new Yuklenen(id, b));
        return liste;
    }

    /// <summary>Çevrimdışı ekran notu — dört ekran aynı cümleyi kullanır.</summary>
    public const string CevrimdisiNotu = "Çevrimdışı: cihaza indirilmiş fotoğraflar gösteriliyor.";

    private static DateTime _sonDoldurma = DateTime.MinValue;
    private static int _dolduruluyor;

    /// <summary>
    /// ⭐ 2026-10-04 — ARKA PLANDA ÖNBELLEK DOLDURMA (kullanıcı isteği: "internete bağlı olmasam bile fotoğrafların
    /// sunucudan cihazıma inmiş olması gerek"). Firmanın fotoğraf dizini TEK istekle alınır; cihazda olmayanlar
    /// indirilir, sunucuda silinenler önbellekten kaldırılır. Kayıt hiç açılmamış olsa bile fotoğraf cihazda olur.
    /// Kurallar: başarılı turdan sonra en fazla 30 dakikada bir · aynı anda tek koşu · hata/çevrimdışı sessiz
    /// (sonraki turda yeniden dener) · eski sunucu (uç yok) → hiçbir şey yapılmaz.
    /// </summary>
    public static async Task OnbellegiDoldurAsync(SessionContext s)
    {
        if ((DateTime.UtcNow - _sonDoldurma).TotalMinutes < 30) return;
        if (System.Threading.Interlocked.Exchange(ref _dolduruluyor, 1) == 1) return;
        try
        {
            var dizin = await OrgServerClient.ListPhotoIndexAsync();
            if (dizin is null) return;
            bool tamam = true;
            foreach (var g in dizin.GroupBy(x => (x.EntityType, x.EntityId)))
            {
                var api = ApiEntity(g.Key.EntityType);
                foreach (var p in g)
                {
                    if (Onbellek.Var(s.CompanyId, g.Key.EntityType, g.Key.EntityId, p.Id)) continue;
                    var bytes = await OrgServerClient.DownloadPhotoAsync(api, g.Key.EntityId, p.Id);
                    if (bytes is null) { tamam = false; continue; }
                    Onbellek.Yaz(s.CompanyId, g.Key.EntityType, g.Key.EntityId, p.Id, bytes);
                }
                Onbellek.ListeYaz(s.CompanyId, g.Key.EntityType, g.Key.EntityId, g.Select(p => p.Id));
            }
            Onbellek.FazlalariTemizle(s.CompanyId, dizin.Select(x => (x.EntityType, x.EntityId)));
            if (tamam) _sonDoldurma = DateTime.UtcNow;
        }
        catch { /* sessiz — sonraki turda yeniden dener */ }
        finally { System.Threading.Interlocked.Exchange(ref _dolduruluyor, 0); }
    }

    /// <summary>
    /// Cihaz fotoğraf önbelleği: <c>%LOCALAPPDATA%\DepoWise\FotoOnbellek\{firma}\{tür}\{kayıt}\</c>.
    /// Her fotoğraf kendi kimliğiyle saklanır; <c>liste.txt</c> sunucudaki son bilinen listeyi tutar (sunucuda
    /// silinen fotoğraf çevrimdışıyken de görünmesin diye). Yalnız sunucudan inen kopyalar burada durur.
    /// </summary>
    internal static class Onbellek
    {
        internal static string? KokOverride;   // testler için
        private static string Kok => KokOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DepoWise", "FotoOnbellek");

        // Yol parçası güvenliği: kimlikler yalnız harf/rakam/-/_ ile klasör adına dönüşür (yol kaçışı yok).
        private static string Temiz(string v)
            => string.Concat(v.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));

        private static string Klasor(string firma, string tur, string kayit)
            => Path.Combine(Kok, Temiz(firma), Temiz(tur), Temiz(kayit));

        private static string Dosya(string firma, string tur, string kayit, string id)
            => Path.Combine(Klasor(firma, tur, kayit), Temiz(id) + ".img");

        internal static bool Var(string firma, string tur, string kayit, string id)
            => File.Exists(Dosya(firma, tur, kayit, id));

        internal static byte[]? Oku(string firma, string tur, string kayit, string id)
        {
            try { var yol = Dosya(firma, tur, kayit, id); return File.Exists(yol) ? File.ReadAllBytes(yol) : null; }
            catch { return null; }
        }

        internal static void Yaz(string firma, string tur, string kayit, string id, byte[] bytes)
        {
            try
            {
                var yol = Dosya(firma, tur, kayit, id);
                if (File.Exists(yol)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
                var gecici = yol + ".tmp";
                File.WriteAllBytes(gecici, bytes);
                File.Move(gecici, yol, overwrite: true);   // yarım dosya kalmaz
            }
            catch { }
        }

        /// <summary>Sunucudaki güncel liste yazılır; listede olmayan (sunucuda silinmiş) fotoğraflar kaldırılır.</summary>
        internal static void ListeYaz(string firma, string tur, string kayit, IEnumerable<string> ids)
        {
            try
            {
                var klasor = Klasor(firma, tur, kayit);
                Directory.CreateDirectory(klasor);
                var liste = ids.ToList();
                File.WriteAllLines(Path.Combine(klasor, "liste.txt"), liste);
                var tut = liste.Select(i => Temiz(i) + ".img").ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(klasor, "*.img"))
                    if (!tut.Contains(Path.GetFileName(f))) File.Delete(f);
            }
            catch { }
        }

        internal static List<string>? ListeOku(string firma, string tur, string kayit)
        {
            try
            {
                var yol = Path.Combine(Klasor(firma, tur, kayit), "liste.txt");
                return File.Exists(yol) ? File.ReadAllLines(yol).Where(x => x.Length > 0).ToList() : null;
            }
            catch { return null; }
        }

        /// <summary>Dizinde artık hiç fotoğrafı olmayan kayıtların önbelleği "boş liste" olur (çevrimdışıyken
        /// silinmiş fotoğraf görünmez). Boş liste "bu kaydın fotoğrafı yok" bilgisidir.</summary>
        internal static void FazlalariTemizle(string firma, IEnumerable<(string Tur, string Kayit)> mevcut)
        {
            try
            {
                var set = mevcut.Select(x => (Temiz(x.Tur), Temiz(x.Kayit))).ToHashSet();
                var firmaKok = Path.Combine(Kok, Temiz(firma));
                if (!Directory.Exists(firmaKok)) return;
                foreach (var turDir in Directory.GetDirectories(firmaKok))
                    foreach (var kayitDir in Directory.GetDirectories(turDir))
                        if (!set.Contains((Path.GetFileName(turDir), Path.GetFileName(kayitDir))))
                            ListeYaz(firma, Path.GetFileName(turDir), Path.GetFileName(kayitDir), Array.Empty<string>());
            }
            catch { }
        }
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
