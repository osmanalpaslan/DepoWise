using System.Data.Common;

namespace DepoWise.Application.Security;

/// <summary>
/// G4-3b / G1-scope — ŞUBE KAPSAMI: TEK YETKİ OTORİTESİ (kullanıcı isteği 2026-08-12).
///
/// <b>NEDEN VAR:</b> projede şube ile ilgili İKİ ayrı kavram vardı ve ön muhasebe yanlış olanı
/// kullanıyordu:
/// <list type="number">
///   <item><see cref="SessionContext.OperatingBranchId"/> + <see cref="BranchScope"/> — oturumun
///     ÇALIŞMA şubesi. Bu bir <b>görünüm tercihi</b>dir, güvenlik kapısı DEĞİLDİR. Masaüstü girişte
///     seçilir; <b>web/API tarafında hiç doldurulmaz</b> (PermissionSnapshot.ToSession onu taşımaz)
///     → web'de şube filtresi fiilen ÇALIŞMIYORDU.</item>
///   <item><c>user_scopes</c> tablosu + <c>ScopeResolver</c> — kullanıcının ERİŞMEYE YETKİLİ olduğu
///     şubeler. Gerçek güvenlik kapısı budur ama yalnız Şube ve Personel ekranlarında kullanılıyordu;
///     cari, fatura, kasa/banka ve raporlar bunu hiç sormuyordu.</item>
/// </list>
///
/// Bu sınıf ikisini <b>tek formülde</b> birleştirir ve ikinci bir şube/yetki sistemi kurmaz:
///
/// <code>
/// ETKİN KAPSAM = İZİNLİ ŞUBELER ∩ (İSTENEN ŞUBELER ?? OTURUM ŞUBESİ ?? İZİNLİ ŞUBELER)
/// </code>
///
/// <b>FAIL-CLOSED:</b> istenen şube izinli kümede yoksa <b>sessizce yok sayılmaz</b> —
/// <see cref="Require"/> hata atar, <see cref="Effective"/> ise kesişimi alır. Kullanıcı API'ye elle
/// <c>branchId</c> yazarak kapsamını genişletemez.
///
/// <b>İZİNLİ ŞUBELER nasıl belirlenir (öncelik sırası):</b>
/// <list type="number">
///   <item><c>user_scopes</c> satırları varsa <b>yalnız onlar</b> — admin olsa bile. (Süper admin
///     bir yöneticiyi 3 şubeye kısıtlayabilsin diye; mevcut <c>ScopeResolver</c> kuralının aynısı.)</item>
///   <item>Aksi halde admin / süper admin / <see cref="SessionContext.CanViewAllBranches"/> →
///     <b>sınırsız</b> (null).</item>
///   <item>Aksi halde kullanıcının kendi şubesi (<c>users.branch_id</c>) varsa <b>yalnız o şube</b>.</item>
///   <item>Hiçbiri yoksa → <b>sınırsız</b> (null). ⚠️ Bu bilinçli bir karardır: şubesi atanmamış bir
///     kullanıcıyı "hiçbir şey göremez" yapmak, bugün çalışan kullanıcıları sessizce kilitlerdi.
///     Sıkılaştırmanın doğru yolu kullanıcıya şube ATAMAKTIR.</item>
/// </list>
///
/// <b>NULL ŞUBELİ KAYITLAR:</b> şubesiz (firma geneli) kayıtlar filtrede GİZLENMEZ — eski/şubesiz
/// veri görünmez olmasın (<see cref="BranchScope"/> ile aynı ilke). Bu yalnız OKUMA içindir;
/// <see cref="Require"/> yazma yolunda hedef şubeyi ayrıca doğrular.
/// </summary>
public static class BranchAccess
{
    /// <summary>
    /// Kullanıcının erişmeye YETKİLİ olduğu şubeler. <c>null</c> → sınırsız (filtre uygulanmaz).
    /// Boş küme → hiçbir şubeye erişim yok (yalnız şubesiz kayıtlar görünür).
    /// </summary>
    public static IReadOnlyList<string>? Allowed(SessionContext s)
    {
        // 1) Açık kapsam her şeyin ÜSTÜNDEDİR — admin bypass'ı bunu kaldırmaz.
        if (s.ScopeBranchIds is { Count: > 0 }) return Expand(s, s.ScopeBranchIds);

        // 2) Tüm şubeleri görme yetkisi / admin → sınırsız.
        if (s.CanViewAllBranches || AccessControl.IsAdmin(s)) return null;

        // 3) Kendi şubesi → o şube VE altındakiler (ŞB-04).
        if (!string.IsNullOrEmpty(s.HomeBranchId)) return Expand(s, new[] { s.HomeBranchId! });

        // 4) Şubesi atanmamış kullanıcı → sınırsız (bkz. sınıf açıklaması).
        return null;
    }

