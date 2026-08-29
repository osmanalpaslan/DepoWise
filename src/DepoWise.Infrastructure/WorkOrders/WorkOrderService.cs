using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;

namespace DepoWise.Infrastructure.WorkOrders;

/// <summary>İş emri liste satırı.</summary>
public sealed record WorkOrderRow(string Id, string WoNo, string Title, string? Description, string Status,
    string Priority, string? BranchId, string? BranchName, string? CostCenterId, string? CostCenterName,
    string? AssigneePersonnelId, string? AssigneeName, long? PlannedStart, long? PlannedEnd,
    long? ActualStart, long? ActualEnd, string? ClosingNote, long Version)
{
    public string StatusDisplay => WorkOrderService.StatusLabel(Status);
    public string StatusColor => WorkOrderService.StatusColor(Status);
    public string PriorityDisplay => WorkOrderService.PriorityLabel(Priority);
    public string BranchDisplay => string.IsNullOrEmpty(BranchName) ? "—" : BranchName!;
    public string AssigneeDisplay => string.IsNullOrEmpty(AssigneeName) ? "—" : AssigneeName!;
    public string CostCenterDisplay => string.IsNullOrEmpty(CostCenterName) ? "—" : CostCenterName!;
    public string PlannedDisplay => Tarih(PlannedStart) + " – " + Tarih(PlannedEnd);
    public string ActualDisplay => ActualStart is null && ActualEnd is null ? "—" : Tarih(ActualStart) + " – " + Tarih(ActualEnd);
    private static string Tarih(long? ms) => ms is null ? "…"
        : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).UtcDateTime.ToString("dd.MM.yyyy");
}

public sealed record WorkOrderAssignmentRow(string Id, string ResourceType, string ResourceId, string ResourceLabel, string? Note)
{
    public string ResourceTypeDisplay => ResourceType switch
    { "vehicle" => "Araç", "equipment" => "Ekipman", _ => "Personel" };
}

public sealed record WorkOrderLinkRow(string Id, string EntityType, string EntityId, string Label)
{
    public string EntityTypeDisplay => EntityType switch
    { "stock_document" => "Malzeme Tüketimi", "vehicle_maintenance" => "Bakım", "purchase_order" => "Sipariş", _ => EntityType };
}

public sealed record WorkOrderHistoryRow(string? FromStatus, string ToStatus, string UserName, string? Note, long CreatedAt)
{
    public string Line => $"{DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime:dd.MM.yyyy HH:mm} · " +
        $"{WorkOrderService.StatusLabel(FromStatus ?? "")}{(FromStatus is null ? "" : " → ")}{WorkOrderService.StatusLabel(ToStatus)} · {UserName}" +
        (string.IsNullOrEmpty(Note) ? "" : $" · {Note}");
}

public sealed record NewWorkOrder(string WoNo, string Title, string? Description = null, string? Priority = null,
    string? BranchId = null, string? CostCenterId = null, string? AssigneePersonnelId = null,
    long? PlannedStart = null, long? PlannedEnd = null);

/// <summary>
/// ═══ EMR-01 (ADR-170, 2026-08-28) — İŞ EMRİ ═══
///
/// PK-F1: <c>draft → assigned → in_progress ⇄ on_hold → completed</c>; her aktif durumdan <c>cancelled</c>.
/// Onay katmanı YOK — geçişler modül yetkisiyle: durum ilerletme = Edit · iptal = Delete.
/// PK-F2: <c>completed</c> ve <c>cancelled</c> TERMİNALDİR — yeniden açma YOLU YOKTUR.
///
/// <b>MALZEME TÜKETİMİ (PK-F3):</b> MEVCUT <see cref="StockService.IssueOutTx"/> TEK transaction'da
/// (satın alma/zimmet/fatura emsali): negatif stok kalkanı + stok yetkisi + TRH-01 aynen; idempotency
/// STOK DEFTERİNDEN (<c>wo:</c> izi) — retry ikinci çıkış üretmez. Maliyet merkezi seçiliyse belge
/// D'nin dış-bağıyla merkeze bağlanır (çift sayım yok — özet tek kaynaktan okur).
///
/// <b>YETKİ:</b> yeni <c>work_orders</c> modülü (deny-by-default) + tüketimde STOK kapısı DA aranır
/// (yan kapı yok). <b>KAPSAM:</b> şantiye/saha üzerinden <see cref="BranchAccess"/>. Tenant: company_id.
/// <b>GEÇMİŞ:</b> her durum değişikliği append-only deftere yazılır; silme/güncelleme yolu yok.
/// </summary>
public sealed class WorkOrderService
{
    public const string Module = "work_orders";

