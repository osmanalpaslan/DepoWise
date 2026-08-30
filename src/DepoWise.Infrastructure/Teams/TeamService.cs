using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Teams;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Teams;

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — EKİP TANIMI SERVİSİ ═══
///
/// <b>YETKİ (PK-EK-07=B):</b> ekip yönetimi <b>mevcut <c>users</c> modülüne</b> bağlıdır —
/// yeni bir <c>teams</c> yetki modülü OLUŞTURULMAZ.
/// <b>İK-6 istisnası:</b> ekibin LİDERİ, <c>users</c> düzenleme yetkisi olmasa da <b>kendi ekibinin</b>
/// üyelerini ekler/çıkarır. Ekip oluşturma/silme bu istisnaya girmez — o daima <c>users</c> yetkisidir.
///
/// <b>TENANT:</b> <c>company_id</c> DAİMA oturumdan (<see cref="SessionContext.CompanyId"/>) gelir;
/// istemci gövdesinden firma alınmaz. Tüm sorgular <c>company_id</c> süzgeçlidir → başka firmanın
/// ekibi okunamaz, güncellenemez, silinemez (IDOR kapalı).
///
/// <b>KULLANICI BÜTÜNLÜĞÜ:</b> <c>users</c> masaüstüne senkronlanmadığı için Migration084 kullanıcıya
/// FK vermez; bu yüzden <c>user_id</c>/<c>lead_user_id</c>'nin gerçekten <b>aynı firmanın</b> kullanıcısı
/// olduğu BURADA (sunucu tarafında, <c>users</c>'ın otorite olduğu yerde) doğrulanır.
///
/// <b>ONAY İLE BAĞ YOK:</b> lider/üye kavramı onay zinciriyle BAĞLANMAZ (ADR-187 §3/§5).
/// </summary>
public sealed class TeamService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    /// <summary>Ekip yönetimi yetki modülü — PK-EK-07=B gereği mevcut Kullanıcılar modülü.</summary>
    public const string Module = "users";

    public TeamService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    // ══════════════════════════════════════ OKUMA ══════════════════════════════════════

    /// <summary>Firmanın ekipleri. İK-7: ekipler arası görünürlük AÇIK — üyelik süzgeci uygulanmaz.</summary>
    public IReadOnlyList<Team> List(SessionContext s, bool includeInactive = false)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var list = new List<Team>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, name, lead_user_id, is_active, created_at, updated_at " +
            "FROM teams WHERE company_id=@c AND is_deleted=0 " +
            (includeInactive ? "" : "AND is_active=1 ") +
            "ORDER BY name;";
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadTeam(r));
        return list;
    }

    /// <summary>Tek ekip — BAŞKA firmanın ekibi ASLA dönmez (tenant izolasyonu).</summary>
    public Team? ById(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        return ByIdCore(s, id);
    }

    private Team? ByIdCore(SessionContext s, string id)
    {
        using var conn = _factory.Create();
        return ByIdCore(conn, null, s.CompanyId, id);
    }

    private static Team? ByIdCore(DbConnection conn, DbTransaction? tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, company_id, name, lead_user_id, is_active, created_at, updated_at " +
            "FROM teams WHERE id=@i AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@i", id);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadTeam(r) : null;
    }

    private static Team ReadTeam(DbDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        Convert.ToInt64(r.GetValue(4)) == 1,
        Convert.ToInt64(r.GetValue(5)), Convert.ToInt64(r.GetValue(6)));

    /// <summary>Bir ekibin aktif üyeleri (tenant + ekip süzgeçli).</summary>
    public IReadOnlyList<TeamMember> Members(SessionContext s, string teamId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        _ = ByIdCore(s, teamId) ?? throw new ForbiddenException("Ekip bulunamadı veya başka firmaya ait.");
        var list = new List<TeamMember>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT id, company_id, team_id, user_id, is_lead, created_at, updated_at " +
            "FROM team_members WHERE company_id=@c AND team_id=@t AND is_deleted=0 ORDER BY created_at;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@t", teamId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new TeamMember(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Convert.ToInt64(r.GetValue(4)) == 1, Convert.ToInt64(r.GetValue(5)), Convert.ToInt64(r.GetValue(6))));
        return list;
    }

    /// <summary>Bir kullanıcının üye olduğu ekipler. İK-1 gereği BİRDEN FAZLA olabilir.</summary>
    public IReadOnlyList<Team> TeamsOfUser(SessionContext s, string userId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var list = new List<Team>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT t.id, t.company_id, t.name, t.lead_user_id, t.is_active, t.created_at, t.updated_at " +
            "FROM team_members m JOIN teams t ON t.id = m.team_id " +
            "WHERE m.company_id=@c AND m.user_id=@u AND m.is_deleted=0 AND t.is_deleted=0 ORDER BY t.name;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadTeam(r));
        return list;
    }

    // ══════════════════════════════════════ EKİP CRUD ══════════════════════════════════════

    /// <summary>Yeni ekip. Lider ATANMAZ — lider ancak ekibe üye olduktan sonra
    /// <see cref="Update"/> ile atanabilir (ADR-187: lider gerçekten üye olmalı).</summary>
    public string Create(SessionContext s, string name)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        var (ok, error) = TeamRules.ValidateName(name);
        if (!ok) throw new ArgumentException(error);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO teams(id, company_id, name, lead_user_id, is_active, created_at, updated_at, version, is_deleted)
