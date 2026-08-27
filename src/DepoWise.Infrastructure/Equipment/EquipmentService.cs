using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Equipment;

/// <summary>Ekipman liste satırı (tür + şube adları JOIN ile — satır başına ek sorgu YOK).</summary>
public sealed record EquipmentRow(string Id, string Code, string Name, string? TypeId, string? TypeName,
    string Status, string? StatusNote, string? BranchId, string? BranchName,
    string? SerialNo, string? Location, string? Description, long Version)
{
    public string StatusDisplay => EquipmentService.StatusLabel(Status);
    public string TypeDisplay => string.IsNullOrEmpty(TypeName) ? "—" : TypeName!;
    public string BranchDisplay => string.IsNullOrEmpty(BranchName) ? "—" : BranchName!;
    public string SerialDisplay => string.IsNullOrEmpty(SerialNo) ? "—" : SerialNo!;
}

/// <summary>Yeni/düzenlenen ekipman. Kod + ad zorunlu; kalan alanlar opsiyonel (alan İCAT EDİLMEDİ — PK-E).</summary>
public sealed record NewEquipment(string Code, string Name, string? TypeId = null, string? Status = null,
    string? StatusNote = null, string? BranchId = null, string? SerialNo = null,
    string? Location = null, string? Description = null);

/// <summary>
/// ═══ EKP-01 (ADR-166, 2026-08-28) — VARLIK / EKİPMAN YÖNETİMİ ═══
///
/// AYRI varlık (PK-E1): araç zincirine (bakım/yakıt/muayene/rapor) DOKUNMAZ. İş verisidir:
/// masaüstünde yerel SQLite'a yazılır, senkronla taşınır (vehicles deseni; LWW tanımsal kart için uygundur).
///
/// <b>YETKİ:</b> yeni <c>equipment</c> modülü (deny-by-default). <b>ŞUBE KAPSAMI:</b> <see cref="BranchAccess"/> —
/// kapsam dışı şubenin ekipmanı listede görünmez, düzenlenemez; şubesiz ekipman gizlenmez
/// (şubesiz kayıt ilkesi). <b>TENANT:</b> her sorgu company_id ile sınırlı.
/// <b>SİLME:</b> soft delete + audit + Çöp Kutusu.
/// </summary>
public sealed class EquipmentService
{
    public const string Module = "equipment";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public EquipmentService(IDbConnectionFactory factory, IClock? clock = null)
    { _factory = factory; _clock = clock ?? new SystemClock(); }

    /// <summary>Bilinen durumlar (vehicles.status ile aynı küme); bilinmeyen değer fail-safe 'active'.</summary>
    private static string NormStatus(string? status)
        => status is "passive" or "maintenance" ? status : "active";

    public static string StatusLabel(string status) => status switch
    {
        "passive" => "Pasif",
        "maintenance" => "Bakımda",
        _ => "Aktif",
    };

