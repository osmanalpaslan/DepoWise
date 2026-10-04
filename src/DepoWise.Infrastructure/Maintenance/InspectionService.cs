using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Maintenance;

public sealed record NewInspection(string VehicleId, string DocType, long? LastDate, long? NextDate,
    string? Result = null, string? Place = null, string? Note = null);

public enum DateAlertLevel { Normal, Approaching, Expired }

public sealed record InspectionAlert(string VehicleId, string DocType, long? NextDate, DateAlertLevel Level,
    // ⭐ 2026-10-04: uyarı neden sürüyor / ne yapılmalı — bilgilendirme notu (null = özel durum yok).
    string? Note = null);

/// <param name="Id">B-5: kaydın kimliği — iptal edebilmek için gerekli. Liste bunu döndürmüyordu,
/// bu yüzden hiçbir arayüz belirli bir belgeyi hedefleyemiyordu. Sona eklendi → geriye uyumlu.</param>
/// <param name="Version">B-5: DÜZENLEME KİLİDİ jetonu; iptal ederken geri gönderilir. 0 = bilinmiyor → kontrol yok.</param>
public sealed record InspectionRow(string VehicleCode, string Plate, string DocType,
    long? LastDate, long? NextDate, string Place, string Result, DateAlertLevel Level,
    string Id = "", long Version = 0,
    // ⭐ 2026-10-04 (kullanıcı bildirimi: "yeni muayene girsem de eski uyarı silinmiyor"): aynı araç+tür için
    // daha geç biten bir belge varsa bu belge YENİLENMİŞTİR — listede artık "Süresi geçti" diye görünmez.
    bool Superseded = false)
{
    private static string D(long? ms) => ms is null ? "—" : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime.ToString("dd.MM.yyyy");
    public string VehicleText => string.IsNullOrEmpty(Plate) ? VehicleCode : $"{VehicleCode} - {Plate}";
    public string DocTypeText => DocType switch { "inspection" => "Muayene", "insurance" => "Sigorta", "kasko" => "Kasko", "calibration" => "Kalibrasyon", _ => DocType };
    public string LastText => D(LastDate);
    public string NextText => D(NextDate);
    public string StatusText => Superseded ? "Yenilendi"
        : Level switch { DateAlertLevel.Expired => "Süresi geçti", DateAlertLevel.Approaching => "Yaklaşıyor", _ => "Normal" };
}

/// <summary>Muayene/sigorta/kasko/kalibrasyon belgeleri + tarih bazlı uyarı (yaklaşan/geçmiş).</summary>
public sealed class InspectionService
{

    /// <summary>
    /// ⭐ PRT-02 (FAZ G, 2026-09-04) — MUAYENE LİSTESİNİN EXCEL TABLOSU.
    /// Projenin liste ekranı kuralı gereği (bkz. `.claude/rules/list-screens.md` Kural 2) her liste
    /// ekranı filtrelenmiş TÜM sonucu Excel'e aktarabilmelidir; muayene ekranı bunun dışında kalmıştı.
    /// Kolonlar ekrandakiyle aynı sırada.
    /// </summary>
    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<InspectionRow> rows)
        => new("Muayene / Sigorta", new[] { "Araç", "Belge Türü", "Son İşlem", "Sonraki", "Yer", "Sonuç", "Durum" },
            rows.Select(r => (IReadOnlyList<object?>)new object?[]
            {
                r.VehicleText, r.DocTypeText, r.LastText, r.NextText,
                string.IsNullOrWhiteSpace(r.Place) ? "—" : r.Place,
                string.IsNullOrWhiteSpace(r.Result) ? "—" : r.Result,
                r.StatusText,
            }).ToList());
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
       COALESCE(vi.place,''), COALESCE(vi.result,''), vi.id, vi.version, vi.vehicle_id, vi.created_at
