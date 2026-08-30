using System.Data.Common;
using DepoWise.Application.Approvals;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Approvals;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 2 (ADR-187) — KULLANICI HİYERARŞİSİ ═══
///
/// <b>PK-EK-02:</b> hiyerarşi <c>users</c> tablosunda DEĞİL, ayrı <c>user_hierarchy</c> tablosundadır.
/// <b>İK-2:</b> azami <b>4 düğüm</b> (kullanıcı dâhil) → en çok <b>3 onaycı</b>.
/// <b>İK-8:</b> firma bazlı — <c>branch_id</c> yok, <c>BranchAccess</c> genişletilmez.
///
/// <b>EKİPLERLE KARIŞTIRILMAZ (ADR-188 §5):</b> onay zincirinin kaynağı BU tablodur; ekip lideri
/// otomatik onaycı DEĞİLDİR ve <c>teams</c> burada hiç kullanılmaz.
///
/// <b>Yazım kapıları (yalnız UI'ya güvenilmez — kapı buradadır):</b> tenant · kullanıcı gerçekten bu
/// firmanın mı · self-reference · döngü (A→B→C→A) · derinlik (yukarı + aşağı toplam ≤ 4).
///
/// <b>Performans (§21):</b> zincir çözümleme firmanın kenarlarını TEK sorguda okur ve bellekte yürür —
/// adım başına sorgu (N+1) YOKTUR.
/// </summary>
public sealed class UserHierarchyService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    /// <summary>Hiyerarşi yönetimi yetki modülü — ekip yönetimiyle aynı (PK-EK-07=B: yeni modül YOK).</summary>
    public const string Module = "users";

    public UserHierarchyService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    // ══════════════════════════════════════ OKUMA ══════════════════════════════════════

    public IReadOnlyList<HierarchyEdge> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        return Edges(conn, null, s.CompanyId);
    }

    /// <summary>Bir kullanıcının üstü (yoksa null).</summary>
    public string? ManagerOf(SessionContext s, string userId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        return ManagerOf(conn, null, s.CompanyId, userId);
    }

    /// <summary>
    /// Bir kullanıcının ÜSTÜNDEKİ onaycı zinciri (en yakın üstten yukarıya). En çok
    /// <see cref="HierarchyRules.MaxApprovers"/> kişi döner. Döngü koruması burada da vardır:
    /// veriye bir şekilde döngü girse bile çözümleme sonsuza gitmez.
    /// </summary>
    public IReadOnlyList<string> ResolveChain(SessionContext s, string userId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        return ResolveChain(conn, null, s.CompanyId, userId);
    }

    /// <summary>Aynı transaction içinden çağrılan çözümleme (onay süreci başlatılırken kullanılır).</summary>
    public static IReadOnlyList<string> ResolveChain(DbConnection conn, DbTransaction? tx, string companyId, string userId)
    {
        var ust = Harita(conn, tx, companyId);
        var zincir = new List<string>();
        var gorulen = new HashSet<string>(StringComparer.Ordinal) { userId };
        var mevcut = userId;
        while (zincir.Count < HierarchyRules.MaxApprovers && ust.TryGetValue(mevcut, out var yonetici))
        {
            if (!gorulen.Add(yonetici)) break;   // döngü koruması (çözümleme tarafı)
            zincir.Add(yonetici);
            mevcut = yonetici;
        }
        return zincir;
    }

    // ══════════════════════════════════════ YAZIM ══════════════════════════════════════

    /// <summary>
    /// Kullanıcının üstünü belirler (varsa mevcut ilişkiyi değiştirir). Tüm doğrulamalar
    /// SERVİSTE yapılır — UI doğrulaması bağlayıcı değildir.
    /// </summary>
    public string SetManager(SessionContext s, string userId, string managerUserId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(managerUserId))
            throw new ArgumentException("Kullanıcı ve üst seçilmelidir.");
        if (string.Equals(userId, managerUserId, StringComparison.Ordinal))
            throw new ArgumentException("Bir kullanıcı kendi üstü olamaz.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        EnsureUserOfCompany(conn, tx, s.CompanyId, userId);
        EnsureUserOfCompany(conn, tx, s.CompanyId, managerUserId);

        // Doğrulama, YENİ kenar eklenmiş gibi yapılan haritada çalışır.
        var harita = new Dictionary<string, string>(Harita(conn, tx, s.CompanyId), StringComparer.Ordinal)
        {
            [userId] = managerUserId,
        };
        DogrulaDongu(harita, userId);
        DogrulaDerinlik(harita, userId);

        // Mevcut aktif ilişki varsa yumuşak kapatılır (kısmi benzersiz indeks tekilliği zorlar).
        using (var kapat = conn.CreateCommand())
        {
            kapat.Transaction = tx;
            kapat.CommandText =
                "UPDATE user_hierarchy SET is_deleted=1, updated_at=@now, version=version+1 " +
                "WHERE company_id=@c AND user_id=@u AND is_deleted=0;";
            kapat.AddWithValue("@c", s.CompanyId);
            kapat.AddWithValue("@u", userId);
            kapat.AddWithValue("@now", now);
            kapat.ExecuteNonQuery();
        }

        var id = Guid.NewGuid().ToString("N");
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO user_hierarchy(id, company_id, user_id, manager_user_id, created_at, updated_at, version, is_deleted)
VALUES(@i,@c,@u,@m,@now,@now,1,0);";
            ins.AddWithValue("@i", id);
            ins.AddWithValue("@c", s.CompanyId);
            ins.AddWithValue("@u", userId);
            ins.AddWithValue("@m", managerUserId);
            ins.AddWithValue("@now", now);
            ins.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "user_hierarchy", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>İlişkiyi yumuşak siler (yeniden kurulabilir).</summary>
    public void RemoveManager(SessionContext s, string userId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE user_hierarchy SET is_deleted=1, updated_at=@now, version=version+1 " +
                "WHERE company_id=@c AND user_id=@u AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@u", userId);
            cmd.AddWithValue("@now", now);
            if (cmd.ExecuteNonQuery() == 0) throw new ArgumentException("Bu kullanıcının tanımlı üstü yok.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "user_hierarchy", userId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ══════════════════════════════════════ DOĞRULAMA ══════════════════════════════════════

    /// <summary>Döngü: kullanıcıdan yukarı yürürken aynı düğüme dönülüyorsa ilişki reddedilir.</summary>
    private static void DogrulaDongu(Dictionary<string, string> ust, string userId)
    {
        var gorulen = new HashSet<string>(StringComparer.Ordinal) { userId };
        var mevcut = userId;
        while (ust.TryGetValue(mevcut, out var yonetici))
        {
            if (!gorulen.Add(yonetici))
                throw new ArgumentException("Bu ilişki hiyerarşide döngü oluşturur (A→B→C→A). İzin verilmiyor.");
            mevcut = yonetici;
        }
    }

    /// <summary>
    /// Derinlik: bu kullanıcının ALTINDAKİ en uzun zincir + ÜSTÜNDEKİ zincir toplamı
    /// <see cref="HierarchyRules.MaxChainNodes"/> düğümü aşamaz. Yalnız yukarıya bakmak YETMEZ —
    /// kullanıcının astları varsa toplam zincir sessizce 4'ü geçerdi.
    /// </summary>
    private static void DogrulaDerinlik(Dictionary<string, string> ust, string userId)
    {
        var yukari = 1;                                   // kullanıcının kendisi
        var mevcut = userId;
        var gorulen = new HashSet<string>(StringComparer.Ordinal) { userId };
        while (ust.TryGetValue(mevcut, out var yonetici) && gorulen.Add(yonetici))
        {
            yukari++;
            mevcut = yonetici;
        }

        // Aşağı: ast → üst haritasını ters çevirip kullanıcının altındaki en uzun yolu ölç.
        var astlar = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (ast, yon) in ust)
        {
            if (!astlar.TryGetValue(yon, out var liste)) astlar[yon] = liste = new List<string>();
            liste.Add(ast);
        }
        var asagi = Derinlik(userId, astlar, new HashSet<string>(StringComparer.Ordinal));

        var toplam = yukari + asagi;
        if (toplam > HierarchyRules.MaxChainNodes)
            throw new ArgumentException(
                $"Hiyerarşi en fazla {HierarchyRules.MaxChainNodes} seviye olabilir; bu ilişki {toplam} seviye oluşturur.");
    }

    /// <summary>Bir düğümün ALTINDAKİ en uzun zincirin düğüm sayısı (düğümün kendisi hariç).</summary>
    private static int Derinlik(string dugum, Dictionary<string, List<string>> astlar, HashSet<string> yol)
    {
        if (!yol.Add(dugum)) return 0;                    // döngü koruması
        var en = 0;
        if (astlar.TryGetValue(dugum, out var cocuklar))
            foreach (var c in cocuklar)
                en = Math.Max(en, 1 + Derinlik(c, astlar, yol));
        yol.Remove(dugum);
        return en;
    }

    // ══════════════════════════════════════ YARDIMCI ══════════════════════════════════════

    /// <summary>Firmanın AKTİF ast→üst haritası. Zincir çözümleme bunu TEK sorguda alır (N+1 yok).</summary>
    private static Dictionary<string, string> Harita(DbConnection conn, DbTransaction? tx, string companyId)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT user_id, manager_user_id FROM user_hierarchy WHERE company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[r.GetString(0)] = r.GetString(1);
        return map;
    }

    private static List<HierarchyEdge> Edges(DbConnection conn, DbTransaction? tx, string companyId)
    {
        var list = new List<HierarchyEdge>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, company_id, user_id, manager_user_id, created_at, updated_at " +
            "FROM user_hierarchy WHERE company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new HierarchyEdge(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Convert.ToInt64(r.GetValue(4)), Convert.ToInt64(r.GetValue(5))));
        return list;
    }

    private static string? ManagerOf(DbConnection conn, DbTransaction? tx, string companyId, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT manager_user_id FROM user_hierarchy WHERE company_id=@c AND user_id=@u AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@u", userId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Kullanıcı bu firmanın mı? (Migration085 kullanıcıya FK vermez — kapı burasıdır.)</summary>
    private static void EnsureUserOfCompany(DbConnection conn, DbTransaction? tx, string companyId, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(1) FROM users WHERE id=@u AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) == 0)
            throw new ForbiddenException("Kullanıcı bulunamadı veya başka firmaya ait.");
    }
}
