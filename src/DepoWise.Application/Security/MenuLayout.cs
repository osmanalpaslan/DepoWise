namespace DepoWise.Application.Security;

/// <summary>Bir EKRANIN firmaya özel menü tercihi. Alan <c>null</c> = o konuda tercih yok → katalog varsayılanı.</summary>
public sealed record ScreenLayoutOverride(string ScreenKey, string? Label, string? GroupKey, int? SortOrder);

/// <summary>
/// Bir ÜST MENÜNÜN firmaya özel tercihi. <paramref name="IsCustom"/> = kullanıcının oluşturduğu grup.
/// <paramref name="ParentGroupKey"/> (SEC, 2026-08-19) = bağlı olduğu ÜST GRUP; <c>null</c> ise
/// grup en üst seviyededir (bugünkü davranış).
/// </summary>
public sealed record GroupLayoutOverride(string GroupKey, string? Title, int? SortOrder, bool IsCustom,
    string? ParentGroupKey = null);

/// <summary>Firmanın tüm menü tercihleri (ekran + grup). Boş küme = her şey katalog varsayılanı.</summary>
public sealed record MenuLayoutSet(
    IReadOnlyDictionary<string, ScreenLayoutOverride> Screens,
    IReadOnlyDictionary<string, GroupLayoutOverride> Groups)
{
    public static readonly MenuLayoutSet Empty = new(
        new Dictionary<string, ScreenLayoutOverride>(StringComparer.Ordinal),
        new Dictionary<string, GroupLayoutOverride>(StringComparer.Ordinal));

    public bool IsEmpty => Screens.Count == 0 && Groups.Count == 0;
}

/// <summary>Çözümlenmiş menü girişi — menüler bunu doğrudan basar.</summary>
public sealed record MenuEntry(AppScreen Screen, string Label);

/// <summary>Çözümlenmiş üst menü — <paramref name="Key"/> sistem anahtarı, <paramref name="Title"/> görünen ad.</summary>
public sealed record MenuGroupView(string Key, string Title, string DesktopIcon, IReadOnlyList<MenuEntry> Entries);

/// <summary>
/// SEC (2026-08-19) — menünün EN ÜST seviyesindeki bir düğüm.
///
/// İki hâli vardır ve <see cref="IsSection"/> ile ayrılır:
/// <list type="bullet">
///   <item><b>Üst grup</b> (<c>IsSection = true</c>): altında <see cref="Groups"/> taşır; kendi
///   ekranı yoktur. Menüde bir seviye daha derinlik açar.</item>
///   <item><b>Doğrudan üst menü</b> (<c>IsSection = false</c>): üst gruba bağlanmamış sıradan grup.
///   <see cref="Groups"/> tek elemanlıdır ve <b>bugünkü davranışın aynısıdır</b>.</item>
/// </list>
/// Böylece üst grup hiç tanımlanmadığında ağaç, bugünkü düz listenin birebir karşılığı olur.
/// </summary>
public sealed record MenuNodeView(string Key, string Title, string DesktopIcon,
    bool IsSection, IReadOnlyList<MenuGroupView> Groups);

/// <summary>
/// ═══ MNU — MENÜ DÜZENİ ÇÖZÜMLEYİCİSİ (saf mantık, 2026-08-18) ═══
///
/// <b>DÖRT KAVRAM AYRI DURUR</b> (hiçbiri diğerinin yerine geçmez):
/// <list type="bullet">
///   <item><b>Kimlik</b> — ekran anahtarı, route, yetki anahtarı (<see cref="AppScreens"/>) → <b>ASLA DEĞİŞMEZ</b></item>
///   <item><b>Platform</b> — bu ekran bu platformda açık mı? (<see cref="ScreenVisibility"/>)</item>
///   <item><b>Yetki</b> — kullanıcı bu modülü görebilir mi? (<see cref="AccessControl"/>)</item>
///   <item><b>Düzen</b> — menüde hangi adla, hangi grupta, kaçıncı sırada? (burası)</item>
/// </list>
///
/// <b>DÜZEN ERİŞİM KARARI VERMEZ.</b> Bir ekranın adını değiştirmek, başka gruba taşımak veya
/// sırasını kaydırmak; route'unu, yetkisini, servisini ve API ucunu ETKİLEMEZ. Menüden gizleme
/// düzenin işi değildir — o <see cref="ScreenVisibility"/> ile yapılır.
///
/// <b>YETİM EKRAN OLUŞMAZ:</b> bir ekran var olmayan bir gruba taşınmışsa (grup silinmiş olabilir)
/// sessizce <b>katalogdaki kendi grubuna</b> döner. Menüden düşmez.
///
/// <b>SIRA DETERMİNİSTİKTİR:</b> anahtar = (tercih edilen sıra ?? katalog sırası), eşitlik hâlinde
/// katalog sırası, sonra ekran anahtarı. İki kayda aynı sıra verilse bile sonuç her çağrıda AYNIDIR.
/// </summary>
public static class MenuLayout
{
    /// <summary>Kullanıcının oluşturduğu grup anahtarlarının öneki.</summary>
    public const string CustomGroupPrefix = "custom:";

