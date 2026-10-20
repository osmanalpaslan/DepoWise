using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Equipment;

/// <summary>⭐ M-48: ekipmanın bir taşıma hareketi (konum geçmişi satırı).</summary>
public sealed record EkipmanTasima(string Id, string? KaynakAdi, string? HedefAdi, string? KaynakKonum, string? HedefKonum,
    long Tarih, string? Not, string? Kullanici)
{
    public string Satir => $"{DateTimeOffset.FromUnixTimeMilliseconds(Tarih).UtcDateTime:dd.MM.yyyy} · {KaynakAdi ?? "—"}" +
                           (string.IsNullOrWhiteSpace(KaynakKonum) ? "" : $" ({KaynakKonum})") + $" → {HedefAdi ?? "—"}" +
                           (string.IsNullOrWhiteSpace(HedefKonum) ? "" : $" ({HedefKonum})") +
                           (string.IsNullOrWhiteSpace(Not) ? "" : $" · {Not}") + (Kullanici is null ? "" : $" · {Kullanici}");
}

/// <summary>
/// ═══ M-48 (AR-GE 2026-10-19, 05 F-058) — EKİPMAN TAŞIMA HAREKETİ + KONUM GEÇMİŞİ ═══
/// Taşıma artık kartın şube alanını elle düzeltmek değil, ayrı bir HAREKETTİR: tek işlemde kartın şube/konumu güncellenir ve
/// değişmez geçmiş satırı yazılır (kimden → kime, iş günü, not, kullanıcı). Tekrar gönderim (aynı işlem kimliği) zararsız.
/// Kaynak ve hedef şube kullanıcının kapsamında olmalı (<see cref="BranchAccess"/>); zimmetteki ekipman taşınabilir (zimmet
/// kişiye, konum şantiyeye aittir) ama aynı yere taşıma reddedilir.
/// </summary>
public sealed class EkipmanTasimaService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public EkipmanTasimaService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static bool Destekli(System.Data.Common.DbConnection conn) => DbIntrospect.TableExists(conn, null, "equipment_moves");

    public void Tasi(SessionContext s, string equipmentId, string? hedefSube, string? hedefKonum, long? tarih, string? not, string operationId)
    {
        AccessControl.Require(s, EquipmentService.Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        hedefSube = string.IsNullOrWhiteSpace(hedefSube) ? null : hedefSube;
        hedefKonum = string.IsNullOrWhiteSpace(hedefKonum) ? null : hedefKonum.Trim();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var gun = DateEntryPolicy.Uygula(s, tarih) ?? now;
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı ekipman taşıma geçmişini henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(*) FROM equipment_moves WHERE company_id=@c AND operation_id=@op;";
            chk.AddWithValue("@c", s.CompanyId); chk.AddWithValue("@op", operationId);
            if (Convert.ToInt64(chk.ExecuteScalar()) > 0) { tx.Commit(); return; }   // tekrar gönderim
        }
        string? kaynak, kaynakKonum;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT branch_id, location FROM equipment WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@id", equipmentId); cmd.AddWithValue("@c", s.CompanyId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new ForbiddenException("Ekipman bulunamadı veya başka firmaya ait.");
            kaynak = r.IsDBNull(0) ? null : r.GetString(0);
            kaynakKonum = r.IsDBNull(1) ? null : r.GetString(1);
        }
        BranchAccess.Require(s, kaynak, "ekipman taşıma (kaynak)");
        if (hedefSube is not null)
        {
            BranchAccess.Require(s, hedefSube, "ekipman taşıma (hedef)");
            using var b = conn.CreateCommand();
            b.Transaction = tx;
            b.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@b AND company_id=@c AND is_deleted=0;";
            b.AddWithValue("@b", hedefSube); b.AddWithValue("@c", s.CompanyId);
            if (Convert.ToInt64(b.ExecuteScalar()) == 0) throw new ArgumentException("Hedef şube/şantiye bulunamadı veya bu firmaya ait değil.");
        }
        if (kaynak == hedefSube && string.Equals(kaynakKonum ?? "", hedefKonum ?? "", StringComparison.Ordinal))
            throw new ArgumentException("Ekipman zaten bu konumda.");
        using (var u = conn.CreateCommand())
        {
            u.Transaction = tx;
            u.CommandText = "UPDATE equipment SET branch_id=@b, location=@l, updated_at=@n, version=version+1 WHERE id=@id AND company_id=@c;";
            u.AddWithValue("@b", (object?)hedefSube ?? DBNull.Value); u.AddWithValue("@l", (object?)hedefKonum ?? DBNull.Value);
            u.AddWithValue("@n", now); u.AddWithValue("@id", equipmentId); u.AddWithValue("@c", s.CompanyId);
            u.ExecuteNonQuery();
        }
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO equipment_moves(id, company_id, equipment_id, from_branch_id, to_branch_id, from_location, to_location, move_date, note, operation_id, created_by, created_at) " +
                              "VALUES(@id,@c,@e,@fb,@tb,@fl,@tl,@d,@note,@op,@u,@n);";
            ins.AddWithValue("@id", Guid.NewGuid().ToString("N")); ins.AddWithValue("@c", s.CompanyId); ins.AddWithValue("@e", equipmentId);
            ins.AddWithValue("@fb", (object?)kaynak ?? DBNull.Value); ins.AddWithValue("@tb", (object?)hedefSube ?? DBNull.Value);
            ins.AddWithValue("@fl", (object?)kaynakKonum ?? DBNull.Value); ins.AddWithValue("@tl", (object?)hedefKonum ?? DBNull.Value);
            ins.AddWithValue("@d", gun); ins.AddWithValue("@note", string.IsNullOrWhiteSpace(not) ? DBNull.Value : not!.Trim());
            ins.AddWithValue("@op", operationId); ins.AddWithValue("@u", s.UserId); ins.AddWithValue("@n", now);
            ins.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment", equipmentId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"tasima\":{{\"from\":\"{kaynak}\",\"to\":\"{hedefSube}\"}}}}"), _clock);
        tx.Commit();
    }

    /// <summary>Konum geçmişi (en yeni üstte).</summary>
    public IReadOnlyList<EkipmanTasima> Gecmis(SessionContext s, string equipmentId)
    {
        AccessControl.Require(s, EquipmentService.Module, PermissionAction.View);
        using var conn = _factory.Create();
        if (!Destekli(conn)) return Array.Empty<EkipmanTasima>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT m.id, bf.name, bt.name, m.from_location, m.to_location, m.move_date, m.note, u.username FROM equipment_moves m " +
                          "LEFT JOIN branches bf ON bf.id=m.from_branch_id AND bf.company_id=m.company_id " +
                          "LEFT JOIN branches bt ON bt.id=m.to_branch_id AND bt.company_id=m.company_id " +
                          "LEFT JOIN users u ON u.id=m.created_by " +
                          "WHERE m.company_id=@c AND m.equipment_id=@e ORDER BY m.move_date DESC, m.created_at DESC;";
        cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@e", equipmentId);
        var list = new List<EkipmanTasima>();
        using var r = cmd.ExecuteReader();
        string? N(int i) => r.IsDBNull(i) ? null : r.GetString(i);
        while (r.Read()) list.Add(new EkipmanTasima(r.GetString(0), N(1), N(2), N(3), N(4), Convert.ToInt64(r.GetValue(5)), N(6), N(7)));
        return list;
    }
}
