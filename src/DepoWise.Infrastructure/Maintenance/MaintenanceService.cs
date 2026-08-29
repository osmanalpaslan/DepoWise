using DepoWise.Application.Common;
using DepoWise.Application.Maintenance;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Materials;
using System.Data.Common;

namespace DepoWise.Infrastructure.Maintenance;

/// <summary>Bakımda kullanılan malzeme satırı. <paramref name="FromTeamStock"/> = "Bakım Ekibi Stoğundan
/// Kullanıldı" (kullanıcı isteği 2026-08-08): malzeme bakım kaydına YAZILIR, maliyete DÂHİL olur; ancak
/// merkez depo stoğundan DÜŞÜLMEZ (bakiye + hareket defteri) ve iptalde ters hareket üretilmez —
/// çünkü malzeme daha önce ekibe teslim edilmiştir. Varsayılan false = bugünkü davranış (hiç değişmez).</summary>
public sealed record MaintenanceMaterialLine(string MaterialId, decimal Quantity, bool FromTeamStock = false);

public sealed record NewMaintenance(
    string VehicleId, string DefinitionId, string? SubDefinitionId = null, string? TechnicianId = null,
    string? Description = null, string? SubDefinitionNote = null,
    decimal? PerformedKm = null, decimal? PerformedHour = null, long? PerformedDate = null,
    IReadOnlyList<MaintenanceMaterialLine>? Materials = null,
    // ── BKM-04 / KARAR-9 (2026-08-11) ────────────────────────────────────────────────────────────
    /// <summary>
    /// MALZEMENİN ÇEKİLDİĞİ DEPO — stok hangi lokasyondan düşecek. Kullanıcının AÇIK seçimidir.
    ///
    /// ⚠️ Bu alan <c>op_branch_id</c> DEĞİLDİR: o, kaydı İŞLEYEN şubedir (bakım raporundaki "Şube");
    /// bu ise stoğun FİZİKSEL çıktığı yerdir. İkisi bilinçli olarak ayrıdır (KARAR-9 md. 11).
    /// ⚠️ Aracın şubesi (<c>vehicles.branch_id</c>) bu amaçla KULLANILMAZ (KARAR-9 md. 10): araç
    /// şantiyede olabilir ama parça merkez depodan gelmiş olabilir → sessiz yanlış stok.
    ///
    /// <c>null</c> → ATANMAMIŞ (geriye dönük davranış; <c>branchId</c> göndermeyen eski istemci kırılmaz).
    /// Verilmişse serviste <see cref="MaintenanceService"/> içinde firma sahipliği doğrulanır (403).
    /// Kullanıcının seçimi hiçbir yerde sessizce BAŞKA bir lokasyona çevrilmez.
    /// </summary>
    string? StockLocationId = null);

public sealed record MaintenanceAlert(
    string VehicleId, string DefinitionId, string DefinitionName, AlertLevel Level, double Progress, decimal Consumed, decimal Interval,
    bool NeverPerformed = false);   // araca atanmış ama HİÇ yapılmamış bakım → "ilk bakım bekliyor" (2026-07-25)

public sealed record MaintenanceRow(
    string Id, string VehicleCode, string DefinitionName, string? SubDefinitionName,
    decimal? PerformedKm, decimal? PerformedHour, long? PerformedDate,
    decimal? NextDueKm, decimal? NextDueHour, long? NextDueDate, bool IsCancelled, string VehicleId = "",
    long Version = 0, string? Description = null, string? SubDefinitionNote = null, string? TechnicianId = null)
{
    private static string Fmt(decimal? km, decimal? hour, long? date) =>
        km is not null ? $"{km:0.##} km"
        : hour is not null ? $"{hour:0.##} saat"
        : date is not null ? DateTimeOffset.FromUnixTimeMilliseconds(date.Value).LocalDateTime.ToString("dd.MM.yyyy")
        : "—";
    public string PerformedDisplay => Fmt(PerformedKm, PerformedHour, PerformedDate);
    public string NextDueDisplay => Fmt(NextDueKm, NextDueHour, NextDueDate);
    public string SubDisplay => string.IsNullOrEmpty(SubDefinitionName) ? "—" : SubDefinitionName!;
    public string StatusText => IsCancelled ? "İptal" : "Aktif";
}

public sealed record MaintenanceMaterialRow(string Code, string Name, decimal Quantity, bool FromTeamStock = false)
{
    /// <summary>Geçmiş kayıtta gösterim: işaretliyse kaynağı belli olsun (kullanıcı isteği 2026-08-08).</summary>
    public string SourceText => FromTeamStock ? "Bakım ekibi stoğu" : "Merkez depo";
}

