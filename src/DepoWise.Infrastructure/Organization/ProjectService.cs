using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Organization;

/// <summary>Liste satırı. <c>BranchIds/BranchNames</c> bugün 0-1 eleman taşır (PK-C1 ilk sürüm: UI tek
/// şantiye bağlar); model çoklu bağa hazırdır — ileride UI genişleyince bu sözleşme DEĞİŞMEZ.</summary>
public sealed record ProjectRow(string Id, string Name, string Status, long? StartDate, long? EndDate,
    string? ManagerPersonnelId, string? ManagerName, string? Location, string? Description,
    IReadOnlyList<string> BranchIds, IReadOnlyList<string> BranchNames, long Version)
{
    public string StatusDisplay => ProjectService.StatusLabel(Status);
    public string ManagerDisplay => string.IsNullOrEmpty(ManagerName) ? "—" : ManagerName!;
    public string BranchDisplay => BranchNames.Count == 0 ? "—" : string.Join(", ", BranchNames);
}

/// <summary>Yeni/düzenlenen proje. Ad dışındaki TÜM alanlar opsiyoneldir (PK-C3: mevcut/eski kayıtlar
/// hiçbir zaman zorunlu alan doldurmaya zorlanmaz).</summary>
public sealed record NewProject(string Name, string? Status = null, long? StartDate = null, long? EndDate = null,
    string? ManagerPersonnelId = null, string? Location = null, string? Description = null,
    IReadOnlyList<string>? BranchIds = null);

/// <summary>
/// ═══ PRJ-01 (ADR-164, 2026-08-27) — PROJE / ŞANTİYE YÖNETİMİ ═══
///
/// <b>YETKİ (PK-C4):</b> ayrı proje yetkisi YOKTUR — ekran ve tüm işlemler mevcut
/// <c>branches</c> modülü üzerinden yetkilenir (Şube/Şantiye'yi yöneten projeyi de yönetir).
/// Veri KAPSAMI ise <see cref="BranchAccess"/>'ten gelir ve BYPASS EDİLMEZ: kullanıcı, bağlı
/// şantiyelerinden HİÇBİRİNE erişemediği projeyi listede GÖREMEZ; kapsamı dışındaki şantiyeye
/// proje BAĞLAYAMAZ (fail-closed, <see cref="BranchAccess.Require"/>).
/// Şantiyesiz proje, "şubesiz kayıt gizlenmez" ilkesiyle (BranchAccess sınıf kuralı) herkese görünür.
///
/// <b>TENANT:</b> her sorgu <c>company_id</c> ile sınırlıdır; şantiye bağlama şubenin firmaya
/// aitliğini ayrıca doğrular (başka firmanın şubesine bağ kurulamaz).
///
/// <b>SİLME:</b> fiziksel DELETE YOK (canlı veri kuralı + CLAUDE.md §4) — soft delete + audit;
/// Çöp Kutusu (<c>TrashService</c>) geri getirir. Bağ satırları (project_branches) korunur:
/// geri yüklemede şantiye bağı aynen döner.
///
/// <b>SUNUCU-OTORİTELİ:</b> şubeler gibi; BusinessSync'e girmez (bkz. Migration073 açıklaması).
/// </summary>
public sealed class ProjectService
{
    private const string Module = "branches";   // PK-C4: Şube/Şantiye modülüyle AYNI yetki kapısı
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public ProjectService(IDbConnectionFactory factory, IClock? clock = null)
    { _factory = factory; _clock = clock ?? new SystemClock(); }

    /// <summary>Bilinen durumlar; bilinmeyen değer fail-safe olarak 'active' yazılır (uydurma durum DB'ye girmez).</summary>
    private static string NormStatus(string? status)
        => status is "on_hold" or "completed" ? status : "active";

    public static string StatusLabel(string status) => status switch
    {
        "on_hold" => "Beklemede",
        "completed" => "Tamamlandı",
        _ => "Aktif",
    };

