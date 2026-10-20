using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Maintenance;

/// <summary>Ekipman muayene/belge girdisi — araç tarafındaki <see cref="NewInspection"/> karşılığı.</summary>
public sealed record NewEquipmentInspection(string EquipmentId, string DocType, long? LastDate, long? NextDate,
    string? Result = null, string? Place = null, string? Note = null,
    // ⭐ M-41 (F-116): yasal periyodik kontrol — kontrolü yapan yetkili kişi/firma + rapor numarası.
    string? Inspector = null, string? ReportNo = null);

/// <summary>Ekipman muayene satırı — araç tarafındaki <see cref="InspectionRow"/> karşılığı.
/// Durum eşiği ve belge tipi metinleri araç tarafıyla AYNI kaynaktan gelir (ikinci kural kümesi yok).</summary>
public sealed record EquipmentInspectionRow(string EquipmentCode, string EquipmentName, string DocType,
    long? LastDate, long? NextDate, DateAlertLevel Level, string? Result, string? Place, string? Note,
    string Id = "", string EquipmentId = "")
{
    private static string D(long? ms) => ms is null ? "—"
        : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime.ToString("dd.MM.yyyy");
    public string EquipmentText => string.IsNullOrEmpty(EquipmentName) ? EquipmentCode : $"{EquipmentCode} - {EquipmentName}";
    public string DocTypeText => DepoWise.Application.Ui.InspectionDocTypes.Label(DocType);   // M-41: tek kaynak
    public string LastText => D(LastDate);
    public string NextText => D(NextDate);
    public string StatusText => Level switch
    { DateAlertLevel.Expired => "Süresi geçti", DateAlertLevel.Approaching => "Yaklaşıyor", _ => "Normal" };
}

/// <summary>
/// ═══ 7b — EKİPMAN MUAYENE/BELGE SERVİSİ (PK-F9, ADR-191) ═══
///
/// <see cref="InspectionService"/>'in ekipman karşılığı. Araç servisi HİÇ değiştirilmedi.
/// Belge tipi kümesi (<c>inspection|insurance|kasko|calibration</c>) ve yaklaşma eşiği
/// (<see cref="InspectionService.ApproachingDays"/>) araç tarafıyla AYNI kaynaktan gelir —
/// ikinci bir kural kümesi tanımlanmaz.
///
/// Yetki modülü <c>inspection</c> (mevcut modül; yeni yetki modülü YOK).
/// Ekipman sahipliği serviste doğrulanır (IDOR) — masaüstü bu servisi çevrimdışı da çağırır.
/// </summary>
public sealed class EquipmentInspectionService
{
    private const string Module = "inspection";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public EquipmentInspectionService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Save(SessionContext s, NewEquipmentInspection dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (!DepoWise.Application.Ui.InspectionDocTypes.IsValid(dto.DocType))
            throw new ArgumentException("Geçersiz belge tipi.");
        // ⭐ M-41: periyodik kontrolde yetkili kişi/firma ZORUNLU (mevzuat kanıtı) ve sonraki kontrol tarihi gerekli (uyarı).
        if (dto.DocType == DepoWise.Application.Ui.InspectionDocTypes.Periodic)
        {
            if (string.IsNullOrWhiteSpace(dto.Inspector)) throw new ArgumentException("Periyodik kontrolde kontrolü yapan yetkili kişi/firma zorunludur.");
            if (dto.NextDate is null) throw new ArgumentException("Periyodik kontrolde sonraki kontrol tarihi zorunludur (uyarı bu tarihe göre çıkar).");
        }

        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Araç tarafındaki B-2 korumasının aynısı: istemciden gelen kimlik firmaya ait olmalı.
        EquipmentMaintenanceService.EnsureEquipmentOwned(conn, tx, s.CompanyId, dto.EquipmentId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO equipment_inspections(id, company_id, equipment_id, doc_type, last_date, next_date,
    result, place, note, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@e,@dt,@ld,@nd,@res,@pl,@note,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@e", dto.EquipmentId);
            cmd.AddWithValue("@dt", dto.DocType);
            cmd.AddWithValue("@ld", (object?)dto.LastDate ?? DBNull.Value);
            cmd.AddWithValue("@nd", (object?)dto.NextDate ?? DBNull.Value);
            cmd.AddWithValue("@res", (object?)dto.Result ?? DBNull.Value);
            cmd.AddWithValue("@pl", (object?)dto.Place ?? DBNull.Value);
            cmd.AddWithValue("@note", (object?)dto.Note ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        // ⭐ M-41: yasal periyodik kontrol kanıtı — yetkili kişi/firma + rapor no (kolon yoksa yalnız boşsa geçer).
        if (!string.IsNullOrWhiteSpace(dto.Inspector) || !string.IsNullOrWhiteSpace(dto.ReportNo))
        {
            if (!DbIntrospect.ColumnExists(conn, tx, "equipment_inspections", "inspector"))
                throw new InvalidOperationException("Veritabanı periyodik kontrol alanlarını henüz desteklemiyor (güncelleme bekleniyor).");
            using var pk = conn.CreateCommand();
            pk.Transaction = tx;
            pk.CommandText = "UPDATE equipment_inspections SET inspector=@i, report_no=@r WHERE id=@id AND company_id=@c;";
            pk.AddWithValue("@i", string.IsNullOrWhiteSpace(dto.Inspector) ? DBNull.Value : dto.Inspector!.Trim());
            pk.AddWithValue("@r", string.IsNullOrWhiteSpace(dto.ReportNo) ? DBNull.Value : dto.ReportNo!.Trim());
            pk.AddWithValue("@id", id);
            pk.AddWithValue("@c", s.CompanyId);
            pk.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment_inspection", id,
            AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Ekipman muayene/belge kayıtları (salt okuma).</summary>
    public IReadOnlyList<EquipmentInspectionRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
        var yaklasan = nowMs + (long)TimeSpan.FromDays(InspectionService.ApproachingDays).TotalMilliseconds;

        var list = new List<EquipmentInspectionRow>();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT e.code, e.name, i.doc_type, i.last_date, i.next_date, i.result, i.place, i.note, i.id, i.equipment_id
FROM equipment_inspections i
JOIN equipment e ON e.id = i.equipment_id
WHERE i.company_id=@c AND i.is_deleted=0
ORDER BY i.next_date;";
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long? next = r.IsDBNull(4) ? null : Convert.ToInt64(r.GetValue(4));
            var level = next is null ? DateAlertLevel.Normal
                : next < nowMs ? DateAlertLevel.Expired
                : next <= yaklasan ? DateAlertLevel.Approaching
                : DateAlertLevel.Normal;
            list.Add(new EquipmentInspectionRow(
                r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : Convert.ToInt64(r.GetValue(3)), next, level,
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.GetString(8), r.GetString(9)));
        }
        return list;
    }

    /// <summary>Yumuşak silme (fiziksel silme yok — proje geneli kural).</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE equipment_inspections SET is_deleted=1, version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
                throw new ForbiddenException("Kayıt bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "equipment_inspection", id,
            AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }
}