FROM vehicle_inspections vi JOIN vehicles v ON v.id = vi.vehicle_id AND v.company_id = vi.company_id
WHERE vi.company_id=@c AND vi.is_deleted=0
ORDER BY (vi.next_date IS NULL), vi.next_date;";
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<InspectionRow>();
        var grup = new List<(string Key, long? Next, long Created)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long? next = r.IsDBNull(4) ? null : r.GetInt64(4);
            grup.Add((r.GetString(9) + "|" + r.GetString(2), next, r.GetInt64(10)));
            var level = next is null ? DateAlertLevel.Normal
                : next.Value < nowMs ? DateAlertLevel.Expired
                : next.Value - nowMs <= (long)ApproachingDays * 86_400_000 ? DateAlertLevel.Approaching
                : DateAlertLevel.Normal;
            list.Add(new InspectionRow(r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3), next, r.GetString(5), r.GetString(6), level,
                r.GetString(7), r.GetInt64(8)));   // B-5: iptal için id + düzenleme kilidi jetonu
        }
        // Uyarıyla AYNI kural (GetAlerts): araç+tür başına esas belge = bitişi en geç olan (eşitlikte son girilen).
        var esas = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < grup.Count; i++)
        {
            if (grup[i].Next is null) continue;
            if (!esas.TryGetValue(grup[i].Key, out var j)
                || grup[i].Next > grup[j].Next
                || (grup[i].Next == grup[j].Next && grup[i].Created > grup[j].Created))
                esas[grup[i].Key] = i;
        }
        for (int i = 0; i < list.Count; i++)
            if (esas.TryGetValue(grup[i].Key, out var j) && j != i)
                list[i] = list[i] with { Superseded = true, Level = DateAlertLevel.Normal };
        return list;
    }

    /// <summary>
    /// ⭐ 2026-10-04 — uyarı bilgilendirme notu: yeni belge girildiği hâlde uyarının neden sürdüğünü ve yapılacak
    /// işi söyler. Seviyeyi değiştirmez.
    /// </summary>
    internal static string? AlertNote(DateAlertLevel level, long nextDate, long createdAt, bool lastDiffers,
        long? lastNext, long? lastLast, long nowMs)
    {
        static string T(long? ms) => ms is null ? "—"
            : DateTimeOffset.FromUnixTimeMilliseconds(ms.Value).LocalDateTime.ToString("dd.MM.yyyy");
        if (lastDiffers && lastNext is null)
            return $"En son girilen belgede (yapılış {T(lastLast)}) geçerlilik bitiş tarihi boş; uyarı önceki belgeye " +
                   $"(bitiş {T(nextDate)}) göre sürüyor. Yeni belgeye bitiş tarihini girin.";
        if (lastDiffers && lastNext is not null)
            return $"En son girilen belgenin bitişi ({T(lastNext)}) mevcut belgeden ({T(nextDate)}) daha erken olduğu için " +
                   "esas alınmadı. Tarihleri kontrol edin.";
        if (level == DateAlertLevel.Expired && nowMs - createdAt <= RecentDays * 86_400_000L)
            return $"Son girilen belgenin bitiş tarihi ({T(nextDate)}) geçmiş. Yeni belge girdiyseniz bitiş tarihini kontrol edin.";
        return null;
    }

    /// <summary>"Yakın zamanda girildi" penceresi (gün) — süresi geçmiş tarihle girilen yeni belgeyi yakalamak için.</summary>
    public const int RecentDays = 30;

    public IReadOnlyList<InspectionAlert> GetAlerts(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        var now = _clock.UtcNow;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // Her (araç,tip) için en güncel belge = GEÇERLİLİĞİ EN GEÇ biten belge (2026-10-01 kullanıcı bildirimi:
        // "yeni muayene girsem de eski uyarı silinmiyor"). Eskiden GİRİŞ zamanına (MAX created_at) göre
        // seçiliyordu: sonradan girilen/eşitlenen eski bir belge, yenisini gölgeleyip süresi dolmuş uyarıyı
        // canlı tutuyordu. Tarihsiz belge uyarı üretmez (eskisi gibi); created_at yalnız eşitlik bozucudur.
        // 2026-10-04: bilgilendirme notu için EN SON GİRİLEN belge de okunur (bitiş tarihsiz olsa bile).
        // NULL sıralaması lehçeye göre değişir (PG'de DESC'te NULL başa gelir) → CASE ile açıkça sona atılır.
        cmd.CommandText = @"
SELECT vehicle_id, doc_type, next_date, id, created_at, last_id, last_next, last_last FROM (
    SELECT vehicle_id, doc_type, next_date, id, created_at,
           ROW_NUMBER() OVER (PARTITION BY vehicle_id, doc_type
               ORDER BY CASE WHEN next_date IS NULL THEN 1 ELSE 0 END, next_date DESC, created_at DESC) AS rn,
           FIRST_VALUE(id) OVER (PARTITION BY vehicle_id, doc_type ORDER BY created_at DESC) AS last_id,
           FIRST_VALUE(next_date) OVER (PARTITION BY vehicle_id, doc_type ORDER BY created_at DESC) AS last_next,
           FIRST_VALUE(last_date) OVER (PARTITION BY vehicle_id, doc_type ORDER BY created_at DESC) AS last_last
    FROM vehicle_inspections
    WHERE company_id=@c AND is_deleted=0
) t WHERE rn = 1 AND next_date IS NOT NULL;";
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
            string? note = null;
            if (level != DateAlertLevel.Normal)
            {
                var id = r.GetString(3);
                var created = r.GetInt64(4);
                var lastId = r.IsDBNull(5) ? null : r.GetString(5);
                long? lastNext = r.IsDBNull(6) ? null : r.GetInt64(6);
                long? lastLast = r.IsDBNull(7) ? null : r.GetInt64(7);
                note = AlertNote(level, nextDate, created, lastId is not null && lastId != id, lastNext, lastLast,
                    now.ToUnixTimeMilliseconds());
            }
            list.Add(new InspectionAlert(r.GetString(0), r.GetString(1), nextDate, level, note));
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
