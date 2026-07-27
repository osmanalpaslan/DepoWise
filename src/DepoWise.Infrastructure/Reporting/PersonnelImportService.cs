using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Security;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>
/// Personel içe aktarımı — sütunlar YENİ KAYIT FORMUYLA BİREBİR (kullanıcı kuralı 2026-07-16).
///
/// ── "Saha Personeli" ve "Kullanıcı Adı" NE ANLAMA GELİR? ────────────────────────────────────────
/// Bu ikisi personel ekranındaki iki ayrı kavramın Excel karşılığıdır ve BİRBİRİNİ DIŞLAR:
///
///  • <b>Saha Personeli = Evet</b> → kişi uygulamaya HİÇ GİRMEZ (şoför, operatör…). Sistemde yalnız
///    "kayıt" olarak durur; "kullanıcı bağlanmadı" uyarısı çıkmaz. Formdaki kutucuğun karşılığıdır ve
///    işaretlendiğinde form da kullanıcı bağını temizler (Personnel.razor → OnFieldStaffChanged).
///
///  • <b>Kullanıcı Adı</b> → kişi uygulamaya GİRECEK. Bu sütuna, "Kullanıcılar" ekranında ZATEN AÇILMIŞ
///    hesabın kullanıcı adı yazılır ve o hesap bu personele bağlanır (bir personele TEK hesap).
///    ⚠️ Bu sütun hesap AÇMAZ: hesap açmak şifre + rol + yetki ister; Excel'den yapılmaz (güvenlik).
///    Hesabı olmayan biri için burayı BOŞ bırakın, sonra Kullanıcılar ekranından açıp bağlayın.
///
/// İkisi birden dolu olamaz (çelişki) → satır reddedilir. İkisi de boşsa: kişi uygulamaya girmeyen ama
/// "saha personeli" işaretlenmemiş biri olur — form bu durumda uyarı gösterir, içe aktarımda serbesttir.
///
/// Unvan sabit tanım listesindendir; yoksa OTOMATİK oluşturulur (PersonnelTitleService.Create idempotent
/// ve TÜRKÇE duyarlı karşılaştırır: "Şoför" == "şoför"). Şube de yoksa otomatik oluşur.
///
/// ⚠️ MÜKERRER: personelin benzersiz kodu YOKTUR (araçtaki iç kod gibi). Bu yüzden mükerrer anahtarı
/// NORMALİZE EDİLMİŞ AD'dır (PersonnelService.ImportKey — FindDuplicates ile aynı normalizasyon:
/// boşluksuz + küçük harf). Aynı dosya iki kez aktarılırsa kayıt TEKRARLANMAZ. Bedeli: gerçekten aynı
/// isimli İKİ FARKLI kişi varsa ikincisi "zaten var" diye atlanır — bu yüzden atlananlar raporlanır.
/// </summary>
public sealed class PersonnelImportService
{
    public const string ColName = "Ad Soyad";              // ZORUNLU
    public const string ColTitle = "Unvan";
    public const string ColPhone = "Telefon";
    public const string ColBranch = "Şube";
    public const string ColActive = "Aktif";               // Evet/Hayır (boş = Evet)
    public const string ColFieldStaff = "Saha Personeli";  // Evet/Hayır (boş = Hayır)
    public const string ColUsername = "Kullanıcı Adı";     // MEVCUT hesabın kullanıcı adı (hesap AÇMAZ)

    private readonly PersonnelService _personnel;
    private readonly PersonnelTitleService _titles;
    private readonly UserService _users;
    private readonly LookupService _lookups;

    public PersonnelImportService(PersonnelService personnel, PersonnelTitleService titles,
        UserService users, LookupService lookups)
    { _personnel = personnel; _titles = titles; _users = users; _lookups = lookups; }

    public IReadOnlyList<string> SampleHeaders()
        => new[] { ColName, ColTitle, ColPhone, ColBranch, ColActive, ColFieldStaff, ColUsername };

