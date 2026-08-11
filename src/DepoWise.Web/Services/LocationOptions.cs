using System.Text.Json;

namespace DepoWise.Web.Services;

/// <summary>Stok lokasyonu seçeneği (şube/şantiye).</summary>
public sealed record LocationOption(string Id, string Name);

/// <summary>
/// STK-04 (FAZ C, 2026-08-11) — STOK LOKASYONU seçeneklerinin TEK kaynağı.
///
/// <b>Neden ayrı servis:</b> lokasyon listesi artık Stok · Sayım · Hareketler · Malzeme kartı ·
/// Günlük Faaliyet ekranlarının hepsinde lazım. Her ekran kendi <c>/api/branches</c> çağrısını yapsaydı
/// bir oturumda aynı liste 5+ kez inerdi. Burada <b>oturum boyunca bir kez</b> çekilir ve paylaşılır
/// (scoped = Blazor Server'da kullanıcı devresi başına bir örnek).
///
/// <b>⚠️ EN KRİTİK AYRIM (KARAR K-2):</b> "Tüm Şubeler" ile "Atanmamış" AYNI ŞEY DEĞİLDİR ve
/// arayüzde asla aynı anlamda kullanılmaz:
/// <list type="bullet">
///   <item><b>🌐 Tüm Şubeler</b> = firmanın TÜM lokasyonlarının TOPLAMI (Atanmamış dahil). Yalnız
///         GÖRÜNTÜLEME/FİLTRE anlamıdır; bir yazma hedefi değildir.</item>
///   <item><b>📦 Atanmamış</b> = yalnız <c>locationId = ""</c>, yani lokasyonu BİLİNMEYEN geçmiş stok.
///         Görüntülenebilir/filtrelenebilir ama YENİ kayıt buraya yazılamaz — yeni belirsizlik
///         üretmemek için (bkz. <see cref="WriteTargets"/>).</item>
/// </list>
/// </summary>
public sealed class LocationOptions
{
    /// <summary>Filtrede "hepsi" seçeneğinin değeri. Sunucuya GÖNDERİLMEZ — filtre yokluğu demektir.</summary>
    public const string AllId = "__all__";

    /// <summary>Lokasyonu bilinmeyen (geçmiş) stok kovası. API sözleşmesinde boş metindir.</summary>
    public const string UnassignedId = "";

    public const string AllLabel = "🌐 Tüm Şubeler (firma toplamı)";
    public const string UnassignedLabel = "📦 Atanmamış";

    /// <summary>"Atanmamış" ne demek? Kullanıcı bunu bir depo sanmamalı.</summary>
    public const string UnassignedHelp =
        "Geçmişte hangi depoya/şantiyeye ait olduğu girilmemiş stok. Bir depo değildir; " +
        "transferle gerçek depoya taşınabilir.";

    private readonly ApiClient _api;
    private IReadOnlyList<LocationOption>? _cache;

    public LocationOptions(ApiClient api) => _api = api;

    /// <summary>Firmanın gerçek lokasyonları (şube/şantiye). İlk çağrıda indirilir, sonra önbellekten.</summary>
    public async Task<IReadOnlyList<LocationOption>> AllAsync()
    {
        if (_cache is not null) return _cache;
        var rows = await _api.GetArrayAsync("/api/branches");
        _cache = rows.Select(r => new LocationOption(Str(r, "id"), Str(r, "name")))
                     .Where(x => x.Id.Length > 0)
                     .ToList();
        return _cache;
    }

    /// <summary>Şube listesi değiştiyse (yeni şube eklendi) önbelleği düşür.</summary>
    public void Invalidate() => _cache = null;

    /// <summary>
    /// GÖRÜNTÜLEME/FİLTRE seçenekleri: Tüm Şubeler + gerçek lokasyonlar + Atanmamış.
    /// Atanmamış EN SONDA — kullanıcı önce gerçek depolarını görür.
    /// </summary>
    public async Task<IReadOnlyList<LocationOption>> FilterOptionsAsync()
    {
        var list = new List<LocationOption> { new(AllId, AllLabel) };
        list.AddRange(await AllAsync());
        list.Add(new(UnassignedId, UnassignedLabel));
        return list;
    }

    /// <summary>
    /// YAZMA hedefleri: YALNIZ gerçek lokasyonlar. "Tüm Şubeler" (belirsiz) ve "Atanmamış"
    /// (geçmişin bilinmezliği) burada BİLİNÇLİ olarak YOKTUR — yeni kayıt belirsiz olamaz.
    /// </summary>
    public Task<IReadOnlyList<LocationOption>> WriteTargets() => AllAsync();

    /// <summary>Ekranda gösterilecek ad. Boş kimlik → "Atanmamış"; bilinmeyen kimlik → kimliğin kendisi.</summary>
    public async Task<string> NameAsync(string? locationId)
    {
        if (string.IsNullOrEmpty(locationId)) return "Atanmamış";
        var all = await AllAsync();
        return all.FirstOrDefault(x => x.Id == locationId)?.Name ?? locationId;
    }

    private static string Str(JsonElement e, string k)
        => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