    /// <summary>SEC — ÜST GRUP anahtarlarının öneki (menünün üçüncü seviyesi).</summary>
    public const string SectionPrefix = "section:";

    /// <summary>Bu anahtar bir ÜST GRUP mu (ekran taşımaz, altında üst menüler bulunur)?</summary>
    public static bool IsSectionKey(string key)
        => key.StartsWith(SectionPrefix, StringComparison.Ordinal);

    /// <summary>Katalog grubu mu (yoksa kullanıcı grubu mu)?</summary>
    public static bool IsCatalogGroup(string groupKey)
        => AppScreens.Groups.Any(g => string.Equals(g.Title, groupKey, StringComparison.Ordinal));

    /// <summary>Ekranın menüde GÖRÜNEN adı (tercih yoksa katalog etiketi).</summary>
    public static string LabelOf(AppScreen screen, MenuLayoutSet set)
        => set.Screens.TryGetValue(screen.Key, out var o) && !string.IsNullOrWhiteSpace(o.Label)
            ? o.Label!.Trim()
            : screen.Label;

    /// <summary>
    /// Ekranın ETKİN grup anahtarı. Tercih edilen grup mevcut değilse (silinmiş/bilinmeyen) katalog
    /// grubuna döner → <b>yetim ekran oluşmaz</b>.
    /// </summary>
    public static string GroupKeyOf(AppScreen screen, MenuLayoutSet set)
    {
        if (!set.Screens.TryGetValue(screen.Key, out var o) || string.IsNullOrWhiteSpace(o.GroupKey))
            return screen.Group;
        var key = o.GroupKey!;
        return IsCatalogGroup(key) || set.Groups.ContainsKey(key) ? key : screen.Group;
    }

    /// <summary>Grubun GÖRÜNEN başlığı. Katalog grubunda anahtar aynı zamanda varsayılan başlıktır.</summary>
    public static string GroupTitleOf(string groupKey, MenuLayoutSet set)
    {
        if (set.Groups.TryGetValue(groupKey, out var g) && !string.IsNullOrWhiteSpace(g.Title))
            return g.Title!.Trim();
        if (IsCatalogGroup(groupKey)) return groupKey;
        // Kullanıcı grubu / üst grup ama başlığı yok → anahtarın önekini atıp ham hâlini göster (fail-safe).
        if (groupKey.StartsWith(CustomGroupPrefix, StringComparison.Ordinal))
            return groupKey[CustomGroupPrefix.Length..];
        if (groupKey.StartsWith(SectionPrefix, StringComparison.Ordinal))
            return groupKey[SectionPrefix.Length..];
        return groupKey;
    }

    /// <summary>Katalogdaki grup sırası (kullanıcı grupları katalogdan SONRA gelir).</summary>
    private static int CatalogGroupIndex(string groupKey)
    {
        for (int i = 0; i < AppScreens.Groups.Count; i++)
            if (string.Equals(AppScreens.Groups[i].Title, groupKey, StringComparison.Ordinal)) return i;
        return int.MaxValue / 2;   // kullanıcı grupları sona
    }

    /// <summary>Katalogdaki ekran sırası.</summary>
    private static int CatalogScreenIndex(AppScreen screen)
    {
        for (int i = 0; i < AppScreens.All.Count; i++)
            if (ReferenceEquals(AppScreens.All[i], screen)) return i;
        return int.MaxValue / 2;
    }

    /// <summary>Masaüstü ikonu — katalog grubunda katalogdan, kullanıcı grubunda nötr.</summary>
    private static string IconOf(string groupKey)
    {
        foreach (var g in AppScreens.Groups)
            if (string.Equals(g.Title, groupKey, StringComparison.Ordinal)) return g.DesktopIcon;
        return "📁";
    }