    public IReadOnlyList<EquipmentRow> List(SessionContext s, string? search = null, string? typeId = null, string? status = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var list = new List<EquipmentRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT e.id, e.code, e.name, e.type_id, t.name, e.status, e.status_note, " +
                "e.branch_id, b.name, e.serial_no, e.location, e.description, e.version " +
                "FROM equipment e " +
                "LEFT JOIN equipment_types t ON t.id = e.type_id AND t.is_deleted=0 " +
                "LEFT JOIN branches b ON b.id = e.branch_id AND b.is_deleted=0 " +
                "WHERE e.company_id=@c AND e.is_deleted=0" +
                (string.IsNullOrWhiteSpace(typeId) ? "" : " AND e.type_id=@t") +
                (string.IsNullOrWhiteSpace(status) ? "" : " AND e.status=@st") +
                " ORDER BY e.code;";
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(typeId)) cmd.AddWithValue("@t", typeId);
            if (!string.IsNullOrWhiteSpace(status)) cmd.AddWithValue("@st", NormStatus(status));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new EquipmentRow(r.GetString(0), r.GetString(1), r.GetString(2),
                    S(r, 3), S(r, 4), r.GetString(5), S(r, 6), S(r, 7), S(r, 8), S(r, 9), S(r, 10), S(r, 11),
                    r.GetInt64(12)));
        }

        // ŞUBE KAPSAMI (BranchAccess): kapsam dışı şubenin ekipmanı GÖRÜNMEZ; şubesiz ekipman gizlenmez.
        var izinli = BranchAccess.Allowed(s);
        if (izinli is not null)
        {
            var set = izinli.ToHashSet(StringComparer.Ordinal);
            list = list.Where(e => e.BranchId is null || set.Contains(e.BranchId)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(e =>
                e.Code.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (e.SerialNo?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.TypeName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.BranchName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (e.Location?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return list;
    }

    public string Create(SessionContext s, NewEquipment dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        Validate(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureBranchUsable(s, conn, tx, dto.BranchId);
        EnsureCodeFree(conn, tx, s.CompanyId, dto.Code, excludeId: null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO equipment(id, company_id, code, name, type_id, status, status_note, " +
                "branch_id, serial_no, location, description, created_at, updated_at, version, is_deleted) " +
                "VALUES(@id,@c,@code,@n,@t,@st,@sn,@b,@ser,@loc,@d,@now,@now,1,0);";
            Fields(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <param name="expectedVersion">Düzenleme kilidi: form açıldığından beri değiştiyse ConcurrencyException.</param>
    public void Update(SessionContext s, string id, NewEquipment dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        Validate(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwnedAndInScope(s, conn, tx, id, expectedVersion);
        EnsureBranchUsable(s, conn, tx, dto.BranchId);
        EnsureCodeFree(conn, tx, s.CompanyId, dto.Code, excludeId: id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE equipment SET code=@code, name=@n, type_id=@t, status=@st, status_note=@sn, " +
                "branch_id=@b, serial_no=@ser, location=@loc, description=@d, updated_at=@now, version=version+1 " +
                "WHERE id=@id AND company_id=@c;";
            Fields(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Soft delete + audit; Çöp Kutusu geri getirir.</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureOwnedAndInScope(s, conn, tx, id, expectedVersion: null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE equipment SET is_deleted=1, updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Filtrelenmiş TÜM sonucu Excel'e hazırlar (liste kuralı 2: sayfa değil, tüm küme).</summary>
    public static DepoWise.Application.Reports.TableModel ToTableModel(IReadOnlyList<EquipmentRow> rows)
        => new("Ekipman",
            new[] { "Kod", "Ad", "Tür", "Durum", "Şube/Şantiye", "Seri No", "Konum", "Durum Notu", "Açıklama" },
            rows.Select(e => (IReadOnlyList<object?>)new object?[]
                { e.Code, e.Name, e.TypeDisplay, e.StatusDisplay, e.BranchDisplay, e.SerialNo, e.Location, e.StatusNote, e.Description }).ToList());

    // ── yardımcılar ──────────────────────────────────────────────────────────────────────────────

    private static void Validate(NewEquipment dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code)) throw new ArgumentException("Ekipman kodu zorunlu.");
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Ekipman adı zorunlu.");
    }

    private static void Fields(System.Data.Common.DbCommand cmd, NewEquipment dto)
    {
        cmd.AddWithValue("@code", dto.Code.Trim());
        cmd.AddWithValue("@n", dto.Name.Trim());
        cmd.AddWithValue("@t", N(dto.TypeId));
        cmd.AddWithValue("@st", NormStatus(dto.Status));
        cmd.AddWithValue("@sn", N(dto.StatusNote));
        cmd.AddWithValue("@b", N(dto.BranchId));
        cmd.AddWithValue("@ser", N(dto.SerialNo));
        cmd.AddWithValue("@loc", N(dto.Location));
        cmd.AddWithValue("@d", N(dto.Description));
    }
    private static object N(string? v) => string.IsNullOrWhiteSpace(v) ? DBNull.Value : v!.Trim();

    /// <summary>Aynı kodla İKİNCİ aktif ekipman engellenir — ham UNIQUE ihlali yerine anlaşılır mesaj.</summary>
    private static void EnsureCodeFree(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        string companyId, string code, string? excludeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM equipment WHERE company_id=@c AND code=@code AND is_deleted=0" +
                          (excludeId is null ? ";" : " AND id<>@id;");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@code", code.Trim());
        if (excludeId is not null) cmd.AddWithValue("@id", excludeId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
            throw new ArgumentException($"'{code.Trim()}' kodu başka bir ekipmanda kullanılıyor.");
    }

    /// <summary>Bağlanan şube firmaya ait olmalı + kullanıcının kapsamında olmalı (kapsam dışına yazma kapalı).</summary>
    private static void EnsureBranchUsable(SessionContext s, System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx, string? branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId)) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@b AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@b", branchId!);
        cmd.AddWithValue("@c", s.CompanyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ArgumentException("Şube bulunamadı veya bu firmaya ait değil.");
        BranchAccess.Require(s, branchId, "ekipman");
    }

    /// <summary>Tenant + kapsam + düzenleme kilidi: kayıt bu firmanın, mevcut şubesi kullanıcının
    /// kapsamında (listede göremediğini id tahminiyle değiştiremesin) ve beklenen sürümde.</summary>
    private static void EnsureOwnedAndInScope(SessionContext s, System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx, string id, long? expectedVersion)
    {
        string? branchId; long version;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT branch_id, version FROM equipment WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ArgumentException("Ekipman bulunamadı.");
            branchId = r.IsDBNull(0) ? null : r.GetString(0);
            version = r.GetInt64(1);
        }
        if (branchId is not null) BranchAccess.Require(s, branchId, "ekipman");
        if (expectedVersion is { } ev && version != ev) throw new ConcurrencyException(ev, version);
    }

    private static string? S(System.Data.Common.DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
}
