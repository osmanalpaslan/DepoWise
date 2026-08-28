using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Announcements;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Organization;

namespace DepoWise.Infrastructure.Search;

/// <summary>Tek arama sonucu satırı. NavigateKey masaüstü gezinme anahtarıdır (web `_`→`-`, `:`→`/`).</summary>
public sealed record SearchHit(string Module, string ModuleDisplay, string Id, string Label,
    string? SubLabel, string NavigateKey);

/// <summary>Kategori grubu — kategori başına en fazla <see cref="SearchService.PerSourceLimit"/> satır;
/// fazlası varsa <paramref name="HasMore"/> ("daha fazlası için ekrana git").</summary>
public sealed record SearchGroup(string ModuleDisplay, string NavigateKey, IReadOnlyList<SearchHit> Hits, bool HasMore);

/// <summary>
/// ═══ ARA-01 (ADR-174, 2026-08-28) — GLOBAL ARAMA ═══
///
/// PK-K1..K5 AYNEN: kayıt/kart nitelikli kaynaklar (hareket defterleri HARİÇ) · yalnız KİMLİK alanları
/// (kod/no/ad/başlık/plaka — açıklama/not aranmaz) · silinmişler ARANMAZ (Çöp Kutusu'nda kalır) ·
/// yeni yetki modülü YOK — her kaynak bloğu KENDİ modülünün View kapısıyla sarılıdır (yetkisiz kategori
/// HİÇ SORGULANMAZ → sızma yapısal olarak imkânsız); şubeli kaynaklarda BranchAccess süzgeci; tenant
/// her sorguda. PARALEL VERİ/İNDEKS YOK: salt-okunur türetme; FTS/fuzzy/harici motor bilinçli yok.
///
/// <b>Lehçe/Türkçe notu:</b> süzme SQL LIKE ile DEĞİL bellek içinde yapılır (SQL yalnız firma+silinmemiş
/// daraltır, dar kimlik kolonları çekilir): SQLite ile PostgreSQL'de BİREBİR aynı sonuç + Türkçe
/// büyük/küçük harf doğru (LIKE iki lehçede farklı ve TR'ye duyarsızdır). Firma başına kayıt hacmi
/// küçük (canlı: tek firma) — mevcut modül aramalarının çoğu da aynı bellek-içi desendedir.
/// Sıralama: kategori içinde aramayla BAŞLAYAN önce, sonra içeren; skor motoru yok.
/// </summary>
public sealed class SearchService
{
    public const int MinQueryLength = 2;
    public const int PerSourceLimit = 5;

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly AnnouncementService _announcements;
    private readonly ProjectService _projects;
    private readonly DocumentService? _documents;   // masaüstünde null — evrak sunucu-otoriteli (çevrimiçi API'den)

    public SearchService(IDbConnectionFactory factory, DocumentService? documents = null, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _announcements = new AnnouncementService(factory, _clock);
        _projects = new ProjectService(factory, _clock);
        _documents = documents;
    }

    /// <summary>Aranan SQL kaynağı: (yetki modülü, tablo, id-etiket-altetiket kolonları, şube kolonu?, nav, başlık).</summary>
    private sealed record Source(string Module, string ModuleDisplay, string Table,
        string LabelCol, string? SubCol, string? BranchCol, string NavigateKey);

    // PK-K1: kayıt/kart nitelikli kaynaklar. Tedarikçi kartı Tanımlar ekranında yönetilir → kapı+hedef "definitions".
    private static readonly IReadOnlyList<Source> Sources = new[]
    {
        new Source("materials",    "Malzemeler",         "materials",         "name",          "code",          null,        "materials"),
        new Source("vehicles",     "Araçlar",            "vehicles",          "internal_code", "plate",         null,        "vehicles"),
        new Source("personnel",    "Personel",           "personnel",         "full_name",     null,            null,        "personnel"),
        new Source("equipment",    "Ekipman",            "equipment",         "name",          "code",          null,        "equipment"),
        new Source("branches",     "Şube / Şantiye",     "branches",          "name",          "code",          null,        "branches"),
        new Source("parties",      "Cari",               "parties",           "title",         "code",          null,        "parties"),
        new Source("definitions",  "Tedarikçiler",       "suppliers",         "name",          null,            null,        "definitions"),
        new Source("cost_centers", "Maliyet Merkezleri", "cost_centers",      "name",          "code",          null,        "cost_centers"),
        new Source("work_orders",  "İş Emirleri",        "work_orders",       "title",         "wo_no",         "branch_id", "work_orders"),
        new Source("purchasing",   "Satın Alma",         "purchase_orders",   "order_no",      null,            "branch_id", "purchasing"),
        new Source("requests",     "Talepler",           "material_requests", "doc_no",        null,            "branch_id", "requests:form"),
        new Source("calendar",     "Takvim",             "calendar_events",   "title",         null,            "branch_id", "calendar"),
    };

