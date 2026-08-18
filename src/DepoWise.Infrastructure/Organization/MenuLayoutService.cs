using System.Collections.Concurrent;
using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Yönetim ekranının bir EKRAN satırı (katalog gerçeği + firma tercihi + çözümlenmiş sonuç).</summary>
public sealed record MenuLayoutRow(
    string ScreenKey, string ModuleKey, string CatalogGroup, string CatalogLabel,
    string EffectiveLabel, string EffectiveGroupKey, string EffectiveGroupTitle, int SortOrder,
    string? WebRoute, string? DesktopNavKey, string PermissionKey,
    bool DefaultDesktop, bool DefaultWeb, bool EffectiveDesktop, bool EffectiveWeb,
    bool IsProtected)
{
    public string PlatformText => (EffectiveDesktop, EffectiveWeb) switch
    {
        (true, true) => "Masaüstü + Web",
        (true, false) => "Yalnız Masaüstü",
        (false, true) => "Yalnız Web",
        _ => "Kapalı",
    };

    /// <summary>Katalog varsayılanından sapıyor mu (yönetim ekranında rozet)?</summary>
    public bool IsCustomized =>
        !string.Equals(EffectiveLabel, CatalogLabel, StringComparison.Ordinal) ||
        !string.Equals(EffectiveGroupKey, CatalogGroup, StringComparison.Ordinal);
}

/// <summary>Yönetim ekranının bir ÜST MENÜ satırı.</summary>
public sealed record MenuGroupRow(string GroupKey, string Title, int SortOrder, bool IsCustom, int ScreenCount);

/// <summary>Kaydetme girdisi — bir ekranın istenen düzeni.</summary>
public sealed record ScreenLayoutInput(string ScreenKey, string? Label, string? GroupKey, int SortOrder);

/// <summary>Kaydetme girdisi — bir üst menünün istenen düzeni.</summary>
public sealed record GroupLayoutInput(string GroupKey, string? Title, int SortOrder, bool IsCustom);

/// <summary>Kaydetme özeti (kullanıcıya "kaç şey değişti" bilgisi için).</summary>
public sealed record MenuLayoutSaveResult(int ScreensChanged, int GroupsChanged, int CustomGroups);

/// <summary>
/// ═══ MNU — MENÜ DÜZENİ SERVİSİ (2026-08-18) ═══
///
/// Firma bazında ekranların menüdeki <b>adı · üst menüsü · sırası</b> ve üst menülerin
/// <b>adı · sırası</b> kaydını okur/yazar. Kayıt YOKSA <see cref="AppScreens"/> katalog varsayılanı
/// geçerlidir → migration sonrası menü birebir aynı kalır.
///
/// <b>Desen <see cref="ScreenVisibilityService"/> ile BİREBİR aynıdır</b> (yeni mimari icat edilmedi):
/// firma bazlı tablo · TTL önbellek · yazmada anında <see cref="Invalidate"/> · audit · tek transaction.
/// Yetki de yeni bir sistem değildir: <b>aynı modül anahtarı</b> (<c>screen_visibility</c>) kullanılır,
/// çünkü ikisi de aynı yönetim ekranının parçasıdır ve <c>AppModules.IsSuperAdminOnly</c> kapsamındadır.
///
/// <b>KAYDETME TAM DURUM (full-state) ve ATOMİKTİR:</b> arayüz istenen NİHAİ düzeni gönderir; servis
/// tek transaction içinde firmanın satırlarını değiştirir. Kısmi kaydetme sonucu bozuk menü oluşamaz.
///
/// <b>KATALOG VARSAYILANINA EŞİT SATIR YAZILMAZ:</b> ad katalogla aynıysa, grup katalogla aynıysa ve
/// grubun ekran sırası katalog sırasıyla aynıysa kayıt tutulmaz. Böylece tablo yalnız GERÇEK
/// tercihleri taşır; ileride katalog sırası değişirse dokunulmamış gruplar yeni sırayı otomatik alır.
/// </summary>
public sealed class MenuLayoutService
{
    /// <summary>Yönetim ekranının yetki modülü — platform yönetimiyle AYNI ekran, AYNI modül.</summary>
    public const string Module = ScreenVisibilityService.Module;

    /// <summary>Görünen adlar için üst sınır (menüyü taşırmasın).</summary>
    public const int MaxLabelLength = 60;

    /// <summary>Önbellek ömrü — platform görünürlüğüyle aynı (kısa; yazmada zaten düşürülür).</summary>
    public const int CacheTtlSeconds = 60;

    private sealed record Entry(MenuLayoutSet Set, DateTimeOffset Expires);