    /// <summary>
    /// Proje listesi (arama + durum filtresi). N+1 YOK: tek proje sorgusu + tek bağ sorgusu, birleşim C#'ta.
    /// Şube kapsamı BURADA uygulanır: kapsam dışı şantiyeye bağlı proje sonuçtan çıkarılır.
    /// </summary>
    public IReadOnlyList<ProjectRow> List(SessionContext s, string? search = null, string? status = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        // 1) Bağlar: proje → (şantiye id, ad). Firma başına küçük küme; tek sorgu.
        var baglar = new Dictionary<string, List<(string Id, string Name)>>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT pb.project_id, pb.branch_id, b.name FROM project_branches pb " +
                              "JOIN branches b ON b.id = pb.branch_id " +
                              "WHERE pb.company_id=@c AND b.is_deleted=0 ORDER BY b.name;";
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!baglar.TryGetValue(r.GetString(0), out var l)) baglar[r.GetString(0)] = l = new();
                l.Add((r.GetString(1), r.GetString(2)));
            }
        }

        // 2) Projeler (sorumlu adı JOIN ile; satır başına ek sorgu YOK).
        var list = new List<ProjectRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT p.id, p.name, p.status, p.start_date, p.end_date, " +
                              "p.manager_personnel_id, per.full_name, p.location, p.description, p.version " +
                              "FROM projects p LEFT JOIN personnel per ON per.id = p.manager_personnel_id " +
                              "WHERE p.company_id=@c AND p.is_deleted=0" +
                              (string.IsNullOrWhiteSpace(status) ? "" : " AND p.status=@st") +
                              " ORDER BY p.name;";
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(status)) cmd.AddWithValue("@st", NormStatus(status));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetString(0);
                var b = baglar.TryGetValue(id, out var l) ? l : new List<(string, string)>();
                list.Add(new ProjectRow(id, r.GetString(1), r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetInt64(3), r.IsDBNull(4) ? null : r.GetInt64(4),
                    r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                    r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
                    b.Select(x => x.Item1).ToList(), b.Select(x => x.Item2).ToList(), r.GetInt64(9)));
            }
        }

        // 3) ŞUBE KAPSAMI (PK-C4 / BranchAccess): şantiyeli proje, kullanıcı o şantiyelerden en az birine
        //    erişebiliyorsa görünür. Şantiyesiz proje gizlenmez (şubesiz kayıt ilkesi). null = sınırsız.
        var izinli = BranchAccess.Allowed(s);
        if (izinli is not null)
        {
            var set = izinli.ToHashSet(StringComparer.Ordinal);
            list = list.Where(p => p.BranchIds.Count == 0 || p.BranchIds.Any(set.Contains)).ToList();
        }

        // 4) Arama (ad / şantiye / sorumlu / konum) — bellek içi: firma başına proje sayısı küçük,
        //    kapsam filtresiyle AYNI yerde kalması sızıntı riskini sıfırlar.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(p =>
                p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.BranchNames.Any(n => n.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (p.ManagerName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (p.Location?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return list;
    }

    public string Create(SessionContext s, NewProject dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Proje adı zorunlu.");
        ValidateDates(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var branches = NormBranches(s, conn, tx, dto.BranchIds);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO projects(id, company_id, name, status, start_date, end_date, " +
                "manager_personnel_id, location, description, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id,@c,@n,@st,@sd,@ed,@mp,@loc,@d,@now,@now,1,0);";
            AddFields(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        WriteBranches(conn, tx, s.CompanyId, id, branches, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "project", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <param name="expectedVersion">Düzenleme kilidi (BranchService.Update ile aynı desen):
    /// form açıldığından beri başka biri değiştirdiyse <see cref="ConcurrencyException"/> — hiçbir alan yazılmaz.</param>
    public void Update(SessionContext s, string id, NewProject dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Proje adı zorunlu.");
        ValidateDates(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureProjectOwned(conn, tx, s.CompanyId, id, expectedVersion);
        RequireExistingScope(s, conn, tx, id);   // kapsam dışı projeyi düzenleyemez (fail-closed)
        var branches = NormBranches(s, conn, tx, dto.BranchIds);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE projects SET name=@n, status=@st, start_date=@sd, end_date=@ed, " +
                "manager_personnel_id=@mp, location=@loc, description=@d, updated_at=@now, version=version+1 " +
                "WHERE id=@id AND company_id=@c;";
            AddFields(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        // Bağ kümesi HEDEF duruma eşitlenir (sil+yaz, aynı transaction). project_branches saf bağ
        // tablosudur (iş verisi taşımaz) → yeniden yazımı operasyonel kaydı silme yasağına GİRMEZ.
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM project_branches WHERE project_id=@p AND company_id=@c;";
            del.AddWithValue("@p", id);
            del.AddWithValue("@c", s.CompanyId);
            del.ExecuteNonQuery();
        }
        WriteBranches(conn, tx, s.CompanyId, id, branches, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "project", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Soft delete (fiziksel silme YOK). Şantiye bağları korunur → geri yüklemede aynen döner.</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureProjectOwned(conn, tx, s.CompanyId, id, expectedVersion: null);
        RequireExistingScope(s, conn, tx, id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE projects SET is_deleted=1, updated_at=@now WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "project", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ── yardımcılar ──────────────────────────────────────────────────────────────────────────────

    private static void ValidateDates(NewProject dto)
    {
        if (dto.StartDate is { } sd && dto.EndDate is { } ed && ed < sd)
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");
    }

    private static void AddFields(System.Data.Common.DbCommand cmd, NewProject dto)
    {
        cmd.AddWithValue("@n", dto.Name.Trim());
        cmd.AddWithValue("@st", NormStatus(dto.Status));
        cmd.AddWithValue("@sd", (object?)dto.StartDate ?? DBNull.Value);
        cmd.AddWithValue("@ed", (object?)dto.EndDate ?? DBNull.Value);
        cmd.AddWithValue("@mp", string.IsNullOrWhiteSpace(dto.ManagerPersonnelId) ? DBNull.Value : dto.ManagerPersonnelId!);
        cmd.AddWithValue("@loc", string.IsNullOrWhiteSpace(dto.Location) ? DBNull.Value : dto.Location!.Trim());
        cmd.AddWithValue("@d", string.IsNullOrWhiteSpace(dto.Description) ? DBNull.Value : dto.Description!.Trim());
    }

    /// <summary>Bağlanacak şantiyeler: firma aitliği + ŞUBE KAPSAMI doğrulanır (kapsam dışına bağ kurulamaz).</summary>
    private IReadOnlyList<string> NormBranches(SessionContext s, System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx, IReadOnlyList<string>? branchIds)
    {
        var ids = (branchIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var b in ids)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@b AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@b", b);
            cmd.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                throw new ArgumentException("Şantiye bulunamadı veya bu firmaya ait değil.");
            BranchAccess.Require(s, b, "proje-şantiye bağlama");
        }
        return ids;
    }

    private static void WriteBranches(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        string companyId, string projectId, IReadOnlyList<string> branchIds, long now)
    {
        foreach (var b in branchIds)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO project_branches(project_id, branch_id, company_id, created_at) VALUES(@p,@b,@c,@now);";
            cmd.AddWithValue("@p", projectId);
            cmd.AddWithValue("@b", b);
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Tenant + düzenleme kilidi: proje bu firmaya ait ve (verilmişse) beklenen sürümde olmalı.</summary>
    private static void EnsureProjectOwned(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        string companyId, string id, long? expectedVersion)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT version FROM projects WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        if (v is null || v is DBNull) throw new ArgumentException("Proje bulunamadı.");
        if (expectedVersion is { } ev && Convert.ToInt64(v) != ev)
            throw new ConcurrencyException(ev, Convert.ToInt64(v));
    }

    /// <summary>Kullanıcı, projenin MEVCUT şantiye bağlarının en az birine erişebilmeli (şantiyesiz proje serbest).
    /// Aksi hâlde listede göremediği projeyi id tahmin ederek düzenleyebilir/silebilirdi (fail-closed).</summary>
    private static void RequireExistingScope(SessionContext s, System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx, string projectId)
    {
        var izinli = BranchAccess.Allowed(s);
        if (izinli is null) return;   // sınırsız kapsam
        var mevcut = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT branch_id FROM project_branches WHERE project_id=@p AND company_id=@c;";
            cmd.AddWithValue("@p", projectId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) mevcut.Add(r.GetString(0));
        }
        if (mevcut.Count == 0) return; // şantiyesiz proje gizlenmez ilkesinin yazma karşılığı
        var set = izinli.ToHashSet(StringComparer.Ordinal);
        if (!mevcut.Any(set.Contains))
            throw new ForbiddenException("Bu proje, erişim kapsamınız dışındaki bir şantiyeye bağlı.");
    }
}
