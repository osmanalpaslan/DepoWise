using DepoWise.Application.Common;
using DepoWise.Application.Requests;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using System.Data.Common;

namespace DepoWise.Infrastructure.Requests;

public sealed record RequestItemInput(string MaterialId, decimal Quantity, string? VehicleId = null, string? Note = null);

public sealed record NewRequest(
    IReadOnlyList<RequestItemInput> Items, string? BranchId = null, string? RequesterId = null,
    string? WarehouseId = null, string? ApproverId = null, string? Description = null,
    long? RequestDate = null, bool SubmitImmediately = false);

public sealed record RequestHeader(string Id, string DocNo, RequestStatus Status, string CompanyId);

public sealed record RequestListRow(string Id, string DocNo, RequestStatus Status, long RequestDate, int ItemCount, string? Description);

public sealed record RequestItemRow(string MaterialCode, string MaterialName, decimal Quantity, string? Note);

public sealed record RequestPdfLine(string Code, string Name, string Unit, decimal Quantity, string? VehicleCode, string? VehicleChassis);

public sealed record RequestEditItem(string MaterialId, string Code, string Name, decimal Quantity,
    string? VehicleId, string? VehicleCode, string? VehiclePlate);

public sealed record RequestEditData(string? BranchId, string? RequesterId, string? WarehouseId, string? ApproverId,
    string? Description, long RequestDate, RequestStatus Status, IReadOnlyList<RequestEditItem> Items);

public sealed record RequestPdfData(
    string DocNo, long RequestDate, RequestStatus Status, string? BranchName,
    string? RequesterName, string? WarehouseName, string? ApproverName, string? Description,
    IReadOnlyList<RequestPdfLine> Items);

/// <summary>
/// Malzeme talep/onay — talep BELGEDİR; onay/ret STOK DEĞİŞTİRMEZ. Durum makinesi + yetki fail-closed +
/// çift onay engeli + durum geçmişi. Onaylı talepten KONTROLLÜ stok çıkışı ayrı, açık işlemle başlatılır.
/// </summary>
public sealed class RequestService
{
    private const string Module = "requests";                 // Talep Formu (oluştur/düzenle/görüntüle)
    private const string ApprovalModule = "request_approval"; // Talep Onaylama (ayrı ekran + ayrı yetki)
    private readonly IDbConnectionFactory _factory;
    private readonly StockService _stock;
    private readonly IClock _clock;

    public RequestService(IDbConnectionFactory factory, StockService stock, IClock? clock = null)
    {
        _factory = factory;
        _stock = stock;
        _clock = clock ?? new SystemClock();
    }

