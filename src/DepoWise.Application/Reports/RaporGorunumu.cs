using System.Text.Json;

namespace DepoWise.Application.Reports;

/// <summary>
/// ═══ M-58 (AR-GE 2026-10-25, F-122) — KAYITLI RAPOR GÖRÜNÜMÜ ═══
/// Sık kullanılan "rapor + filtre seçimi" bir adla saklanır, tek tıkla geri yüklenir. KİŞİSELDİR: değer mevcut
/// <c>user_list_preferences</c> tablosunda <see cref="PrefKey"/> anahtarı altında (her görünüm bir JSON metni)
/// durur — "son seçim" tercihiyle AYNI desen, MIGRATION YOK; user_id daima oturumdan gelir.
/// Web bu dosyayı derler (csproj Compile Include) → iki platform AYNI biçimi yazar/okur.
///
/// <b>Bilinçli sınırlar:</b> tarih aralığı SAKLANMAZ (görünüm açılınca raporun kendi varsayılanı — tarih zorunluysa
/// «Bu Ay» — gelir; eski bir aralık sessizce kalmasın). Görünüm yalnız kimlik saklar; yüklenince yetki/kapsam
/// YENİDEN uygulanır (sunucu kesişimi + ekranda olmayan seçenek işaretlenmez) → görünüm kapsam genişletemez.
/// </summary>
public sealed class RaporGorunumu
{
    public string Ad { get; set; } = "";
    public string Rapor { get; set; } = "";
    /// <summary>Filtre alanı (<see cref="RaporGorunumleri.Alan"/>) → seçili kimlikler.</summary>
    public Dictionary<string, List<string>> Secim { get; set; } = new();
    /// <summary>Arama ile seçilen kimliklerin görünen adı (malzeme / cari) — liste önceden yüklenmediği için.</summary>
    public Dictionary<string, string> Etiket { get; set; } = new();
    public string? Arama { get; set; }
    public string? Siralama { get; set; }

    public IReadOnlyList<string> Kimlikler(string alan)
        => Secim.TryGetValue(alan, out var l) ? l : (IReadOnlyList<string>)Array.Empty<string>();

    public void Ekle(string alan, IEnumerable<string>? ids)
    {
        var l = ids?.Where(x => x is not null).Distinct().ToList();
        if (l is { Count: > 0 }) Secim[alan] = l; else Secim.Remove(alan);
    }
}

public static class RaporGorunumleri
{
    public const string PrefKey = "pref:report-views";
    public const int EnFazla = 30;
    public const int AdUzunluk = 60;

    /// <summary>Filtre alanı anahtarları — iki platform AYNI anahtarla yazar.</summary>
    public static class Alan
    {
        public const string Branch = "branch", Vehicle = "vehicle", VehicleType = "vehicleType",
            MaintenanceDef = "maintenanceDef", Technician = "technician", Supplier = "supplier",
            Requester = "requester", Status = "status", Recipient = "recipient", Location = "location",
            MovementType = "movementType", ActivityType = "activityType", Material = "material", Party = "party";
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly StringComparer AdKarsilastir =
        StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    /// <summary>Saklanan metinler → görünümler. Bozuk/boş kayıt atlanır (ekran açılışı asla patlamaz).</summary>
    public static List<RaporGorunumu> Coz(IEnumerable<string>? kayitlar)
    {
        var list = new List<RaporGorunumu>();
        if (kayitlar is null) return list;
        foreach (var k in kayitlar)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            try
            {
                var g = JsonSerializer.Deserialize<RaporGorunumu>(k, Json);
                if (g is null || string.IsNullOrWhiteSpace(g.Ad) || string.IsNullOrWhiteSpace(g.Rapor)) continue;
                g.Secim ??= new(); g.Etiket ??= new();
                list.Add(g);
            }
            catch (JsonException) { }
        }
        return list;
    }

    public static List<string> Yaz(IEnumerable<RaporGorunumu> gorunumler)
        => gorunumler.Select(g => JsonSerializer.Serialize(g, Json)).ToList();

    /// <summary>Görünümü ekler; AYNI ad (büyük/küçük harf duyarsız) varsa onun yerine geçer. Ad zorunlu.</summary>
    public static List<RaporGorunumu> Kaydet(IEnumerable<RaporGorunumu> mevcut, RaporGorunumu yeni)
    {
        var ad = (yeni.Ad ?? "").Trim();
        if (ad.Length == 0) throw new ArgumentException("Görünüm adı girin.");
        if (ad.Length > AdUzunluk) throw new ArgumentException($"Görünüm adı en fazla {AdUzunluk} karakter olabilir.");
        if (string.IsNullOrWhiteSpace(yeni.Rapor)) throw new ArgumentException("Önce bir rapor seçin.");
        yeni.Ad = ad;
        var list = mevcut.Where(g => !AdKarsilastir.Equals(g.Ad, ad)).ToList();
        if (list.Count >= EnFazla) throw new ArgumentException($"En fazla {EnFazla} görünüm kaydedilebilir; önce birini silin.");
        list.Add(yeni);
        return list.OrderBy(g => g.Ad, AdKarsilastir).ToList();
    }

    public static List<RaporGorunumu> Sil(IEnumerable<RaporGorunumu> mevcut, string ad)
        => mevcut.Where(g => !AdKarsilastir.Equals(g.Ad, (ad ?? "").Trim())).ToList();
}