    private static readonly ConcurrentDictionary<string, Entry> _cache = new(StringComparer.Ordinal);

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MenuLayoutService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public static void Invalidate(string companyId) => _cache.TryRemove(companyId, out _);
    public static void InvalidateAll() => _cache.Clear();

    // ═══ OKUMA ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Firmanın menü düzeni. <b>Yetki GEREKTİRMEZ</b>: menüyü çizen her istek çağırır ve bu bilgi
    /// yetki taşımaz — bir ekranın adı/sırası, kullanıcının ona erişip erişemeyeceğinden bağımsızdır
    /// (erişim <see cref="AccessControl"/> + <see cref="ScreenVisibility"/> ile ayrıca kararlaşır).
    /// </summary>
    public MenuLayoutSet LayoutFor(string companyId)
    {
        if (_cache.TryGetValue(companyId, out var hit) && hit.Expires > _clock.UtcNow) return hit.Set;

        var screens = new Dictionary<string, ScreenLayoutOverride>(StringComparer.Ordinal);
        var groups = new Dictionary<string, GroupLayoutOverride>(StringComparer.Ordinal);
        try
        {
            using var conn = _factory.Create();
            if (!DbIntrospect.TableExists(conn, null, "screen_menu_layout"))
                return Store(companyId, MenuLayoutSet.Empty);

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT screen_key, label_override, group_key_override, sort_order " +
                                  "FROM screen_menu_layout WHERE company_id=@c;";
                cmd.AddWithValue("@c", companyId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var key = r.GetString(0);
                    screens[key] = new ScreenLayoutOverride(key,
                        r.IsDBNull(1) ? null : r.GetString(1),
                        r.IsDBNull(2) ? null : r.GetString(2),
                        r.IsDBNull(3) ? null : Convert.ToInt32(r.GetValue(3)));
                }
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT group_key, title_override, sort_order, is_custom " +
                                  "FROM menu_group_layout WHERE company_id=@c;";
                cmd.AddWithValue("@c", companyId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var key = r.GetString(0);
                    groups[key] = new GroupLayoutOverride(key,
                        r.IsDBNull(1) ? null : r.GetString(1),
                        r.IsDBNull(2) ? null : Convert.ToInt32(r.GetValue(2)),
                        Convert.ToInt64(r.GetValue(3)) == 1);
                }
            }
        }
        catch { /* okuma hatası menüyü çökertmez → katalog varsayılanı geçerli kalır */ }

