using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Requests;

/// <summary>Talep Operasyonları listesi satırı (yalnız ONAYLANMIŞ talepler).</summary>
public sealed record RequestOperationRow(
    string Id, string DocNo, long RequestDate, int ItemCount, string? Description,
    string? OperationStatusDb, string PriorityDb,
    string? BranchName, string? RequesterName, string? ApproverName,
    string? FromBranchId, string? FromBranchName, string? ToBranchId, string? ToBranchName, string? OpsNote)
{
    public string DateText => DateTimeOffset.FromUnixTimeMilliseconds(RequestDate).LocalDateTime.ToString("dd.MM.yyyy");
    public string OperationStatusText => RequestOperationStatusInfo.LabelOrDash(OperationStatusDb);
    public string OperationStatusColor => RequestOperationStatusInfo.ColorOrNeutral(OperationStatusDb);
    public string PriorityText => RequestPriorityInfo.LabelOf(PriorityDb);
    public string PriorityColor => RequestPriorityInfo.ColorOf(PriorityDb);
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description!;
    public string FromBranchDisplay => string.IsNullOrWhiteSpace(FromBranchName) ? "—" : FromBranchName!;
    public string ToBranchDisplay => string.IsNullOrWhiteSpace(ToBranchName) ? "—" : ToBranchName!;
    public string OpsNoteDisplay => string.IsNullOrWhiteSpace(OpsNote) ? "—" : OpsNote!;
}

/// <summary>İşlem geçmişi satırı (şartname §13) — hiçbir kayıt silinmez.</summary>
public sealed record RequestOperationHistoryRow(
    long CreatedAt, string? FromStatusDb, string ToStatusDb, string? UserName, string? BranchName, string? Reason)
{
    public string TimeText => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string FromText => RequestOperationStatusInfo.LabelOrDash(FromStatusDb);
    public string ToText => RequestOperationStatusInfo.LabelOrDash(ToStatusDb);
    public string UserDisplay => string.IsNullOrWhiteSpace(UserName) ? "—" : UserName!;
    public string BranchDisplay => string.IsNullOrWhiteSpace(BranchName) ? "—" : BranchName!;
    public string Line => $"{TimeText} · {FromText} → {ToText} · {UserDisplay} ({BranchDisplay})"
                          + (string.IsNullOrWhiteSpace(Reason) ? "" : $" — {Reason}");
}

/// <summary>
/// TALEP OPERASYONLARI (Faz 2, kullanıcı isteği 2026-08-08). Onaylanmış taleplerin operasyon sürecini yönetir:
/// durum değiştirme (onaylı geçiş matrisi + yetki), gönderim bilgileri (gönderen/gönderilecek şube, operasyon
/// notu) ve işlem geçmişi.
///
/// SINIRLAR (kullanıcı kararı — Faz 2 kapsamı): kısmi karşılama MİKTARLARI, alternatif malzeme, talebin
/// bölünmesi, satın alma sipariş detayları, teslim alan/şekli, dosya eki, bildirim ve OTOMATİK STOK
/// HAREKETLERİ bu fazda YOKTUR. Bu servis stok DEĞİŞTİRMEZ (V6 §6.12: talep stoğu doğrudan değiştirmez).
///
/// GÜVENLİK: işlemin yapıldığı şube (op_branch_id) İSTEMCİDEN ALINMAZ — sunucuda oturumun çalışma şubesinden
/// (BranchScope) belirlenir. Gönderen/gönderilecek şube kullanıcı seçimidir ama firmaya ait olduğu doğrulanır.
/// </summary>
public sealed class RequestOperationsService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public RequestOperationsService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Operasyon ekranı listesi: YALNIZ onaylanmış talepler (operasyon süreci onayla başlar).
    /// Şube kapsamı mevcut BranchScope ile (yetkisiz kullanıcı başka şubenin talebini göremez).</summary>
    public IReadOnlyList<RequestOperationRow> List(SessionContext s, string? operationStatusDb = null, int limit = 300)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT mr.id, mr.doc_no, mr.request_date,
       (SELECT COUNT(*) FROM material_request_items i WHERE i.request_id = mr.id),
       mr.description, mr.operation_status, COALESCE(mr.priority,'normal'),
       b.name, pr.full_name, pa.full_name,
       mr.ops_from_branch_id, fb.name, mr.ops_to_branch_id, tb.name, mr.ops_note