    public ImportResult DryRun(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "personnel", PermissionAction.View);
        var errors = new List<ImportRowError>(); int valid = 0;
        var linkable = LinkableUsernames(s);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!Validate(row, linkable, out var err))
            {
                if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, err!));
                continue;
            }
            // Dosya İÇİNDE aynı ad iki kez → ikincisi atlanacak; kullanıcı ŞİMDİ bilsin.
            var key = PersonnelService.ImportKey(Get(row, ColName));
            if (!seen.Add(key))
            {
                if (errors.Count < ImportResult.MaxReportedErrors)
                    errors.Add(new ImportRowError(row.RowNumber, $"Bu ad dosyada birden çok kez geçiyor: {Get(row, ColName)}"));
                continue;
            }
            valid++;
        }
        return new ImportResult(true, rows.Count, valid, 0, 0, rows.Count - valid, errors);
    }

    /// <summary>Commit + bu aktarımda OLUŞAN yeni tanımlar (unvan / şube).</summary>
    public (ImportResult Result, IReadOnlyList<string> CreatedLookups) CommitWithLookups(SessionContext s, IReadOnlyList<ImportRow> rows)
    {
        AccessControl.Require(s, "personnel", PermissionAction.Create);
        var res = new ImportLookupResolver(_lookups, s);
        var created = new List<string>();

        // ── Önbellekler: 2600 satırda satır başına DB sorgusu OLMAMALI ──
        var existing = _personnel.AllNameToId(s);                    // mükerrer kontrolü (sayfalamasız)
        var titleCache = LoadTitles(s);                              // unvan adı → ad (sabit tanım)
        var linkable = LinkableUsernames(s);                         // kullanıcı adı → userId (bağlanabilir)

        var errors = new List<ImportRowError>(); int added = 0, skipped = 0, failed = 0;
        foreach (var row in rows)
        {
            if (!Validate(row, linkable, out var verr))
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, verr!)); continue; }
            try
            {
                var fullName = Get(row, ColName)!.Trim();
                var key = PersonnelService.ImportKey(fullName);
                if (existing.ContainsKey(key)) { skipped++; continue; }   // zaten var → atla (idempotent)

                var fieldStaff = ParseBool(Get(row, ColFieldStaff)) ?? false;

                var id = _personnel.Create(s, new NewPersonnel(
                    FullName: fullName,
                    Title: ResolveTitle(s, titleCache, created, Get(row, ColTitle)),
                    Phone: Empty(Get(row, ColPhone)),
                    // Satırda "Şube" boşsa içe aktarım ekranında seçilen şubeye (oturum) düşer (2026-07-26).
                    BranchId: res.Branch(Get(row, ColBranch)) ?? s.OperatingBranchId,
                    IsActive: ParseBool(Get(row, ColActive)) ?? true,     // boş = Aktif
                    IsFieldStaff: fieldStaff));
                existing[key] = id;

                // Kullanıcı bağlama: hesap AÇILMAZ, MEVCUT hesap bağlanır. Bağlanan hesap listeden düşer
                // (bir personele tek hesap) → aynı kullanıcı adı iki satırda geçerse ikincisi hata verir.
                var username = Get(row, ColUsername);
                if (!string.IsNullOrWhiteSpace(username))
                {
                    var uname = username.Trim();
                    _users.LinkPersonnel(s, linkable[UserKey(uname)], id);
                    linkable.Remove(UserKey(uname));
                }
                added++;
            }
            catch (Exception ex)
            { failed++; if (errors.Count < ImportResult.MaxReportedErrors) errors.Add(new ImportRowError(row.RowNumber, ex.Message)); }
        }
        created.AddRange(res.CreatedNames);
        return (new ImportResult(false, rows.Count, added, added, skipped, failed, errors), created);
    }

    public ImportResult Commit(SessionContext s, IReadOnlyList<ImportRow> rows) => CommitWithLookups(s, rows).Result;

    // ── Unvan (sabit tanım; yoksa oluştur) ─────────────────────────────────────────────────
    /// <summary>Unvan adı → kanonik ad. NewPersonnel.Title ADI tutar (id değil) — web formuyla aynı.</summary>
    private Dictionary<string, string> LoadTitles(SessionContext s)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try { foreach (var t in _titles.List(s)) map[TitleKey(t.Name)] = t.Name; } catch { }
        return map;
    }

    private string? ResolveTitle(SessionContext s, Dictionary<string, string> cache, List<string> created, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var k = TitleKey(name);
        if (cache.TryGetValue(k, out var existing)) return existing;
        // Create İDEMPOTENT + Türkçe duyarlı: aynı isim varsa mevcudunu döner (sessiz tekrar oluşmaz).
        var t = _titles.Create(s, name.Trim());
        cache[k] = t.Name;
        created.Add($"Unvan: {t.Name}");
        return t.Name;
    }

    // ── Kullanıcı bağlama ──────────────────────────────────────────────────────────────────
    /// <summary>Bağlanabilir hesaplar: kullanıcı adı → userId. Zaten bir personele bağlı olanlar ve
    /// süper adminler bu listede YOKTUR (UserService.ListLinkableUsers kuralı).</summary>
    private Dictionary<string, string> LinkableUsernames(SessionContext s)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        // Yalnız Admin/Süper Admin bağlayabilir; değilse liste boş kalır → "Kullanıcı Adı" dolu satır
        // net bir mesajla reddedilir (sessizce bağsız eklenmez).
        if (!AccessControl.IsAdmin(s)) return map;
        try { foreach (var u in _users.ListLinkableUsers(s)) map[UserKey(u.Username)] = u.Id; } catch { }
        return map;
    }

    private bool Validate(ImportRow row, IReadOnlyDictionary<string, string> linkable, out string? error)
    {
        if (string.IsNullOrWhiteSpace(Get(row, ColName))) { error = "Ad Soyad zorunlu."; return false; }

        foreach (var col in new[] { ColActive, ColFieldStaff })
        {
            var raw = Get(row, col);
            if (!string.IsNullOrWhiteSpace(raw) && ParseBool(raw) is null)
            { error = $"{col}: Evet ya da Hayır yazın ({raw})"; return false; }
        }

        var fieldStaff = ParseBool(Get(row, ColFieldStaff)) ?? false;
        var username = Get(row, ColUsername);
        var hasUser = !string.IsNullOrWhiteSpace(username);

        // ÇELİŞKİ: "saha personeli" = uygulamaya girmez; kullanıcı bağlamak = girer. İkisi birden olamaz.
        // Ekranda da böyle: kutucuk işaretlenince form kullanıcı bağını temizler.
        if (fieldStaff && hasUser)
        { error = "Çelişki: 'Saha Personeli = Evet' ise kişi uygulamaya girmez; 'Kullanıcı Adı' boş olmalı."; return false; }

        if (hasUser && !linkable.ContainsKey(UserKey(username!)))
        {
            error = $"Kullanıcı bulunamadı ya da bağlanamaz: {username}. " +
                    "Hesap önce 'Kullanıcılar' ekranından açılmalı; zaten başka personele bağlıysa kullanılamaz. " +
                    "(İçe aktarım hesap AÇMAZ.)";
            return false;
        }
        error = null; return true;
    }

    /// <summary>Türkçe Excel'de evet/hayır çok farklı yazılır — hepsini kabul et; tanınmayan değeri REDDET
    /// (sessizce "hayır" saymak yanlış veri üretir).</summary>
    private static bool? ParseBool(string? s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant();
        if (t.Length == 0) return null;   // boş = "belirtilmemiş" (çağıran varsayılanı uygular)
        return t switch
        {
            "evet" or "e" or "var" or "x" or "1" or "true" or "aktif" or "yes" => true,
            "hayır" or "hayir" or "h" or "yok" or "0" or "false" or "pasif" or "no" => false,
            _ => null,
        };
    }

    private static string TitleKey(string s) => s.Trim().ToLowerInvariant();
    private static string UserKey(string s) => s.Trim().ToUpperInvariant();
    private static string? Empty(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string? Get(ImportRow row, string col) => row.Values.TryGetValue(col, out var v) ? v : null;
}
