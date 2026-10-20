using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.WorkOrders;

/// <summary>⭐ M-35: aracın açık (kapanmamış) arıza iş emri.</summary>
public sealed record AcikAriza(string WorkOrderId, string WoNo, string Status, long ReportedAt);

/// <summary>
/// ═══ M-35 (AR-GE 2026-10-18, 05 F-077) — ARIZA BİLDİR → İŞ EMRİ; UYARIDAN İŞ BAŞLAT ═══
/// Araç kartından (ya da bakım uyarısından) TEK adımda taslak iş emri açılır: araç iş emrine atanır, arıza bildiriminde araç
/// "Arızalı" olur ve bildirim anı saklanır (<c>work_orders.fault_vehicle_id</c> + <c>created_at</c>) → iş emri tamamlanınca
/// arıza süresi ölçülür ve araç (hâlâ Arızalı ise) yeniden "Aktif" olur. Aynı araç için açık arıza iş emri varken ikincisi
/// açılmaz (saha talebi kaybolmaz, çift iş de doğmaz). Tek işlem; iş emri kuralları (numara, kapsam, geçmiş) aynen.
/// </summary>
public sealed partial class WorkOrderService
{
    private static bool ArizaDestekli(DbConnection conn, DbTransaction? tx)
        => DbIntrospect.ColumnExists(conn, tx, "work_orders", "fault_vehicle_id");

    /// <summary>Aracın açık arıza iş emri (yoksa ya da kolon yoksa null).</summary>
    public AcikAriza? AcikArizasi(SessionContext s, string vehicleId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        return AcikArizaOku(conn, null, s.CompanyId, vehicleId);
    }

    private static AcikAriza? AcikArizaOku(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        if (!ArizaDestekli(conn, tx)) return null;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, wo_no, status, created_at FROM work_orders WHERE company_id=@c AND fault_vehicle_id=@v " +
                          "AND is_deleted=0 AND status NOT IN ('completed','cancelled') ORDER BY created_at LIMIT 1;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", vehicleId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new AcikAriza(r.GetString(0), r.GetString(1), r.GetString(2), Convert.ToInt64(r.GetValue(3))) : null;
    }

    /// <summary>
    /// ARIZA BİLDİR (<paramref name="ariza"/>=true) ya da UYARIDAN İŞ EMRİ (false): taslak iş emri + araç ataması tek işlemde.
    /// Arızada araç "Arızalı" yapılır ve açıklama durum notuna yazılır. Dönüş: (iş emri kimliği, numarası).
    /// </summary>
    public (string Id, string WoNo) AractanIsEmri(SessionContext s, string vehicleId, string aciklama, bool ariza, string? priority = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(aciklama)) throw new ArgumentException(ariza ? "Arıza açıklaması zorunlu." : "İş açıklaması zorunlu.");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!ArizaDestekli(conn, null))
            throw new InvalidOperationException("Veritabanı araçtan iş emri açmayı henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        string kod, durum; string? sube;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT internal_code, plate, status, branch_id FROM vehicles WHERE id=@v AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@v", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
            kod = r.GetString(0) + (r.IsDBNull(1) || r.GetString(1).Length == 0 ? "" : " - " + r.GetString(1));
            durum = r.GetString(2);
            sube = r.IsDBNull(3) ? null : r.GetString(3);
        }
        if (ariza && AcikArizaOku(conn, tx, s.CompanyId, vehicleId) is { } acik)
            throw new InvalidOperationException($"Bu araç için açık arıza iş emri var ({acik.WoNo}). Yeni arıza yerine o iş emrine not ekleyin.");
        if (sube is not null && !BranchAccess.CanAccess(s, sube)) sube = null;   // kapsam dışı şubeye yazma — iş emri şubesiz açılır
        var dto = new NewWorkOrder("", (ariza ? "Arıza — " : "Bakım/İş — ") + kod, aciklama.Trim(),
            string.IsNullOrWhiteSpace(priority) ? (ariza ? "high" : "normal") : priority, sube);
        dto = dto with { WoNo = BelgeNumarasi.Sonraki(conn, tx, "work_orders", "wo_no", s.CompanyId,
            $"IE-{DateTimeOffset.FromUnixTimeMilliseconds(now).Year}-", s.MachineCode) };
        Dogrula(dto, numaraZorunlu: false);
        EnsureRefs(s, conn, tx, dto);
        EnsureNoFree(conn, tx, s.CompanyId, dto.WoNo, null);
        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            // Create ile AYNI kolonlar (+ arıza bağı). İş emri kuralları değişmez.
            cmd.CommandText = @"