    private static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["draft"] = new[] { "assigned", "in_progress", "cancelled" },
        ["assigned"] = new[] { "in_progress", "on_hold", "cancelled" },
        ["in_progress"] = new[] { "on_hold", "completed", "cancelled" },
        ["on_hold"] = new[] { "in_progress", "completed", "cancelled" },
        ["completed"] = Array.Empty<string>(),   // PK-F2: terminal
        ["cancelled"] = Array.Empty<string>(),   // terminal
    };

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly StockService _stock;
    private readonly CostCenterService _costCenters;

    public WorkOrderService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _stock = new StockService(factory, _clock);
        _costCenters = new CostCenterService(factory, _clock);
    }

    public static string StatusLabel(string s) => s switch
    {
        "draft" => "Taslak", "assigned" => "Atandı", "in_progress" => "Devam Ediyor",
        "on_hold" => "Beklemede", "completed" => "Tamamlandı", "cancelled" => "İptal", _ => s,
    };
    public static string StatusColor(string s) => s switch
    {
        "in_progress" => "ok", "on_hold" => "warn", "completed" => "muted", "cancelled" => "muted", _ => "warn",
    };
    public static string PriorityLabel(string p) => p switch
    { "high" => "Yüksek", "urgent" => "Acil", "critical" => "Kritik", _ => "Normal" };
    private static string NormPriority(string? p) => p is "high" or "urgent" or "critical" ? p : "normal";
    public static IReadOnlyList<string> NextStates(string from)
        => Allowed.TryGetValue(from, out var t) ? t : Array.Empty<string>();

    // ══════════════ LİSTE / DETAY ══════════════

    public IReadOnlyList<WorkOrderRow> List(SessionContext s, string? search = null, string? status = null,
        string? priority = null, string? branchId = null, string? assigneeId = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        var list = new List<WorkOrderRow>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT w.id, w.wo_no, w.title, w.description, w.status, w.priority, w.branch_id, b.name,
       w.cost_center_id, cc.name, w.assignee_personnel_id, per.full_name,
       w.planned_start, w.planned_end, w.actual_start, w.actual_end, w.closing_note, w.version
FROM work_orders w
LEFT JOIN branches b ON b.id = w.branch_id
LEFT JOIN cost_centers cc ON cc.id = w.cost_center_id
LEFT JOIN personnel per ON per.id = w.assignee_personnel_id
WHERE w.company_id=@c AND w.is_deleted=0" +
                (string.IsNullOrWhiteSpace(status) ? "" : " AND w.status=@st") +
                (string.IsNullOrWhiteSpace(priority) ? "" : " AND w.priority=@pr") +
                (string.IsNullOrWhiteSpace(branchId) ? "" : " AND w.branch_id=@b") +
                (string.IsNullOrWhiteSpace(assigneeId) ? "" : " AND w.assignee_personnel_id=@a") +
                " ORDER BY w.created_at DESC;";
            cmd.AddWithValue("@c", s.CompanyId);
            if (!string.IsNullOrWhiteSpace(status)) cmd.AddWithValue("@st", status);
            if (!string.IsNullOrWhiteSpace(priority)) cmd.AddWithValue("@pr", priority);
            if (!string.IsNullOrWhiteSpace(branchId)) cmd.AddWithValue("@b", branchId);
            if (!string.IsNullOrWhiteSpace(assigneeId)) cmd.AddWithValue("@a", assigneeId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new WorkOrderRow(r.GetString(0), r.GetString(1), r.GetString(2), N(r, 3),
                    r.GetString(4), r.GetString(5), N(r, 6), N(r, 7), N(r, 8), N(r, 9), N(r, 10), N(r, 11),
                    L(r, 12), L(r, 13), L(r, 14), L(r, 15), N(r, 16), r.GetInt64(17)));
        }

        // ŞUBE KAPSAMI: kapsam dışı şantiyenin iş emri görünmez; şubesiz iş emri gizlenmez.
        var izinli = BranchAccess.Allowed(s);
        if (izinli is not null)
        {
            var set = izinli.ToHashSet(StringComparer.Ordinal);
            list = list.Where(w => w.BranchId is null || set.Contains(w.BranchId)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(w =>
                w.WoNo.Contains(q, StringComparison.OrdinalIgnoreCase)
                || w.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (w.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (w.BranchName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (w.AssigneeName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        return list;
    }

    // ══════════════ OLUŞTUR / DÜZENLE ══════════════

    public string Create(SessionContext s, NewWorkOrder dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        Dogrula(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureRefs(s, conn, tx, dto);
        EnsureNoFree(conn, tx, s.CompanyId, dto.WoNo, null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO work_orders(id, company_id, wo_no, title, description, status, priority, branch_id,
    cost_center_id, assignee_personnel_id, planned_start, planned_end, closing_note, created_by,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@no,@t,@d,'draft',@pr,@b,@cc,@a,@ps,@pe,NULL,@u,@now,@now,1,0);";
            Alanlar(cmd, s, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@u", s.UserId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        Gecmis(conn, tx, s, id, null, "draft", null, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "work_order", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"woNo\":\"{dto.WoNo.Trim()}\"}}"), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Meta düzenleme — TERMİNAL durumda (Tamamlandı/İptal) DÜZENLENEMEZ (PK-F2: geçmişe yazma yok).</summary>
    public void UpdateMeta(SessionContext s, string id, NewWorkOrder dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        Dogrula(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var (durum, _, _) = Getir(s, conn, tx, id, expectedVersion);
        if (Terminal(durum)) throw new ArgumentException("Tamamlanmış veya iptal edilmiş iş emri düzenlenemez. Yeni iş emri açın (PK-F2).");
        EnsureRefs(s, conn, tx, dto);
        EnsureNoFree(conn, tx, s.CompanyId, dto.WoNo, id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE work_orders SET wo_no=@no, title=@t, description=@d, priority=@pr, " +
                "branch_id=@b, cost_center_id=@cc, assignee_personnel_id=@a, planned_start=@ps, planned_end=@pe, " +
                "updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            Alanlar(cmd, s, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "work_order", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    // ══════════════ DURUM (PK-F1/F2) ══════════════

    /// <summary>Durum geçişi. Matris dışı geçiş REDDEDİLİR; terminalden çıkış YOKTUR (PK-F2).
    /// İptal DELETE yetkisi ister; diğer ilerlemeler EDIT. Geçmiş defterine iz düşülür;
    /// Devam Ediyor'a İLK geçişte actual_start, Tamamlandı'da actual_end otomatik yazılır (iş günü).</summary>
    public void SetStatus(SessionContext s, string id, string toStatus, string? note = null, long? docDate = null)
    {
        AccessControl.Require(s, Module, toStatus == "cancelled" ? PermissionAction.Delete : PermissionAction.Edit);
        if (!Allowed.ContainsKey(toStatus)) throw new ArgumentException("Bilinmeyen durum.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var isGunu = DateEntryPolicy.Uygula(s, docDate) ?? now;
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var (mevcut, _, actualStart) = Getir(s, conn, tx, id, null);
        if (!NextStates(mevcut).Contains(toStatus))
            throw new ArgumentException(Terminal(mevcut)
                ? $"'{StatusLabel(mevcut)}' iş emri değiştirilemez — yeniden açma yoktur (PK-F2); yeni iş emri açın."
                : $"'{StatusLabel(mevcut)}' durumundan '{StatusLabel(toStatus)}' durumuna geçilemez.");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE work_orders SET status=@st, " +
                "actual_start=@as, actual_end=@ae, completed_by=@cb, closing_note=@cn, " +
                "updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@st", toStatus);
            cmd.AddWithValue("@as", toStatus == "in_progress" && actualStart is null ? isGunu : (object?)actualStart ?? DBNull.Value);
            cmd.AddWithValue("@ae", toStatus == "completed" ? isGunu : DBNull.Value);
            cmd.AddWithValue("@cb", toStatus == "completed" ? s.UserId : (object)DBNull.Value);
            cmd.AddWithValue("@cn", toStatus is "completed" or "cancelled" && !string.IsNullOrWhiteSpace(note)
                ? note!.Trim() : (object)DBNull.Value);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        Gecmis(conn, tx, s, id, mevcut, toStatus, note, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "work_order", id,
            toStatus == "cancelled" ? AuditActions.Reverse : AuditActions.Update, s.UserId,
            AfterJson: $"{{\"status\":\"{toStatus}\"}}"), _clock);
        tx.Commit();
    }

    // ══════════════ ATAMALAR (PK-F4/F8: yalnız atama) ══════════════

    public void AddAssignment(SessionContext s, string workOrderId, string resourceType, string resourceId, string? note = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var tablo = resourceType switch
        {
            "personnel" => "personnel", "vehicle" => "vehicles", "equipment" => "equipment",
            _ => throw new ArgumentException("Atanabilir kaynak: personel, araç veya ekipman."),
        };
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        var (durum, _, _) = Getir(s, conn, tx, workOrderId, null);
        if (Terminal(durum)) throw new ArgumentException("Kapanmış iş emrine atama yapılamaz.");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT COUNT(*) FROM {tablo} WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", resourceId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                throw new ArgumentException("Kaynak bulunamadı veya bu firmaya ait değil.");
        }
        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(*) FROM work_order_assignments WHERE work_order_id=@w AND resource_type=@t AND resource_id=@r AND is_deleted=0;";
            chk.AddWithValue("@w", workOrderId);
            chk.AddWithValue("@t", resourceType);
            chk.AddWithValue("@r", resourceId);
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0) { tx.Commit(); return; }   // zaten atanmış — sessiz
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO work_order_assignments(id, work_order_id, company_id, resource_type, resource_id, note, " +
                "created_at, updated_at, version, is_deleted) VALUES(@id,@w,@c,@t,@r,@n,@now,@now,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@w", workOrderId);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@t", resourceType);
            cmd.AddWithValue("@r", resourceId);
            cmd.AddWithValue("@n", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note!.Trim());
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "work_order", workOrderId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"assign\":\"{resourceType}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>Atama kaldırma = SOFT (kimin atanmış olduğu izi kalır).</summary>
    public void RemoveAssignment(SessionContext s, string assignmentId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        string woId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT work_order_id FROM work_order_assignments WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", assignmentId);
            cmd.AddWithValue("@c", s.CompanyId);
            woId = cmd.ExecuteScalar() as string ?? throw new ArgumentException("Atama bulunamadı.");
        }
        var (durum, _, _) = Getir(s, conn, tx, woId, null);
        if (Terminal(durum)) throw new ArgumentException("Kapanmış iş emrinin atamaları değiştirilemez.");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE work_order_assignments SET is_deleted=1, updated_at=@now, version=version+1 WHERE id=@id;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", assignmentId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<WorkOrderAssignmentRow> Assignments(SessionContext s, string workOrderId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        GetirSaltOkur(s, conn, workOrderId);
        var list = new List<WorkOrderAssignmentRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT a.id, a.resource_type, a.resource_id,
       COALESCE(per.full_name, v.internal_code, eq.name, '—'), a.note
FROM work_order_assignments a
LEFT JOIN personnel per ON a.resource_type='personnel' AND per.id = a.resource_id
LEFT JOIN vehicles v ON a.resource_type='vehicle' AND v.id = a.resource_id
LEFT JOIN equipment eq ON a.resource_type='equipment' AND eq.id = a.resource_id
WHERE a.company_id=@c AND a.work_order_id=@w AND a.is_deleted=0 ORDER BY a.created_at;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@w", workOrderId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WorkOrderAssignmentRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), N(r, 4)));
        return list;
    }

    // ══════════════ MALZEME TÜKETİMİ (PK-F3) ══════════════

    /// <summary>
    /// TÜKETİM = MEVCUT stok çıkışı, TEK transaction'da: IssueOutTx (stok yetkisi + negatif stok kalkanı +
    /// TRH-01 aynen) + iş emri bağı. İDEMPOTENT: aynı operationId ikinci kez → stok DEFTERİNDEN tespit,
    /// hiçbir şey ikinci kez uygulanmaz. Maliyet merkezi seçiliyse belge D dış-bağıyla merkeze bağlanır.
    /// </summary>
    public string ConsumeMaterial(SessionContext s, string workOrderId, IReadOnlyList<StockLine> lines,
        string operationId, long? docDate = null, string? note = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (lines is not { Count: > 0 }) throw new ArgumentException("Tüketilecek malzeme seçin.");
        var stockOp = "wo:" + operationId;
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        var (durum, branchId, _) = Getir(s, conn, tx, workOrderId, null);
        if (Terminal(durum)) throw new ArgumentException("Kapanmış iş emrine tüketim işlenemez.");
        if (string.IsNullOrWhiteSpace(branchId))
            throw new ArgumentException("Malzeme tüketimi için iş emrinde şantiye/saha (depo) seçili olmalı.");

        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            // ⭐ FIN-B1 (ADR-185, Migration082 ile birlikte): FİRMA KAPSAMLI — başka firmanın aynı
            // operation_id'si bu firmanın iş emri tüketimini sessizce atlatamaz. Aynı-firma retry aynen.
            chk.CommandText = "SELECT COUNT(*) FROM stock_movements WHERE company_id=@c AND operation_id LIKE @op;";
            chk.AddWithValue("@c", s.CompanyId);
            chk.AddWithValue("@op", stockOp + "%");
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0) { tx.Commit(); return stockOp; }   // retry: İKİNCİ çıkış YOK
        }

        string? costCenterId;
        using (var cc = conn.CreateCommand())
        {
            cc.Transaction = tx;
            cc.CommandText = "SELECT cost_center_id FROM work_orders WHERE id=@id;";
            cc.AddWithValue("@id", workOrderId);
            costCenterId = cc.ExecuteScalar() as string;
        }

        var doc = _stock.IssueOutTx(conn, tx, s, lines, stockOp, branchId,
            note: string.IsNullOrWhiteSpace(note) ? "İş emri tüketimi" : note, docDate: docDate);
        Bagla(conn, tx, s.CompanyId, workOrderId, "stock_document", doc.DocumentId, now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "work_order", workOrderId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"consume\":\"{stockOp}\",\"doc\":\"{doc.DocumentId}\"}}"), _clock);
        tx.Commit();

        // Maliyet merkezi bağı — MLY-01 kuralı: kabul sonrası ayrı tx, bilgilendirici (satın alma emsali).
        if (!string.IsNullOrWhiteSpace(costCenterId))
        {
            try { _costCenters.Link(s, "stock_document", doc.DocumentId, costCenterId); }
            catch { /* bağ Maliyet Merkezleri ekranından sonradan kurulabilir */ }
        }
        return doc.DocumentId;
    }

    // ══════════════ İLİŞKİLİ KAYIT BAĞLARI (PK-F9: bakım yalnız bağ) ══════════════

    /// <summary>Mevcut bakım kaydını veya siparişi iş emrine bağlar — kaynak kayıt DEĞİŞMEZ (dış bağ).</summary>
    public void LinkExisting(SessionContext s, string workOrderId, string entityType, string entityId)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var tablo = entityType switch
        {
            "vehicle_maintenance" => "vehicle_maintenances",
            "purchase_order" => "purchase_orders",
            _ => throw new ArgumentException("Bağlanabilir kayıt: bakım veya sipariş."),
        };
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Getir(s, conn, tx, workOrderId, null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT COUNT(*) FROM {tablo} WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@id", entityId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                throw new ArgumentException("Kayıt bulunamadı veya bu firmaya ait değil.");
        }
        Bagla(conn, tx, s.CompanyId, workOrderId, entityType, entityId, now);
        tx.Commit();
    }

    public IReadOnlyList<WorkOrderLinkRow> Links(SessionContext s, string workOrderId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        GetirSaltOkur(s, conn, workOrderId);
        var list = new List<WorkOrderLinkRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT l.id, l.entity_type, l.entity_id,
       COALESCE(sd.doc_no, po.order_no, vm.id, l.entity_id)
FROM work_order_links l
LEFT JOIN stock_documents sd ON l.entity_type='stock_document' AND sd.id = l.entity_id
LEFT JOIN purchase_orders po ON l.entity_type='purchase_order' AND po.id = l.entity_id
LEFT JOIN vehicle_maintenances vm ON l.entity_type='vehicle_maintenance' AND vm.id = l.entity_id
WHERE l.company_id=@c AND l.work_order_id=@w AND l.is_deleted=0 ORDER BY l.created_at;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@w", workOrderId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WorkOrderLinkRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    // ══════════════ MALİYET ÖZETİ (mevcut hesapları DEĞİŞTİRMEZ — yalnız okur) ══════════════

    /// <summary>Bağlı STOK BELGELERİNİN satırları (qty×fiyat; satır fiyatı boşsa kart fiyatı — MLY deseni)
    /// + bağlı BAKIM kayıtlarının malzeme maliyeti. C# decimal, para birimi bazında ayrı.</summary>
    public IReadOnlyList<CostCenterSummaryRow> CostSummary(SessionContext s, string workOrderId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        GetirSaltOkur(s, conn, workOrderId);
        var toplam = new Dictionary<(string Cat, string Cur), (decimal Amt, int N)>();
        void Ekle(string cat, string cur, decimal amt)
        {
            var k = (cat, string.IsNullOrEmpty(cur) ? "TRY" : cur);
            toplam[k] = toplam.TryGetValue(k, out var v) ? (v.Amt + amt, v.N + 1) : (amt, 1);
        }
        static decimal D(DbDataReader r, int i) => r.IsDBNull(i) ? 0m
            : decimal.Parse(r.GetString(i), System.Globalization.CultureInfo.InvariantCulture);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT m.quantity, COALESCE(m.unit_price, mat.unit_price, '0'), COALESCE(m.currency_code, mat.currency_code, 'TRY')
FROM work_order_links l
JOIN stock_documents d ON d.id = l.entity_id AND d.status='active' AND d.is_deleted=0
JOIN stock_movements m ON m.document_id = d.id
JOIN materials mat ON mat.id = m.material_id
WHERE l.company_id=@c AND l.work_order_id=@w AND l.entity_type='stock_document' AND l.is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@w", workOrderId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Ekle("Malzeme Tüketimi", r.GetString(2), D(r, 0) * D(r, 1));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
SELECT mm.quantity, mm.unit_price
FROM work_order_links l
JOIN vehicle_maintenances vm ON vm.id = l.entity_id AND vm.is_cancelled=0 AND vm.is_deleted=0
JOIN maintenance_materials mm ON mm.maintenance_id = vm.id
WHERE l.company_id=@c AND l.work_order_id=@w AND l.entity_type='vehicle_maintenance' AND l.is_deleted=0;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@w", workOrderId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) Ekle("Bakım Malzemesi", "TRY", D(r, 0) * D(r, 1));
        }
        return toplam.Select(kv => new CostCenterSummaryRow(workOrderId, "", kv.Key.Cat, kv.Key.Cur, kv.Value.Amt, kv.Value.N))
            .OrderBy(x => x.Category).ToList();
    }

    public IReadOnlyList<WorkOrderHistoryRow> History(SessionContext s, string workOrderId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        GetirSaltOkur(s, conn, workOrderId);
        var list = new List<WorkOrderHistoryRow>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT h.from_status, h.to_status, COALESCE(u.username,'—'), h.note, h.created_at " +
                          "FROM work_order_status_history h LEFT JOIN users u ON u.id = h.user_id " +
                          "WHERE h.company_id=@c AND h.work_order_id=@w ORDER BY h.created_at DESC;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@w", workOrderId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WorkOrderHistoryRow(N(r, 0), r.GetString(1), r.GetString(2), N(r, 3), r.GetInt64(4)));
        return list;
    }

    /// <summary>Excel (liste kuralı 2): filtrelenmiş TÜM liste.</summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<WorkOrderRow> rows)
        => new("İş Emirleri",
            new[] { "No", "Başlık", "Durum", "Öncelik", "Şantiye/Saha", "Sorumlu", "Plan", "Gerçekleşen", "Maliyet Merkezi" },
            rows.Select(w => (IReadOnlyList<object?>)new object?[]
                { w.WoNo, w.Title, w.StatusDisplay, w.PriorityDisplay, w.BranchDisplay, w.AssigneeDisplay,
                  w.PlannedDisplay, w.ActualDisplay, w.CostCenterDisplay }).ToList());

    // ══════════════ yardımcılar ══════════════

    private static bool Terminal(string s) => s is "completed" or "cancelled";

    private static void Dogrula(NewWorkOrder dto)
    {
        if (string.IsNullOrWhiteSpace(dto.WoNo)) throw new ArgumentException("İş emri no zorunlu.");
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new ArgumentException("Başlık zorunlu.");
        if (dto.PlannedStart is { } a && dto.PlannedEnd is { } b && b < a)
            throw new ArgumentException("Planlanan bitiş başlangıçtan önce olamaz.");
    }

    private void Alanlar(DbCommand cmd, SessionContext s, NewWorkOrder dto)
    {
        cmd.AddWithValue("@no", dto.WoNo.Trim());
        cmd.AddWithValue("@t", dto.Title.Trim());
        cmd.AddWithValue("@d", string.IsNullOrWhiteSpace(dto.Description) ? DBNull.Value : dto.Description!.Trim());
        cmd.AddWithValue("@pr", NormPriority(dto.Priority));
        cmd.AddWithValue("@b", Nv(dto.BranchId));
        cmd.AddWithValue("@cc", Nv(dto.CostCenterId));
        cmd.AddWithValue("@a", Nv(dto.AssigneePersonnelId));
        // PLAN tarihleri işlem tarihi DEĞİLDİR (Projeler emsali) — geri-tarih kapısına girmez;
        // gerçek başlangıç/bitiş (actual_*) ise SetStatus'ta iş günü kapısından geçer.
        cmd.AddWithValue("@ps", (object?)dto.PlannedStart ?? DBNull.Value);
        cmd.AddWithValue("@pe", (object?)dto.PlannedEnd ?? DBNull.Value);
    }

    /// <summary>Tenant + kapsam + (verilmişse) kilit. Dönüş: (status, branch_id, actual_start).</summary>
    private static (string Status, string? BranchId, long? ActualStart) Getir(SessionContext s, DbConnection conn,
        DbTransaction? tx, string id, long? expectedVersion)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT status, branch_id, actual_start, version FROM work_orders WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ArgumentException("İş emri bulunamadı.");
        var branchId = r.IsDBNull(1) ? null : r.GetString(1);
        if (branchId is not null) BranchAccess.Require(s, branchId, "iş emri");
        if (expectedVersion is { } ev && r.GetInt64(3) != ev) throw new ConcurrencyException(ev, r.GetInt64(3));
        return (r.GetString(0), branchId, r.IsDBNull(2) ? null : r.GetInt64(2));
    }

    private static void GetirSaltOkur(SessionContext s, DbConnection conn, string id)
        => Getir(s, conn, null, id, null);

    private static void EnsureRefs(SessionContext s, DbConnection conn, DbTransaction tx, NewWorkOrder dto)
    {
        void Var(string? id, string tablo, string ad)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT COUNT(*) FROM {tablo} WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", id!);
            cmd.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                throw new ArgumentException($"{ad} bulunamadı veya bu firmaya ait değil.");
        }
        Var(dto.BranchId, "branches", "Şantiye/Saha");
        Var(dto.CostCenterId, "cost_centers", "Maliyet merkezi");
        Var(dto.AssigneePersonnelId, "personnel", "Sorumlu personel");
        if (!string.IsNullOrWhiteSpace(dto.BranchId)) BranchAccess.Require(s, dto.BranchId, "iş emri");
    }

    private static void EnsureNoFree(DbConnection conn, DbTransaction tx, string companyId, string woNo, string? excludeId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM work_orders WHERE company_id=@c AND wo_no=@no AND is_deleted=0" +
                          (excludeId is null ? ";" : " AND id<>@id;");
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@no", woNo.Trim());
        if (excludeId is not null) cmd.AddWithValue("@id", excludeId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) > 0)
            throw new ArgumentException($"'{woNo.Trim()}' iş emri numarası zaten kullanılıyor.");
    }

    private static void Bagla(DbConnection conn, DbTransaction tx, string companyId, string workOrderId,
        string entityType, string entityId, long now)
    {
        using var chk = conn.CreateCommand();
        chk.Transaction = tx;
        chk.CommandText = "SELECT COUNT(*) FROM work_order_links WHERE company_id=@c AND entity_type=@t AND entity_id=@e;";
        chk.AddWithValue("@c", companyId);
        chk.AddWithValue("@t", entityType);
        chk.AddWithValue("@e", entityId);
        if (Convert.ToInt64(chk.ExecuteScalar()) > 0) return;   // bir kayıt tek iş emrine (UNIQUE) — sessiz
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO work_order_links(id, work_order_id, company_id, entity_type, entity_id, " +
            "created_at, updated_at, version, is_deleted) VALUES(@id,@w,@c,@t,@e,@now,@now,1,0);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@w", workOrderId);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@t", entityType);
        cmd.AddWithValue("@e", entityId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private void Gecmis(DbConnection conn, DbTransaction tx, SessionContext s, string workOrderId,
        string? from, string to, string? note, long now)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO work_order_status_history(id, work_order_id, company_id, from_status, to_status, " +
            "user_id, note, created_at, updated_at, version, is_deleted) VALUES(@id,@w,@c,@f,@t,@u,@n,@now,@now,1,0);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@w", workOrderId);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@f", (object?)from ?? DBNull.Value);
        cmd.AddWithValue("@t", to);
        cmd.AddWithValue("@u", s.UserId);
        cmd.AddWithValue("@n", string.IsNullOrWhiteSpace(note) ? DBNull.Value : note!.Trim());
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static string? N(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static long? L(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);
    private static object Nv(string? v) => string.IsNullOrWhiteSpace(v) ? DBNull.Value : v!.Trim();
}