    /// <summary>
    /// ⭐ TEK KAPI — bir platformun menüsünü düzen tercihleriyle birlikte üretir.
    ///
    /// <paramref name="isOpen"/> ekran bazlı süzgeçtir: platform + yetki kararını ÇAĞIRAN verir
    /// (bu sınıf erişim kararı vermez). Tek görünür ekranı kalmayan grup listeye GİRMEZ — mevcut
    /// menü davranışının aynısıdır.
    ///
    /// <para><paramref name="platform"/> bir bayrak MASKESİDİR: <c>Desktop|Web</c> verildiğinde
    /// "iki platformda da olan" değil <b>"en az birinde olan"</b> ekranlar döner. Yönetim ekranı
    /// TÜM ekranları listeleyebilsin diye böyledir — <c>HasFlag</c> kullanılsaydı yalnız bir
    /// platformda bulunan ekranlar (ör. Kota İzleme, Malzeme Şablonları) sessizce düşerdi.</para>
    /// </summary>
    public static IReadOnlyList<MenuGroupView> Build(ScreenPlatform platform, MenuLayoutSet set,
        Func<AppScreen, bool> isOpen)
    {
        var buckets = new Dictionary<string, List<AppScreen>>(StringComparer.Ordinal);

        foreach (var sc in AppScreens.All)
        {
            if ((sc.Platforms & platform) == 0) continue;   // katalogda bu platform(lar)da yok
            if (!isOpen(sc)) continue;                        // platform kaydı + yetki (çağıranın kararı)
            var key = GroupKeyOf(sc, set);
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<AppScreen>();
            list.Add(sc);
        }

        var groups = new List<MenuGroupView>();
        foreach (var (key, screens) in buckets)
        {
            var ordered = screens
                .OrderBy(s => set.Screens.TryGetValue(s.Key, out var o) && o.SortOrder is not null
                    ? o.SortOrder!.Value : CatalogScreenIndex(s))
                .ThenBy(CatalogScreenIndex)
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .Select(s => new MenuEntry(s, LabelOf(s, set)))
                .ToList();

            groups.Add(new MenuGroupView(key, GroupTitleOf(key, set), IconOf(key), ordered));
        }

        return groups
            .OrderBy(g => set.Groups.TryGetValue(g.Key, out var o) && o.SortOrder is not null
                ? o.SortOrder!.Value : CatalogGroupIndex(g.Key))
            .ThenBy(g => CatalogGroupIndex(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Grubun bağlı olduğu ÜST GRUP. Geçersizse (kayıt yok / üst grup silinmiş / üst grup
    /// aslında üst grup değil / kendine bağlı) <c>null</c> döner → grup en üst seviyede kalır.</summary>
    public static string? SectionKeyOf(string groupKey, MenuLayoutSet set)
    {
        if (!set.Groups.TryGetValue(groupKey, out var g)) return null;
        var parent = g.ParentGroupKey;
        if (string.IsNullOrWhiteSpace(parent)) return null;
        if (string.Equals(parent, groupKey, StringComparison.Ordinal)) return null;   // kendine bağlanamaz
        if (!IsSectionKey(parent!)) return null;                                      // yalnız üst gruba bağlanır
        return set.Groups.ContainsKey(parent!) ? parent : null;                       // silinmişse yetim bırakma
    }

    /// <summary>
    /// ⭐ SEC (2026-08-19) — MENÜYÜ ÜÇ SEVİYELİ AĞAÇ OLARAK ÜRETİR: ÜST GRUP → ÜST MENÜ → EKRAN.
    ///
    /// <b><see cref="Build"/> DEĞİŞTİRİLMEDİ</b> — bu metot onun çıktısını sarmalar. Böylece mevcut
    /// sıralama/ad/grup çözümlemesi tek yerde kalır ve iki platform aynı sonucu görür.
    ///
    /// <b>GERİ UYUMLULUK (en önemli garanti):</b> hiç üst grup tanımlanmamışsa her grup KENDİ
    /// düğümü olur (<c>IsSection=false</c>, tek elemanlı) → sonuç bugünkü düz listenin birebir
    /// karşılığıdır. Üçüncü seviye yalnız yönetici üst grup oluşturup gruba bağladığında belirir.
    ///
    /// <b>SIRALAMA:</b> bir üst grup, ilk üyesinin bulunduğu yerde görünür. Böylece ikinci bir
    /// sıralama alanı doğmaz; yönetici grupları taşıdıkça üst grup da onlarla birlikte yer değiştirir.
    ///
    /// <b>BOŞ ÜST GRUP GÖSTERİLMEZ:</b> altında görünür üst menü kalmayan üst grup listeye girmez —
    /// "görünür ekranı kalmayan grup gizlenir" kuralının aynısı.
    /// </summary>
    public static IReadOnlyList<MenuNodeView> BuildTree(ScreenPlatform platform, MenuLayoutSet set,
        Func<AppScreen, bool> isOpen)
    {
        var groups = Build(platform, set, isOpen);

        // 1) Üst seviye sırası = ilk görülme sırası. Üst gruba bağlı olmayan grup kendi düğümüdür;
        //    üst grup ise İLK ÜYESİNİN bulunduğu yerde açılır.
        var order = new List<string>();                                              // üst seviye anahtarlar
        var children = new Dictionary<string, List<MenuGroupView>>(StringComparer.Ordinal);

        foreach (var g in groups)
        {
            var key = SectionKeyOf(g.Key, set) ?? g.Key;
            if (!children.TryGetValue(key, out var list))
            {
                children[key] = list = new List<MenuGroupView>();
                order.Add(key);
            }
            list.Add(g);
        }

        // 2) Düğümleri kur. Üst grup değilse başlık/ikon grubun kendisinden gelir (bugünkü davranış).
        var nodes = new List<MenuNodeView>(order.Count);
        foreach (var key in order)
        {
            var kids = children[key];
            if (IsSectionKey(key))
                nodes.Add(new MenuNodeView(key, GroupTitleOf(key, set), SectionIcon, true, kids));
            else
                nodes.Add(new MenuNodeView(key, kids[0].Title, kids[0].DesktopIcon, false, kids));
        }
        return nodes;
    }

    /// <summary>Üst grup ikonu (katalogda karşılığı olmadığı için sabit).</summary>
    public const string SectionIcon = "📂";
}