    public RequestHeader Create(SessionContext s, NewRequest dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.Items.Count == 0) throw new ArgumentException("En az bir kalem gerekli.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        var status = dto.SubmitImmediately ? RequestStatus.Pending : RequestStatus.Draft;

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var docNo = NextDocNo(conn, tx, s.CompanyId, dto.RequestDate ?? now);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO material_requests(id, company_id, doc_no, request_date, branch_id, requester_id, warehouse_id,
    approver_id, description, status, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@no,@dt,@br,@req,@wh,@ap,@desc,@st,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@no", docNo);
            cmd.AddWithValue("@dt", dto.RequestDate ?? now);
            cmd.AddWithValue("@br", (object?)dto.BranchId ?? DBNull.Value);
            cmd.AddWithValue("@req", (object?)dto.RequesterId ?? DBNull.Value);
            cmd.AddWithValue("@wh", (object?)dto.WarehouseId ?? DBNull.Value);
            cmd.AddWithValue("@ap", (object?)dto.ApproverId ?? DBNull.Value);
            cmd.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.AddWithValue("@st", RequestStatusMachine.ToDb(status));
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        foreach (var item in dto.Items)
        {
            EnsureMaterialOwned(conn, tx, s.CompanyId, item.MaterialId);
            using var ic = conn.CreateCommand();
            ic.Transaction = tx;
            ic.CommandText = "INSERT INTO material_request_items(id, request_id, material_id, quantity, vehicle_id, note) VALUES(@id,@r,@m,@q,@v,@n);";
            ic.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            ic.AddWithValue("@r", id);
            ic.AddWithValue("@m", item.MaterialId);
            ic.AddWithValue("@q", Money.Serialize(item.Quantity));
            ic.AddWithValue("@v", (object?)item.VehicleId ?? DBNull.Value);
            ic.AddWithValue("@n", (object?)item.Note ?? DBNull.Value);
            ic.ExecuteNonQuery();
        }
        WriteHistory(conn, tx, id, null, status, s.UserId, null, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_request", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return new RequestHeader(id, docNo, status, s.CompanyId);
    }

    /// <summary>Mevcut talebi günceller (başlık + kalemler tam değiştirir). ONAYLI talep değiştirilemez. Belge no/durum korunur.</summary>
    public void Update(SessionContext s, string requestId, NewRequest dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (dto.Items.Count == 0) throw new ArgumentException("En az bir kalem gerekli.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var (status, companyId) = LoadStatusTx(conn, tx, s.CompanyId, requestId);
        TenantAccessGuard.EnsureOwnership(s, companyId);
        if (status == RequestStatus.Approved)
            throw new InvalidOperationException("Onaylanmış talep güncellenemez.");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
UPDATE material_requests SET branch_id=@br, requester_id=@req, warehouse_id=@wh, approver_id=@ap,
    description=@desc, request_date=@dt, version=version+1, updated_at=@now WHERE id=@id;";
            cmd.AddWithValue("@br", (object?)dto.BranchId ?? DBNull.Value);
            cmd.AddWithValue("@req", (object?)dto.RequesterId ?? DBNull.Value);
            cmd.AddWithValue("@wh", (object?)dto.WarehouseId ?? DBNull.Value);
            cmd.AddWithValue("@ap", (object?)dto.ApproverId ?? DBNull.Value);
            cmd.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
            cmd.AddWithValue("@dt", dto.RequestDate ?? now);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", requestId);
            cmd.ExecuteNonQuery();
        }
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM material_request_items WHERE request_id=@r;";
            del.AddWithValue("@r", requestId);
            del.ExecuteNonQuery();
        }
        foreach (var item in dto.Items)
        {
            EnsureMaterialOwned(conn, tx, s.CompanyId, item.MaterialId);
            using var ic = conn.CreateCommand();
            ic.Transaction = tx;
            ic.CommandText = "INSERT INTO material_request_items(id, request_id, material_id, quantity, vehicle_id, note) VALUES(@id,@r,@m,@q,@v,@n);";
            ic.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            ic.AddWithValue("@r", requestId);
            ic.AddWithValue("@m", item.MaterialId);
            ic.AddWithValue("@q", Money.Serialize(item.Quantity));
            ic.AddWithValue("@v", (object?)item.VehicleId ?? DBNull.Value);
            ic.AddWithValue("@n", (object?)item.Note ?? DBNull.Value);
            ic.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_request", requestId, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Düzenleme için tam veri (başlık id'leri + kalemler: malzeme/araç id'leri).</summary>
    public RequestEditData GetForEdit(SessionContext s, string requestId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        string? br, rq, wh, ap, desc; long date; string status;
        using (var hc = conn.CreateCommand())
        {
            hc.CommandText = "SELECT branch_id, requester_id, warehouse_id, approver_id, description, request_date, status, company_id FROM material_requests WHERE id=@id;";
            hc.AddWithValue("@id", requestId);
            using var hr = hc.ExecuteReader();
            if (!hr.Read()) throw new ForbiddenException("Talep bulunamadı.");
            if (hr.GetString(7) != s.CompanyId) throw new ForbiddenException("Talep başka firmaya ait.");
            br = hr.IsDBNull(0) ? null : hr.GetString(0);
            rq = hr.IsDBNull(1) ? null : hr.GetString(1);
            wh = hr.IsDBNull(2) ? null : hr.GetString(2);
            ap = hr.IsDBNull(3) ? null : hr.GetString(3);
            desc = hr.IsDBNull(4) ? null : hr.GetString(4);
            date = hr.GetInt64(5);
            status = hr.GetString(6);
        }

        var items = new List<RequestEditItem>();
        using (var ic = conn.CreateCommand())
        {
            ic.CommandText = @"
SELECT i.material_id, m.code, m.name, i.quantity, i.vehicle_id, v.internal_code, v.plate
FROM material_request_items i
JOIN materials m ON m.id = i.material_id
LEFT JOIN vehicles v ON v.id = i.vehicle_id
WHERE i.request_id=@r ORDER BY m.code;";
            ic.AddWithValue("@r", requestId);
            using var ir = ic.ExecuteReader();
            while (ir.Read())
                items.Add(new RequestEditItem(ir.GetString(0), ir.GetString(1), ir.GetString(2), Money.Parse(ir.GetString(3)),
                    ir.IsDBNull(4) ? null : ir.GetString(4),
                    ir.IsDBNull(5) ? null : ir.GetString(5),
                    ir.IsDBNull(6) ? null : ir.GetString(6)));
        }
        return new RequestEditData(br, rq, wh, ap, desc, date, RequestStatusMachine.FromDb(status), items);
    }

    public void Submit(SessionContext s, string requestId)
        => Transition(s, requestId, RequestStatus.Pending, PermissionAction.Edit, null);

    public void Approve(SessionContext s, string requestId)
    {
        // Onay ayrı ekran/yetki: "Talep Onaylama" (request_approval). Form (requests) Edit'i YETMEZ.
        Transition(s, requestId, RequestStatus.Approved, PermissionAction.Edit, null, setApproval: true, gateModule: ApprovalModule);
    }

    public void Reject(SessionContext s, string requestId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Ret gerekçesi zorunlu.");
        Transition(s, requestId, RequestStatus.Rejected, PermissionAction.Edit, reason, gateModule: ApprovalModule);
    }

    public void Cancel(SessionContext s, string requestId, string? reason = null)
        => Transition(s, requestId, RequestStatus.Cancelled, PermissionAction.Edit, reason);

    /// <summary>Onaylı talepten KONTROLLÜ stok çıkışı başlatır (otomatik DEĞİL, açık çağrı). Stok burada düşer.</summary>
    public StockDocResult CreateIssueFromRequest(SessionContext s, string requestId, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var (status, companyId) = LoadStatus(s, requestId);
        TenantAccessGuard.EnsureOwnership(s, companyId);
        if (status != RequestStatus.Approved)
            throw new InvalidOperationException("Yalnız onaylı talepten stok çıkışı başlatılabilir.");

        var lines = LoadItems(requestId);
        // Stok çıkışı ayrı, kontrollü işlem (StockService kendi negatif guard/transaction'ı)
        return _stock.IssueOut(s, lines, operationId, note: $"Talep: {requestId}");
    }

    public RequestStatus GetStatus(SessionContext s, string requestId) => LoadStatus(s, requestId).Status;

    public IReadOnlyList<(RequestStatus? From, RequestStatus To, string? Reason)> GetHistory(string requestId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT from_status, to_status, reason FROM request_status_history WHERE request_id=@r ORDER BY created_at;";
        cmd.AddWithValue("@r", requestId);
        var list = new List<(RequestStatus?, RequestStatus, string?)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.IsDBNull(0) ? null : RequestStatusMachine.FromDb(r.GetString(0)),
                RequestStatusMachine.FromDb(r.GetString(1)), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>Talep başlıkları (salt okuma) — durum filtresi + belge no/açıklama araması.</summary>
    public IReadOnlyList<RequestListRow> List(SessionContext s, RequestStatus? status = null, string? search = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
SELECT mr.id, mr.doc_no, mr.status, mr.request_date, mr.description,
       (SELECT COUNT(*) FROM material_request_items i WHERE i.request_id = mr.id)
FROM material_requests mr
WHERE mr.company_id=@c AND mr.is_deleted=0
  AND (@st IS NULL OR mr.status=@st)
  AND (@s IS NULL OR {SqlDialect.LikeTr(conn, "mr.doc_no", "@like")} OR {SqlDialect.LikeTr(conn, "COALESCE(mr.description,'')", "@like")})
ORDER BY mr.request_date DESC, mr.created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@st", status is null ? DBNull.Value : RequestStatusMachine.ToDb(status.Value));
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        cmd.AddWithValue("@s", (object?)term ?? DBNull.Value);
        cmd.AddWithValue("@like", term is null ? "%" : "%" + term + "%");
        cmd.AddWithValue("@lim", limit);
        var list = new List<RequestListRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new RequestListRow(r.GetString(0), r.GetString(1), RequestStatusMachine.FromDb(r.GetString(2)),
                r.GetInt64(3), r.GetInt32(5), r.IsDBNull(4) ? null : r.GetString(4)));
        return list;
    }

    /// <summary>Talep kalemleri (salt okuma) — malzeme kod/ad + miktar.</summary>
    public IReadOnlyList<RequestItemRow> GetItems(SessionContext s, string requestId)
    {
        LoadStatus(s, requestId); // tenant guard (firma sahipliği)
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT m.code, m.name, i.quantity, i.note
FROM material_request_items i JOIN materials m ON m.id = i.material_id
WHERE i.request_id=@r ORDER BY m.code;";
        cmd.AddWithValue("@r", requestId);
        var list = new List<RequestItemRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new RequestItemRow(r.GetString(0), r.GetString(1), Money.Parse(r.GetString(2)),
                r.IsDBNull(3) ? null : r.GetString(3)));
        return list;
    }

    /// <summary>PDF için tam veri (isimler + araç etiketli kalemler). Tenant guard'lı.</summary>
    public RequestPdfData GetPdfData(SessionContext s, string requestId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();

        string docNo, status; long date; string? desc, branch, req, wh, ap;
        using (var hc = conn.CreateCommand())
        {
            hc.CommandText = @"
SELECT mr.doc_no, mr.request_date, mr.status, mr.description,
       b.name, pr.full_name, pw.full_name, pa.full_name, mr.company_id
FROM material_requests mr
LEFT JOIN branches b ON b.id = mr.branch_id
LEFT JOIN personnel pr ON pr.id = mr.requester_id
LEFT JOIN personnel pw ON pw.id = mr.warehouse_id
LEFT JOIN personnel pa ON pa.id = mr.approver_id
WHERE mr.id=@id;";
            hc.AddWithValue("@id", requestId);
            using var hr = hc.ExecuteReader();
            if (!hr.Read()) throw new ForbiddenException("Talep bulunamadı.");
            if (hr.GetString(8) != s.CompanyId) throw new ForbiddenException("Talep başka firmaya ait.");
            docNo = hr.GetString(0); date = hr.GetInt64(1); status = hr.GetString(2);
            desc = hr.IsDBNull(3) ? null : hr.GetString(3);
            branch = hr.IsDBNull(4) ? null : hr.GetString(4);
            req = hr.IsDBNull(5) ? null : hr.GetString(5);
            wh = hr.IsDBNull(6) ? null : hr.GetString(6);
            ap = hr.IsDBNull(7) ? null : hr.GetString(7);
        }

        var items = new List<RequestPdfLine>();
        using (var ic = conn.CreateCommand())
        {
            ic.CommandText = @"
SELECT m.code, m.name, COALESCE(u.name,''), i.quantity, v.internal_code, v.chassis_no
FROM material_request_items i
JOIN materials m ON m.id = i.material_id
LEFT JOIN units u ON u.id = m.unit_id
LEFT JOIN vehicles v ON v.id = i.vehicle_id
WHERE i.request_id=@r ORDER BY m.code;";
            ic.AddWithValue("@r", requestId);
            using var ir = ic.ExecuteReader();
            while (ir.Read())
            {
                items.Add(new RequestPdfLine(
                    ir.GetString(0), ir.GetString(1), ir.GetString(2), Money.Parse(ir.GetString(3)),
                    ir.IsDBNull(4) ? null : ir.GetString(4),
                    ir.IsDBNull(5) ? null : ir.GetString(5)));
            }
        }
        return new RequestPdfData(docNo, date, RequestStatusMachine.FromDb(status), branch, req, wh, ap, desc, items);
    }

    // ---- çekirdek ----
    private void Transition(SessionContext s, string requestId, RequestStatus to, PermissionAction perm, string? reason, bool setApproval = false, string? gateModule = null)
    {
        AccessControl.Require(s, gateModule ?? Module, perm);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();

        var (from, companyId) = LoadStatusTx(conn, tx, s.CompanyId, requestId);
        TenantAccessGuard.EnsureOwnership(s, companyId);
        if (!RequestStatusMachine.CanTransition(from, to))
            throw new InvalidOperationException($"Geçersiz durum geçişi: {from} → {to} (çift onay/yetkisiz geçiş engellendi).");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = setApproval
                ? "UPDATE material_requests SET status=@st, approved_by=@by, approved_at=@now, version=version+1, updated_at=@now WHERE id=@id;"
                : "UPDATE material_requests SET status=@st, version=version+1, updated_at=@now WHERE id=@id;";
            cmd.AddWithValue("@st", RequestStatusMachine.ToDb(to));
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", requestId);
            if (setApproval) cmd.AddWithValue("@by", s.UserId);
            cmd.ExecuteNonQuery();
        }
        WriteHistory(conn, tx, requestId, from, to, s.UserId, reason, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "material_request", requestId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"status\":\"{RequestStatusMachine.ToDb(to)}\"}}"), _clock);
        tx.Commit();
    }