INSERT INTO work_orders(id, company_id, wo_no, title, description, status, priority, branch_id,
    cost_center_id, assignee_personnel_id, planned_start, planned_end, closing_note, created_by,
    created_at, updated_at, version, is_deleted, fault_vehicle_id)
VALUES(@id,@c,@no,@t,@d,'draft',@pr,@b,@cc,@a,@ps,@pe,NULL,@u,@now,@now,1,0,@fv);";
            Alanlar(cmd, s, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@u", s.UserId);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@fv", ariza ? vehicleId : DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO work_order_assignments(id, work_order_id, company_id, resource_type, resource_id, note, " +
                "created_at, updated_at, version, is_deleted) VALUES(@id,@w,@c,'vehicle',@r,NULL,@now,@now,1,0);";
            cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.AddWithValue("@w", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@r", vehicleId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        if (ariza && durum != DepoWise.Application.Ui.VehicleStatus.Faulty)
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE vehicles SET status=@st, status_note=@n, updated_at=@now, version=version+1 WHERE id=@v AND company_id=@c;";
                cmd.AddWithValue("@st", DepoWise.Application.Ui.VehicleStatus.Faulty);
                cmd.AddWithValue("@n", $"{dto.WoNo}: {aciklama.Trim()}");
                cmd.AddWithValue("@now", now);
                cmd.AddWithValue("@v", vehicleId);
                cmd.AddWithValue("@c", s.CompanyId);
                cmd.ExecuteNonQuery();
            }
        Gecmis(conn, tx, s, id, null, "draft", ariza ? "Arıza bildirimi" : "Uyarıdan iş emri", now);
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "work_order", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"woNo\":\"{dto.WoNo}\",\"vehicle\":\"{vehicleId}\",\"ariza\":{(ariza ? "true" : "false")}}}"), _clock);
        tx.Commit();
        return (id, dto.WoNo);
    }

    /// <summary>Tamamlanan arıza iş emri: araç hâlâ "Arızalı" ise "Aktif" yapılır (başka bir durum seçilmişse dokunulmaz).
    /// <see cref="SetStatus"/> içinden, aynı işlemde çağrılır.</summary>
    private void ArizaKapandi(DbConnection conn, DbTransaction tx, SessionContext s, string workOrderId, long now)
    {
        if (!ArizaDestekli(conn, tx)) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE vehicles SET status=@a, status_note=NULL, updated_at=@now, version=version+1 " +
                          "WHERE company_id=@c AND status=@f AND id=(SELECT fault_vehicle_id FROM work_orders WHERE id=@w AND company_id=@c) " +
                          "AND NOT EXISTS (SELECT 1 FROM work_orders o WHERE o.company_id=@c AND o.fault_vehicle_id=vehicles.id AND o.id<>@w " +
                          "AND o.is_deleted=0 AND o.status NOT IN ('completed','cancelled'));";
        cmd.AddWithValue("@a", DepoWise.Application.Ui.VehicleStatus.Active);
        cmd.AddWithValue("@f", DepoWise.Application.Ui.VehicleStatus.Faulty);
        cmd.AddWithValue("@now", now);
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@w", workOrderId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Arıza süresi (saat): bildirimden tamamlanmaya. Açık/arızasız iş emrinde null.</summary>
    public decimal? ArizaSuresiSaat(SessionContext s, string workOrderId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        if (!ArizaDestekli(conn, null)) return null;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT created_at, actual_end FROM work_orders WHERE id=@w AND company_id=@c AND fault_vehicle_id IS NOT NULL;";
        cmd.AddWithValue("@w", workOrderId);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(1)) return null;
        return Math.Round((Convert.ToInt64(r.GetValue(1)) - Convert.ToInt64(r.GetValue(0))) / 3_600_000m, 1);
    }
}