/// <summary>
/// Bakım kaydı — TEK transaction: kayıt + malzeme stok düşümü (negatif guard, tek düşüm) + sayaç ileri +
/// sonraki hedef + audit; operation_id idempotent. İptal = ters stok + hedef yeniden hesaplama.
/// Uyarı eşikleri AlertRules (%85/95/100). Yeni bakım → en-son kayıt değişir → uyarı otomatik temizlenir.
/// </summary>
public sealed class MaintenanceService
{
    private const string Module = "maintenance";
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MaintenanceService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Faz 3-Ön: bakiye yarışında tüm kayıt geri alınıp baştan denenir (en fazla 3 tekrar).
    /// Aynı operationId kullanılır; başarısız deneme geri alındığı için idempotency korunur.</summary>
    public string Save(SessionContext s, NewMaintenance dto, string operationId)
    {
        AccessControl.Require(s, Module, PermissionAction.Create);
        if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("operation_id zorunlu.");
        return StockBalanceWriter.Run(() => SaveOnce(s, dto, operationId), $"maintenance:save op={operationId}");
    }

    private string SaveOnce(SessionContext s, NewMaintenance dto, string operationId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate(); // negatif stok / sayaç için serialize

        var existing = FindByOperation(conn, tx, operationId);
        if (existing is not null) { tx.Commit(); return existing; } // idempotent

        EnsureVehicleOwned(conn, tx, s.CompanyId, dto.VehicleId);
        var def = LoadDefinition(conn, tx, s.CompanyId, dto.DefinitionId)
            ?? throw new ForbiddenException("Bakım tanımı bulunamadı veya başka firmaya ait.");

        var id = Guid.NewGuid().ToString("N");

        // Sonraki hedef
        decimal? nextKm = null, nextHour = null; long? nextDate = null;
        switch (AlertRules.ParseUnit(def.IntervalUnit))
        {
            case IntervalUnit.Km when dto.PerformedKm is not null: nextKm = dto.PerformedKm + def.IntervalValue; break;
            case IntervalUnit.Hour when dto.PerformedHour is not null: nextHour = dto.PerformedHour + def.IntervalValue; break;
            case IntervalUnit.Day when dto.PerformedDate is not null:
                nextDate = DateTimeOffset.FromUnixTimeMilliseconds(dto.PerformedDate.Value)
                    .AddDays((double)def.IntervalValue).ToUnixTimeMilliseconds();
                break;
        }

        InsertMaintenance(conn, tx, s.CompanyId, id, dto, nextKm, nextHour, nextDate, operationId, now, s.OperatingBranchId);

        // Malzeme stok düşümü — TEK düşüm, fiyat snapshot. NEGATİF STOK ENGELLENMEZ (madde 5.3, kullanıcı isteği
        // 2026-08-06): kullanıcı stok girişini henüz yapmamış olabilir; bakım iş akışı stok yüzünden durmamalı.
        // Uyarı + opsiyonel "Taslak Talep" istemci (masaüstü/web) tarafındadır; defter tüketimi yine kayıtlıdır
        // (bakiye eksiye düşebilir — açılış stoğu gibi; ADR-086). İptal ters hareketle geri ekler.
        // "Bakım Ekibi Stoğundan Kullanıldı" (kullanıcı isteği 2026-08-08): işaretli satırda malzeme bakım
        // kaydına YAZILIR (maliyete dâhil) ama merkez depo stoğuna HİÇ dokunulmaz — ne bakiye (ApplyDelta) ne de
        // hareket defteri (InsertUsageMovement). Defter ile bakiye böylece tutarlı kalır (yeni paralel stok
        // mantığı YOK). İptalde de ters hareket üretilmez (aşağıda Cancel).
        // BKM-04 / KARAR-9: malzemenin çekildiği depo. Kullanıcının AÇIK seçimi; sessizce oturum şubesine,
        // aracın şubesine ya da op_branch_id'ye ÇEVRİLMEZ. Verilmemişse ATANMAMIŞ (eski davranış).
        // Doğrulama BURADA (servis katmanında) — masaüstü bu servisi ÇEVRİMDIŞI çağırıyor; API'de olsaydı
        // o yol korumasız kalırdı (STK-03'te alınan aynı karar).
        var stockLocation = string.IsNullOrWhiteSpace(dto.StockLocationId) ? null : dto.StockLocationId!.Trim();
        EnsureLocationOwned(conn, tx, s.CompanyId, stockLocation);
        var locationKey = stockLocation ?? StockBalanceWriter.Unassigned;

        var teamStockUsed = new List<string>();
        for (int i = 0; i < (dto.Materials?.Count ?? 0); i++)
        {
            var line = dto.Materials![i];
            if (line.Quantity <= 0) throw new ArgumentException("Malzeme miktarı pozitif olmalı.");
            EnsureMaterialOwned(conn, tx, s.CompanyId, line.MaterialId);
            var price = ReadMaterialPrice(conn, tx, line.MaterialId);
            if (!line.FromTeamStock)
            {
                // Defter ve bakiye AYNI lokasyonu kullanır — ikisi ayrışırsa stok sessizce tutarsızlaşır.
                ApplyDelta(conn, tx, s.CompanyId, line.MaterialId, locationKey, -line.Quantity, now, allowNegative: true);
                InsertUsageMovement(conn, tx, s.CompanyId, line.MaterialId, id, line.Quantity, price, $"{operationId}:mat:{i}", now, stockLocation);
            }
            else teamStockUsed.Add(line.MaterialId);
            InsertMaintenanceMaterial(conn, tx, s.CompanyId, id, line.MaterialId, line.Quantity, price, line.FromTeamStock);
        }

        // Sayaç ileri (geçmiş kaydı engellemez)
        AdvanceMeterInTx(conn, tx, s.CompanyId, dto.VehicleId, def.IntervalUnit, dto.PerformedKm, dto.PerformedHour, now);

        // Denetim: hangi malzemeler ekip stoğundan işaretlendi → kullanıcı + zaman audit kaydından gelir
        // (ek kolon açılmadı; kullanıcı kararı 2026-08-08).
        var afterJson = teamStockUsed.Count == 0 ? null
            : "{\"teamStockMaterials\":[" + string.Join(",", teamStockUsed.Select(m => "\"" + m + "\"")) + "]}";
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_maintenance", id, AuditActions.Create, s.UserId,
            AfterJson: afterJson), _clock);
        tx.Commit();
        return id;
    }

    /// <summary>İptal: malzeme stoğu ters hareketle geri eklenir + kayıt iptal işaretlenir (silinmez).</summary>
    public void Cancel(SessionContext s, string maintenanceId, string reason)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("İptal gerekçesi zorunlu.");
        StockBalanceWriter.Run(() => CancelOnce(s, maintenanceId, reason), $"maintenance:cancel id={maintenanceId}");
    }

    private void CancelOnce(SessionContext s, string maintenanceId, string reason)
    {
        using var conn = _factory.Create();
        using var tx = conn.BeginImmediate();
        CancelCore(conn, tx, s, maintenanceId, reason);
        tx.Commit();
    }

    /// <summary>
    /// PAYLAŞIMLI TRANSACTION GİRİŞİ (kullanıcı kararı K1, 2026-08-09 · İş 2).
    ///
    /// Bakım iptalini ÇAĞIRANIN bağlantısı/transaction'ı içinde çalıştırır → Günlük Faaliyet iptali ile
    /// bakım+stok iptali TEK atomik işlem olur; herhangi bir adım hata verirse HEPSİ geri alınır.
    /// Faz 3-Ön'de <c>StockService</c> için kurulan desenin aynısıdır; iş kuralları DEĞİŞMEZ.
    ///
    /// ⚠️ YETKİ: Bu metot yetki KONTROL ETMEZ — çağıranın yetkilendirmeyi yapmış olması ZORUNLUDUR.
    /// Kullanıcı kararı K2: Günlük Faaliyet iptal yetkisi olan kullanıcı için ayrıca bakım/Ters Kayıt
    /// yetkisi aranmaz. Bu yüzden <c>internal</c>'dır: yalnız Infrastructure içinden çağrılabilir,
    /// API/UI doğrudan çağıramaz (arayüz değiştirilerek veya uç nokta çağrılarak atlatılamaz).
    /// </summary>
    internal void CancelInTransaction(DbConnection conn, DbTransaction tx, SessionContext s,
        string maintenanceId, string reason)
        => CancelCore(conn, tx, s, maintenanceId, reason);

    /// <summary>İptalin ortak gövdesi — transaction'ı AÇMAZ/KAPATMAZ (çağırana aittir).</summary>
    private void CancelCore(DbConnection conn, DbTransaction tx, SessionContext s, string maintenanceId, string reason)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        var status = LoadMaintenanceStatus(conn, tx, s.CompanyId, maintenanceId)
            ?? throw new ForbiddenException("Bakım kaydı bulunamadı veya başka firmaya ait.");
        if (status) return; // zaten iptal — idempotent (stok İKİNCİ KEZ geri eklenmez)

        // ── Malzemeleri geri ekle (ters hareket) ──────────────────────────────────────────────────
        // BKM-04 / KARAR-9 — İPTAL SİMETRİSİ (en kritik kabul kriteri):
        // Ters hareketin lokasyonu iptal anındaki OTURUMDAN YENİDEN HESAPLANMAZ. ORİJİNAL stok
        // hareketinin `branch_id` değeri okunur ve geri ekleme AYNI depoya yapılır.
        //   Depo A'dan 5 düşmüşse, kullanıcı sonradan Depo B ile giriş yapmış olsa bile +5 DEPO A'ya döner.
        // Aksi hâlde bir depo sessizce şişer, diğeri eksik kalır — defterle bakiye ayrışır.
        //
        // Kaynak artık `maintenance_materials` DEĞİL, DEFTERİN KENDİSİ (`stock_movements`):
        //  • lokasyon yalnız defterde tutuluyor (malzeme satırında depo kolonu yok, migration da açılmadı),
        //  • "bakım ekibi stoğu" satırları zaten hiç hareket üretmediği için doğal olarak dışarıda kalır
        //    (eskiden bayrakla atlanıyordu — artık YAPISAL),
        //  • aynı malzeme iki satırda geçse bile her hareket kendi lokasyonuna geri döner.
        foreach (var mv in LoadUsageMovements(conn, tx, s.CompanyId, maintenanceId))
        {
            ApplyDelta(conn, tx, s.CompanyId, mv.MaterialId, mv.BranchId ?? StockBalanceWriter.Unassigned,
                +mv.Quantity, now, allowNegative: true);
            InsertUsageMovement(conn, tx, s.CompanyId, mv.MaterialId, maintenanceId, mv.Quantity, mv.Price,
                $"cancel:{maintenanceId}:{mv.Id}", now, mv.BranchId, reverse: true, reversesMovementId: mv.Id);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vehicle_maintenances SET is_cancelled=1, version=version+1, updated_at=@now WHERE id=@id;";
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", maintenanceId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_maintenance", maintenanceId, AuditActions.Reverse, s.UserId,
            AfterJson: $"{{\"reason\":\"{reason}\"}}"), _clock);
    }

    /// <summary>Her (araç,tanım) için EN SON iptal edilmemiş bakımdan ilerleme + uyarı seviyesi.</summary>
    /// <summary>
    /// Bakım kaydının YAN ETKİSİZ (metadata) alanlarını günceller — İş #5 (2026-08-09), seçenek A.
    ///
    /// NEDEN YALNIZ BU ALANLAR: <c>Save</c> akışında malzeme satırları stok bakiyesini ve hareket defterini
    /// (<c>ApplyDelta</c> + <c>InsertUsageMovement</c>), <c>PerformedKm/Hour</c> ise ARAÇ SAYACINI
    /// (<c>AdvanceMeterInTx</c>) ve sonraki bakım hedefini değiştirir. Bunları geriye dönük düzenlemek
    /// "defter ana kaynaktır / bakiye doğrudan değiştirilmez" ve "sayaç geriye gitmez" (<c>MeterRule</c>)
    /// kurallarıyla çelişir. O alanlar için mevcut yol korunur: <b>iptal + yeniden oluştur</b>
    /// (bkz. <see cref="Cancel"/>). Burada yalnız açıklama/not/teknisyen düzeltilir — hiçbir hareket üretmez.
    ///
    /// Firma izolasyonu + düzenleme kilidi: UPDATE, <c>company_id</c> ve (verilmişse) <c>version</c> ile
    /// eşleşir; 0 satır etkilenirse neden ayırt edilir (eski sürüm mü, başka firma mı).
    /// </summary>
    public void UpdateMetadata(SessionContext s, string maintenanceId, string? description,
        string? subDefinitionNote, string? technicianId, long? expectedVersion = null)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (technicianId is not null) EnsurePersonnelOwned(conn, tx, s.CompanyId, technicianId);

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "UPDATE vehicle_maintenances SET description=@d, sub_definition_note=@n, technician_id=@t, " +
                "version=version+1, updated_at=@now " +
                "WHERE id=@id AND company_id=@c AND is_deleted=0 AND is_cancelled=0"
                + EditLockGuard.Clause(expectedVersion) + ";";
            EditLockGuard.Bind(cmd, expectedVersion);
            cmd.AddWithValue("@d", (object?)Trim(description) ?? DBNull.Value);
            cmd.AddWithValue("@n", (object?)Trim(subDefinitionNote) ?? DBNull.Value);
            cmd.AddWithValue("@t", (object?)technicianId ?? DBNull.Value);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@id", maintenanceId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0)
            {
                EditLockGuard.ThrowIfStale(conn, tx, "vehicle_maintenances", maintenanceId, s.CompanyId, expectedVersion);
                throw new ForbiddenException("Bakım kaydı bulunamadı, iptal edilmiş veya başka firmaya ait.");
            }
        }

        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle_maintenance", maintenanceId,
            AuditActions.Update, s.UserId), _clock);
        tx.Commit();
    }

    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Teknisyen (personel) oturumun firmasına mı ait? (İş #5 — yabancı personel atanamaz.)</summary>
    private static void EnsurePersonnelOwned(DbConnection conn, DbTransaction tx, string companyId, string personnelId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM personnel WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", personnelId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Personel bulunamadı veya başka firmaya ait.");
    }

    public IReadOnlyList<MaintenanceAlert> GetAlerts(SessionContext s)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // Her (araç,tanım) için EN SON iptal edilmemiş bakım. Eski sorgu SQLite'a özel "MAX ile bare-kolon"
        // davranışına dayanıyordu (GROUP BY'da olmayan kolonlar max satırdan gelir); PostgreSQL bunu reddeder
        // (42803). Standart, her iki DB'de de çalışan pencere fonksiyonu (ROW_NUMBER) ile yeniden yazıldı.
        // Kolon sırası AYNI (okuyucu değişmedi): 0..10 = vehicle_id..created_at.
        cmd.CommandText = @"