FROM material_requests mr
LEFT JOIN branches b  ON b.id  = mr.branch_id
LEFT JOIN personnel pr ON pr.id = mr.requester_id
LEFT JOIN personnel pa ON pa.id = mr.approver_id
LEFT JOIN branches fb ON fb.id = mr.ops_from_branch_id
LEFT JOIN branches tb ON tb.id = mr.ops_to_branch_id
WHERE mr.company_id=@c AND mr.is_deleted=0 AND mr.status='approved'
  AND (CAST(@ops AS TEXT) IS NULL OR mr.operation_status=@ops)
  {BranchScope.Sql(s, "mr.branch_id")}
ORDER BY mr.request_date DESC, mr.created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@ops", (object?)operationStatusDb ?? DBNull.Value);
        if (BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        cmd.AddWithValue("@lim", limit);

        var list = new List<RequestOperationRow>();
        using var r = cmd.ExecuteReader();
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (r.Read())
            list.Add(new RequestOperationRow(
                r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt32(3), S(4), S(5), r.GetString(6),
                S(7), S(8), S(9), S(10), S(11), S(12), S(13), S(14)));
        return list;
    }

    /// <summary>Bu talep için kullanıcının SEÇEBİLECEĞİ sonraki durumlar (matris + yetki birlikte).</summary>
    public IReadOnlyList<RequestOperationStatus> AllowedNextStates(SessionContext s, string requestId)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.View);
        var current = LoadOperationStatus(s, requestId);
        if (current is null) return Array.Empty<RequestOperationStatus>();
        return RequestOperationStateMachine.NextStates(current.Value)
            .Where(t => RequestOperationStateMachine.CanUserPerform(s, t)).ToList();
    }

    /// <summary>
    /// Operasyon durumunu değiştirir + gönderim bilgilerini günceller (verilmişse) + geçmişe yazar.
    /// TEK transaction; geçiş matrisi ve yetki sunucuda doğrulanır. Stok DEĞİŞTİRİLMEZ (Faz 2 sınırı).
    /// </summary>
    public void ChangeStatus(SessionContext s, string requestId, RequestOperationStatus to,
        string? note = null, string? fromBranchId = null, string? toBranchId = null, bool updateBranches = false)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.Edit);
        if (!RequestOperationStateMachine.CanUserPerform(s, to))
            throw new ForbiddenException("Bu operasyon adımı için yetkiniz yok (Ana Depo / Satın Alma yetkisi gerekir).");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var (current, companyId, approvalStatus) = LoadOperationStatusTx(conn, tx, s.CompanyId, requestId);
        TenantAccessGuard.EnsureOwnership(s, companyId);
        if (approvalStatus != "approved")
            throw new InvalidOperationException("Operasyon süreci yalnız ONAYLANMIŞ talepte yürütülür.");
        if (current is null)
            throw new InvalidOperationException("Talebin operasyon durumu bulunamadı.");
        if (!RequestOperationStateMachine.CanTransition(current.Value, to))
            throw new InvalidOperationException(
                $"Geçersiz operasyon geçişi: {RequestOperationStatusInfo.Label(current.Value)} → {RequestOperationStatusInfo.Label(to)}.");

        // Gönderen/gönderilecek şube kullanıcı seçimidir → FİRMAYA AİT olduğu doğrulanır (istemciye güvenilmez).
        if (updateBranches)
        {
            EnsureBranchOwned(conn, tx, s.CompanyId, fromBranchId);
            EnsureBranchOwned(conn, tx, s.CompanyId, toBranchId);
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = updateBranches
                ? @"UPDATE material_requests SET operation_status=@ops, ops_from_branch_id=@fb, ops_to_branch_id=@tb,
                        ops_note=@note, version=version+1, updated_at=@now WHERE id=@id;"
                : @"UPDATE material_requests SET operation_status=@ops, ops_note=COALESCE(@note, ops_note),
                        version=version+1, updated_at=@now WHERE id=@id;";
            cmd.AddWithValue("@ops", RequestOperationStatusInfo.ToDb(to));
            cmd.AddWithValue("@note", (object?)Trim(note) ?? DBNull.Value);
            if (updateBranches)
            {
                cmd.AddWithValue("@fb", (object?)Trim(fromBranchId) ?? DBNull.Value);
                cmd.AddWithValue("@tb", (object?)Trim(toBranchId) ?? DBNull.Value);
            }
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", requestId);
            cmd.ExecuteNonQuery();
        }

        WriteOperationHistory(conn, tx, requestId, current.Value, to, s, Trim(note), now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_request", requestId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"operationStatus\":\"{RequestOperationStatusInfo.ToDb(to)}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>Yalnız gönderim bilgisi/not güncellemesi (durum değişmeden). Geçmişe DURUM satırı yazılmaz.</summary>
    public void UpdateShipmentInfo(SessionContext s, string requestId, string? fromBranchId, string? toBranchId, string? note)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var (_, companyId, approvalStatus) = LoadOperationStatusTx(conn, tx, s.CompanyId, requestId);
        TenantAccessGuard.EnsureOwnership(s, companyId);
        if (approvalStatus != "approved")
            throw new InvalidOperationException("Operasyon bilgileri yalnız onaylanmış talepte düzenlenir.");
        EnsureBranchOwned(conn, tx, s.CompanyId, fromBranchId);
        EnsureBranchOwned(conn, tx, s.CompanyId, toBranchId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"UPDATE material_requests SET ops_from_branch_id=@fb, ops_to_branch_id=@tb,
                                    ops_note=@note, version=version+1, updated_at=@now WHERE id=@id;";
            cmd.AddWithValue("@fb", (object?)Trim(fromBranchId) ?? DBNull.Value);
            cmd.AddWithValue("@tb", (object?)Trim(toBranchId) ?? DBNull.Value);
            cmd.AddWithValue("@note", (object?)Trim(note) ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", requestId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_request", requestId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>İşlem geçmişi (şartname §13): yalnız OPERASYON geçişleri (kind='operation'), en eski → en yeni.</summary>
    public IReadOnlyList<RequestOperationHistoryRow> GetHistory(SessionContext s, string requestId)
    {
        AccessControl.Require(s, RequestOperationStateMachine.ModuleOps, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT h.created_at, h.from_status, h.to_status, u.full_name, br.name, h.reason
FROM request_status_history h
LEFT JOIN users u    ON u.id  = h.by_user
LEFT JOIN branches br ON br.id = h.op_branch_id
WHERE h.request_id=@r AND h.kind='operation'
ORDER BY h.created_at;";
        cmd.AddWithValue("@r", requestId);
        var list = new List<RequestOperationHistoryRow>();
        using var r = cmd.ExecuteReader();
        string? S(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (r.Read())
            list.Add(new RequestOperationHistoryRow(r.GetInt64(0), S(1), r.GetString(2), S(3), S(4), S(5)));
        return list;
    }

    // ── yardımcılar ──
    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private RequestOperationStatus? LoadOperationStatus(SessionContext s, string requestId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT operation_status FROM material_requests WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", requestId);
        cmd.AddWithValue("@c", s.CompanyId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? null : RequestOperationStatusInfo.FromDb((string)v);
    }

    private static (RequestOperationStatus? Ops, string CompanyId, string ApprovalStatus) LoadOperationStatusTx(
        DbConnection conn, DbTransaction tx, string companyId, string requestId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT operation_status, company_id, status FROM material_requests WHERE id=@id AND is_deleted=0;";
        cmd.AddWithValue("@id", requestId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Talep bulunamadı.");
        var ops = r.IsDBNull(0) ? null : RequestOperationStatusInfo.FromDb(r.GetString(0));
        return (ops, r.GetString(1), r.GetString(2));
    }

    /// <summary>Seçilen şube bu firmaya mı ait? (İstemciden gelen şube kimliğine körü körüne güvenilmez.)</summary>
    private static void EnsureBranchOwned(DbConnection conn, DbTransaction tx, string companyId, string? branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId)) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", branchId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Seçilen şube bu firmaya ait değil.");
    }

    /// <summary>
    /// Operasyon geçmişi satırı. <c>kind='operation'</c> ile ONAY geçmişinden ayrılır (kullanıcı kararı).
    /// <c>op_branch_id</c> İSTEMCİDEN DEĞİL, oturumun çalışma şubesinden (BranchScope) alınır → kullanıcı
    /// yetkisi olmayan bir şube adına işlem kaydedemez.
    /// </summary>
    private static void WriteOperationHistory(DbConnection conn, DbTransaction tx, string requestId,
        RequestOperationStatus from, RequestOperationStatus to, SessionContext s, string? reason, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO request_status_history(id, request_id, from_status, to_status, by_user, reason, created_at, kind, op_branch_id) " +
            "VALUES(@id,@r,@from,@to,@by,@reason,@now,'operation',@ob);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@r", requestId);
        cmd.AddWithValue("@from", RequestOperationStatusInfo.ToDb(from));
        cmd.AddWithValue("@to", RequestOperationStatusInfo.ToDb(to));
        cmd.AddWithValue("@by", s.UserId);
        cmd.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@ob", (object?)BranchScope.Active(s) ?? DBNull.Value);   // sunucu tarafında belirlenir
        cmd.ExecuteNonQuery();
    }
}