    private (RequestStatus Status, string CompanyId) LoadStatus(SessionContext s, string requestId)
    {
        using var conn = _factory.Create();
        return LoadStatusTx(conn, null, s.CompanyId, requestId);
    }

    private static (RequestStatus, string) LoadStatusTx(DbConnection conn, DbTransaction? tx, string companyId, string requestId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT status, company_id FROM material_requests WHERE id=@id;";
        cmd.AddWithValue("@id", requestId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ForbiddenException("Talep bulunamadı.");
        var cid = r.GetString(1);
        if (cid != companyId) throw new ForbiddenException("Talep başka firmaya ait.");
        return (RequestStatusMachine.FromDb(r.GetString(0)), cid);
    }

    private IReadOnlyList<StockLine> LoadItems(string requestId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT material_id, quantity FROM material_request_items WHERE request_id=@r;";
        cmd.AddWithValue("@r", requestId);
        var list = new List<StockLine>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new StockLine(r.GetString(0), Money.Parse(r.GetString(1))));
        return list;
    }

    private static void WriteHistory(DbConnection conn, DbTransaction tx, string requestId,
        RequestStatus? from, RequestStatus to, string? byUser, string? reason, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO request_status_history(id, request_id, from_status, to_status, by_user, reason, created_at) " +
            "VALUES(@id,@r,@from,@to,@by,@reason,@now);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@r", requestId);
        cmd.AddWithValue("@from", from is null ? DBNull.Value : RequestStatusMachine.ToDb(from.Value));
        cmd.AddWithValue("@to", RequestStatusMachine.ToDb(to));
        cmd.AddWithValue("@by", (object?)byUser ?? DBNull.Value);
        cmd.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static string NextDocNo(DbConnection conn, DbTransaction tx, string companyId, long dateMs)
    {
        var year = DateTimeOffset.FromUnixTimeMilliseconds(dateMs).Year;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT COALESCE(MAX(CAST(substr(doc_no, length(@p)+1) AS INTEGER)),0) FROM material_requests " +
            "WHERE company_id=@c AND doc_no LIKE @like;";
        cmd.AddWithValue("@p", $"TLP-{year}-");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@like", $"TLP-{year}-%");
        var next = Convert.ToInt64(cmd.ExecuteScalar()) + 1;
        return $"TLP-{year}-{next:0000}";
    }

    private static void EnsureMaterialOwned(DbConnection conn, DbTransaction tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM materials WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", materialId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException("Malzeme bulunamadı veya başka firmaya ait.");
    }
}