SELECT vehicle_id, maintenance_def_id, name, interval_value, interval_unit,
       performed_km, performed_hour, performed_date, current_meter, meter_unit, created_at
FROM (
    SELECT vm.vehicle_id, vm.maintenance_def_id, d.name, d.interval_value, d.interval_unit,
           vm.performed_km, vm.performed_hour, vm.performed_date,
           v.current_meter, v.meter_unit, vm.created_at,
           ROW_NUMBER() OVER (PARTITION BY vm.vehicle_id, vm.maintenance_def_id ORDER BY vm.created_at DESC) AS rn
    FROM vehicle_maintenances vm
    JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
    JOIN vehicles v ON v.id = vm.vehicle_id
    WHERE vm.company_id = @c AND vm.is_cancelled = 0 AND vm.is_deleted = 0
) t
WHERE rn = 1;";
        cmd.AddWithValue("@c", s.CompanyId);

        var list = new List<MaintenanceAlert>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var vehicleId = r.GetString(0);
            var defId = r.GetString(1);
            var defName = r.GetString(2);
            var interval = Money.Parse(r.GetString(3));
            var unit = AlertRules.ParseUnit(r.GetString(4));
            var perfKm = r.IsDBNull(5) ? (decimal?)null : Money.Parse(r.GetString(5));
            var perfHour = r.IsDBNull(6) ? (decimal?)null : Money.Parse(r.GetString(6));
            var perfDate = r.IsDBNull(7) ? (long?)null : r.GetInt64(7);
            var currentMeter = Money.Parse(r.GetString(8));

            decimal consumed = unit switch
            {
                IntervalUnit.Km => perfKm is null ? 0 : currentMeter - perfKm.Value,
                IntervalUnit.Hour => perfHour is null ? 0 : currentMeter - perfHour.Value,
                IntervalUnit.Day => perfDate is null ? 0 :
                    (decimal)(DateTimeOffset.FromUnixTimeMilliseconds(_clock.UtcNow.ToUnixTimeMilliseconds())
                        - DateTimeOffset.FromUnixTimeMilliseconds(perfDate.Value)).TotalDays,
                _ => 0,
            };
            if (consumed < 0) consumed = 0;
            var progress = AlertRules.Progress(consumed, interval);
            list.Add(new MaintenanceAlert(vehicleId, defId, defName, AlertRules.Level(progress), progress, consumed, interval));
        }

        // HİÇ YAPILMAMIŞ atanmış bakımlar (2026-07-25 kullanıcı bulgusu: "bakım periyodu doldu ama uyarı çıkmadı"):
        // bir bakım tanımı araca ATANMIŞ ama o araç için HİÇ (iptal edilmemiş) bakım kaydı YOKSA, ilk bakım
        // bekliyor demektir → "İlk bakım yapılmadı" (Overdue). Baz metre/tarih tutulmadığından yüzde hesaplanmaz.
        // AYRI BAĞLANTI: yukarıdaki `r` okuyucusu bu metodun sonuna kadar açık kalıyor; ikinci sorguyu AYNI
        // bağlantıda çalıştırmak PostgreSQL'de "command already in progress" (500) verir (SQLite izin verir →
        // test yeşil, canlı PG patlıyordu — web'de catch'le gizleniyordu). Ayrı bağlantı bu çakışmayı bitirir.
        using (var conn2 = _factory.Create())
        using (var cmd2 = conn2.CreateCommand())
        {
            cmd2.CommandText = @"
SELECT mdv.vehicle_id, d.id, d.name, d.interval_value
FROM maintenance_definition_vehicles mdv
JOIN maintenance_definitions d ON d.id = mdv.definition_id AND d.is_deleted = 0
JOIN vehicles v ON v.id = mdv.vehicle_id AND v.is_deleted = 0 AND v.company_id = @c
WHERE NOT EXISTS (
    SELECT 1 FROM vehicle_maintenances vm
    WHERE vm.vehicle_id = mdv.vehicle_id AND vm.maintenance_def_id = mdv.definition_id
      AND vm.is_cancelled = 0 AND vm.is_deleted = 0);";
            cmd2.AddWithValue("@c", s.CompanyId);
            using var r2 = cmd2.ExecuteReader();
            while (r2.Read())
            {
                var interval = Money.Parse(r2.GetString(3));
                list.Add(new MaintenanceAlert(r2.GetString(0), r2.GetString(1), r2.GetString(2),
                    AlertLevel.Overdue, 1.0, 0, interval, NeverPerformed: true));
            }
        }
        return list;
    }

    /// <summary>Bakım kayıtları (salt okuma) — araç/tanım/alt-tanım adları + yapılma/sonraki; araç filtresi.</summary>
    public IReadOnlyList<MaintenanceRow> ListMaintenances(SessionContext s, string? vehicleId = null, int limit = 200)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT vm.id, v.internal_code, d.name, sd.name,
       vm.performed_km, vm.performed_hour, vm.performed_date,
       vm.next_due_km, vm.next_due_hour, vm.next_due_date, vm.is_cancelled, vm.vehicle_id,
       vm.version, vm.description, vm.sub_definition_note, vm.technician_id