        return Store(companyId, new MenuLayoutSet(screens, groups));
    }

    private MenuLayoutSet Store(string companyId, MenuLayoutSet set)
    {
        _cache[companyId] = new Entry(set, _clock.UtcNow.AddSeconds(CacheTtlSeconds));
        return set;
    }

    /// <summary>Yönetim ekranının EKRAN listesi (platform durumu dahil, çözümlenmiş sırada).</summary>
    public IReadOnlyList<MenuLayoutRow> List(SessionContext s,
        IReadOnlyDictionary<string, ScreenVisibilityOverride>? visibility)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var set = LayoutFor(s.CompanyId);

        // Sıra: menüde görüldüğü gibi. Platform/yetki süzgeci UYGULANMAZ — yönetici TÜM ekranları
        // görmeli (kapalı olanı yeniden açabilmek için).
        var views = MenuLayout.Build(ScreenPlatform.Desktop | ScreenPlatform.Web, set, _ => true);

        var rows = new List<MenuLayoutRow>();
        foreach (var g in views)
        {
            int i = 0;
            foreach (var e in g.Entries)
            {
                var sc = e.Screen;
                var eff = ScreenVisibility.Effective(sc, visibility);
                rows.Add(new MenuLayoutRow(
                    sc.Key, sc.ModuleKey, sc.Group, sc.Label,
                    e.Label, g.Key, g.Title, i++,
                    sc.WebRoute, sc.DesktopNavKey, sc.WebPermKey,
                    sc.OnDesktop, sc.OnWeb,
                    eff.HasFlag(ScreenPlatform.Desktop), eff.HasFlag(ScreenPlatform.Web),
                    AppScreens.IsProtected(sc.Key)));
            }
        }
        return rows;
    }

    /// <summary>Yönetim ekranının ÜST MENÜ listesi (çözümlenmiş sırada, ekran sayısıyla).</summary>
    public IReadOnlyList<MenuGroupRow> Groups(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var set = LayoutFor(s.CompanyId);
        var views = MenuLayout.Build(ScreenPlatform.Desktop | ScreenPlatform.Web, set, _ => true);

        var rows = views.Select((g, i) => new MenuGroupRow(
            g.Key, g.Title, i, !MenuLayout.IsCatalogGroup(g.Key), g.Entries.Count)).ToList();

        // Hiç ekranı kalmayan gruplar Build'den DÜŞER; yönetim ekranında yine görünmeliler ki
        // yönetici oraya ekran taşıyabilsin veya grubu kaldırabilsin.
        var bilinen = new HashSet<string>(rows.Select(r => r.GroupKey), StringComparer.Ordinal);
        foreach (var g in AppScreens.Groups)
            if (bilinen.Add(g.Title))
                rows.Add(new MenuGroupRow(g.Title, MenuLayout.GroupTitleOf(g.Title, set), rows.Count, false, 0));
        foreach (var key in set.Groups.Keys)
            if (bilinen.Add(key))
                rows.Add(new MenuGroupRow(key, MenuLayout.GroupTitleOf(key, set), rows.Count,
                    !MenuLayout.IsCatalogGroup(key), 0));

        return rows;
    }

    // ═══ YAZMA ══════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// İstenen NİHAİ düzeni tek transaction içinde yazar. Doğrulama fail-closed'dır: bilinmeyen ekran,
    /// var olmayan gruba taşıma (yetim ekran) ve aşırı uzun ad REDDEDİLİR — kısmi kayıt oluşmaz.
    /// </summary>
    public MenuLayoutSaveResult Save(SessionContext s,
        IReadOnlyList<ScreenLayoutInput> screens, IReadOnlyList<GroupLayoutInput> groups)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);

        // ── 1) Grupları doğrula ─────────────────────────────────────────────────────────────────
        var gecerliGruplar = new HashSet<string>(AppScreens.Groups.Select(g => g.Title), StringComparer.Ordinal);
        var temizGruplar = new List<GroupLayoutInput>();
        foreach (var g in groups)
        {
            var key = (g.GroupKey ?? "").Trim();
            if (key.Length == 0) throw new ArgumentException("Üst menü anahtarı boş olamaz.");
            var ozel = !MenuLayout.IsCatalogGroup(key);
            if (ozel && !key.StartsWith(MenuLayout.CustomGroupPrefix, StringComparison.Ordinal))
                throw new ArgumentException($"Bilinmeyen üst menü: {key}");

            var baslik = Temizle(g.Title);
            if (ozel && string.IsNullOrEmpty(baslik))
                throw new ArgumentException("Yeni üst menü için ad girin.");

            gecerliGruplar.Add(key);
            temizGruplar.Add(new GroupLayoutInput(key, baslik, g.SortOrder, ozel));
        }

        // ── 2) Ekranları doğrula ────────────────────────────────────────────────────────────────
        var temizEkranlar = new List<ScreenLayoutInput>();
        foreach (var sc in screens)
        {
            var key = (sc.ScreenKey ?? "").Trim();
            var ekran = AppScreens.ByKey(key) ?? throw new ArgumentException($"Bilinmeyen ekran: {key}");

            var grup = (sc.GroupKey ?? "").Trim();
            if (grup.Length == 0) grup = ekran.Group;
            // ⭐ YETİM EKRAN KORUMASI: var olmayan gruba taşıma sessizce kabul edilmez.
            if (!gecerliGruplar.Contains(grup))
                throw new ArgumentException(
                    $"'{ekran.Label}' ekranı var olmayan bir üst menüye taşınamaz ({grup}).");

            temizEkranlar.Add(new ScreenLayoutInput(key, Temizle(sc.Label), grup, sc.SortOrder));
        }

        // ── 3) Katalog varsayılanına eşit olanları ELE (gereksiz satır yazma) ───────────────────
        var yazilacakEkranlar = new List<(string Key, string? Label, string? Group, int? Sort)>();
        foreach (var grupAnahtari in temizEkranlar.Select(e => e.GroupKey!).Distinct(StringComparer.Ordinal))
        {
            var grubunEkranlari = temizEkranlar
                .Where(e => string.Equals(e.GroupKey, grupAnahtari, StringComparison.Ordinal))
                .OrderBy(e => e.SortOrder)
                .ToList();

            // Bu grubun sırası katalog sırasıyla AYNI mı? Aynıysa sıra kaydı tutulmaz.
            var katalogSirasi = AppScreens.All
                .Where(a => string.Equals(a.Group, grupAnahtari, StringComparison.Ordinal))
                .Select(a => a.Key).ToList();
            var istenenSira = grubunEkranlari.Select(e => e.ScreenKey).ToList();
            var siraVarsayilan = katalogSirasi.SequenceEqual(istenenSira, StringComparer.Ordinal);

            for (int i = 0; i < grubunEkranlari.Count; i++)
            {
                var e = grubunEkranlari[i];
                var ekran = AppScreens.ByKey(e.ScreenKey)!;
                var label = string.Equals(e.Label, ekran.Label, StringComparison.Ordinal) ? null : e.Label;
                var grup = string.Equals(e.GroupKey, ekran.Group, StringComparison.Ordinal) ? null : e.GroupKey;
                int? sira = siraVarsayilan ? null : i;
                if (label is null && grup is null && sira is null) continue;   // saf varsayılan → satır yok
                yazilacakEkranlar.Add((e.ScreenKey, label, grup, sira));
            }
        }

        // Grup sırası katalogla aynıysa grup sıra kaydı da tutulmaz.
        var katalogGrupSirasi = AppScreens.Groups.Select(g => g.Title).ToList();
        var istenenGrupSirasi = temizGruplar.OrderBy(g => g.SortOrder).Select(g => g.GroupKey).ToList();
        var grupSirasiVarsayilan = katalogGrupSirasi.SequenceEqual(istenenGrupSirasi, StringComparer.Ordinal);

        var yazilacakGruplar = new List<(string Key, string? Title, int? Sort, bool Custom)>();
        var sirali = temizGruplar.OrderBy(g => g.SortOrder).ToList();
        for (int i = 0; i < sirali.Count; i++)
        {
            var g = sirali[i];
            var baslik = !g.IsCustom && string.Equals(g.Title, g.GroupKey, StringComparison.Ordinal) ? null : g.Title;
            int? sira = grupSirasiVarsayilan ? null : i;
            if (baslik is null && sira is null && !g.IsCustom) continue;
            yazilacakGruplar.Add((g.GroupKey, baslik, sira, g.IsCustom));
        }

        // ── 4) Tek transaction: eskiyi sil, yeniyi yaz ──────────────────────────────────────────
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        Sil(conn, tx, "screen_menu_layout", s.CompanyId);
        Sil(conn, tx, "menu_group_layout", s.CompanyId);

        foreach (var e in yazilacakEkranlar)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO screen_menu_layout(id,company_id,screen_key,label_override," +
                              "group_key_override,sort_order,created_at,updated_at) " +
                              "VALUES(@id,@c,@s,@l,@g,@o,@now,@now);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@s", e.Key);
            cmd.AddWithValue("@l", (object?)e.Label ?? DBNull.Value);
            cmd.AddWithValue("@g", (object?)e.Group ?? DBNull.Value);
            cmd.AddWithValue("@o", (object?)e.Sort ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }

        foreach (var g in yazilacakGruplar)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO menu_group_layout(id,company_id,group_key,title_override," +
                              "sort_order,is_custom,created_at,updated_at) " +
                              "VALUES(@id,@c,@g,@t,@o,@ic,@now,@now);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@g", g.Key);
            cmd.AddWithValue("@t", (object?)g.Title ?? DBNull.Value);
            cmd.AddWithValue("@o", (object?)g.Sort ?? DBNull.Value);
            cmd.AddWithValue("@ic", g.Custom ? 1 : 0);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }

        var ozelSayisi = yazilacakGruplar.Count(g => g.Custom);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "menu_layout", s.CompanyId,
            AuditActions.Update, s.UserId,
            AfterJson: $"{{\"screens\":{yazilacakEkranlar.Count},\"groups\":{yazilacakGruplar.Count}," +
                       $"\"customGroups\":{ozelSayisi}}}"), _clock);

        tx.Commit();
        Invalidate(s.CompanyId);

        return new MenuLayoutSaveResult(yazilacakEkranlar.Count, yazilacakGruplar.Count, ozelSayisi);
    }

    /// <summary>
    /// "Varsayılan düzene dön" — firmanın TÜM düzen tercihlerini kaldırır. Platform ayarlarına
    /// DOKUNMAZ (o ayrı bir karardır ve ayrı ekranda/kolonda yönetilir).
    /// </summary>
    public void ResetToDefaults(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Sil(conn, tx, "screen_menu_layout", s.CompanyId);
        Sil(conn, tx, "menu_group_layout", s.CompanyId);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "menu_layout", s.CompanyId,
            AuditActions.Update, s.UserId, AfterJson: "{\"reset\":true}"), _clock);
        tx.Commit();
        Invalidate(s.CompanyId);
    }

    private static void Sil(DbConnection conn, DbTransaction tx, string table, string companyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"DELETE FROM {table} WHERE company_id=@c;";
        cmd.AddWithValue("@c", companyId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Görünen ad temizliği: kırp, boşsa null (varsayılana dön), uzunsa reddet.</summary>
    private static string? Temizle(string? value)
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;
        if (v.Length > MaxLabelLength)
            throw new ArgumentException($"Ad en fazla {MaxLabelLength} karakter olabilir.");
        // Satır sonu / sekme menüyü bozar → tek boşluğa indirilir.
        return string.Join(' ', v.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }
}