VALUES(@i,@c,@n,NULL,1,@now,@now,1,0);";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", name.Trim());
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "team", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Ekibi günceller. <paramref name="leadUserId"/> verilirse o kullanıcının bu ekipte
    /// AKTİF ÜYE olduğu doğrulanır; değilse işlem reddedilir (ADR-187 zorunluluğu).</summary>
    public void Update(SessionContext s, string id, string name, string? leadUserId, bool isActive)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var (ok, error) = TeamRules.ValidateName(name);
        if (!ok) throw new ArgumentException(error);

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var mevcut = ByIdCore(conn, tx, s.CompanyId, id)
                     ?? throw new ForbiddenException("Ekip bulunamadı veya başka firmaya ait.");

        var lead = string.IsNullOrWhiteSpace(leadUserId) ? null : leadUserId.Trim();
        if (lead is not null && !IsActiveMember(conn, tx, s.CompanyId, id, lead))
            throw new ArgumentException("Ekip yöneticisi olarak yalnız bu ekibin aktif üyesi seçilebilir.");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE teams SET name=@n, lead_user_id=@l, is_active=@a, updated_at=@now, version=version+1
WHERE id=@i AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@n", name.Trim());
            cmd.AddWithValue("@l", (object?)lead ?? DBNull.Value);
            cmd.AddWithValue("@a", isActive ? 1L : 0L);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        // is_lead işareti teams.lead_user_id ile TUTARLI tutulur (iki yerde çelişki oluşmasın).
        SyncLeadFlag(conn, tx, s.CompanyId, id, lead, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "team", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
        _ = mevcut;
    }

    /// <summary>Yumuşak silme — fiziksel silme YOK (ADR-083 dışında proje geneli kural).
    /// Ekip silinince üyelikleri de yumuşak silinir; sarkan üyelik bırakılmaz.</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        _ = ByIdCore(conn, tx, s.CompanyId, id)
            ?? throw new ForbiddenException("Ekip bulunamadı veya başka firmaya ait.");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE teams SET is_deleted=1, updated_at=@now, version=version+1 " +
                "WHERE id=@i AND company_id=@c;";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE team_members SET is_deleted=1, updated_at=@now, version=version+1 " +
                "WHERE team_id=@i AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "team", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ══════════════════════════════════════ ÜYELİK ══════════════════════════════════════

    /// <summary>Üye ekler. İK-1: aynı kullanıcı başka ekiplere de eklenebilir; ancak AYNI ekibe
    /// aktif olarak iki kez eklenemez (kısmi benzersiz indeks yarış durumunda da korur).</summary>
    public string AddMember(SessionContext s, string teamId, string userId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var team = ByIdCore(conn, tx, s.CompanyId, teamId)
                   ?? throw new ForbiddenException("Ekip bulunamadı veya başka firmaya ait.");
        EnsureCanManageMembers(s, team);
        EnsureUserOfCompany(conn, tx, s.CompanyId, userId);

        if (IsActiveMember(conn, tx, s.CompanyId, teamId, userId))
            throw new ArgumentException("Bu kullanıcı zaten bu ekibin üyesi.");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO team_members(id, company_id, team_id, user_id, is_lead, created_at, updated_at, version, is_deleted)
VALUES(@i,@c,@t,@u,0,@now,@now,1,0);";
            cmd.AddWithValue("@i", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@t", teamId);
            cmd.AddWithValue("@u", userId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "team_member", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Üyeliği yumuşak siler. Çıkarılan kişi ekibin LİDERİ ise liderlik de temizlenir —
    /// aksi halde "lider üye olmalı" değişmezi bozulurdu.</summary>
    public void RemoveMember(SessionContext s, string teamId, string userId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var team = ByIdCore(conn, tx, s.CompanyId, teamId)
                   ?? throw new ForbiddenException("Ekip bulunamadı veya başka firmaya ait.");
        EnsureCanManageMembers(s, team);

        if (!IsActiveMember(conn, tx, s.CompanyId, teamId, userId))
            throw new ArgumentException("Bu kullanıcı bu ekibin üyesi değil.");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE team_members SET is_deleted=1, is_lead=0, updated_at=@now, version=version+1 " +
                "WHERE company_id=@c AND team_id=@t AND user_id=@u AND is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@t", teamId);
            cmd.AddWithValue("@u", userId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        if (string.Equals(team.LeadUserId, userId, StringComparison.Ordinal))
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE teams SET lead_user_id=NULL, updated_at=@now, version=version+1 " +
                "WHERE id=@t AND company_id=@c;";
            cmd.AddWithValue("@t", teamId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "team_member", teamId + ":" + userId,
            AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ══════════════════════════════════════ KAPILAR ══════════════════════════════════════

    /// <summary>İK-6: <c>users</c> düzenleme yetkisi OLAN ya da <b>bu ekibin lideri</b> olan kişi
    /// üye yönetebilir. Lider ayrıcalığı YALNIZ kendi ekibi içindir — başka ekibe geçmez.</summary>
    private static void EnsureCanManageMembers(SessionContext s, Team team)
    {
        if (AccessControl.Can(s, Module, PermissionAction.Edit)) return;
        if (string.Equals(team.LeadUserId, s.UserId, StringComparison.Ordinal)) return;
        throw new ForbiddenException("Ekip üyeliğini yönetme yetkiniz yok.");
    }

    /// <summary>Kullanıcının AYNI FİRMAYA ait ve silinmemiş bir kullanıcı olduğunu doğrular.
    /// Migration084 kullanıcıya FK vermediği için (ayna/senkron gerekçesi) bütünlük kapısı burasıdır.</summary>
    private static void EnsureUserOfCompany(DbConnection conn, DbTransaction? tx, string companyId, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Kullanıcı seçilmedi.");
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(1) FROM users WHERE id=@u AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) == 0)
            throw new ForbiddenException("Kullanıcı bulunamadı veya başka firmaya ait.");
    }

    private static bool IsActiveMember(DbConnection conn, DbTransaction? tx, string companyId, string teamId, string userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COUNT(1) FROM team_members WHERE company_id=@c AND team_id=@t AND user_id=@u AND is_deleted=0;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@t", teamId);
        cmd.AddWithValue("@u", userId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L) > 0;
    }

    private static void SyncLeadFlag(DbConnection conn, DbTransaction tx, string companyId, string teamId,
        string? leadUserId, long now)
    {
        using var clear = conn.CreateCommand();
        clear.Transaction = tx;
        clear.CommandText =
            "UPDATE team_members SET is_lead=0, updated_at=@now, version=version+1 " +
            "WHERE company_id=@c AND team_id=@t AND is_deleted=0 AND is_lead=1;";
        clear.AddWithValue("@c", companyId);
        clear.AddWithValue("@t", teamId);
        clear.AddWithValue("@now", now);
        clear.ExecuteNonQuery();
        if (leadUserId is null) return;

        using var set = conn.CreateCommand();
        set.Transaction = tx;
        set.CommandText =
            "UPDATE team_members SET is_lead=1, updated_at=@now, version=version+1 " +
            "WHERE company_id=@c AND team_id=@t AND user_id=@u AND is_deleted=0;";
        set.AddWithValue("@c", companyId);
        set.AddWithValue("@t", teamId);
        set.AddWithValue("@u", leadUserId);
        set.AddWithValue("@now", now);
        set.ExecuteNonQuery();
    }
}