    /// <summary>
    /// ŞB-04 (2026-08-18) — ŞUBE AĞACI GENİŞLETMESİ: verilen şubelere TÜM alt şubeleri eklenir.
    ///
    /// <b>NEDEN:</b> "Üst Şube" alanı bugüne kadar yalnız bir etiketti — Merkez'e yetkili bir kullanıcı
    /// Merkez'in altındaki şantiyeleri GÖREMİYOR, Merkez seçildiğinde rapor altları TOPLAMIYORDU.
    /// Hiyerarşinin tek anlamı budur; kapsam ve rapor artık ağaca uyar.
    ///
    /// <b>FAIL-SAFE:</b> ağaç yüklenmemişse (<see cref="SessionContext.BranchDescendants"/> null)
    /// girdi AYNEN döner → ŞB-04 öncesi davranış. Kapsam kazara genişlemez.
    /// Sıra korunur (önce istenenler, sonra altlar) ve tekrarlar ayıklanır.
    /// </summary>
    public static IReadOnlyList<string> Expand(SessionContext s, IReadOnlyList<string> ids)
    {
        var tree = s.BranchDescendants;
        if (tree is null || tree.Count == 0 || ids.Count == 0) return ids;

        List<string>? genis = null;
        var set = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!tree.TryGetValue(id, out var altlar)) continue;
            foreach (var alt in altlar)
            {
                if (!set.Add(alt)) continue;
                (genis ??= new List<string>(ids)).Add(alt);
            }
        }
        return genis ?? ids;   // alt şube yoksa yeni liste üretme
    }

    /// <summary>
    /// Kullanıcı bu şubeye erişebilir mi? Şubesiz (null) kayıt herkese açıktır.
    ///
    /// <b>İKİ KISIT BİRLİKTE uygulanır:</b> kullanıcının İZİNLİ şubeleri VE oturumun ÇALIŞMA şubesi.
    /// Yani "ŞUBE A ile giriş yapan" bir yönetici, yetkisi olsa bile o oturumda ŞUBE B'ye yazamaz —
    /// seçtiği şubede çalıştığını sanırken yanlışlıkla başka şubeye kayıt atmasın. Firma geneli
    /// çalışmak isteyen "Tüm Şubeler" ile girer.
    /// </summary>
    public static bool CanAccess(SessionContext s, string? branchId)
    {
        if (string.IsNullOrEmpty(branchId)) return true;
        var eff = Effective(s);   // izinli ∩ oturum şubesi
        return eff is null || eff.Contains(branchId, StringComparer.Ordinal);
    }

    /// <summary>
    /// Yazma yolunun kapısı: hedef şube kapsam dışıysa hata atar.
    /// UI'da şube gizlemek YETMEZ — bu kontrol servis katmanındadır, API atlanarak da geçilemez.
    /// </summary>
    public static void Require(SessionContext s, string? branchId, string op = "işlem")
    {
        if (!CanAccess(s, branchId))
            throw new ForbiddenException($"Şube kapsam dışı: bu şubede {op} yapamazsınız.");
    }

    /// <summary>
    /// Yazma yolunda hedef şubeyi ÇÖZER:
    /// verilmediyse oturumun çalışma şubesine, o da yoksa tek izinli şubeye düşer;
    /// verildiyse kapsam içinde olduğunu DOĞRULAR.
    /// </summary>
    public static string? Resolve(SessionContext s, string? branchId, string op = "işlem")
    {
        if (!string.IsNullOrEmpty(branchId)) { Require(s, branchId, op); return branchId; }

        // Belirtilmemiş: oturumun çalışma şubesi varsa onu kullan (o da kapsam içinde olmalı).
        if (!string.IsNullOrEmpty(s.OperatingBranchId)) { Require(s, s.OperatingBranchId, op); return s.OperatingBranchId; }

        // Kullanıcının TEK izinli şubesi varsa oraya yaz (şube seçmek zorunda kalmasın).
        var allowed = Allowed(s);
        if (allowed is { Count: 1 }) return allowed[0];

        return null;   // firma geneli / kullanıcı seçmeli
    }

    /// <summary>
    /// OKUMA kapsamı: <c>İZİNLİ ∩ (İSTENEN ?? OTURUM ?? İZİNLİ)</c>.
    /// <c>null</c> → filtre yok. Boş liste → hiçbir şube (yalnız şubesiz kayıtlar).
    ///
    /// <b>"Tümü" kullanıcının TÜM FİRMA ŞUBELERİ demek DEĞİLDİR</b> — kullanıcının yetkili olduğu
    /// şubeler demektir. Tek şubeli kullanıcıda "Tümü" yine kendi şubesidir.
    /// </summary>
    public static IReadOnlyList<string>? Effective(SessionContext s, IReadOnlyList<string>? requested = null)
    {
        var allowed = Allowed(s);

        // ŞB-04: İSTENEN şube de ağaca göre genişler — kullanıcı "Merkez" seçtiğinde altındaki
        // şantiyelerin verisi de gelir (raporun "üst şube toplar" beklentisi budur). İzinli kümeyle
        // kesişim AYNEN korunur → genişletme kapsamı AŞMAZ, yalnız izinli olanları getirir.
        IReadOnlyList<string>? wanted = requested is { Count: > 0 }
            ? Expand(s, requested)
            : (!string.IsNullOrEmpty(s.OperatingBranchId) ? Expand(s, new[] { s.OperatingBranchId! }) : null);

        if (wanted is null) return allowed;                       // istenen yok → izinli küme (null olabilir)
        if (allowed is null) return wanted;                       // sınırsız kullanıcı → istediği

        // ⭐ KESİŞİM: istenen şubelerden YALNIZ izinli olanlar. Elle branch_id göndererek
        // kapsam genişletilemez (fail-closed).
        var set = new HashSet<string>(allowed, StringComparer.Ordinal);
        return wanted.Where(set.Contains).ToList();
    }

    /// <summary>
    /// WHERE parçası. Boş/null kapsam → <c>""</c> (filtre yok).
    /// Aksi halde <c>AND (col IN (@ba0,…) OR col IS NULL)</c> — şubesiz kayıtlar GİZLENMEZ.
    /// Kapsam boş listeyse <c>AND col IS NULL</c> (hiçbir şubeye erişim yok, fail-closed).
    /// </summary>
    public static string Sql(SessionContext s, string col, IReadOnlyList<string>? requested = null, string prefix = "@ba")
    {
        var eff = Effective(s, requested);
        if (eff is null) return "";
        if (eff.Count == 0) return $" AND {col} IS NULL";
        var ps = string.Join(",", Enumerable.Range(0, eff.Count).Select(i => prefix + i));
        return $" AND ({col} IN ({ps}) OR {col} IS NULL)";
    }

    /// <summary>Kapsam parametrelerini bağlar. <see cref="Sql"/> ile AYNI kaynağı kullanır (deterministik).</summary>
    public static void Bind(DbCommand cmd, SessionContext s, IReadOnlyList<string>? requested = null, string prefix = "@ba")
    {
        var eff = Effective(s, requested);
        if (eff is null) return;
        for (int i = 0; i < eff.Count; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = prefix + i;
            p.Value = eff[i];
            cmd.Parameters.Add(p);
        }
    }

    /// <summary>
    /// YETKİ DEVRİ TAVANI (G1 kuralının şube karşılığı): bir kullanıcı kendisinde OLMAYAN şube
    /// kapsamını başkasına veremez.
    ///
    /// Döner: hedefe verilebilecek şube listesi. <c>null</c> → sınırsız verebilir (devreden de sınırsız).
    /// Boş liste → hiçbir şube veremez.
    /// </summary>
    public static IReadOnlyList<string>? GrantCeiling(SessionContext actor, IReadOnlyList<string>? requested)
    {
        var mine = Allowed(actor);
        if (mine is null) return requested;                    // sınırsız devreden → istediğini verebilir
        if (requested is null || requested.Count == 0) return Array.Empty<string>();

        var set = new HashSet<string>(mine, StringComparer.Ordinal);
        return requested.Where(set.Contains).ToList();          // yalnız kendi kapsamının alt kümesi
    }

    /// <summary>
    /// Devir denetimi: istenen şubelerin tamamı devredenin kapsamındaysa geçer, değilse HATA.
    /// Sessizce kırpmaz — kullanıcı ne veremediğini görsün.
    /// </summary>
    public static void RequireGrantable(SessionContext actor, IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0) return;
        var mine = Allowed(actor);
        if (mine is null) return;
        var set = new HashSet<string>(mine, StringComparer.Ordinal);
        var disi = requested.Where(x => !set.Contains(x)).ToList();
        if (disi.Count > 0)
            throw new ForbiddenException(
                "Kendinizde olmayan şube kapsamını devredemezsiniz. " +
                $"Kapsam dışı şube sayısı: {disi.Count}.");
    }
}
