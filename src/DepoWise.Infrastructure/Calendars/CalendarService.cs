using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Files;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.WorkOrders;

namespace DepoWise.Infrastructure.Calendars;

/// <summary>
/// Takvim öğesi — hem EL İLE kayıt (source="event") hem TÜRETİLMİŞ öğe (kaynak modülden salt-okunur).
/// Türetilmişte <paramref name="Id"/> kaynak kaydın kimliğidir, <paramref name="Version"/> 0'dır
/// (takvimden düzenlenemez — yalnız gezinme).
/// </summary>
public sealed record CalendarItem(string Source, string Id, string Title, long StartDate, long? EndDate,
    string? BranchId, string? BranchName, string? ResponsibleName, string? Detail,
    string? WorkOrderId, string? WorkOrderNo, string? Note, long Version,
    string? ResponsiblePersonnelId = null)
{
    public bool IsEvent => Source == "event";
    public string SourceDisplay => CalendarService.SourceLabel(Source);
    public string BranchDisplay => string.IsNullOrEmpty(BranchName) ? "—" : BranchName!;
    public string ResponsibleDisplay => string.IsNullOrEmpty(ResponsibleName) ? "—" : ResponsibleName!;
    public string DateDisplay => Tarih(StartDate) + (EndDate is { } e && e != StartDate ? " – " + Tarih(e) : "");
    private static string Tarih(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("dd.MM.yyyy");
}

public sealed record NewCalendarEvent(string Title, long StartDate, long? EndDate = null,
    string? BranchId = null, string? ResponsiblePersonnelId = null, string? WorkOrderId = null, string? Note = null);

/// <summary>
/// ═══ TKV-01 (ADR-171, 2026-08-28) — TAKVİM ═══
///
/// PK-H1 HİBRİT: (1) EL İLE plan kayıtları (calendar_events, CRUD burada) + (2) TÜRETİLMİŞ katman —
/// mevcut kayıtlar KOPYALANMADAN, kaynak servislerin KENDİ list metotlarıyla salt-okunur toplanır
/// (kaynağın yetki + BranchAccess + tenant kuralları otomatik aynen uygulanır; kopya gerçeklik yok).
///
/// <b>YAN KAPI YOK:</b> merkezi ekran <c>calendar</c> modülüyle açılır (deny-by-default); her türetilmiş
/// kaynak AYRICA kendi modülünün View yetkisine bakar (<see cref="AccessControl.Can"/> — yetki yoksa o
/// kaynak SESSİZCE atlanır, DocumentService.List emsali). Bakım yetkisi olmayan, takvimden bakım
/// tarihlerini OKUYAMAZ.
///
/// <b>PK-H5:</b> el ile kaydın iş emri bağı YALNIZ gezinme içindir — takvim iş emrinin durumunu,
/// stok hareketini veya başka bir modülün iş mantığını ASLA tetiklemez (burada öyle bir çağrı yoktur).
/// <b>PK-H4:</b> gün bazlı; tarihler PLAN tarihidir (ADR-162: geri-tarih kapısına girmez, created_at
/// kayıt anı audit'te korunur). <b>SİLME:</b> soft delete + Çöp Kutusu; fiziksel silme yok.
/// </summary>
public sealed class CalendarService
{
    public const string Module = "calendar";

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly WorkOrderService _workOrders;
    private readonly InspectionService _inspections;
    private readonly ProjectService _projects;
    private readonly DocumentService? _documents;   // masaüstü çevrimdışı: evrak sunucu-otoriteli → null geçilebilir

    public CalendarService(IDbConnectionFactory factory, DocumentService? documents = null, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _workOrders = new WorkOrderService(factory, _clock);
        _inspections = new InspectionService(factory, _clock);
        _projects = new ProjectService(factory, _clock);
        _documents = documents;
    }

    public static string SourceLabel(string source) => source switch
    {
        "event" => "Takvim Kaydı", "work_order" => "İş Emri", "inspection" => "Muayene/Sigorta",
        "document" => "Evrak Geçerlilik", "project" => "Proje", "maintenance" => "Bakım Hedefi", _ => source,
    };

    /// <summary>Kaynak anahtarları (filtre seçenekleri) — PK-H2 kararındaki beş türetilmiş + el ile.</summary>
    public static readonly IReadOnlyList<string> Sources =
        new[] { "event", "work_order", "inspection", "document", "project", "maintenance" };

    // ══════════════ BİRLEŞİK GÖRÜNÜM (türetilmiş + el ile) ══════════════

    /// <summary>
    /// [from,to] penceresiyle KESİŞEN tüm takvim öğeleri. Kesişim: start &lt;= to VE (end ?? start) &gt;= from.
    /// <paramref name="source"/> tek kaynağa süzer; <paramref name="branchId"/> şube süzer (şubesiz öğeler
    /// yalnız "tümü"nde görünür — şube seçiliyken o şubenin öğeleri istenmiştir).
    /// </summary>
    public IReadOnlyList<CalendarItem> Items(SessionContext s, long fromMs, long toMs,
        string? source = null, string? branchId = null, string? search = null)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        if (toMs < fromMs) throw new ArgumentException("Bitiş başlangıçtan önce olamaz.");
        var list = new List<CalendarItem>();

        bool Istenen(string kaynak) => string.IsNullOrWhiteSpace(source) || source == kaynak;
        bool Pencerede(long start, long? end) => start <= toMs && (end ?? start) >= fromMs;

        if (Istenen("event")) list.AddRange(Events(s, fromMs, toMs));

        // TÜRETİLMİŞ KAYNAKLAR — her biri kendi modül yetkisiyle (yoksa sessiz atlanır; yan kapı yok).
        // Kaynak servislerin list metotları çağrılır: BranchAccess/tenant/iki-kapı kuralları OTOMATİK aynen.
        if (Istenen("work_order") && AccessControl.Can(s, WorkOrderService.Module, PermissionAction.View))
            foreach (var w in _workOrders.List(s))
            {
                if (w.PlannedStart is null && w.PlannedEnd is null) continue;
                var start = w.PlannedStart ?? w.PlannedEnd!.Value;
                if (!Pencerede(start, w.PlannedEnd)) continue;
                list.Add(new CalendarItem("work_order", w.Id, $"{w.WoNo} · {w.Title}", start, w.PlannedEnd,
                    w.BranchId, w.BranchName, w.AssigneeName, w.StatusDisplay, w.Id, w.WoNo, null, 0));
            }

        if (Istenen("inspection") && AccessControl.Can(s, "inspection", PermissionAction.View))
            foreach (var i in _inspections.List(s))
            {
                if (i.NextDate is not { } next || !Pencerede(next, null)) continue;
                list.Add(new CalendarItem("inspection", i.Id, $"{i.VehicleText} · {i.DocTypeText}", next, null,
                    null, null, null, i.StatusText, null, null, null, 0));
            }

        if (Istenen("document") && _documents is not null && AccessControl.Can(s, DocumentService.Module, PermissionAction.View))
            foreach (var d in _documents.List(s))
            {
                if (d.ValidUntil is not { } vu || !Pencerede(vu, null)) continue;
                var etiket = d.EntityLabel == "—" ? "" : $" · {d.EntityLabel}";
                list.Add(new CalendarItem("document", d.Id, $"{d.Title}{etiket}", vu, null,
                    null, null, null, "Geçerlilik sonu", null, null, null, 0));
            }

        if (Istenen("project") && AccessControl.Can(s, "branches", PermissionAction.View))
            foreach (var p in _projects.List(s))
            {
                if (p.StartDate is null && p.EndDate is null) continue;
                var start = p.StartDate ?? p.EndDate!.Value;
                if (!Pencerede(start, p.EndDate)) continue;
                list.Add(new CalendarItem("project", p.Id, p.Name, start, p.EndDate,
                    null, p.BranchDisplay == "—" ? null : p.BranchDisplay, p.ManagerName, p.StatusDisplay, null, null, null, 0));
            }

        if (Istenen("maintenance") && AccessControl.Can(s, "maintenance", PermissionAction.View))
            foreach (var m in GunBazliBakimHedefleri(s))
            {
                if (!Pencerede(m.Due, null)) continue;
                list.Add(new CalendarItem("maintenance", m.VehicleId + ":" + m.DefId, $"{m.VehicleText} · {m.DefName}",
                    m.Due, null, null, null, null, "Bakım hedefi", null, null, null, 0));
            }

        // Şube filtresi (öğeler kaynak servislerin BranchAccess kapsamından ZATEN geçti — bu yalnız ekran süzgeci).
        if (!string.IsNullOrWhiteSpace(branchId))
            list = list.Where(i => i.BranchId == branchId).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            list = list.Where(i =>
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (i.Detail?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (i.Note?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (i.BranchName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                || (i.ResponsibleName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }

        return list.OrderBy(i => i.StartDate).ThenBy(i => i.Title, StringComparer.CurrentCulture).ToList();
    }

    // ══════════════ EL İLE KAYIT CRUD ══════════════

    private IReadOnlyList<CalendarItem> Events(SessionContext s, long fromMs, long toMs)
    {
        using var conn = _factory.Create();
        var list = new List<CalendarItem>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT e.id, e.title, e.start_date, e.end_date, e.branch_id, b.name, per.full_name,
       e.work_order_id, w.wo_no, e.note, e.version, e.responsible_personnel_id
FROM calendar_events e
LEFT JOIN branches b ON b.id = e.branch_id
LEFT JOIN personnel per ON per.id = e.responsible_personnel_id
LEFT JOIN work_orders w ON w.id = e.work_order_id
WHERE e.company_id=@c AND e.is_deleted=0
  AND e.start_date <= @to AND COALESCE(e.end_date, e.start_date) >= @from;";
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@from", fromMs);
        cmd.AddWithValue("@to", toMs);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new CalendarItem("event", r.GetString(0), r.GetString(1), r.GetInt64(2),
                r.IsDBNull(3) ? null : r.GetInt64(3), N(r, 4), N(r, 5), N(r, 6), Detail: null,
                N(r, 7), N(r, 8), N(r, 9), r.GetInt64(10), N(r, 11)));

        // ŞUBE KAPSAMI: kapsam dışı şubenin kaydı görünmez; şubesiz kayıt gizlenmez (sınıf kuralı).
        var izinli = BranchAccess.Allowed(s);
        if (izinli is not null)
        {
            var set = izinli.ToHashSet(StringComparer.Ordinal);
            list = list.Where(e => e.BranchId is null || set.Contains(e.BranchId)).ToList();
        }
        return list;
    }

    public string Create(SessionContext s, NewCalendarEvent dto)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        Dogrula(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        var id = Guid.NewGuid().ToString("N");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        EnsureRefs(s, conn, tx, dto);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO calendar_events(id, company_id, branch_id, title, note, start_date, end_date,
    responsible_personnel_id, work_order_id, created_by, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@b,@t,@n,@sd,@ed,@rp,@wo,@u,@now,@now,1,0);";
            Alanlar(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@u", s.UserId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "calendar_event", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"title\":{System.Text.Json.JsonSerializer.Serialize(dto.Title.Trim())}}}"), _clock);
        tx.Commit();
        return id;
    }

    public void Update(SessionContext s, string id, NewCalendarEvent dto, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        Dogrula(dto);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Getir(s, conn, tx, id, expectedVersion);
        EnsureRefs(s, conn, tx, dto);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE calendar_events SET title=@t, note=@n, start_date=@sd, end_date=@ed, " +
                "branch_id=@b, responsible_personnel_id=@rp, work_order_id=@wo, " +
                "updated_at=@now, version=version+1 WHERE id=@id AND company_id=@c;";
            Alanlar(cmd, dto);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "calendar_event", id, AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Soft delete (fiziksel silme YOK) — Çöp Kutusu'ndan geri yüklenir.</summary>
    public void Delete(SessionContext s, string id)
    {
        AccessControl.Require(s, Module, PermissionAction.Delete);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Getir(s, conn, tx, id, null);
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE calendar_events SET is_deleted=1, updated_at=@now, version=version+1 " +
                "WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "calendar_event", id, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
    }

    // ══════════════ Excel (liste kuralı 2) ══════════════

    public static Application.Reports.TableModel ToTableModel(IReadOnlyList<CalendarItem> rows)
        => new("Takvim",
            new[] { "Tarih", "Kaynak", "Başlık", "Şantiye/Saha", "Sorumlu", "Durum/Detay", "Not" },
            rows.Select(i => (IReadOnlyList<object?>)new object?[]
                { i.DateDisplay, i.SourceDisplay, i.Title, i.BranchDisplay, i.ResponsibleDisplay,
                  i.Detail ?? "—", i.Note ?? "—" }).ToList());

    // ══════════════ yardımcılar ══════════════

    private static void Dogrula(NewCalendarEvent dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) throw new ArgumentException("Başlık zorunlu.");
        if (dto.StartDate <= 0) throw new ArgumentException("Başlangıç tarihi zorunlu.");
        if (dto.EndDate is { } e && e < dto.StartDate) throw new ArgumentException("Bitiş başlangıçtan önce olamaz.");
    }

    private static void Alanlar(DbCommand cmd, NewCalendarEvent dto)
    {
        cmd.AddWithValue("@t", dto.Title.Trim());
        cmd.AddWithValue("@n", string.IsNullOrWhiteSpace(dto.Note) ? DBNull.Value : dto.Note!.Trim());
        // PLAN tarihi (ADR-162 emsali: Projeler/İş Emri planları) — geri-tarih kapısına GİRMEZ.
        cmd.AddWithValue("@sd", dto.StartDate);
        cmd.AddWithValue("@ed", (object?)dto.EndDate ?? DBNull.Value);
        cmd.AddWithValue("@b", Nv(dto.BranchId));
        cmd.AddWithValue("@rp", Nv(dto.ResponsiblePersonnelId));
        cmd.AddWithValue("@wo", Nv(dto.WorkOrderId));
    }

    /// <summary>Tenant + kapsam + (verilmişse) düzenleme kilidi.</summary>
    private static void Getir(SessionContext s, DbConnection conn, DbTransaction tx, string id, long? expectedVersion)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT branch_id, version FROM calendar_events WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", s.CompanyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new ArgumentException("Takvim kaydı bulunamadı.");
        if (!r.IsDBNull(0)) BranchAccess.Require(s, r.GetString(0), "takvim kaydı");
        if (expectedVersion is { } ev && r.GetInt64(1) != ev) throw new ConcurrencyException(ev, r.GetInt64(1));
    }

    private static void EnsureRefs(SessionContext s, DbConnection conn, DbTransaction tx, NewCalendarEvent dto)
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
        Var(dto.ResponsiblePersonnelId, "personnel", "Sorumlu personel");
        // PK-H5: bağ YALNIZ gezinme — iş emrinin durumu/iş mantığı burada ASLA çağrılmaz.
        Var(dto.WorkOrderId, "work_orders", "İş emri");
        if (!string.IsNullOrWhiteSpace(dto.BranchId)) BranchAccess.Require(s, dto.BranchId, "takvim kaydı");
    }

    /// <summary>
    /// GÜN-BAZLI bakım hedef tarihleri (PK-H2 ⑤): her (araç,tanım) için EN SON iptal edilmemiş bakımın
    /// tarihi + aralık günü = hedef. Km/saat bazlı tanımlar TARİHSİZDİR → takvime giremez.
    /// MaintenanceService.GetAlerts ile AYNI "en son bakım" penceresi (ROW_NUMBER) — koda DOKUNULMADI,
    /// salt-okunur ikiz sorgu (yalnız Day birimi).
    /// </summary>
    private IReadOnlyList<(string VehicleId, string DefId, string VehicleText, string DefName, long Due)> GunBazliBakimHedefleri(SessionContext s)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT vehicle_id, maintenance_def_id, internal_code, plate, name, interval_value, performed_date
FROM (
    SELECT vm.vehicle_id, vm.maintenance_def_id, v.internal_code, v.plate, d.name,
           d.interval_value, vm.performed_date,
           ROW_NUMBER() OVER (PARTITION BY vm.vehicle_id, vm.maintenance_def_id ORDER BY vm.created_at DESC) AS rn
    FROM vehicle_maintenances vm
    JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id AND d.interval_unit = 'day'
    JOIN vehicles v ON v.id = vm.vehicle_id
    WHERE vm.company_id = @c AND vm.is_cancelled = 0 AND vm.is_deleted = 0
) t
WHERE rn = 1 AND performed_date IS NOT NULL;";
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<(string, string, string, string, long)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var gun = Money.Parse(r.GetString(5));
            if (gun <= 0) continue;
            var arac = r.IsDBNull(3) || string.IsNullOrEmpty(r.GetString(3))
                ? r.GetString(2) : $"{r.GetString(2)} - {r.GetString(3)}";
            var due = r.GetInt64(6) + (long)(gun * 86_400_000m);
            list.Add((r.GetString(0), r.GetString(1), arac, r.GetString(4), due));
        }
        return list;
    }

    private static string? N(DbDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static object Nv(string? v) => string.IsNullOrWhiteSpace(v) ? DBNull.Value : v!;
}