FROM vehicle_maintenances vm
JOIN vehicles v ON v.id = vm.vehicle_id
JOIN maintenance_definitions d ON d.id = vm.maintenance_def_id
LEFT JOIN maintenance_definitions sd ON sd.id = vm.sub_definition_id
WHERE vm.company_id=@c AND vm.is_deleted=0" + DepoWise.Application.Security.BranchScope.Sql(s, "vm.op_branch_id") + @"
  AND (CAST(@vid AS TEXT) IS NULL OR vm.vehicle_id=@vid)
ORDER BY vm.created_at DESC LIMIT @lim;";
        cmd.AddWithValue("@c", s.CompanyId);
        if (DepoWise.Application.Security.BranchScope.Active(s) is { } b) cmd.AddWithValue("@opb", b);
        cmd.AddWithValue("@vid", (object?)vehicleId ?? DBNull.Value);
        cmd.AddWithValue("@lim", limit);
        decimal? D(DbDataReader r, int i) => r.IsDBNull(i) ? (decimal?)null : Money.Parse(r.GetString(i));
        long? L(DbDataReader r, int i) => r.IsDBNull(i) ? (long?)null : r.GetInt64(i);
        var list = new List<MaintenanceRow>();
        using var rr = cmd.ExecuteReader();
        while (rr.Read())
            list.Add(new MaintenanceRow(rr.GetString(0), rr.GetString(1), rr.GetString(2),
                rr.IsDBNull(3) ? null : rr.GetString(3),
                D(rr, 4), D(rr, 5), L(rr, 6), D(rr, 7), D(rr, 8), L(rr, 9), Convert.ToInt64(rr.GetValue(10)) == 1, rr.GetString(11),
                // İş #5: düzenleme formunu doldurmak + düzenleme kilidi için sürüm ve metadata alanları.
                Convert.ToInt64(rr.GetValue(12)),
                rr.IsDBNull(13) ? null : rr.GetString(13),
                rr.IsDBNull(14) ? null : rr.GetString(14),
                rr.IsDBNull(15) ? null : rr.GetString(15)));
        return list;
    }

    /// <summary>Bir bakım kaydında kullanılan malzemeler (kod/ad/miktar).</summary>
    public IReadOnlyList<MaintenanceMaterialRow> GetMaintenanceMaterials(SessionContext s, string maintenanceId)
    {
        AccessControl.Require(s, Module, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        // from_team_stock: geçmiş kayıtta "merkez depo mu, ekip stoğu mu" görünsün (kullanıcı isteği 2026-08-08).
        cmd.CommandText = @"
SELECT m.code, m.name, mm.quantity, COALESCE(mm.from_team_stock,0) FROM maintenance_materials mm
JOIN materials m ON m.id = mm.material_id
WHERE mm.maintenance_id=@mt AND mm.company_id=@c ORDER BY m.code;";   // M-S1a: firma izolasyonu
        cmd.AddWithValue("@mt", maintenanceId);
        cmd.AddWithValue("@c", s.CompanyId);
        var list = new List<MaintenanceMaterialRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new MaintenanceMaterialRow(r.GetString(0), r.GetString(1), Money.Parse(r.GetString(2)),
                Convert.ToInt64(r.GetValue(3)) == 1));
        return list;
    }

    // ================= çekirdek =================
    private sealed record DefRow(decimal IntervalValue, string IntervalUnit);

    private static DefRow? LoadDefinition(DbConnection conn, DbTransaction tx, string companyId, string defId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT interval_value, interval_unit FROM maintenance_definitions WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", defId);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DefRow(Money.Parse(r.GetString(0)), r.GetString(1)) : null;
    }

    private static void InsertMaintenance(DbConnection conn, DbTransaction tx, string companyId, string id,
        NewMaintenance dto, decimal? nextKm, decimal? nextHour, long? nextDate, string operationId, long now, string? opBranchId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO vehicle_maintenances(id, company_id, vehicle_id, maintenance_def_id, sub_definition_id, technician_id,
    description, sub_definition_note, performed_km, performed_hour, performed_date,
    next_due_km, next_due_hour, next_due_date, operation_id, op_branch_id, is_cancelled, created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@v,@d,@sd,@tech,@desc,@sdn,@pk,@ph,@pd,@nk,@nh,@nd,@op,@opb,0,@now,@now,1,0);";
        cmd.AddWithValue("@opb", (object?)opBranchId ?? DBNull.Value);
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@v", dto.VehicleId);
        cmd.AddWithValue("@d", dto.DefinitionId);
        cmd.AddWithValue("@sd", (object?)dto.SubDefinitionId ?? DBNull.Value);
        cmd.AddWithValue("@tech", (object?)dto.TechnicianId ?? DBNull.Value);
        cmd.AddWithValue("@desc", (object?)dto.Description ?? DBNull.Value);
        cmd.AddWithValue("@sdn", dto.SubDefinitionId is null ? DBNull.Value : (object?)dto.SubDefinitionNote ?? DBNull.Value);
        cmd.AddWithValue("@pk", dto.PerformedKm is null ? DBNull.Value : Money.Serialize(dto.PerformedKm.Value));
        cmd.AddWithValue("@ph", dto.PerformedHour is null ? DBNull.Value : Money.Serialize(dto.PerformedHour.Value));
        cmd.AddWithValue("@pd", (object?)dto.PerformedDate ?? DBNull.Value);
        cmd.AddWithValue("@nk", nextKm is null ? DBNull.Value : Money.Serialize(nextKm.Value));
        cmd.AddWithValue("@nh", nextHour is null ? DBNull.Value : Money.Serialize(nextHour.Value));
        cmd.AddWithValue("@nd", (object?)nextDate ?? DBNull.Value);
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Faz 3-Ön (kullanıcı kararı 3): bakım tarafının AYRI bakiye kopyası KALDIRILDI. Stok bakiyesi
    /// artık tek ortak yazıcıdan (<see cref="StockBalanceWriter"/>) geçer → aynı stok için iki farklı
    /// güvenlik mantığı yok. Negatife izin verme davranışı (allowNegative: true) DEĞİŞMEDİ.</summary>
    /// <remarks>
    /// BKM-04 / KARAR-9 (2026-08-11) — LOKASYON ARTIK ÇAĞIRANDAN GELİR. Eskiden burada sabit
    /// <c>Unassigned</c> yazılıyordu; her bakım tüketimi ATANMAMIŞ kovasına düşüyordu.
    /// Defter (<see cref="InsertUsageMovement"/>) ve bakiye <b>AYNI</b> lokasyonu kullanır — ayrışırlarsa
    /// stok sessizce tutarsızlaşır. Lokasyon verilmezse ATANMAMIŞ (geriye dönük davranış korunur).
    /// </remarks>
    private static void ApplyDelta(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        string locationId, decimal signedQty, long now, bool allowNegative = false)
    {
        StockBalanceWriter.ApplyDelta(conn, tx, companyId, materialId, locationId ?? StockBalanceWriter.Unassigned,
            signedQty, now, allowNegative);
    }

    /// <param name="branchId">BKM-04: malzemenin çekildiği depo. <c>null</c> → ATANMAMIŞ.
    /// İptalde bu değer ORİJİNAL hareketten okunur, yeniden hesaplanmaz.</param>
    /// <param name="reversesMovementId">Geri izlenebilirlik: ters kaydın hangi hareketi geri aldığı.</param>
    private static void InsertUsageMovement(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        string maintenanceId, decimal qty, decimal? price, string operationId, long now, string? branchId,
        bool reverse = false, string? reversesMovementId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO stock_movements(id, company_id, material_id, branch_id, movement_type, direction, quantity,
    unit_price, currency_code, fx_rate, operation_id, note, created_at, document_id, is_reversed, reverses_movement_id, updated_at)
VALUES(@id,@c,@m,@br,@type,@dir,@q,@price,'TRY',NULL,@op,@note,@now,NULL,0,@rev,@now);";
        cmd.AddWithValue("@br", (object?)branchId ?? DBNull.Value);
        cmd.AddWithValue("@rev", (object?)reversesMovementId ?? DBNull.Value);
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@type", reverse ? "usage_reverse" : "usage");
        cmd.AddWithValue("@dir", reverse ? 1 : -1);
        cmd.AddWithValue("@q", Money.Serialize(qty));
        cmd.AddWithValue("@price", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.AddWithValue("@op", operationId);
        cmd.AddWithValue("@note", maintenanceId);
        cmd.AddWithValue("@now", now);
        cmd.ExecuteNonQuery();
    }

    private static void InsertMaintenanceMaterial(DbConnection conn, DbTransaction tx, string companyId, string maintenanceId,
        string materialId, decimal qty, decimal? price, bool fromTeamStock = false)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO maintenance_materials(id, company_id, maintenance_id, material_id, quantity, unit_price, from_team_stock) VALUES(@id,@c,@mt,@m,@q,@p,@ts);";
        cmd.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.AddWithValue("@c", companyId);   // M-S1a: firma izolasyonu
        cmd.AddWithValue("@mt", maintenanceId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@q", Money.Serialize(qty));
        cmd.AddWithValue("@p", price is null ? DBNull.Value : Money.Serialize(price.Value));
        cmd.AddWithValue("@ts", fromTeamStock ? 1L : 0L);
        cmd.ExecuteNonQuery();
    }

    private void AdvanceMeterInTx(DbConnection conn, DbTransaction tx, string companyId, string vehicleId,
        string unit, decimal? performedKm, decimal? performedHour, long now)
    {
        var incoming = unit == "hour" ? performedHour : performedKm;
        if (incoming is null) return;
        decimal current;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT current_meter FROM vehicles WHERE id=@id AND company_id=@c;";
            read.AddWithValue("@id", vehicleId);
            read.AddWithValue("@c", companyId);
            current = Money.Parse(read.ExecuteScalar() as string);
        }
        if (!MeterRule.ShouldAdvance(current, incoming.Value)) return;
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE vehicles SET current_meter=@m, version=version+1, updated_at=@now WHERE id=@id;";
            upd.AddWithValue("@m", Money.Serialize(incoming.Value));
            upd.AddWithValue("@now", now);
            upd.AddWithValue("@id", vehicleId);
            upd.ExecuteNonQuery();
        }
        using var log = conn.CreateCommand();
        log.Transaction = tx;
        log.CommandText =
            "INSERT INTO vehicle_meter_logs(id, company_id, vehicle_id, old_value, new_value, source, created_at) " +
            "VALUES(@id,@c,@v,@o,@n,'maintenance',@now);";
        log.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        log.AddWithValue("@c", companyId);
        log.AddWithValue("@v", vehicleId);
        log.AddWithValue("@o", Money.Serialize(current));
        log.AddWithValue("@n", Money.Serialize(incoming.Value));
        log.AddWithValue("@now", now);
        log.ExecuteNonQuery();
    }

    private static decimal ReadMaterialPrice(DbConnection conn, DbTransaction tx, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT unit_price FROM materials WHERE id=@m;";
        cmd.AddWithValue("@m", materialId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    private static string? FindByOperation(DbConnection conn, DbTransaction tx, string operationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM vehicle_maintenances WHERE operation_id=@op;";
        cmd.AddWithValue("@op", operationId);
        return cmd.ExecuteScalar() as string;
    }

    private static bool? LoadMaintenanceStatus(DbConnection conn, DbTransaction tx, string companyId, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT is_cancelled FROM vehicle_maintenances WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        return v is null ? null : Convert.ToInt64(v) == 1;
    }

    /// <summary>BKM-04 — İPTALİN KAYNAĞI: bu bakımın ürettiği ORİJİNAL tüketim hareketleri (defter).
    ///
    /// Lokasyon yalnız defterde tutulur (<c>maintenance_materials</c>'ta depo kolonu yok ve BKM-04
    /// migration açmadı) → ters kayıt lokasyonunu buradan okumak, "orijinal hareketin deposuna geri yaz"
    /// kuralının TEK doğru uygulamasıdır. "Bakım ekibi stoğu" satırları hiç hareket üretmediği için
    /// bu listede DOĞAL OLARAK yer almaz (eskiden bayrakla atlanıyordu — artık yapısal).
    ///
    /// Yalnız <c>usage</c> okunur; <c>usage_reverse</c> zaten ters kayıttır (tekrar ters alınmaz).
    /// Sıra kararlıdır (<c>created_at, id</c>) → iki lehçede de aynı sonuç.</summary>
    private static IReadOnlyList<UsageMovementRow> LoadUsageMovements(
        DbConnection conn, DbTransaction tx, string companyId, string maintenanceId)
    {
        var list = new List<UsageMovementRow>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT id, material_id, branch_id, quantity, unit_price FROM stock_movements " +
            "WHERE company_id=@c AND note=@mt AND movement_type='usage' ORDER BY created_at, id;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@mt", maintenanceId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new UsageMovementRow(r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                Money.Parse(r.GetString(3)),
                r.IsDBNull(4) ? null : Money.Parse(r.GetString(4))));
        return list;
    }

    private sealed record UsageMovementRow(string Id, string MaterialId, string? BranchId, decimal Quantity, decimal? Price);

    /// <summary>BKM-04 — malzemenin çekildiği depo bu firmaya ait ve aktif mi?
    /// <see cref="Materials.StockService"/> ve <see cref="Materials.OpeningStockService"/>'teki eşleriyle
    /// BİREBİR aynı desen (yeni yetki mimarisi kurulmadı; <c>is_deleted=1</c> = pasif/silinmiş depo).
    /// İstemciden gelen kimliğe körü körüne güvenilmez — UI doğrulaması tek başına yeterli değildir.</summary>
    private static void EnsureLocationOwned(DbConnection conn, DbTransaction tx, string companyId, string? locationId)
    {
        if (string.IsNullOrEmpty(locationId)) return;   // ATANMAMIŞ — uydurma yok, kontrol edilecek kimlik yok
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM branches WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", locationId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new ForbiddenException("Şube bulunamadı veya başka firmaya ait.");
    }

    // BKM-04: `LoadMaintenanceMaterials` KALDIRILDI. İptal artık malzeme satırlarından değil DEFTERDEN
    // (`LoadUsageMovements`) besleniyor — lokasyon yalnız orada tutuluyor. Ekip-stoğu satırlarının
    // atlanması da bayrak kontrolüyle değil, hiç hareket üretmemeleriyle yapısal olarak sağlanıyor.

    private static void EnsureVehicleOwned(DbConnection conn, DbTransaction tx, string companyId, string vehicleId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM vehicles WHERE id=@id AND company_id=@c AND is_deleted=0;";
        cmd.AddWithValue("@id", vehicleId);
        cmd.AddWithValue("@c", companyId);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0) throw new ForbiddenException("Araç bulunamadı veya başka firmaya ait.");
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
