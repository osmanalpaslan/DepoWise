using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Maintenance;

public sealed record NewInspection(string VehicleId, string DocType, long? LastDate, long? NextDate,
    string? Result = null, string? Place = null, string? Note = null);

public enum DateAlertLevel { Normal, Approaching, Expired }

public sealed record InspectionAlert(string VehicleId, string DocType, long? NextDate, DateAlertLevel Level);

/// <param name="Id">B-5: kaydın kimliği — iptal edebilmek için gerekli. Liste bunu döndürmüyordu,
/// bu yüzden hiçbir arayüz belirli bir belgeyi hedefleyemiyordu. Sona eklendi → geriye uyumlu.</param>
/// <param name="Version">B-5: DÜZENLEME KİLİDİ jetonu; iptal ederken geri gönderilir. 0 = bilinmiyor → kontrol yok.</param>
public sealed record InspectionRow(string VehicleCode, string Plate, string DocType,
    long? LastDate, long? NextDate, string Place, string Result, DateAlertLevel Level,
    string Id = "", long Version = 0)
{
    private static string D(long? ms) => ms is null ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime.ToString("dd.MM.yyyy");
    public string VehicleText => string.IsNullOrEmpty(Plate) ? VehicleCode : $"{VehicleCode} - {Plate}";
    public string DocTypeText => DocType switch { "inspection" => "Muayene", "insurance" => "Sigorta", "kasko" => "Kasko", "calibration" => "Kalibrasyon", _ => DocType };
    public string LastText => D(LastDate);
    public string NextText => D(NextDate);
    public string StatusText => Level switch { DateAlertLevel.Expired => "Süresi geçti", DateAlertLevel.Approaching => "Yaklaşıyor", _ => "Normal" };
}

/// <summary>Muayene/sigorta/kasko/kalibrasyon belgeleri + tarih bazlı uyarı (yaklaşan/geçmiş).</summary>
public sealed class InspectionService
{
    private const string Module = "inspection";
    public const int ApproachingDays = 30;
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public InspectionService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public string Save(SessionContext s, NewInspection dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (dto.DocType is not ("inspection" or "insurance" or "kasko" or "calibration"))
            throw new ArgumentException("Geçersiz belge tipi.");
        var id = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        // B-2 (PRT-01 Grup 5, 2026-08-10): araç id'si İSTEMCİDEN gelir → firmaya ait olduğu doğrulanır.
        // Eskiden hiç kontrol yoktu: başka firmanın araç id'siyle muayene kaydı oluşturulabiliyordu
        // (satır doğru company_id alıyordu ama yabancı araca REFERANS veriyordu). Aynı korumanın emsali:
        // MaintenanceService:85 ve MaintenanceDefinitionService:71,192 ("yabancı araç bağlanamaz").
        EnsureVehicleOwned(conn, tx, s.CompanyId, dto.VehicleId);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO vehicle_inspections(id, company_id, vehicle_id, doc_type, last_date, next_date, result, place, note,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@v,@dt,@ld,@nd,@res,@pl,@note,@now,@now,1,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@v", dto.VehicleId);
            cmd.AddWithValue("@dt", dto.DocType);
            cmd.AddWithValue("@ld", (object?)dto.LastDate ?? DBNull.Value);
            cmd.AddWithValue("@nd", (object?)dto.NextDate ?? DBNull.Value);
            cmd.AddWithValue("@res", (object?)dto.Result ?? DBNull.Value);
            cmd.AddWithValue("@pl", (object?)dto.Place ?? DBNull.Value);
            cmd.AddWithValue("@note", (object?)dto.Note ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_inspection", id, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>Muayene/Sigorta kayıtları (salt okuma) — araç + belge tipi + tarihler + durum.</summary>
    public IReadOnlyList<InspectionRow> List(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var nowMs = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT v.internal_code, COALESCE(v.plate,''), vi.doc_type, vi.last_date, vi.next_date,
       COALESCE(vi.place,''), COALESCE(vi.result,''), vi.id, vi.version
FROM vehicle_inspections vi JOIN vehicles v ON v.id = vi.vehicle_id AND v.company_id = vi.company_id
WHERE vi.company_id=@c AND vi.is_deleted=0
ORDER BY (vi.next_date IS NULL), vi.next_date;";
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<InspectionRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long? next = r.IsDBNull(4) ? null : r.GetInt64(4);
            var level = next is null ? DateAlertLevel.Normal
                : next.Value < nowMs ? DateAlertLevel.Expired
                : next.Value - nowMs <= (long)ApproachingDays * 86_400_000 ? DateAlertLevel.Approaching
                : DateAlertLevel.Normal;
            list.Add(new InspectionRow(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3), next, r.GetString(5), r.GetString(6), level,
                r.GetString(7), r.GetInt64(8)));   // B-5: iptal için id + düzenleme kilidi jetonu
        }
        return list;
    }

    public IReadOnlyList<InspectionAlert> GetAlerts(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var now = _clock.UtcNow;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // Her (araç,tip) için en güncel belge
        cmd.CommandText = @"
SELECT vehicle_id, doc_type, next_date FROM vehicle_inspections vi
WHERE company_id=@c AND is_deleted=0 AND next_date IS NOT NULL
AND created_at = (SELECT MAX(created_at) FROM vehicle_inspections x
                  WHERE x.vehicle_id=vi.vehicle_id AND x.doc_type=vi.doc_type AND x.is_deleted=0);";
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<InspectionAlert>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var nextDate = r.GetInt64(2);
            var next = DateTimeOffset.FromUnixTimeMilliseconds(nextDate);
            var days = (next - now).TotalDays;
            var level = days < 0 ? DateAlertLevel.Expired
                : days <= ApproachingDays ? DateAlertLevel.Approaching
                : DateAlertLevel.Normal;
            list.Add(new InspectionAlert(r.GetString(0), r.GetString(1), nextDate, level));
        }
        return list;
    }