    /// <summary>
    /// Global arama. <paramref name="onlySources"/> verilirse yalnız o NavigateKey'li kaynaklar taranır
    /// (masaüstü, sunucu-otoriteli Proje+Evrak'ı çevrimiçiyken API'den BÖYLE ister).
    /// </summary>
    public IReadOnlyList<SearchGroup> Search(SessionContext s, string query, IReadOnlyCollection<string>? onlySources = null)
    {
        var q = (query ?? "").Trim();
        if (q.Length < MinQueryLength) return Array.Empty<SearchGroup>();
        bool Istenen(string nav) => onlySources is null || onlySources.Contains(nav);

        var izinli = BranchAccess.Allowed(s);
        var kapsam = izinli?.ToHashSet(StringComparer.Ordinal);
        var groups = new List<SearchGroup>();

        using var conn = _factory.Create();
        foreach (var src in Sources)
        {
            // YAN KAPI YOK: modül yetkisi olmayan kategori HİÇ sorgulanmaz.
            if (!Istenen(src.NavigateKey)) continue;
            if (!AccessControl.Can(s, src.Module, PermissionAction.View)) continue;
            var hits = new List<(string Id, string Label, string? Sub, string? Branch)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT id, {src.LabelCol}{(src.SubCol is null ? "" : ", " + src.SubCol)}" +
                                  $"{(src.BranchCol is null ? "" : ", " + src.BranchCol)} " +
                                  $"FROM {src.Table} WHERE company_id=@c AND is_deleted=0;";   // PK-K4: silinmiş ARANMAZ
                cmd.AddWithValue("@c", s.CompanyId);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var i = 1;
                    var label = r.IsDBNull(1) ? "" : r.GetString(1);
                    var sub = src.SubCol is null ? null : (r.IsDBNull(++i) ? null : r.GetString(i));
                    var branch = src.BranchCol is null ? null : (r.IsDBNull(++i) ? null : r.GetString(i));
                    hits.Add((r.GetString(0), label, sub, branch));
                }
            }
            // ŞUBE KAPSAMI: kapsam dışı şubenin kaydı sonuca SIZMAZ; şubesiz gizlenmez (sınıf kuralı).
            if (kapsam is not null && src.BranchCol is not null)
                hits = hits.Where(h => h.Branch is null || kapsam.Contains(h.Branch)).ToList();

            Ekle(groups, src.ModuleDisplay, src.NavigateKey, q,
                hits.Select(h => new SearchHit(src.Module, src.ModuleDisplay, h.Id, h.Label, h.Sub, src.NavigateKey)));
        }

        // ÖZEL KURALLI kaynaklar KENDİ servislerinden aranır (yetki/kapsam/pencere kuralları İÇERİDE):
        // Duyuru — okuma herkese; yönetici-dışı yalnız AKTİF görür (DYR-01 kuralı serviste).
        if (Istenen("announcements") && AccessControl.Can(s, AnnouncementService.Module, PermissionAction.View))
            Ekle(groups, "Duyurular", "announcements", q,
                Guvenli(() => _announcements.List(s, includeInactive: true, search: q)
                    .Select(a => new SearchHit(AnnouncementService.Module, "Duyurular", a.Id, a.Title, a.BranchDisplay, "announcements"))));
        // Proje — modül branches; BranchAccess kapsamı serviste (masaüstünde tablo boş → boş döner).
        if (Istenen("projects") && AccessControl.Can(s, "branches", PermissionAction.View))
            Ekle(groups, "Projeler", "projects", q,
                Guvenli(() => _projects.List(s, search: q)
                    .Select(p => new SearchHit("branches", "Projeler", p.Id, p.Name, p.BranchDisplay == "—" ? null : p.BranchDisplay, "projects"))));
        // Evrak — YALNIZ METADATA (başlık/dosya adı/bağlı kayıt); iki kapı + kapsam DocumentService içinde.
        if (Istenen("documents") && _documents is not null && AccessControl.Can(s, DocumentService.Module, PermissionAction.View))
            Ekle(groups, "Evrak", "documents", q,
                Guvenli(() => _documents.List(s, search: q)
                    .Select(d => new SearchHit(DocumentService.Module, "Evrak", d.Id, d.Title,
                        d.EntityLabel == "—" ? d.FileName : d.EntityLabel, "documents"))));

        return groups;
    }

    /// <summary>Eşleşme + sıralama + limit: BAŞLAYAN önce, sonra İÇEREN (Label/SubLabel; TR duyarsız).</summary>
    private static void Ekle(List<SearchGroup> groups, string display, string nav, string q, IEnumerable<SearchHit> adaylar)
    {
        bool Icerir(SearchHit h) =>
            h.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || (h.SubLabel?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false);
        bool Baslar(SearchHit h) =>
            h.Label.StartsWith(q, StringComparison.CurrentCultureIgnoreCase)
            || (h.SubLabel?.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) ?? false);

        var eslesen = adaylar.Where(Icerir)
            .OrderByDescending(Baslar)
            .ThenBy(h => h.Label, StringComparer.CurrentCulture)
            .ToList();
        if (eslesen.Count == 0) return;
        groups.Add(new SearchGroup(display, nav, eslesen.Take(PerSourceLimit).ToList(), eslesen.Count > PerSourceLimit));
    }

    /// <summary>Servis tabanlı kaynakta beklenmeyen hata TÜM aramayı düşürmesin (kategori sessiz atlanır).</summary>
    private static IEnumerable<SearchHit> Guvenli(Func<IEnumerable<SearchHit>> f)
    {
        try { return f(); } catch { return Array.Empty<SearchHit>(); }
    }
}