    /// <summary>
    /// B-5 (PRT-01 Grup 5, 2026-08-11) — muayene/sigorta belgesi İPTALİ (kullanıcı kararı: SEÇENEK B).
    ///
    /// Fiziksel silme veya geçmişi kaybettiren UPDATE YOKTUR: kayıt <c>is_deleted=1</c> ile iptal edilir,
    /// satır veritabanında KALIR (CLAUDE.md §4 "operasyonel kaydı fiziksel silme"). Gerekçe ZORUNLUDUR ve
    /// denetim kaydına yazılır — <c>vehicle_inspections</c>'ta gerekçe kolonu yoktur ve yalnız bunun için
    /// migration açılmadı; yakıt iptalinin (Grup 3) birebir aynı deseni kullanıldı.
    ///
    /// Kolonlar Migration008'de ZATEN mevcut (<c>is_deleted</c>, <c>version</c>) → MIGRATION GEREKMEZ.
    /// </summary>
    /// <param name="expectedVersion">DÜZENLEME KİLİDİ: ekranın açıldığı andaki <c>version</c>. Verilirse ve
    /// kayıt o andan beri değiştiyse <see cref="ConcurrencyException"/> atılır. null = kontrol yok.</param>
    public void Cancel(SessionContext s, string id, string reason, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        // Tenant + "zaten iptal edilmiş mi" kontrolü tek okumada (transaction içinde).
        bool alreadyCancelled;
        using (var chk = conn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT is_deleted FROM vehicle_inspections WHERE id=@id AND company_id=@c;";
            chk.AddWithValue("@id", id);
            chk.AddWithValue("@c", s.CompanyId);
            var found = chk.ExecuteScalar();
            if (found is null) throw new ForbiddenException("Belge bulunamadı veya başka firmaya ait.");
            alreadyCancelled = Convert.ToInt64(found) != 0;
        }
        if (alreadyCancelled) throw new InvalidOperationException("Bu belge zaten iptal edilmiş.");

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE vehicle_inspections SET is_deleted=1, version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0" + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "vehicle_inspections", id, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Belge bulunamadı veya başka firmaya ait.");
            }
        }

        // Gerekçe denetim kaydında saklanır (yakıt iptali deseni). Geçmiş HİÇ silinmez.
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_inspection", id, AuditActions.Reverse,
            s.UserId, AfterJson: $"{{\"reason\":\"{Escape(reason)}\"}}"), _clock);
        tx.Commit();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>B-2: araç bu firmaya ait mi? (MaintenanceService/MaintenanceDefinitionService ile aynı desen.)</summary>
    private static void EnsureVehicleOwned(DbConnection conn, DbTransaction? tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
    }
}
